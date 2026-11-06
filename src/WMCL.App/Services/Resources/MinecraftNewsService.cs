using System.Net;
using System.Text.Json;

namespace WMCL.App.Services;

public sealed record MinecraftNewsItem(
    string Title,
    string Url,
    string Description,
    string? Author,
    string? Image,
    string? ImageAltText,
    long Time)
{
    public string PublishDateText => Time <= 0
        ? ""
        : DateTimeOffset.FromUnixTimeSeconds(Time).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public bool HasImage => !string.IsNullOrWhiteSpace(Image);
}

/// <summary>CE 首页新闻使用的 Minecraft 官方新闻搜索 API。</summary>
public sealed class MinecraftNewsService
{
    private const string BaseApiUrl = "https://net-secondary.web.minecraft-services.net/api/v1.0/zh-cn/search";
    private static readonly string[] AllowedHosts = ["minecraft.net", "minecraft-services.net", "microsoft.com"];
    private readonly HttpClient _http;

    public MinecraftNewsService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
    }

    public async Task<IReadOnlyList<MinecraftNewsItem>> GetAsync(
        int page = 1, CancellationToken ct = default)
    {
        var url = $"{BaseApiUrl}?pageSize=24&sortType=Recent&category=News&newsOnly=true&page={Math.Max(1, page)}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return [];
        var items = new List<MinecraftNewsItem>();
        foreach (var item in results.EnumerateArray())
        {
            var title = GetString(item, "title");
            var link = GetString(item, "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;
            items.Add(new MinecraftNewsItem(
                WebUtility.HtmlDecode(title),
                link,
                WebUtility.HtmlDecode(GetString(item, "description") ?? ""),
                GetString(item, "author"),
                GetString(item, "image"),
                GetString(item, "imageAltText"),
                item.TryGetProperty("time", out var time) && time.TryGetInt64(out var stamp) ? stamp : 0));
        }
        return items;
    }

    public static bool IsSafeNewsLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
        var host = uri.IdnHost;
        return AllowedHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
