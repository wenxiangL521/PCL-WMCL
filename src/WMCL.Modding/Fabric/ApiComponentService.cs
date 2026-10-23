using System.Collections.Immutable;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Minecraft;
using WMCL.Modding.Modrinth;

namespace WMCL.Modding.Fabric;

public sealed class ApiComponentService(ModrinthClient client)
{
    public static string Project(string kind) => kind switch
    {
        "Quilt" => "qsl",
        "LegacyFabric" => "legacy-fabric-api",
        _ => "fabric-api"
    };

    public static string ProjectId(string kind) => kind switch
    {
        "Quilt" => "qvIfYCYJ",
        "LegacyFabric" => "9CJED7xi",
        _ => "P7dR8mSH"
    };

    public static string Name(string kind) => kind switch
    {
        "Quilt" => "Quilted Fabric API / QSL",
        "LegacyFabric" => "Legacy Fabric API",
        _ => "Fabric API"
    };

    public static string Loader(string kind) => kind switch
    {
        "Quilt" => "quilt",
        "LegacyFabric" => "legacy-fabric",
        _ => "fabric"
    };

    public static bool IsCompatible(ModrinthVersion version, string minecraft, string kind) =>
        kind is "Fabric" or "Quilt" or "LegacyFabric" && version.ProjectId == ProjectId(kind) &&
        version.GameVersions.Contains(minecraft, StringComparer.OrdinalIgnoreCase) &&
        version.Loaders.Contains(Loader(kind), StringComparer.OrdinalIgnoreCase) &&
        (version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.FirstOrDefault()) is { } file &&
        file.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
        !file.Filename.Contains('/') && !file.Filename.Contains('\\') && file.Size > 0 &&
        file.Sha1 is { Length: 40 } && file.Sha1.All(char.IsAsciiHexDigit);

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(string minecraft, string kind, CancellationToken ct = default) =>
        (await client.GetVersionsAsync(ProjectId(kind), ct, minecraft, Loader(kind)).ConfigureAwait(false))
        .Where(version => IsCompatible(version, minecraft, kind))
        .OrderByDescending(version => version.DatePublished).Select(Snapshot).ToArray();

    public static ModrinthVersion Snapshot(ModrinthVersion version) => version with
    {
        GameVersions = version.GameVersions.ToImmutableArray(),
        Loaders = version.Loaders.ToImmutableArray(),
        Files = version.Files.ToImmutableArray(),
        Dependencies = version.Dependencies.ToImmutableArray()
    };

    public async Task<IReadOnlyList<string>> InstallAsync(
        ModrinthVersion version, string minecraft, string kind, string loaderVersion,
        string modsDirectory, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (!IsCompatible(version, minecraft, kind))
            throw new InvalidOperationException("所选 API 与当前 Minecraft / 加载器不一致");
        var staging = Path.Combine(Path.GetTempPath(), "wmcl-api-" + Guid.NewGuid().ToString("N"));
        var created = new List<string>();
        try
        {
            var result = await client.InstallVersionAsync(version, minecraft, staging, Loader(kind), progress, ct, requireDependencies: true)
                .ConfigureAwait(false);
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in result.ResolvedFiles)
            {
                var source = FileUtil.SafeChildPath(staging, file);
                await LoaderDependencyChecker.ValidateAsync(source, kind, loaderVersion, ct).ConfigureAwait(false);
                var identity = await LoaderDependencyChecker.ReadModIdAsync(source, kind, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(identity) && !identities.TryAdd(identity, file))
                    throw new InvalidOperationException("所选 API 和依赖含有重复的 Mod：" + identity);
                var destination = FileUtil.SafeChildPath(modsDirectory, file);
                await VerifyExistingAsync(source, destination, ct).ConfigureAwait(false);
            }
            await CheckDuplicateModsAsync(modsDirectory, kind, identities, ct).ConfigureAwait(false);
            foreach (var file in result.ResolvedFiles)
            {
                ct.ThrowIfCancellationRequested();
                var destination = FileUtil.SafeChildPath(modsDirectory, file);
                var source = FileUtil.SafeChildPath(staging, file);
                if (await VerifyExistingAsync(source, destination, ct).ConfigureAwait(false)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created.Add(destination);
                await using var input = File.OpenRead(source);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }
            return result.ResolvedFiles;
        }
        catch
        {
            foreach (var file in created)
            {
                try { File.Delete(file); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("回收 API 文件失败：" + exception.Message);
                }
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("回收 API 临时目录失败：" + exception.Message);
                }
            }
        }
    }

    private static async Task<bool> VerifyExistingAsync(string source, string destination, CancellationToken ct)
    {
        if (!File.Exists(destination)) return false;
        if (await InstallManifestStore.ComputeFileSha1Async(destination, ct).ConfigureAwait(false) !=
            await InstallManifestStore.ComputeFileSha1Async(source, ct).ConfigureAwait(false))
            throw new IOException("Mod 文件已存在且内容不同：" + Path.GetFileName(destination));
        return true;
    }

    private static async Task CheckDuplicateModsAsync(
        string directory, string kind, IReadOnlyDictionary<string, string> identities, CancellationToken ct)
    {
        if (!Directory.Exists(directory) || identities.Count == 0) return;
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(file => file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            string? identity;
            try
            {
                identity = await LoaderDependencyChecker.ReadModIdAsync(path, kind, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Log.Warn("读取既有 Mod 身份失败：" + Path.GetFileName(path) + "（" + exception.Message + "）");
                continue;
            }
            if (identity is not null && identities.TryGetValue(identity, out var selectedFile) &&
                Path.GetFileName(path) != selectedFile)
                throw new IOException($"已存在同一 Mod 的其他文件：{Path.GetFileName(path)}（{identity}）。请先在 Mod 管理中禁用或移除旧版本。");
        }
    }
}
