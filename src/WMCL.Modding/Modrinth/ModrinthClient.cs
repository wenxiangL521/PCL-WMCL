using System.Text.Json;
using WMCL.Core.Http;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Modding.Resources;

namespace WMCL.Modding.Modrinth;

/// <summary>
/// Modrinth API：搜索、版本选择、下载与 required 依赖安装。
/// 端点与镜像策略对照原版 PCL2 的 ResourceSearcher / ResourceVersion。
/// </summary>
public sealed record ModrinthProjectInfo(
    string Id, string Slug, string Title, string Description, string ProjectType);

public sealed class ModrinthClient
{
    private const string Api = "https://api.modrinth.com/v2";
    private const string ApiMirror = "https://mod.mcimirror.top/modrinth/v2";
    private const string CdnMirror = "https://mod.mcimirror.top";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly HttpClient _downloadHttp;

    public ModrinthClient(HttpClient? http = null, HttpClient? downloadHttp = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1 (dev@wmcl.local)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _downloadHttp = downloadHttp ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 64
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
    }

    public async Task<ModrinthSearchResult> SearchAsync(
        string query,
        string gameVersion,
        string loader = "fabric",
        int limit = 20,
        CancellationToken ct = default,
        string projectType = "mod",
        int offset = 0)
    {
        var facetsList = new List<string>
        {
            "[\"project_type:" + projectType + "\"]"
        };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            facetsList.Add("[\"versions:" + gameVersion + "\"]");
        if (string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(loader))
            facetsList.Insert(1, "[\"categories:" + loader + "\"]");
        var facets = "[" + string.Join(",", facetsList) + "]";
        var url = Api + "/search?limit=" + limit + "&offset=" + Math.Max(0, offset) +
                  "&index=relevance&facets=" + Uri.EscapeDataString(facets);
        if (!string.IsNullOrWhiteSpace(query))
            url += "&query=" + Uri.EscapeDataString(query);

        var json = await GetStringAsync(url, ct).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<ModrinthSearchResponse>(json, JsonOptions)
                       ?? throw new InvalidDataException("Modrinth 搜索响应无法解析。");

        var projects = response.Hits.Select(hit => new ModrinthProject(
            hit.ProjectId, hit.Slug, hit.Title, hit.Description,
            hit.ProjectType, hit.Downloads, hit.IconUrl, hit.Categories)).ToList();

        return new ModrinthSearchResult(response.TotalHits, projects);
    }

    public async Task<ModrinthProjectInfo?> GetProjectAsync(
        string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        var json = await GetOptionalStringAsync(
            Api + "/project/" + Uri.EscapeDataString(projectId), ct, true).ConfigureAwait(false);
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;
        return new ModrinthProjectInfo(
            id,
            root.TryGetProperty("slug", out var slug) ? slug.GetString() ?? "" : "",
            title,
            root.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
            root.TryGetProperty("project_type", out var type) ? type.GetString() ?? "mod" : "mod");
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        CancellationToken ct = default,
        string? gameVersion = null,
        string? loader = null)
    {
        var filters = gameVersion is null ? "" : "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }));
        if (loader is not null) filters += "&loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }));
        var json = await GetStringAsync(
            Api + "/project/" + Uri.EscapeDataString(projectId) + "/version?include_changelog=false" + filters,
            ct).ConfigureAwait(false);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersionJson>>(json, JsonOptions)
                       ?? throw new InvalidDataException("Modrinth 版本目录响应为空。");

        return versions.Select(MapVersion).ToList();
    }

    /// <summary>按已安装文件的 SHA1 查询 Modrinth 版本；本地/非 Modrinth 文件返回 null。</summary>
    public async Task<ModrinthVersion?> GetVersionByHashAsync(
        string sha1,
        CancellationToken ct = default)
    {
        var json = await GetOptionalStringAsync(
            Api + "/version_file/" + Uri.EscapeDataString(sha1) + "?algorithm=sha1", ct, true).ConfigureAwait(false);
        if (json is null) return null;
        var version = JsonSerializer.Deserialize<ModrinthVersionJson>(json, JsonOptions)
                      ?? throw new InvalidDataException("Modrinth 文件版本响应无法解析。");
        if (string.IsNullOrWhiteSpace(version.Id) || string.IsNullOrWhiteSpace(version.ProjectId))
            throw new InvalidDataException("Modrinth 文件版本缺少版本或项目标识。");
        return MapVersion(version);
    }

