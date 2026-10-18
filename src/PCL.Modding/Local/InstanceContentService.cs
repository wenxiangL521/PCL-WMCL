using System.IO.Compression;

namespace PCL.Modding.Local;

public enum InstanceContentKind
{
    ResourcePack,
    ShaderPack,
    World
}

public sealed record InstanceContentItem(
    InstanceContentKind Kind,
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset Modified)
{
    public string SizeText => Size switch
    {
        >= 1024L * 1024 * 1024 => $"{Size / 1024d / 1024d / 1024d:0.0} GB",
        >= 1024 * 1024 => $"{Size / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Size / 1024d:0.0} KB",
        _ => $"{Size} B"
    };

    public string ModifiedText => Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>实例内的资源包、光影包与存档。</summary>
public static class InstanceContentService
{
    public static string FolderName(InstanceContentKind kind) => kind switch
    {
        InstanceContentKind.ResourcePack => "resourcepacks",
        InstanceContentKind.ShaderPack => "shaderpacks",
        InstanceContentKind.World => "saves",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IReadOnlyList<InstanceContentItem> List(
        string gameDirectory,
        InstanceContentKind kind)
    {
        var folder = Path.Combine(gameDirectory, FolderName(kind));
        if (!Directory.Exists(folder)) return [];

        var paths = kind == InstanceContentKind.World
            ? Directory.EnumerateDirectories(folder)
            : Directory.EnumerateFileSystemEntries(folder).Where(path =>
                Directory.Exists(path) ||
                (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));

        return paths
            .Select(path => CreateItem(kind, path))
            .OrderByDescending(item => item.Modified)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Delete(InstanceContentItem item)
    {
        if (item.IsDirectory)
        {
            if (Directory.Exists(item.FullPath))
                Directory.Delete(item.FullPath, recursive: true);
        }
        else if (File.Exists(item.FullPath))
        {
            File.Delete(item.FullPath);
        }
    }

    /// <summary>
    /// 导入资源包/光影/存档。资源包与光影保留 zip；存档 zip 会解压为 saves/&lt;name&gt;。
    /// </summary>
    public static InstanceContentItem Import(
        string gameDirectory,
        InstanceContentKind kind,
        string sourcePath,
        string? targetName = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException("导入源不存在。", sourcePath);

        var folder = Path.Combine(gameDirectory, FolderName(kind));
        Directory.CreateDirectory(folder);
        var sourceIsDirectory = Directory.Exists(sourcePath);
        var defaultName = sourceIsDirectory
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))
            : Path.GetFileNameWithoutExtension(sourcePath);
        var name = SafeName(string.IsNullOrWhiteSpace(targetName) ? defaultName : targetName);

        if (kind != InstanceContentKind.World)
        {
            var destination = Path.Combine(folder, sourceIsDirectory
                ? name
                : Path.GetFileName(sourcePath));
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException($"目标已存在：{Path.GetFileName(destination)}");

            if (sourceIsDirectory)
                CopyDirectory(sourcePath, destination, ct);
            else
                File.Copy(sourcePath, destination);
            return CreateItem(kind, destination);
        }

        string? temporary = null;
        var sourceRoot = sourcePath;
        try
        {
            if (!sourceIsDirectory)
            {
                temporary = Path.Combine(Path.GetTempPath(),
                    "pcl-world-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                ZipFile.ExtractToDirectory(sourcePath, temporary);
                sourceRoot = temporary;

                var entries = Directory.GetFileSystemEntries(temporary);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    sourceRoot = entries[0];
                    if (string.IsNullOrWhiteSpace(targetName))
                        name = SafeName(Path.GetFileName(entries[0]));
                }
            }

            var destination = Path.Combine(folder, name);
            if (Directory.Exists(destination))
                throw new IOException($"目标存档已存在：{name}");
            CopyDirectory(sourceRoot, destination, ct);
            return CreateItem(kind, destination);
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    public static string BackupWorld(InstanceContentItem world, string destinationDirectory)
    {
        if (world.Kind != InstanceContentKind.World || !world.IsDirectory)
            throw new InvalidOperationException("只能备份存档目录。");
        if (!Directory.Exists(world.FullPath))
            throw new DirectoryNotFoundException(world.FullPath);

        Directory.CreateDirectory(destinationDirectory);
        var safeName = string.Concat(world.Name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var destination = Path.Combine(
            destinationDirectory,
            $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        ZipFile.CreateFromDirectory(
            world.FullPath,
            destination,
            CompressionLevel.Optimal,
            includeBaseDirectory: true);
        return destination;
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Imported" : result;
    }

    private static InstanceContentItem CreateItem(InstanceContentKind kind, string path)
    {
        var isDirectory = Directory.Exists(path);
        var size = isDirectory ? DirectorySize(path) : new FileInfo(path).Length;
        var modified = isDirectory
            ? Directory.GetLastWriteTimeUtc(path)
            : File.GetLastWriteTimeUtc(path);

        return new InstanceContentItem(
            kind,
            Path.GetFileName(path),
            path,
            isDirectory,
            size,
            new DateTimeOffset(modified, TimeSpan.Zero));
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0L;
        }
    }
}
