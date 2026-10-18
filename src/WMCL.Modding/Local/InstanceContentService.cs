using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WMCL.Modding.Local;

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
    DateTimeOffset Modified,
    bool IsEnabled = true)
{
    public string StateText => IsEnabled ? "已启用" : "已禁用";

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

        var enabledPacks = kind == InstanceContentKind.ResourcePack
            ? GetEnabledResourcePacks(gameDirectory)
            : null;

        return paths
            .Select(path => CreateItem(
                kind, path,
                enabledPacks is null || enabledPacks.Contains(ResourceEntry(Path.GetFileName(path)))))
            .OrderByDescending(item => item.Modified)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SetResourcePackEnabled(
        string gameDirectory,
        InstanceContentItem item,
        bool enabled)
    {
        if (item.Kind != InstanceContentKind.ResourcePack)
            throw new InvalidOperationException("只有资源包支持启用/禁用。");

        var options = Path.Combine(gameDirectory, "options.txt");
        var packs = ReadResourcePacks(options);
        var entry = ResourceEntry(item.Name);
        var exists = packs.Any(pack => string.Equals(pack, entry, StringComparison.OrdinalIgnoreCase));
        if (enabled && !exists)
            packs.Add(entry);
        else if (!enabled)
            packs.RemoveAll(pack => string.Equals(pack, entry, StringComparison.OrdinalIgnoreCase));

        WriteResourcePacks(options, packs);
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
                    "wmcl-world-" + Guid.NewGuid().ToString("N"));
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

    private static string ResourceEntry(string name) => "file/" + name;

    private static HashSet<string> GetEnabledResourcePacks(string gameDirectory) =>
        ReadResourcePacks(Path.Combine(gameDirectory, "options.txt"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<string> ReadResourcePacks(string optionsPath)
    {
        if (!File.Exists(optionsPath)) return [];
        foreach (var line in File.ReadLines(optionsPath))
        {
            if (!line.StartsWith("resourcePacks:", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(line["resourcePacks:".Length..]);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
                return document.RootElement.EnumerateArray()
                    .Select(element => element.GetString() ?? "")
                    .Where(value => value.Length > 0)
                    .ToList();
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    private static void WriteResourcePacks(string optionsPath, List<string> packs)
    {
        var lines = File.Exists(optionsPath)
            ? File.ReadAllLines(optionsPath).ToList()
            : [];
        var value = "resourcePacks:" + JsonSerializer.Serialize(packs);
        var index = lines.FindIndex(line =>
            line.StartsWith("resourcePacks:", StringComparison.Ordinal));
        if (index >= 0)
            lines[index] = value;
        else
            lines.Add(value);

        Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
        File.WriteAllLines(optionsPath, lines, new UTF8Encoding(false));
    }

    private static InstanceContentItem CreateItem(
        InstanceContentKind kind,
        string path,
        bool enabled = true)
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
            new DateTimeOffset(modified, TimeSpan.Zero),
            enabled);
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