    /// <summary>下载指定版本文件到目录；已存在返回 false。</summary>
    public Task<bool> InstallFileAsync(
        ModrinthFile file,
        string directory,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default) =>
        DownloadFileAsync(file, directory, "下载 Mod 更新", progress, ct);

    private async Task<bool> DownloadFileAsync(
        ModrinthFile file,
        string directory,
        string stage,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var destination = FileUtil.SafeChildPath(directory, file.Filename);
        if (File.Exists(destination)) return false;

        Directory.CreateDirectory(directory);
        var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
            stage,
            download.Percent,
            (long)download.BytesPerSecond,
            null,
            file.Filename)));

        var item = new DownloadItem
        {
            Destination = destination,
            Urls = [file.Url, ToMirror(file.Url)],
            Sha1 = file.Sha1,
            Size = file.Size
        };
        var downloader = new DownloadManager(_downloadHttp);
        await downloader.DownloadAsync(
            [item],
            new DownloadOptions { MaxConcurrency = 2 },
            adapter,
            ct).ConfigureAwait(false);
        return true;
    }

    private static ModrinthVersion MapVersion(ModrinthVersionJson version) =>
        new(
            version.Id,
            version.ProjectId,
            version.Name,
            version.VersionNumber,
            version.VersionType,
            version.DatePublished,
            version.GameVersions,
            version.Loaders,
            version.Files.Select(file => new ModrinthFile(
                file.Filename,
                file.Url,
                file.Size,
                file.Hashes?.GetValueOrDefault("sha1"),
                file.Primary)).ToList(),
            version.Dependencies.Select(dependency => new ModrinthDependency(
                dependency.VersionId,
                dependency.ProjectId,
                dependency.DependencyType)).ToList());

    public async Task<ModrinthVersion> GetVersionAsync(
        string versionId,
        CancellationToken ct = default)
    {
        var json = await GetStringAsync(
            Api + "/version/" + Uri.EscapeDataString(versionId), ct).ConfigureAwait(false);
        var version = JsonSerializer.Deserialize<ModrinthVersionJson>(json, JsonOptions)
                      ?? throw new InvalidDataException("Modrinth 版本响应无法解析。");
        return MapVersion(version);
    }

    public async Task<ModrinthVersion?> FindCompatibleVersionAsync(
        string projectId,
        string gameVersion,
        string loader = "fabric",
        CancellationToken ct = default,
        string projectType = "mod")
    {
        var versions = await GetVersionsAsync(projectId, ct).ConfigureAwait(false);
        var requireLoader = string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase);
        return versions
            .Where(v => v.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) &&
                        (projectType != "datapack" || v.Loaders.Contains("datapack", StringComparer.OrdinalIgnoreCase)) &&
                        (!requireLoader ||
                         v.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase)) &&
                        v.Files.Count > 0)
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault();
    }

    /// <summary>下载资源包或光影到示例目录，不解析 Mod 依赖。</summary>
    public async Task<ModrinthInstallResult> InstallContentAsync(
        string projectId,
        string gameVersion,
        string targetDirectory,
        string projectType,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("查询资源版本", null, Detail: projectId));
        var version = await FindCompatibleVersionAsync(
            projectId, gameVersion, "", ct, projectType).ConfigureAwait(false);
        if (version is null)
            throw new InvalidOperationException(
                $"找不到适用于 Minecraft {gameVersion} 的 {projectType} 版本。");

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files[0];
        var destination = FileUtil.SafeChildPath(targetDirectory, file.Filename);
        if (File.Exists(destination))
            return new ModrinthInstallResult([]);

        Directory.CreateDirectory(targetDirectory);
        var stage = projectType switch
        {
            "shader" => "下载光影",
            "datapack" => "下载数据包",
            _ => "下载资源包"
        };
        var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
            stage,
            download.Percent,
            (long)download.BytesPerSecond,
            null,
            file.Filename)));

        var item = new DownloadItem
        {
            Destination = destination,
            Urls = [file.Url, ToMirror(file.Url)],
            Sha1 = file.Sha1,
            Size = file.Size
        };
        var downloader = new DownloadManager(_downloadHttp);
        await downloader.DownloadAsync(
            [item],
            new DownloadOptions { MaxConcurrency = 2 },
            adapter,
            ct).ConfigureAwait(false);

        return new ModrinthInstallResult([file.Filename]);
    }

    /// <summary>安装指定版本，并按 required dependency 的 versionId/projectId 递归解析。</summary>
    public async Task<ModrinthInstallResult> InstallVersionAsync(
        ModrinthVersion version,
        string gameVersion,
        string modsDirectory,
        string loader = "fabric",
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool requireDependencies = true)
    {
        var plan = await new ResourceInstallPlanner(this, null).ResolveAsync(
            [new ResourceInstallRequest("Modrinth", version.ProjectId, version.Name, Modrinth: version)],
            gameVersion, loader, ct, requireDependencies).ConfigureAwait(false);
        return await InstallPlanAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
    }

    public async Task<ModrinthInstallResult> InstallPlanAsync(ResourceInstallPlan plan, string modsDirectory,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (plan.Entries.Any(entry => entry.Platform != "Modrinth"))
            throw new InvalidOperationException("Modrinth 安装计划包含其他来源。");
        var result = await new ResourcePlanInstaller(new DownloadManager(_downloadHttp))
            .InstallModsAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
        return new ModrinthInstallResult(result.Files.Select(path => Path.GetFileName(path)).ToArray())
        {
            ResolvedFiles = result.ResolvedFiles.Select(path => Path.GetFileName(path)).ToArray()
        };
    }

    internal Task<ResourceInstallResult> InstallUpdatePlanAsync(ResourceInstallPlan plan, string directory,
        IReadOnlyList<ResourceInstallReplacement> replacements, IProgress<JobProgress>? progress, CancellationToken ct,
        Action<string> recycleBackup) =>
        new ResourcePlanInstaller(new DownloadManager(_downloadHttp)).InstallAsync(
            plan.Entries.Select(entry => new ResourceInstallTarget(entry, directory)).ToArray(), progress, ct,
            replacements: replacements, recycleBackup: recycleBackup);

    /// <summary>安装指定的资源包/光影/数据包版本（不解析 Mod 依赖）。</summary>
    public async Task<ModrinthInstallResult> InstallContentVersionAsync(
        ModrinthVersion version,
        string targetDirectory,
        string projectType,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                   ?? throw new InvalidOperationException("该版本没有可下载文件。");
        var stage = projectType switch
        {
            "shader" => "下载光影",
            "datapack" => "下载数据包",
            _ => "下载资源包"
        };
        var downloaded = await DownloadFileAsync(
            file, targetDirectory, stage, progress, ct).ConfigureAwait(false);
        return new ModrinthInstallResult(downloaded ? [file.Filename] : []);
    }

    public async Task<ModrinthInstallResult> InstallAsync(
        string projectId,
        string gameVersion,
        string modsDirectory,
        string loader = "fabric",
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("检查 Mod 版本及必需依赖", Detail: projectId));
        var plan = await new ResourceInstallPlanner(this, null).ResolveAsync(
            [new ResourceInstallRequest("Modrinth", projectId, projectId)],
            gameVersion, loader, ct).ConfigureAwait(false);
        return await InstallPlanAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct) =>
        await GetOptionalStringAsync(url, ct, false).ConfigureAwait(false)
        ?? throw new InvalidDataException("Modrinth 响应为空。");

    private async Task<string?> GetOptionalStringAsync(string url, CancellationToken ct, bool allowMissing)
    {
        Exception? last = null;
        foreach (var candidate in new[] { url, ToApiMirror(url) }.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                return await _http.GetStringAsync(candidate, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (allowMissing && exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"Modrinth 请求失败（{candidate}）：{ex.Message}");
            }
        }

        throw new InvalidOperationException("无法连接 Modrinth API。", last);
    }

    private static string ToApiMirror(string url) =>
        url.Replace(Api, ApiMirror, StringComparison.OrdinalIgnoreCase);

    internal static string ToMirror(string url) =>
        url.StartsWith("https://cdn.modrinth.com/", StringComparison.OrdinalIgnoreCase)
            ? CdnMirror + url["https://cdn.modrinth.com".Length..]
            : url;

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
