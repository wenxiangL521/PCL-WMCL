using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace WMCL.Core.IO;

public static class RecycleBin
{
    public static void MoveDirectory(string directory)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (source == Path.GetPathRoot(source)) throw new IOException("不能回收文件系统根目录。");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (new DirectoryInfo(source).LinkTarget is not null) throw new IOException("回收备份不能是符号链接：" + source);
        if (OperatingSystem.IsWindows())
        {
            FileSystem.DeleteDirectory(source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\n tell application \"Finder\" to delete (POSIX file (item 1 of argv) as alias)\nend run");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(source);
            using var process = Process.Start(start) ?? throw new IOException("无法打开系统回收站服务。");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new IOException("等待系统回收站服务超时。");
            }
            if (process.ExitCode != 0) throw new IOException("移入回收站失败：" + error.GetAwaiter().GetResult());
            output.GetAwaiter().GetResult();
            return;
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("当前平台没有可用的回收站服务。");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(data) || !Path.IsPathRooted(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        MoveToFreedesktopTrash(source, Path.Combine(data, "Trash"));
    }

    public static string MoveToFreedesktopTrash(string directory, string trashDirectory)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var trash = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trashDirectory));
        if (source == Path.GetPathRoot(source)) throw new IOException("不能回收文件系统根目录。");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (new DirectoryInfo(source).LinkTarget is not null) throw new IOException("回收备份不能是符号链接：" + source);
        if (source == trash || trash.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("回收站不能位于待回收目录中。");
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        CreatePrivateDirectory(trash);
        CreatePrivateDirectory(files);
        CreatePrivateDirectory(info);
        var name = Path.GetFileName(source) + "-" + Guid.NewGuid().ToString("N");
        var destination = Path.Combine(files, name);
        var metadata = Path.Combine(info, name + ".trashinfo");
        using (var stream = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write("[Trash Info]\nPath=" + Uri.EscapeDataString(source).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase) +
                "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n");
        }
        var partial = Path.Combine(files, ".wmcl-trash-" + Guid.NewGuid().ToString("N"));
        try
        {
            try { Directory.Move(source, destination); }
            catch (IOException) when (Directory.Exists(source) && !Path.Exists(destination))
            {
                CopyDirectory(source, partial);
                Directory.Move(partial, destination);
                Directory.Delete(source, recursive: true);
            }
            return destination;
        }
        catch (Exception exception)
        {
            if (!Path.Exists(destination)) File.Delete(metadata);
            else throw new IOException("移入回收站未完全结束，已回收的副本保留于：" + destination, exception);
            throw;
        }
        finally
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        if (new DirectoryInfo(source).LinkTarget is not null) throw new IOException("回收备份包含符号链接：" + source);
        Directory.CreateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("回收备份包含符号链接：" + path);
            var next = Path.Combine(target, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyDirectory(path, next);
            else File.Copy(path, next, overwrite: false);
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
