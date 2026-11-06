using System.Collections.Concurrent;
using System.Net.Http;

namespace WMCL.App.Services;

/// <summary>
/// 远程图标缓存与预取。RemoteImage 用它避免重复下载，搜索结果页可以在结束加载动画前
/// 先把前 20 个图标抓完，保证遮罩消失时列表已经带图。
/// </summary>
public static class RemoteImageCache
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
    private const int MaxCacheEntries = 300;

    static RemoteImageCache() =>
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");

    public static bool TryGet(string url, out byte[] bytes) => Cache.TryGetValue(url, out bytes!);

    /// <summary>测试专用：预置缓存内容，避免真实网络请求。</summary>
    internal static void Seed(string url, byte[] bytes) => Cache[url] = bytes;

    public static async Task<byte[]?> GetAsync(string url, CancellationToken ct)
    {
        if (Cache.TryGetValue(url, out var cached)) return cached;
        try
        {
            var bytes = await Client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (Cache.Count >= MaxCacheEntries && Cache.Keys.FirstOrDefault() is { } oldest)
                Cache.TryRemove(oldest, out _);
            Cache[url] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>并发预取一批图标；超时或失败都静默跳过，不阻塞页面加载。</summary>
    public static async Task PrefetchAsync(
        IEnumerable<string?> urls, TimeSpan timeout, CancellationToken ct = default)
    {
        var pending = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.Ordinal)
            .Where(url => !Cache.ContainsKey(url))
            .ToArray();
        if (pending.Length == 0) return;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        using var gate = new SemaphoreSlim(6);
        var tasks = pending.Select(async url =>
        {
            try
            {
                await gate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }
            try
            {
                await GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // 预取是尽力而为
        }
    }
}
