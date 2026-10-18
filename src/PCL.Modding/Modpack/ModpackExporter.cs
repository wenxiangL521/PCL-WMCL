using System.IO.Compression;
using System.Text.Json;
using PCL.Core;
using PCL.Minecraft;

namespace PCL.Modding.Modpack;

public sealed record ModpackExportResult(string ArchivePath, int FileCount, long Bytes);

/// <summary>
/// 把隔离实例导出为 Modrinth .mrpack：加载器依赖 + overrides（mods/config/资源包等）。
/// 文件全部放入 overrides，避免依赖 Modrinth 文件元数据。
/// </summary>
public static class ModpackExporter
{
    private static readonly string[] OverrideDirectories =
    [
        "mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks"
    ];

    private static readonly string[] OverrideFiles =
    [
        "options.txt", "optionsof.txt"
    ];

    public static async Task<ModpackExportResult> ExportAsync(
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
            throw new NotSupportedException("只有按实例隔离的版本可以导出为整合包。");

        var loader = MinecraftVersionUtil.GetLoader(version);
        if (loader == "vanilla")
            throw new NotSupportedException("原版实例没有加载器依赖，不能导出为 .mrpack。");
        var minecraft = MinecraftVersionUtil.GetBaseGameVersion(versionId, version);
        var loaderVersion = FindLoaderVersion(version, loader, minecraft);

        outputPath ??= Path.Combine(
            PclPaths.DataDirectory, "exports",
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.mrpack");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var dependencies = new Dictionary<string, string>
        {
            ["minecraft"] = minecraft,
            [loader switch
            {
                "fabric" => "fabric-loader",
                "neoforge" => "neoforge",
                _ => "forge"
            }] = loaderVersion
        };

        var index = new
        {
            formatVersion = 1,
            game = "minecraft",
            versionId = "1.0.0",
            name = versionId,
            files = Array.Empty<object>(),
            dependencies
        };

        var fileCount = 0;
        long bytes = 0;
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        var indexEntry = archive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
        await using (var stream = indexEntry.Open())
            await JsonSerializer.SerializeAsync(stream, index, cancellationToken: ct)
                .ConfigureAwait(false);

        foreach (var directory in OverrideDirectories)
        {
            var source = Path.Combine(instanceDirectory, directory);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(instanceDirectory, file).Replace('\\', '/');
                AddFile(archive, file, "overrides/" + relative, ref fileCount, ref bytes);
            }
        }

        foreach (var file in OverrideFiles)
        {
            var source = Path.Combine(instanceDirectory, file);
            if (File.Exists(source))
                AddFile(archive, source, "overrides/" + file, ref fileCount, ref bytes);
        }

        return new ModpackExportResult(outputPath, fileCount, bytes);
    }

    private static string FindLoaderVersion(
        PCL.Minecraft.Models.VersionJson version,
        string loader,
        string minecraft)
    {
        foreach (var library in version.Libraries)
        {
            var parts = library.Name.Split(':');
            if (parts.Length < 3) continue;
            if (loader == "fabric" &&
                parts[0] == "net.fabricmc" && parts[1] == "fabric-loader")
                return parts[2];
            if (loader == "forge" && parts[0] == "net.minecraftforge" &&
                (parts[1] == "forge" || parts[1] == "fmlloader"))
                return StripMinecraftPrefix(parts[2], minecraft);
            if (loader == "neoforge" && parts[0] == "net.neoforged" &&
                parts[1] == "neoforge")
                return parts[2];
        }

        var id = version.Id ?? "";
        if (loader == "fabric" && id.StartsWith("fabric-loader-") && id.EndsWith("-" + minecraft))
            return id["fabric-loader-".Length..^("-" + minecraft).Length];
        if (loader == "forge" && id.StartsWith(minecraft + "-forge-"))
            return id[(minecraft + "-forge-").Length..];
        if (loader == "neoforge" && id.StartsWith("neoforge-"))
            return id["neoforge-".Length..];

        throw new InvalidOperationException("无法确定实例的加载器版本，不能导出整合包。");
    }

    private static string StripMinecraftPrefix(string value, string minecraft) =>
        value.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase)
            ? value[(minecraft.Length + 1)..]
            : value;

    private static void AddFile(
        ZipArchive archive,
        string source,
        string entryName,
        ref int fileCount,
        ref long bytes)
    {
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
        return string.IsNullOrWhiteSpace(result) ? "modpack" : result;
    }
}
