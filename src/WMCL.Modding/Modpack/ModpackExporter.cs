using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.Modding.Modpack;

public sealed record ModpackExportResult(string ArchivePath, int FileCount, long Bytes);

/// <summary>
/// 把隔离实例导出为整合包。支持两种格式：
/// - Modrinth .mrpack：加载器依赖 + overrides/
/// - Mcbbs .mcbpack：manifest.json + 根目录直接放置文件
/// </summary>
public static class ModpackExporter
{
    /// <summary>导出格式。</summary>
    public enum ExportFormat
    {
        Modrinth,
        Mcbbs
    }

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
        CancellationToken ct = default,
        string? isolationMode = null,
        ExportFormat format = ExportFormat.Modrinth)
    {
        if (format == ExportFormat.Mcbbs)
            return await ExportMcbbsAsync(gameRoot, versionId, outputPath, ct, isolationMode)
                .ConfigureAwait(false);
        return await ExportModrinthAsync(gameRoot, versionId, outputPath, ct, isolationMode)
            .ConfigureAwait(false);
    }

    private static async Task<ModpackExportResult> ExportModrinthAsync(
        string gameRoot,
        string versionId,
        string? outputPath,
        CancellationToken ct,
        string? isolationMode)
    {
        var repository = new VersionRepository(gameRoot);
        var version = await repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);
        var instanceDirectory = repository.GetVersionDirectory(versionId);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            gameRoot, versionId, version, isolationMode);
        if (!string.Equals(
                Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(instanceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
            throw new NotSupportedException("只有按实例隔离的版本可以导出为整合包。");

        if (version.Libraries.Any(library => library.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Modrinth .mrpack 不支持 LiteLoader，无法导出包含该组件的实例。");
        var loader = MinecraftVersionUtil.GetLoader(version);
        var dependency = loader switch
        {
            "vanilla" => null,
            "fabric" => "fabric-loader",
            "quilt" => "quilt-loader",
            "forge" => "forge",
            "neoforge" => "neoforge",
            _ => throw new NotSupportedException($"暂不支持将 {loader} 实例导出为 .mrpack。")
        };
        var minecraft = MinecraftVersionUtil.GetBaseGameVersion(versionId, version);
        var loaderVersion = dependency is null ? null : FindLoaderVersion(version, loader, minecraft);

        outputPath ??= Path.Combine(
            WmclPaths.DataDirectory, "exports",
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.mrpack");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var dependencies = new Dictionary<string, string>
        {
            ["minecraft"] = minecraft
        };
        if (dependency is not null) dependencies[dependency] = loaderVersion!;

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

    /// <summary>
    /// Mcbbs 格式导出：manifest.json（MCBBS 标准 schema：addons 数组）+ overrides/ 目录。
    /// 导出格式必须与 LocalModpackInstaller.ParseMcbbs 兼容（它要求 manifest.json
    /// 含 addons 数组、文件位于 overrides/ 前缀下），否则导出的包无法被自家导入器识别。
    /// </summary>
    private static async Task<ModpackExportResult> ExportMcbbsAsync(
        string gameRoot,
        string versionId,
        string? outputPath,
        CancellationToken ct,
        string? isolationMode)
    {
        var repository = new VersionRepository(gameRoot);
        var version = await repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);
        var instanceDirectory = repository.GetVersionDirectory(versionId);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            gameRoot, versionId, version, isolationMode);
        if (!string.Equals(
                Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(instanceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
            throw new NotSupportedException("只有按实例隔离的版本可以导出为整合包。");

        var loader = MinecraftVersionUtil.GetLoader(version);
        var minecraft = MinecraftVersionUtil.GetBaseGameVersion(versionId, version);
        var loaderName = loader switch
        {
            "fabric" => "fabric",
            "quilt" => "quilt",
            "forge" => "forge",
            "neoforge" => "neoforge",
            _ => "vanilla"
        };

        // 加载器版本从版本 JSON 的库坐标解析，不再硬编码 "1.0.0"。
        var loaderVersion = loaderName == "vanilla"
            ? minecraft
            : FindLoaderVersion(version, loader, minecraft);

        outputPath ??= Path.Combine(
            WmclPaths.DataDirectory, "exports",
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.mcbpack");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // addons：game + 加载器两个组件；导入器 LocalModpackComponents.Add
        // 只接受 game/fabric/quilt/forge/neoforge 这些 id。
        var addons = new List<object> { new { id = "game", version = minecraft } };
        if (loaderName != "vanilla")
            addons.Add(new { id = loaderName, version = loaderVersion });
        var manifest = new
        {
            manifestType = "minecraftModpack",
            manifestVersion = 1,
            name = versionId,
            version = "1.0.0",
            author = "",
            addons,
            launchInfo = new { }
        };

        var fileCount = 0;
        long bytes = 0;
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var stream = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: ct)
                .ConfigureAwait(false);

        // Mcbbs 格式：文件放在 overrides/ 前缀下（与导入器的 OverridesFolder 匹配）。
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
        WMCL.Minecraft.Models.VersionJson version,
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
            if (loader == "quilt" &&
                parts[0] == "org.quiltmc" && parts[1] == "quilt-loader")
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
