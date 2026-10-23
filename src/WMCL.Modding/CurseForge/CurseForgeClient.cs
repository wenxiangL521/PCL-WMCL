using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using WMCL.Core.Jobs;
using WMCL.Core.IO;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Modding.Resources;

namespace WMCL.Modding.CurseForge;

public sealed record CurseForgeProject(
    long Id,
    string Name,
    string Slug,
    string Summary,
    long Downloads,
    string? LogoUrl);

public sealed record CurseForgeDependency(long ModId, int RelationType);

public sealed class CurseForgeFileUnavailableException(long projectId, long fileId, Exception innerException)
    : InvalidOperationException($"CurseForge 文件不可用：项目 {projectId} / 文件 {fileId}。", innerException)
{
    public long ProjectId { get; } = projectId;
    public long FileId { get; } = fileId;
}

public sealed record CurseForgeFile(
    long Id,
    string DisplayName,
    string FileName,
    DateTimeOffset FileDate,
    long Length,
    string DownloadUrl,
    string? Sha1,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<CurseForgeDependency> Dependencies,
    IReadOnlyList<string>? Modules = null,
    long ProjectId = 0);

public sealed record CurseForgeSearchResult(
    long TotalCount,
    IReadOnlyList<CurseForgeProject> Projects);

public sealed record CurseForgeInstallResult(IReadOnlyList<string> Files)
{
    public IReadOnlyList<string> ResolvedFiles { get; init; } = Files;
}

/// <summary>
/// CurseForge API 客户端。对照 PCL2：key 来自设置或 WMCL_CURSEFORGE_API_KEY；
/// 搜索 mods 使用 classId=6 + gameVersion + modLoaderType；安装递归 RequiredDependency。
/// </summary>
public sealed class CurseForgeClient
{
    private const string Api = "https://api.curseforge.com/v1";
    private const string Mirror = "https://mod.mcimirror.top/curseforge/v1";
    private const int GameIdMinecraft = 432;
    public const int ClassIdMods = 6;
    public const int ClassIdPlugins = 5;
    public const int ClassIdResourcePacks = 12;
    public const int ClassIdShaders = 6552;
    public const int ClassIdDataPacks = 6945;
    public const int ClassIdModpacks = 4471;
    public const int ClassIdWorlds = 17;

    private readonly HttpClient _http;
    private readonly DownloadManager _downloads;
    private readonly string _apiRoot;
    private readonly string _mirrorRoot;

    public CurseForgeClient(string? apiKey = null, HttpClient? http = null, HttpClient? downloadHttp = null)
    {
        _downloads = new DownloadManager(downloadHttp);
        ApiKey = apiKey ?? "";
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1 (dev@wmcl.local)");
        _apiRoot = Api;
        _mirrorRoot = Mirror;
    }

