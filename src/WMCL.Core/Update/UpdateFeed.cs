using System.Net;
using System.Net.Http;
using CoreHttp = WMCL.Core.Http.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMCL.Core.Update;

/// <summary>
/// 更新来源：优先读 GitHub Releases（发版即生效），失败时回退自建清单。
/// 对照 PCL-ME 的做法，但资产按平台名匹配，并用同仓库的 SHA256SUMS 校验。
/// </summary>
public static class UpdateFeed
{
    public const string Owner = "wenxiangL521";
    public const string Repository = "PCL-WMCL";

    /// <summary>可选的自建清单回退地址；留空表示只用 GitHub。</summary>
    public static string? FallbackManifestUrl { get; set; }

    private static string ReleasesApi =>
        $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=20";

    private static string LatestDownloadRoot =>
        $"https://github.com/{Owner}/{Repository}/releases/latest/download";

    public static async Task<UpdateCheckResult> QueryAsync(
        string currentVersion,
        bool includePrerelease,
        CancellationToken ct = default)
    {
        try
        {
            var releases = await FetchReleasesAsync(ct).ConfigureAwait(false);
            var candidate = SelectLatest(releases, includePrerelease);
            if (candidate is null)
                return new(UpdateCheckStatus.InvalidManifest, Error: "没有可用的发布版本。");

            var version = TrimTag(candidate.TagName);
            if (!UpdateService.TryParseVersion(version, out var remote) ||
                !UpdateService.TryParseVersion(currentVersion, out var current))
                return new(UpdateCheckStatus.IncomparableVersion,
                    Error: $"无法比较版本：远端 {version}，当前 {currentVersion}。");

            if (remote <= current) return new(UpdateCheckStatus.UpToDate);

            var asset = SelectAsset(candidate.Assets);
            if (asset is null)
                return new(UpdateCheckStatus.MissingUrl,
                    Error: "该版本没有适用于当前平台的安装包。");

            var sha256 = await TryGetChecksumAsync(asset.Name, ct).ConfigureAwait(false);
            return new(UpdateCheckStatus.UpdateAvailable, new UpdateInfo(
                version,
                asset.DownloadUrl,
                sha256,
                Summarize(candidate.Body),
                asset.Size));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // GitHub 不可达时回退自建清单（如配置了）
            if (!string.IsNullOrWhiteSpace(FallbackManifestUrl))
                return await UpdateService
                    .CheckAsync(FallbackManifestUrl!, currentVersion, ct)
                    .ConfigureAwait(false);
            return new(UpdateCheckStatus.InvalidManifest,
                Error: "无法连接更新服务器：" + exception.Message);
        }
    }

    private static async Task<IReadOnlyList<GitHubRelease>> FetchReleasesAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("WMCL-Launcher");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await CoreHttp.Client
            .SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct)
            .ConfigureAwait(false);
        return releases ?? [];
    }

    internal static GitHubRelease? SelectLatest(IReadOnlyList<GitHubRelease> releases, bool includePrerelease)
    {
        GitHubRelease? best = null;
        Version? bestVersion = null;
        foreach (var release in releases)
        {
            if (release.Draft) continue;
            if (release.Prerelease && !includePrerelease) continue;
            if (!UpdateService.TryParseVersion(TrimTag(release.TagName), out var version)) continue;
            if (bestVersion is null || version > bestVersion)
            {
                best = release;
                bestVersion = version;
            }
        }
        return best;
    }

    internal static GitHubAsset? SelectAsset(IReadOnlyList<GitHubAsset> assets)
    {
        var preferred = OperatingSystem.IsWindows()
            ? new[] { ".exe" }
            : OperatingSystem.IsMacOS()
                ? new[] { ".zip" }
                : new[] { "linux-x64", ".tar.gz" };
        if (OperatingSystem.IsWindows())
            return assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsMacOS())
            return assets.FirstOrDefault(a =>
                a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("mac", StringComparison.OrdinalIgnoreCase));
        return assets.FirstOrDefault(a =>
            a.Name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>从同版本的 SHA256SUMS 里取当前资产的校验值；取不到就返回 null（跳过校验）。</summary>
    private static async Task<string?> TryGetChecksumAsync(string assetName, CancellationToken ct)
    {
        try
        {
            var text = await CoreHttp.Client
                .GetStringAsync(LatestDownloadRoot + "/SHA256SUMS", ct)
                .ConfigureAwait(false);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^1].Trim() == assetName)
                    return parts[0].Trim();
            }
        }
        catch
        {
            // 校验文件缺失不影响更新
        }
        return null;
    }

    private static string TrimTag(string tag) =>
        tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;

    private static string? Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var line = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => !l.StartsWith('#') && !l.StartsWith('>') && l.Trim().Length > 0);
        return line?.Trim();
    }

    public sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    public sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
