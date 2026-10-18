using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WMCL.Core.Http;

/// <summary>全局 HttpClient 与常用请求封装。</summary>
public static class Http
{
    private static readonly Lazy<HttpClient> LazyClient = new(CreateClient);

    public static HttpClient Client => LazyClient.Value;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 64
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WMCL-Linux", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return client;
    }

    public static async Task<string> GetStringAsync(
        string url,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
    }

    public static async Task<byte[]> GetBytesAsync(
        string url,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
    }

    public static async Task<T?> GetJsonAsync<T>(
        string url,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var text = await GetStringAsync(url, ct, timeout).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }
}
