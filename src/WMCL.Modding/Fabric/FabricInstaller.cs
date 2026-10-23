using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Http;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Minecraft;

namespace WMCL.Modding.Fabric;

public sealed record FabricLoaderInfo(string Version, bool Stable);

/// <summary>Fabric 系加载器类型。对照 PCL2 PageDownloadInstall 的 Fabric/LiteLoader 组合。</summary>
public enum FabricFamilyKind
{
    Fabric,
    Quilt,
    LegacyFabric
}

/// <summary>
/// Fabric 系 Loader 安装：Fabric Meta profile JSON + 复用原版安装器。
/// Fabric 走 meta.fabricmc.net（BMCLAPI 镜像优先），Quilt 走 meta.quiltmc.org，
/// LegacyFabric 走 meta.legacyfabric.net（两者 BMCLAPI 均未镜像，直接官方源）。
/// </summary>
public sealed class FabricInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly FabricFamilyKind _kind;
    private readonly HttpClient? _http;

    public FabricInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricFamilyKind kind = FabricFamilyKind.Fabric,
        HttpClient? http = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _kind = kind;
        _http = http;
    }

    public FabricFamilyKind Kind => _kind;

    public string DisplayName => _kind switch
    {
        FabricFamilyKind.Quilt => "Quilt",
        FabricFamilyKind.LegacyFabric => "LegacyFabric",
        _ => "Fabric"
    };

    private string MetaBase => _kind switch
    {
        FabricFamilyKind.Quilt => "https://meta.quiltmc.org/v3",
        FabricFamilyKind.LegacyFabric => "https://meta.legacyfabric.net/v2",
        _ => "https://meta.fabricmc.net/v2"
    };

    public async Task<IReadOnlyList<FabricLoaderInfo>> GetLoadersAsync(
        string gameVersion,
        CancellationToken ct = default)
    {
        var url = $"{MetaBase}/versions/loader/" + Uri.EscapeDataString(gameVersion);
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var result = new List<FabricLoaderInfo>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("loader", out var loader)) continue;
            var version = loader.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString()
                : null;
            if (string.IsNullOrEmpty(version)) continue;
            // Quilt Meta 没有 stable 字段，按版本号判断 beta/pre。
            var stable = loader.TryGetProperty("stable", out var stableElement)
                ? stableElement.GetBoolean()
                : !version.Contains("beta", StringComparison.OrdinalIgnoreCase) &&
                  !version.Contains("pre", StringComparison.OrdinalIgnoreCase);
            result.Add(new FabricLoaderInfo(version, stable));
        }

        return result;
    }

    public async Task<string> InstallAsync(
        string gameVersion,
        string? loaderVersion = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        string? instanceId = null,
        bool completeInstallation = true)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new JobProgress("获取 Fabric 版本", 0.01));
        var loaders = await GetLoadersAsync(gameVersion, ct).ConfigureAwait(false);
        if (loaders.Count == 0)
            throw new InvalidOperationException($"Fabric 没有支持 Minecraft {gameVersion} 的版本。");

        var loader = loaderVersion is null
            ? loaders.FirstOrDefault(l => l.Stable) ?? loaders[0]
            : loaders.FirstOrDefault(l =>
                  string.Equals(l.Version, loaderVersion, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"找不到 Fabric Loader {loaderVersion}。");

        progress?.Report(new JobProgress($"获取 {DisplayName} 配置", 0.02));
        var url = $"{MetaBase}/versions/loader/" +
                  Uri.EscapeDataString(gameVersion) + "/" +
                  Uri.EscapeDataString(loader.Version) + "/profile/json";
        var json = await GetStringAsync(url, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var id = document.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id))
            id = _kind switch
            {
                FabricFamilyKind.Quilt => $"quilt-loader-{loader.Version}-{gameVersion}",
                FabricFamilyKind.LegacyFabric => $"legacy-fabric-{loader.Version}-{gameVersion}",
                _ => $"fabric-loader-{loader.Version}-{gameVersion}"
            };

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

        if (!completeInstallation) return id;
        progress?.Report(new JobProgress("安装 Fabric 依赖", 0.04));
        await _installer.InstallAsync(
            id,
            new InstallOptions { Mirror = MirrorMode.Auto },
            progress,
            ct).ConfigureAwait(false);

        // 与原版 PCL2 一致：可装 Mod 的版本使用 versions/<id> 作为实例目录，mods 放在实例内。
        Directory.CreateDirectory(Path.Combine(_repository.GetVersionDirectory(id), "mods"));

        Log.Info($"{DisplayName} 安装完成：{id}（Loader {loader.Version}）");
        return id;
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        // 与原版 PCL2 一致：BMCLAPI 优先，官方兜底。
        foreach (var candidate in new[] { ToMirror(url), url }.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                return _http is null
                    ? await Http.GetStringAsync(candidate, ct, TimeSpan.FromSeconds(8)).ConfigureAwait(false)
                    : await _http.GetStringAsync(candidate, ct).ConfigureAwait(false);
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

    public async Task<string> InstallMergedAsync(
        string gameVersion, string loaderVersion, string instanceId,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        InstanceManager.ValidateName(instanceId);
        if (Directory.Exists(_repository.GetVersionDirectory(instanceId))) throw new IOException("实例已存在：" + instanceId);
        var vanillaProgress = new ProgressAdapter(update => progress?.Report(update with { Percent = (update.Percent ?? 0) * 0.35 }));
        await _installer.InstallAsync(gameVersion, progress: vanillaProgress, ct: ct).ConfigureAwait(false);
        using var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, gameVersion, ct).ConfigureAwait(false);
        var loaderProgress = new ProgressAdapter(update => progress?.Report(update with { Percent = 0.35 + (update.Percent ?? 0) * 0.15 }));
        var sourceId = await new FabricInstaller(workspace.Repository, kind: _kind, http: _http).InstallAsync(
            gameVersion, loaderVersion, loaderProgress, ct, completeInstallation: false).ConfigureAwait(false);
        progress?.Report(new JobProgress("合并整合包加载器", 0.5));
        await workspace.MergeAsync(sourceId, instanceId, ct).ConfigureAwait(false);
        var completeProgress = new ProgressAdapter(update => progress?.Report(update with { Percent = 0.5 + (update.Percent ?? 0) * 0.5 }));
        await _installer.InstallAsync(instanceId, progress: completeProgress, ct: ct).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(_repository.GetVersionDirectory(instanceId), "mods"));
        return instanceId;
    }

    private string ToMirror(string url) => _kind switch
    {
        // BMCLAPI 只镜像了 Fabric Meta；Quilt/LegacyFabric 直接走官方源。
        FabricFamilyKind.Fabric => url.Replace(
            "https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta",
            StringComparison.OrdinalIgnoreCase),
        _ => url
    };

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }
}
