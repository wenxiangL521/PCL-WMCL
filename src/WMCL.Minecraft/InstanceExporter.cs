using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.IO;

namespace WMCL.Minecraft;

public sealed record InstanceExportResult(
    string ArchivePath,
    int FileCount,
    long Bytes);

public sealed record InstanceExportOptions(
    bool IncludeMods = true,
    bool IncludeConfigs = true,
    bool IncludeResourcePacks = true,
    bool IncludeShaderPacks = true,
    bool IncludeSaves = true,
    bool IncludeWmcl = true,
    bool IncludeOptions = true);

/// <summary>
/// 导出一个实例为 zip。对照 PCL2 的 PageInstanceExport：
/// 保存版本 JSON/jar 与 mods/config/resourcepacks/shaderpacks/saves 等实例数据。
/// 当前仅支持按实例隔离的版本（Fabric/Forge/NeoForge 等），避免导出共享根目录。
/// </summary>
public static class InstanceExporter
{
    private static readonly string[] DataFiles =
    [
        "options.txt", "optionsof.txt", "servers.dat", "servers.dat_old",
        "usercache.json", "usernamecache.json"
    ];

    public static Task<InstanceExportResult> ExportAsync(
        string gameRoot,
        string versionId,
        string? outputPath = null,
        CancellationToken ct = default) =>
        ExportAsync(gameRoot, versionId, new InstanceExportOptions(), outputPath, ct);

    public static async Task<InstanceExportResult> ExportAsync(
        string gameRoot,
        string versionId,
        InstanceExportOptions options,
        string? outputPath = null,
        CancellationToken ct = default,
        string? isolationMode = null)
    {
        var repository = new VersionRepository(gameRoot);
        await ValidateSourceAsync(repository, versionId, ct).ConfigureAwait(false);
        var version = await repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);
        var instanceDirectory = repository.GetVersionDirectory(versionId);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            gameRoot, versionId, version, isolationMode);

        if (!string.Equals(
                Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(instanceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
            throw new NotSupportedException(
                "该实例使用共享游戏目录，暂不支持导出；请对按实例隔离的版本导出。");

        if (!Directory.Exists(instanceDirectory))
            throw new DirectoryNotFoundException(instanceDirectory);

        outputPath ??= Path.Combine(
            WmclPaths.DownloadsDirectory,
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        var fileCount = 0;
        long bytes = 0;
        await ExportSafety.WriteArchiveAsync(outputPath, async archive =>
        {
            var metadata = JsonSerializer.SerializeToUtf8Bytes(new
            {
                format = 1,
                id = versionId,
                minecraft = MinecraftVersionUtil.GetBaseGameVersion(versionId, version),
                loader = MinecraftVersionUtil.GetLoader(version),
                exportedAt = DateTimeOffset.UtcNow
            });
            var metaEntry = archive.CreateEntry("wmcl-export.json", CompressionLevel.Optimal);
            await using (var stream = metaEntry.Open())
                await stream.WriteAsync(metadata, ct).ConfigureAwait(false);

            AddFileIfExists(archive, instanceDirectory, versionId + ".json", ref fileCount, ref bytes);
            AddFileIfExists(archive, instanceDirectory, versionId + ".jar", ref fileCount, ref bytes);

            foreach (var directory in Directories(options))
                AddDirectory(archive, instanceDirectory, directory, ref fileCount, ref bytes, ct);

            if (options.IncludeOptions)
            {
                foreach (var file in DataFiles)
                    AddFileIfExists(archive, instanceDirectory, file, ref fileCount, ref bytes);
            }
        }, ct).ConfigureAwait(false);

        return new InstanceExportResult(outputPath, fileCount, bytes);
    }

    public static async Task ValidateSourceAsync(
        VersionRepository repository, string versionId, CancellationToken ct = default)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(versionId);
        while (pending.TryPop(out var id))
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(id)) continue;
            var directory = repository.GetVersionDirectory(id);
            ExportSafety.ValidatePath(directory);
            foreach (var relative in new[]
                     {
                         id + ".jar", "WMCL/install.json", "PCL/install.json",
                         "WMCL/instance.json", "PCL/instance.json", "PCL/Setup.ini"
                     })
                ExportSafety.ValidatePath(Path.Combine(directory, relative));
            var jsonPath = Path.Combine(directory, id + ".json");
            if (!ExportSafety.FileExists(jsonPath)) continue;
            using var input = ExportSafety.OpenRead(jsonPath);
            try
            {
                using var document = await JsonDocument.ParseAsync(input, cancellationToken: ct)
                    .ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("inheritsFrom", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(property.Value.GetString()))
                        pending.Push(property.Value.GetString()!);
                }
            }
            catch (JsonException) { }
        }
    }

    private static IEnumerable<string> Directories(InstanceExportOptions options)
    {
        if (options.IncludeMods) yield return "mods";
        if (options.IncludeConfigs)
        {
            yield return "config";
            yield return "defaultconfigs";
            yield return "kubejs";
        }
        if (options.IncludeResourcePacks) yield return "resourcepacks";
        if (options.IncludeShaderPacks) yield return "shaderpacks";
        if (options.IncludeSaves) yield return "saves";
        if (options.IncludeWmcl) yield return "WMCL";
    }

    private static void AddDirectory(
        ZipArchive archive,
        string instanceDirectory,
        string relative,
        ref int fileCount,
        ref long bytes,
        CancellationToken ct)
    {
        foreach (var file in ExportSafety.EnumerateFiles(instanceDirectory, relative, ct))
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
        if (ExportSafety.FileExists(file))
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
        using var input = ExportSafety.OpenRead(source);
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
