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