    public string ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task TestKeyAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        await GetJsonAsync(_apiRoot + "/games?index=0&pageSize=1", ct).ConfigureAwait(false);
    }

    public async Task<CurseForgeSearchResult> SearchAsync(
        string query,
        string gameVersion,
        string loader = "fabric",
        int limit = 20,
        CancellationToken ct = default,
        int classId = ClassIdMods,
        bool filterLoader = true,
        int index = 0)
    {
        EnsureConfigured();
        var url = _apiRoot + "/mods/search?gameId=" + GameIdMinecraft +
                  "&classId=" + classId +
                  "&gameVersion=" + Uri.EscapeDataString(gameVersion) +
                  (filterLoader ? "&modLoaderType=" + LoaderType(loader) : "") +
                  "&pageSize=" + limit +
                  "&index=" + Math.Max(0, index) +
                  (string.IsNullOrWhiteSpace(query)
                      ? ""
                      : "&searchFilter=" + Uri.EscapeDataString(query));

        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<CfSearchResponse>(json, JsonOptions)
                       ?? throw new InvalidDataException("CurseForge 搜索响应无法解析。");
        var projects = response.Data.Select(mod => new CurseForgeProject(
            mod.Id, mod.Name, mod.Slug, mod.Summary, mod.DownloadCount,
            mod.Logo?.ThumbnailUrl)).ToList();
        return new CurseForgeSearchResult(response.Pagination?.TotalCount ?? projects.Count, projects);
    }

    public async Task<CurseForgeFile?> FindFileAsync(
        long modId,
        string gameVersion,
        string? loader,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var url = _apiRoot + "/mods/" + modId + "/files?gameVersion=" +
                  Uri.EscapeDataString(gameVersion) +
                  (string.IsNullOrWhiteSpace(loader) ? "" : "&modLoaderType=" + LoaderType(loader)) +
                  "&pageSize=50";
        var files = await GetFilesAsync(modId, gameVersion, loader, ct).ConfigureAwait(false);
        return files.FirstOrDefault();
    }

    public async Task<IReadOnlyList<CurseForgeFile>> GetFilesAsync(
        long modId,
        string gameVersion,
        string? loader,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var url = _apiRoot + "/mods/" + modId + "/files?gameVersion=" +
                  Uri.EscapeDataString(gameVersion) +
                  (string.IsNullOrWhiteSpace(loader) ? "" : "&modLoaderType=" + LoaderType(loader)) +
                  "&pageSize=50";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<CfFilesResponse>(json, JsonOptions)
                       ?? throw new InvalidDataException("CurseForge 文件响应无法解析。");
        return response.Data
            .Where(file => string.IsNullOrWhiteSpace(gameVersion) || file.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(file => file.FileDate)
            .ThenByDescending(file => file.Id)
            .Select(MapFile)
            .ToList();
    }

    /// <summary>安装指定 fileID 的 Mod，并递归必需依赖（依赖仍取最新兼容文件）。</summary>
    public async Task<CurseForgeInstallResult> InstallSelectedAsync(
        long modId,
        long fileId,
        string gameVersion,
        string loader,
        string modsDirectory,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plan = await new ResourceInstallPlanner(null, this).ResolveAsync(
            [new ResourceInstallRequest("CurseForge", modId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                modId.ToString(), VersionId: fileId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            gameVersion, loader, ct).ConfigureAwait(false);
        return await InstallPlanAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
    }

    public async Task<CurseForgeInstallResult> InstallPlanAsync(ResourceInstallPlan plan, string modsDirectory,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (plan.Entries.Any(entry => entry.Platform != "CurseForge"))
            throw new InvalidOperationException("CurseForge 安装计划包含其他来源。");
        var result = await new ResourcePlanInstaller(_downloads).InstallModsAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
        return new CurseForgeInstallResult(result.Files.Select(path => Path.GetFileName(path)).ToArray())
        {
            ResolvedFiles = result.ResolvedFiles.Select(path => Path.GetFileName(path)).ToArray()
        };
    }

    public async Task<CurseForgeFile> GetFileByIdAsync(
        long modId,
        long fileId,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        string json;
        try
        {
            json = await GetJsonAsync(
                _apiRoot + "/mods/" + modId + "/files/" + fileId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new CurseForgeFileUnavailableException(modId, fileId, exception);
        }
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data))
            throw new InvalidDataException("CurseForge 文件响应缺少 data。");
        if (data.ValueKind == JsonValueKind.Null)
            throw new CurseForgeFileUnavailableException(modId, fileId,
                new InvalidDataException("CurseForge 文件响应没有可用 data。"));
        if (data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CurseForge 文件响应的 data 必须是对象。");
        var file = JsonSerializer.Deserialize<CfFile>(data.GetRawText(), JsonOptions)
                   ?? throw new InvalidDataException("CurseForge 文件响应无法解析。");
        if (file.Id != fileId || file.ModId > 0 && file.ModId != modId)
            throw new InvalidDataException($"CurseForge 返回的项目或文件标识与请求不一致：项目 {modId} / 文件 {fileId}。");
        return MapFile(file);
    }

    /// <summary>安装资源包/光影/数据包：只下载主文件，不递归 Mod 依赖。</summary>
    public async Task<CurseForgeInstallResult> InstallContentAsync(
        long modId,
        string gameVersion,
        string targetDirectory,
        int classId,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var file = await FindFileAsync(modId, gameVersion, null, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"CurseForge 项目 {modId} 没有适用于 Minecraft {gameVersion} 的文件。");
        var destination = FileUtil.SafeChildPath(targetDirectory, file.FileName);
        if (!File.Exists(destination))
            await DownloadFileAsync(file, destination, progress, ct).ConfigureAwait(false);
        return new CurseForgeInstallResult([file.FileName]);
    }

    /// <summary>安装指定 fileID 的资源包/光影/数据包文件。</summary>
    public async Task<CurseForgeInstallResult> InstallContentVersionAsync(
        long projectId,
        long fileId,
        string targetDirectory,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var file = await GetFileByIdAsync(projectId, fileId, ct).ConfigureAwait(false);
        var destination = FileUtil.SafeChildPath(targetDirectory, file.FileName);
        if (!File.Exists(destination))
            await DownloadFileAsync(file, destination, progress, ct).ConfigureAwait(false);
        return new CurseForgeInstallResult([file.FileName]);
    }

    public async Task<CurseForgeInstallResult> InstallAsync(
        long modId,
        string gameVersion,
        string loader,
        string modsDirectory,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("检查 CurseForge 文件及必需依赖", Detail: modId.ToString()));
        var plan = await new ResourceInstallPlanner(null, this).ResolveAsync(
            [new ResourceInstallRequest("CurseForge", modId.ToString(System.Globalization.CultureInfo.InvariantCulture), modId.ToString())],
            gameVersion, loader, ct).ConfigureAwait(false);
        return await InstallPlanAsync(plan, modsDirectory, progress, ct).ConfigureAwait(false);
    }

    public static async Task DownloadFileAsync(
        CurseForgeFile file,
        string destination,
        IProgress<JobProgress>? progress,
        CancellationToken ct,
        DownloadManager? downloads = null)
    {
        ct.ThrowIfCancellationRequested();
        ValidateDownloadMetadata(file);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
            "下载 CurseForge 文件",
            download.Percent,
            (long)download.BytesPerSecond,
            null,
            file.FileName)));
        var item = new DownloadItem
        {
            Destination = destination,
            Urls = [file.DownloadUrl, ToMirror(file.DownloadUrl)],
            Sha1 = file.Sha1,
            Size = file.Length > 0 ? file.Length : null
        };
        var downloader = downloads ?? new DownloadManager();
        await downloader.DownloadAsync(
            [item], new DownloadOptions { MaxConcurrency = 2, VerifyExisting = true }, adapter, ct)
            .ConfigureAwait(false);
        if (file.Length > 0 && new FileInfo(destination).Length != file.Length)
        {
            File.Delete(destination);
            throw new InvalidDataException("CurseForge 文件大小与元数据不一致：" + file.FileName);
        }
    }

    internal static void ValidateDownloadMetadata(CurseForgeFile file)
    {
        if (file.Id <= 0 || file.Length < 0 || string.IsNullOrWhiteSpace(file.FileName) ||
            file.FileName != FileUtil.SafeFileName(file.FileName) || file.FileName.Contains(':') ||
            !Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            file.Sha1 is not null && (file.Sha1.Length != 40 || !file.Sha1.All(Uri.IsHexDigit)))
            throw new InvalidDataException("CurseForge 文件下载元数据无效：" + file.FileName);
    }

    private static CurseForgeFile MapFile(CfFile file)
    {
        var download = string.IsNullOrWhiteSpace(file.DownloadUrl)
            ? $"https://edge.forgecdn.net/files/{file.Id / 1000}/{file.Id % 1000}/{file.FileName}"
            : file.DownloadUrl;
        var sha1 = file.Hashes?.FirstOrDefault(hash => hash.Algo == 1)?.Value;
        return new CurseForgeFile(
            file.Id,
            file.DisplayName,
            file.FileName,
            file.FileDate,
            file.FileLength,
            download,
            sha1,
            file.GameVersions,
            file.Dependencies.Select(d => new CurseForgeDependency(d.ModId, d.RelationType)).ToList(),
            file.Modules?.Select(module => module.Name).ToList(), file.ModId);
    }

    private async Task<string> GetJsonAsync(string officialUrl, CancellationToken ct)
    {
        var mirrored = officialUrl.Replace(_apiRoot, _mirrorRoot, StringComparison.OrdinalIgnoreCase);
        Exception? last = null;
        var unauthorized = false;
        var unavailable = true;

        foreach (var url in new[] { officialUrl, mirrored }.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", ApiKey);
                request.Headers.Accept.ParseAdd("application/json");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    unauthorized = true;
                    unavailable = false;
                    last = new InvalidOperationException(
                        $"CurseForge 返回 {(int)response.StatusCode}：{body}");
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    last = new HttpRequestException(
                        $"CurseForge 返回 {(int)response.StatusCode}：{body}", null, response.StatusCode);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    unavailable = false;
                    last = new HttpRequestException(
                        $"CurseForge 返回 {(int)response.StatusCode}：{body}");
                    continue;
                }

                return body;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                unavailable = false;
                last = ex;
                Log.Warn($"CurseForge 请求失败（{url}）：{ex.Message}");
            }
        }

        if (unauthorized)
            throw new InvalidOperationException(
                "CurseForge API Key 无效或未授权，请在设置中填写有效的 Key。", last);
        if (unavailable && last is HttpRequestException missing)
            throw missing;
        throw new InvalidOperationException("无法连接 CurseForge API。", last);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "未配置 CurseForge API Key。请在设置中填写，或设置 WMCL_CURSEFORGE_API_KEY 环境变量。");
    }

    private static int LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0
    };

    internal static string ToMirror(string url) =>
        url.Replace("https://edge.forgecdn.net", "https://mod.mcimirror.top/curseforge", StringComparison.OrdinalIgnoreCase)
           .Replace("https://media.forgecdn.net", "https://mod.mcimirror.top/curseforge", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }

    private sealed class CfSearchResponse
    {
        [JsonPropertyName("data")] public List<CfMod> Data { get; set; } = [];
        [JsonPropertyName("pagination")] public CfPagination? Pagination { get; set; }
    }

    private sealed class CfPagination
    {
        [JsonPropertyName("totalCount")] public long TotalCount { get; set; }
    }

    private sealed class CfMod
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("downloadCount")] public long DownloadCount { get; set; }
        [JsonPropertyName("logo")] public CfLogo? Logo { get; set; }
    }

    private sealed class CfLogo
    {
        [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
    }

    private sealed class CfFilesResponse
    {
        [JsonPropertyName("data")] public List<CfFile> Data { get; set; } = [];
    }

    private sealed class CfFile
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("modId")] public long ModId { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
        [JsonPropertyName("fileDate")] public DateTimeOffset FileDate { get; set; }
        [JsonPropertyName("fileLength")] public long FileLength { get; set; }
        [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("gameVersions")] public List<string> GameVersions { get; set; } = [];
        [JsonPropertyName("hashes")] public List<CfHash>? Hashes { get; set; }
        [JsonPropertyName("dependencies")] public List<CfDependency> Dependencies { get; set; } = [];
        [JsonPropertyName("modules")] public List<CfModule>? Modules { get; set; }
    }

    private sealed class CfModule
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private sealed class CfHash
    {
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("algo")] public int Algo { get; set; }
    }

    private sealed class CfDependency
    {
        [JsonPropertyName("modId")] public long ModId { get; set; }
        [JsonPropertyName("relationType")] public int RelationType { get; set; }
    }
}
