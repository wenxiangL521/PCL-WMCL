using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Minecraft;

namespace WMCL.Modding.Modpack;

public sealed record ModpackExportResult(string ArchivePath, int FileCount, long Bytes);

/// <summary>
/// 把隔离实例导出为整合包。支持两种格式：
/// - Modrinth .mrpack：加载器依赖 + overrides/
/// - Mcbbs .mcbpack：manifest.json + overrides/
/// </summary>
public static class ModpackExporter
{
    /// <summary>导出格式。</summary>
    public enum ExportFormat
    {
        Modrinth,
        Mcbbs
    }

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
        ExportFormat format = ExportFormat.Modrinth,
        ModpackExportOptions? options = null)
    {
        options ??= new ModpackExportOptions();
        var includedFiles = NormalizeIncludedFiles(options.IncludedFiles);
        if (format == ExportFormat.Mcbbs)
            return await ExportMcbbsAsync(gameRoot, versionId, outputPath, ct, isolationMode, options, includedFiles)
                .ConfigureAwait(false);
        return await ExportModrinthAsync(gameRoot, versionId, outputPath, ct, isolationMode, options, includedFiles)
            .ConfigureAwait(false);
    }

    private static async Task<ModpackExportResult> ExportModrinthAsync(
        string gameRoot,
        string versionId,
        string? outputPath,
        CancellationToken ct,
        string? isolationMode,
        ModpackExportOptions options,
        IReadOnlySet<string>? includedFiles)
    {
        var repository = new VersionRepository(gameRoot);
        await InstanceExporter.ValidateSourceAsync(repository, versionId, ct).ConfigureAwait(false);
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
        var loaderVersion = dependency is null ? null : ResolveLoaderVersion(version, loader, minecraft);

        outputPath ??= Path.Combine(
            WmclPaths.DownloadsDirectory,
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.mrpack");

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
        await ExportSafety.WriteArchiveAsync(outputPath, async archive =>
        {
            var indexEntry = archive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
            await using (var stream = indexEntry.Open())
                await JsonSerializer.SerializeAsync(stream, index, cancellationToken: ct)
                    .ConfigureAwait(false);
            var exportedFiles = AddOverrides(archive, gameDirectory, options, includedFiles, ct);
            fileCount = exportedFiles.Count;
            bytes = exportedFiles.Sum(file => file.Size);
        }, ct).ConfigureAwait(false);

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
        string? isolationMode,
        ModpackExportOptions options,
        IReadOnlySet<string>? includedFiles)
    {
        var repository = new VersionRepository(gameRoot);
        await InstanceExporter.ValidateSourceAsync(repository, versionId, ct).ConfigureAwait(false);
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
            : ResolveLoaderVersion(version, loader, minecraft);

        outputPath ??= Path.Combine(
            WmclPaths.DownloadsDirectory,
            $"{Sanitize(versionId)}-{DateTime.Now:yyyyMMdd-HHmmss}.mcbpack");

        // addons：game + 加载器两个组件；导入器 LocalModpackComponents.Add
        // 只接受 game/fabric/quilt/forge/neoforge 这些 id。
        var addons = new List<object> { new { id = "game", version = minecraft } };
        if (loaderName != "vanilla")
            addons.Add(new { id = loaderName, version = loaderVersion });
        var fileCount = 0;
        long bytes = 0;
        await ExportSafety.WriteArchiveAsync(outputPath, async archive =>
        {
            var exportedFiles = AddOverrides(archive, gameDirectory, options, includedFiles, ct);
            fileCount = exportedFiles.Count;
            bytes = exportedFiles.Sum(file => file.Size);
            var manifest = new
            {
                manifestType = "minecraftModpack",
                manifestVersion = 1,
                name = versionId,
                version = "1.0.0",
                author = "",
                addons,
                launchInfo = new { },
                files = exportedFiles.Select(file => new
                {
                    type = "addon",
                    force = false,
                    path = file.Path,
                    hash = file.Hash
                }).ToArray()
            };
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var stream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: ct)
                    .ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        return new ModpackExportResult(outputPath, fileCount, bytes);
    }

    internal static string ResolveLoaderVersion(
        WMCL.Minecraft.Models.VersionJson version,
        string loader,
        string minecraft)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in version.Libraries)
        {
            var parts = library.Name.Split(':');
            if (parts.Length < 3) continue;
            var matches = loader switch
            {
                "fabric" => parts[0] == "net.fabricmc" && parts[1] == "fabric-loader",
                "quilt" => parts[0] == "org.quiltmc" && parts[1] == "quilt-loader",
                "forge" => parts[0] == "net.minecraftforge" && parts[1] is "forge" or "fmlloader",
                "neoforge" => parts[0] == "net.neoforged" && parts[1] == "neoforge",
                _ => false
            };
            if (matches) candidates.Add(NormalizeLoaderVersion(parts[2], loader, minecraft));
        }

        if (loader is "forge" or "neoforge")
        {
            var flag = loader == "neoforge" ? "--fml.neoForgeVersion" : "--fml.forgeVersion";
            var arguments = version.Arguments?.Game ?? [];
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (!argument.IsLiteral || argument.Rules is not null || argument.Values.Count != 1) continue;
                var value = argument.Values[0];
                if (value == flag)
                {
                    if (index + 1 >= arguments.Count || !arguments[index + 1].IsLiteral ||
                        arguments[index + 1].Rules is not null || arguments[index + 1].Values.Count != 1)
                        throw new InvalidOperationException("加载器版本参数缺少明确的字面量版本。");
                    candidates.Add(NormalizeLoaderVersion(arguments[++index].Values[0], loader, minecraft));
                }
                else if (value.StartsWith(flag + "=", StringComparison.Ordinal))
                    candidates.Add(NormalizeLoaderVersion(value[(flag.Length + 1)..], loader, minecraft));
            }
        }
        if (candidates.Count > 1)
            throw new InvalidOperationException("实例包含相互矛盾的加载器版本，不能导出整合包。");
        if (candidates.Count == 1) return candidates.Single();

        var id = version.Id ?? "";
        if (loader == "fabric" && id.StartsWith("fabric-loader-", StringComparison.Ordinal) &&
            id.EndsWith("-" + minecraft, StringComparison.Ordinal))
            return NormalizeLoaderVersion(id["fabric-loader-".Length..^("-" + minecraft).Length], loader, minecraft);
        if (loader == "forge" && id.StartsWith(minecraft + "-forge-", StringComparison.Ordinal))
            return NormalizeLoaderVersion(id[(minecraft + "-forge-").Length..], loader, minecraft);
        if (loader == "neoforge" && id.StartsWith("neoforge-", StringComparison.Ordinal))
            return NormalizeLoaderVersion(id["neoforge-".Length..], loader, minecraft);

        throw new InvalidOperationException("无法确定实例的加载器版本，不能导出整合包。");
    }

    private static string NormalizeLoaderVersion(string value, string loader, string minecraft)
    {
        if (loader == "forge") value = StripMinecraftPrefix(value, minecraft);
        if (string.IsNullOrEmpty(value) || !char.IsAsciiDigit(value[0]) ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_' or '+')))
            throw new InvalidOperationException("加载器版本不是明确的字面量版本。");
        return value;
    }

    private static string StripMinecraftPrefix(string value, string minecraft) =>
        value.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase)
            ? value[(minecraft.Length + 1)..]
            : value;

    private static IReadOnlySet<string>? NormalizeIncludedFiles(IReadOnlySet<string>? files)
    {
        if (files is null) return null;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException("导出文件路径不能为空。", nameof(files));
            var relative = file.Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Contains(':') ||
                relative.Any(char.IsControl) || relative.Split('/').Any(segment =>
                    string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
                throw new ArgumentException("导出文件必须是游戏目录内的规范相对路径。", nameof(files));
            result.Add(relative);
        }
        return result;
    }

    private static IEnumerable<string> Directories(ModpackExportOptions options)
    {
        if (options.IncludeMods) yield return "mods";
        if (options.IncludeConfigs)
        {
            yield return "config";
            yield return "defaultconfigs";
        }
        if (options.IncludeResourcePacks) yield return "resourcepacks";
        if (options.IncludeShaderPacks) yield return "shaderpacks";
        if (options.IncludeSaves) yield return "saves";
        if (options.IncludeWmcl) yield return "WMCL";
    }

    private static List<ExportedFile> AddOverrides(
        ZipArchive archive,
        string gameDirectory,
        ModpackExportOptions options,
        IReadOnlySet<string>? includedFiles,
        CancellationToken ct)
    {
        var result = new List<ExportedFile>();
        var directories = Directories(options).ToArray();
        if (includedFiles is not null)
        {
            // 显式清单即完整选择：类别开关只在“按类别”模式下生效。
            foreach (var relative in includedFiles.Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var source = SelectedFile(gameDirectory, relative);
                if (source is not null) result.Add(AddFile(archive, source, relative, ct));
            }
            return result;
        }
        foreach (var directory in directories)
            foreach (var file in ExportSafety.EnumerateFiles(gameDirectory, directory, ct))
            {
                var relative = Path.GetRelativePath(gameDirectory, file).Replace('\\', '/');
                result.Add(AddFile(archive, file, relative, ct));
            }
        if (options.IncludeOptions)
            foreach (var relative in OverrideFiles)
            {
                ct.ThrowIfCancellationRequested();
                var source = Path.Combine(gameDirectory, relative);
                if (ExportSafety.FileExists(source)) result.Add(AddFile(archive, source, relative, ct));
            }
        return result;
    }

    private static string? SelectedFile(string gameDirectory, string relative)
    {
        var current = gameDirectory;
        if (!ExportSafety.ValidatePath(current)) return null;
        foreach (var segment in relative.Split('/'))
        {
            if (!Directory.Exists(current)) return null;
            var entry = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), segment, StringComparison.Ordinal));
            if (entry is null || !ExportSafety.ValidatePath(entry)) return null;
            current = entry;
        }
        return ExportSafety.FileExists(current) ? current : null;
    }

    private sealed record ExportedFile(string Path, string Hash, long Size);

    private static ExportedFile AddFile(
        ZipArchive archive,
        string source,
        string relative,
        CancellationToken ct)
    {
        using var input = ExportSafety.OpenRead(source);
        var entry = archive.CreateEntry("overrides/" + relative, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            size += read;
        }
        return new ExportedFile(relative, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), size);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "modpack" : result;
    }
}
