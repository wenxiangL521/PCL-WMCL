using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WMCL.Core.Http;

/// <summary>全局 HttpClient 与常用请求封装。</summary>
public static class Http
{
    // 注意：不是 readonly——代理设置变化时通过 ResetClient 整体替换。
    private static Lazy<HttpClient> _lazyClient = new(CreateClient);

    public static HttpClient Client => _lazyClient.Value;

    /// <summary>
    /// 重置全局 HttpClient。代理配置（环境变量）只在 SocketsHttpHandler 构造时
    /// 读取一次，因此 ProxyManager.Apply 修改代理后必须调用本方法，
    /// 否则已创建的客户端会继续使用旧代理（或直连）直到进程重启。
    /// 进行中的请求不受影响（旧实例被其异步状态机引用，完成后由 GC 回收；
    /// 连接池有 10 分钟生命周期，闲置连接会自然关闭）。
    /// </summary>
    public static void ResetClient()
    {
        if (_lazyClient.IsValueCreated)
            Interlocked.Exchange(ref _lazyClient, new Lazy<HttpClient>(CreateClient));
    }

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
