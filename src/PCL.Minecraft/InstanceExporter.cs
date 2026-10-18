using System.IO.Compression;
using System.Text.Json;
using PCL.Core;

namespace PCL.Minecraft;

public sealed record InstanceExportResult(
    string ArchivePath,
    int FileCount,
    long Bytes);

/// <summary>
/// 导出一个实例为 zip。对照 PCL 的 PageInstanceExport：
/// 保存版本 JSON/jar 与 mods/config/resourcepacks/shaderpacks/saves 等实例数据。
/// 当前仅支持按实例隔离的版本（Fabric/Forge/NeoForge 等），避免导出共享根目录。
/// </summary>
public static class InstanceExporter
{
    private static readonly string[] DataDirectories =
    [
        "mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks",
        "saves", "PCL", "kubejs"
    ];

    private static readonly string[] DataFiles =
    [
        "options.txt", "optionsof.txt", "servers.dat", "servers.dat_old",
        "usercache.json", "usernamecache.json"
    ];

    public static async Task<InstanceExportResult> ExportAsync(
        string gameRoot,
        string versionId,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var repository = new VersionRepository(gameRoot);
        var version = await repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);
        var instanceDirectory = repository.GetVersionDirectory(versionId);
        var gameDirectory = MinecraftVersionUtil.GetGameDirectory(gameRoot, versionId, version);

        if (!string.Equals(
                Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(instanceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
            throw new NotSupportedException(
                "该实例使用共享游戏目录，暂不支持导出；请对按实例隔离的版本导出。");

        if (!Directory.Exists(instanceDirectory))
            throw new DirectoryNotFoundException(instanceDirectory);

        outputPath ??= Path.Combine(
            PclPaths.DataDirectory, "exports",
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var fileCount = 0;
        long bytes = 0;
        using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
        {
            var metadata = JsonSerializer.SerializeToUtf8Bytes(new
            {
                format = 1,
                id = versionId,
                minecraft = MinecraftVersionUtil.GetBaseGameVersion(versionId, version),
                loader = MinecraftVersionUtil.GetLoader(version),
                exportedAt = DateTimeOffset.UtcNow
            });
            var metaEntry = archive.CreateEntry("pcl-export.json", CompressionLevel.Optimal);
            await using (var stream = metaEntry.Open())
                await stream.WriteAsync(metadata, ct).ConfigureAwait(false);

            AddFileIfExists(archive, instanceDirectory, versionId + ".json", ref fileCount, ref bytes);
            AddFileIfExists(archive, instanceDirectory, versionId + ".jar", ref fileCount, ref bytes);

            foreach (var directory in DataDirectories)
                AddDirectory(archive, instanceDirectory, directory, ref fileCount, ref bytes, ct);

            foreach (var file in DataFiles)
                AddFileIfExists(archive, instanceDirectory, file, ref fileCount, ref bytes);
        }

        return new InstanceExportResult(outputPath, fileCount, bytes);
    }

    private static void AddDirectory(
        ZipArchive archive,
        string instanceDirectory,
        string relative,
        ref int fileCount,
        ref long bytes,
        CancellationToken ct)
    {
        var directory = Path.Combine(instanceDirectory, relative);
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(instanceDirectory, file).Replace('\\', '/');
            AddFile(archive, file, "instance/" + relativePath, ref fileCount, ref bytes, ct);
        }
    }

    private static void AddFileIfExists(
        ZipArchive archive,
        string instanceDirectory,
        string relative,
        ref int fileCount,
        ref long bytes)
    {
        var file = Path.Combine(instanceDirectory, relative);
        if (File.Exists(file))
            AddFile(archive, file, "instance/" + relative.Replace('\\', '/'),
                ref fileCount, ref bytes, CancellationToken.None);
    }

    private static void AddFile(
        ZipArchive archive,
        string source,
        string entryName,
        ref int fileCount,
        ref long bytes,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = File.OpenRead(source);
        using var output = entry.Open();
        input.CopyTo(output);
        fileCount++;
        bytes += input.Length;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "instance" : result;
    }
}
