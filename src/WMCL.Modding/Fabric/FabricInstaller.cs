using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Http;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Minecraft;

namespace WMCL.Modding.Fabric;

public sealed record FabricLoaderInfo(string Version, bool Stable);

/// <summary>Fabric Loader 安装：Fabric Meta profile JSON + 复用原版安装器。</summary>
public sealed class FabricInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;

    public FabricInstaller(VersionRepository repository, VersionInstaller? installer = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
    }

    public async Task<IReadOnlyList<FabricLoaderInfo>> GetLoadersAsync(
        string gameVersion,
        CancellationToken ct = default)
    {
        var url = "https://meta.fabricmc.net/v2/versions/loader/" +
                  Uri.EscapeDataString(gameVersion);
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var result = new List<FabricLoaderInfo>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("loader", out var loader)) continue;
            var version = loader.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString()
                : null;
            var stable = loader.TryGetProperty("stable", out var stableElement) &&
                         stableElement.GetBoolean();
            if (!string.IsNullOrEmpty(version))
                result.Add(new FabricLoaderInfo(version, stable));
        }

        return result;
    }

    public async Task<string> InstallAsync(
        string gameVersion,
        string? loaderVersion = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        string? instanceId = null)
    {
        progress?.Report(new JobProgress("获取 Fabric 版本", 0.01));
        var loaders = await GetLoadersAsync(gameVersion, ct).ConfigureAwait(false);
        if (loaders.Count == 0)
            throw new InvalidOperationException($"Fabric 没有支持 Minecraft {gameVersion} 的版本。");

        var loader = loaderVersion is null
            ? loaders.FirstOrDefault(l => l.Stable) ?? loaders[0]
            : loaders.FirstOrDefault(l =>
                  string.Equals(l.Version, loaderVersion, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"找不到 Fabric Loader {loaderVersion}。");

        progress?.Report(new JobProgress("获取 Fabric 配置", 0.02));
        var url = "https://meta.fabricmc.net/v2/versions/loader/" +
                  Uri.EscapeDataString(gameVersion) + "/" +
                  Uri.EscapeDataString(loader.Version) + "/profile/json";
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var id = document.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id))
            id = $"fabric-loader-{loader.Version}-{gameVersion}";

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            id = instanceId;
            var node = JsonNode.Parse(json)!;
            node["id"] = id;
            json = node.ToJsonString();
        }

        var directory = Path.Combine(_repository.VersionsDirectory, id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, id + ".json"), json, ct).ConfigureAwait(false);

        progress?.Report(new JobProgress("安装 Fabric 依赖", 0.04));
        await _installer.InstallAsync(
            id,
            new InstallOptions { Mirror = MirrorMode.Auto },
            progress,
            ct).ConfigureAwait(false);

        // 与原版 PCL2 一致：可装 Mod 的版本使用 versions/<id> 作为实例目录，mods 放在实例内。
        Directory.CreateDirectory(Path.Combine(_repository.GetVersionDirectory(id), "mods"));

        Log.Info($"Fabric 安装完成：{id}（Loader {loader.Version}）");
        return id;
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        // 与原版 PCL2 一致：BMCLAPI 优先，官方兜底。
        foreach (var candidate in new[] { ToMirror(url), url }.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                return await Http.GetStringAsync(candidate, ct, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"Fabric 元数据获取失败（{candidate}）：{ex.Message}");
            }
        }

        throw new InvalidOperationException("无法获取 Fabric 元数据。", last);
    }

    private static string ToMirror(string url) =>
        url.Replace("https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta",
            StringComparison.OrdinalIgnoreCase);
}
