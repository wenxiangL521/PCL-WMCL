using System.IO.Compression;

namespace WMCL.Core.IO;

public static class ExportSafety
{
    public static bool ValidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if (new FileInfo(current).LinkTarget is not null)
                throw new InvalidDataException("导出路径包含符号链接或重解析点：" + current);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("导出路径包含符号链接或重解析点：" + current);
        }
        return true;
    }

    public static bool FileExists(string path) =>
        ValidatePath(path) && (File.GetAttributes(path) & FileAttributes.Directory) == 0;

    public static IEnumerable<string> EnumerateFiles(
        string root, string relativeDirectory, CancellationToken ct = default)
    {
        var directory = FileUtil.SafeChildPath(root, relativeDirectory);
        if (!ValidatePath(directory)) yield break;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            if (!ValidatePath(current)) throw new DirectoryNotFoundException(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                ct.ThrowIfCancellationRequested();
                if (!ValidatePath(entry)) throw new FileNotFoundException("导出文件已消失。", entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    public static FileStream OpenRead(string path)
    {
        if (!ValidatePath(path)) throw new FileNotFoundException("导出文件不存在。", path);
        return File.OpenRead(path);
    }

    public static async Task WriteArchiveAsync(
        string outputPath, Func<ZipArchive, Task> write, CancellationToken ct = default)
    {
        var destination = Path.GetFullPath(outputPath);
        ValidatePath(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("导出目标已存在：" + destination);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        ValidatePath(parent);
        var temporary = Path.Combine(parent, ".wmcl-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                await write(archive).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ValidatePath(destination);
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
