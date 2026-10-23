using System.Net;
using System.Text.Json;
using WMCL.Core.Jobs;

namespace WMCL.Modding.Forgelike;

public sealed record CleanroomVersionInfo(
    string Tag,
    DateTimeOffset? Published,
    string InstallerUrl,
    string InstallerFileName,
    bool IsBeta,
    string PageUrl)
{
    public string DisplayName => Tag;
    public string Detail => IsBeta ? "Alpha/测试版" : "稳定版";
}

/// <summary>
/// Cleanroom 是 CE 的 1.12.2 Forgelike 分支。官方只发布 GitHub Releases，
/// 没有 Forge Maven 的版本列表；安装器资产名为 cleanroom-&lt;tag&gt;-installer.jar。
/// </summary>
public sealed class CleanroomService
{
    private const string ReleasesUrl = "https://api.github.com/repos/CleanroomMC/Cleanroom/releases?per_page=50";
    private readonly HttpClient _http;

    public CleanroomService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<IReadOnlyList<CleanroomVersionInfo>> GetVersionsAsync(
        CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(ReleasesUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json);
    }

    public async Task<string> DownloadInstallerAsync(
        CleanroomVersionInfo version,
        string destination,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            version.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        var part = destination + ".part";
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(part, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                readTotal += read;
                progress?.Report(new JobProgress("下载 Cleanroom 安装器",
                    total > 0 ? (double)readTotal / total : null, null, null, version.InstallerFileName));
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
            File.Move(part, destination, true);
            progress?.Report(new JobProgress("Cleanroom 安装器已下载", 1));
            return destination;
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    public static IReadOnlyList<CleanroomVersionInfo> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cleanroom releases 不是数组。");
        var result = new List<CleanroomVersionInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;
            var tag = release.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(tag)) continue;
            string? installerUrl = null;
            string? installerName = null;
            if (release.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(name) ||
                        !name.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase))
                        continue;
                    installerUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString()
                        : null;
                    installerName = name;
                    if (!string.IsNullOrWhiteSpace(installerUrl)) break;
                }
            }
            if (string.IsNullOrWhiteSpace(installerUrl) || string.IsNullOrWhiteSpace(installerName))
                continue;
            DateTimeOffset? published = release.TryGetProperty("published_at", out var publishedElement) &&
                                        publishedElement.ValueKind == JsonValueKind.String &&
                                        DateTimeOffset.TryParse(publishedElement.GetString(), out var date)
                ? date
                : null;
            var page = release.TryGetProperty("html_url", out var pageElement)
                ? pageElement.GetString() ?? ""
                : "";
            result.Add(new CleanroomVersionInfo(
                tag, published, installerUrl, installerName,
                tag.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("beta", StringComparison.OrdinalIgnoreCase), page));
        }
        return result.OrderByDescending(version => version.Published ?? DateTimeOffset.MinValue).ToArray();
    }
}
