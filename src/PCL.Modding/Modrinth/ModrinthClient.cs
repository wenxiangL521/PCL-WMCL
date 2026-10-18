using System.Text.Json;
using PCL.Core.Http;
using PCL.Core.IO;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;

namespace PCL.Modding.Modrinth;

/// <summary>
/// Modrinth API：搜索、版本选择、下载与 required 依赖安装。
/// 端点与镜像策略对照原版 PCL 的 ResourceSearcher / ResourceVersion。
/// </summary>
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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1 (dev@pcl.local)");
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
        _downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
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
            "[\"project_type:" + projectType + "\"]",
            "[\"versions:" + gameVersion + "\"]"
        };
        if (string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase))
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

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        CancellationToken ct = default)
    {
        var json = await GetStringAsync(
            Api + "/project/" + Uri.EscapeDataString(projectId) + "/version?include_changelog=false",
            ct).ConfigureAwait(false);
        var versions = JsonSerializer.Deserialize<List<ModrinthVersionJson>>(json, JsonOptions)
                       ?? [];

        return versions.Select(MapVersion).ToList();
    }

    /// <summary>按已安装文件的 SHA1 查询 Modrinth 版本；本地/非 Modrinth 文件返回 null。</summary>
    public async Task<ModrinthVersion?> GetVersionByHashAsync(
        string sha1,
        CancellationToken ct = default)
    {
        try
        {
            var json = await GetStringAsync(
                Api + "/version_file/" + Uri.EscapeDataString(sha1) + "?algorithm=sha1",
                ct).ConfigureAwait(false);
            var version = JsonSerializer.Deserialize<ModrinthVersionJson>(json, JsonOptions);
            return version is null ? null : MapVersion(version);
        }
        catch
        {
            return null;
        }
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
        CancellationToken ct = default)
    {
        var installed = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallVersionRecursiveAsync(
                version, gameVersion, modsDirectory, loader, 0, visited, installed, progress, ct)
            .ConfigureAwait(false);
        return new ModrinthInstallResult(installed);
    }

    private async Task InstallVersionRecursiveAsync(
        ModrinthVersion version,
        string gameVersion,
        string modsDirectory,
        string loader,
        int depth,
        HashSet<string> visited,
        List<string> installed,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        if (depth > 4 || !visited.Add(version.ProjectId + "@" + version.Id)) return;
        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null) return;

        var destination = FileUtil.SafeChildPath(modsDirectory, file.Filename);
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(modsDirectory);
            var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
                "下载 Mod",
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
            await new DownloadManager(_downloadHttp).DownloadAsync(
                    [item], new DownloadOptions { MaxConcurrency = 2 }, adapter, ct)
                .ConfigureAwait(false);
            installed.Add(file.Filename);
        }

        foreach (var dependency in version.Dependencies.Where(d =>
                     string.Equals(d.DependencyType, "required", StringComparison.OrdinalIgnoreCase)))
        {
            ModrinthVersion? dependencyVersion = null;
            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                try
                {
                    dependencyVersion = await GetVersionAsync(dependency.VersionId!, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn($"无法获取 Modrinth 依赖版本 {dependency.VersionId}：{ex.Message}");
                }
            }
            if (dependencyVersion is null && !string.IsNullOrWhiteSpace(dependency.ProjectId))
                dependencyVersion = await FindCompatibleVersionAsync(
                    dependency.ProjectId!, gameVersion, loader, ct).ConfigureAwait(false);

            if (dependencyVersion is null)
            {
                Log.Warn($"Modrinth 依赖 {dependency.ProjectId ?? dependency.VersionId} 没有兼容版本，已跳过。");
                continue;
            }

            await InstallVersionRecursiveAsync(
                    dependencyVersion, gameVersion, modsDirectory, loader,
                    depth + 1, visited, installed, progress, ct)
                .ConfigureAwait(false);
        }
    }

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
        var installed = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallRecursiveAsync(
            projectId, gameVersion, modsDirectory, loader, 0, visited, installed, progress, ct)
            .ConfigureAwait(false);
        return new ModrinthInstallResult(installed);
    }

    private async Task InstallRecursiveAsync(
        string projectId,
        string gameVersion,
        string modsDirectory,
        string loader,
        int depth,
        HashSet<string> visited,
        List<string> installed,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        if (depth > 3 || !visited.Add(projectId)) return;

        progress?.Report(new JobProgress("查询 Mod 版本", null, Detail: projectId));
        var version = await FindCompatibleVersionAsync(projectId, gameVersion, loader, ct)
            .ConfigureAwait(false);
        if (version is null)
        {
            if (depth == 0)
                throw new InvalidOperationException(
                    $"找不到适用于 Minecraft {gameVersion} / {loader} 的版本。");
            Log.Warn($"依赖 {projectId} 没有兼容版本，已跳过。");
            return;
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files[0];
        var destination = FileUtil.SafeChildPath(modsDirectory, file.Filename);
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(modsDirectory);
            var target = destination;
            var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
                "下载 Mod",
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
            installed.Add(file.Filename);
        }

        foreach (var dependency in version.Dependencies.Where(d =>
                     string.Equals(d.DependencyType, "required", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(d.ProjectId)))
        {
            await InstallRecursiveAsync(
                dependency.ProjectId!, gameVersion, modsDirectory, loader,
                depth + 1, visited, installed, progress, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
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

    private static string ToMirror(string url) =>
        url.StartsWith("https://cdn.modrinth.com/", StringComparison.OrdinalIgnoreCase)
            ? CdnMirror + url["https://cdn.modrinth.com".Length..]
            : url;

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
