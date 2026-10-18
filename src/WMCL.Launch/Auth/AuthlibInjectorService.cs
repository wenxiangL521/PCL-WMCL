using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Jobs;

namespace WMCL.Launch.Auth;

/// <summary>
/// 下载 authlib-injector，并作为 -javaagent 注入第三方认证服务器。
/// 对应 PCL2 的 authlib-injector latest.json + 本地缓存。
/// </summary>
public sealed class AuthlibInjectorService
{
    private const string PrimaryLatest =
        "https://authlib-injector.yushi.moe/artifact/latest.json";
    private const string MirrorLatest =
        "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    public AuthlibInjectorService(HttpClient? http = null, string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? WmclPaths.CacheDirectory;
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
    }

    public async Task<string> EnsureAsync(
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("获取 authlib-injector", 0.01));
        var latest = await GetLatestAsync(ct).ConfigureAwait(false);
        var directory = Path.Combine(_cacheDirectory, "authlib-injector");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"authlib-injector-{latest.Version}.jar");

        if (!IsZip(target) || !HashMatches(target, latest.Sha256))
        {
            progress?.Report(new JobProgress("下载 authlib-injector", 0.05));
            using var response = await _http.GetAsync(latest.DownloadUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var destination = File.Create(target))
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
        }

        if (!IsZip(target))
            throw new InvalidDataException("下载的 authlib-injector 不是有效 jar。");
        if (!HashMatches(target, latest.Sha256))
            throw new InvalidDataException("authlib-injector SHA256 校验失败。");
        return target;
    }

    private async Task<Latest> GetLatestAsync(CancellationToken ct)
    {
        Exception? first = null;
        foreach (var url in new[] { PrimaryLatest, MirrorLatest })
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var download = root.GetProperty("download_url").GetString();
                var version = root.GetProperty("version").GetString();
                string? sha256 = null;
                if (root.TryGetProperty("checksums", out var checksums) &&
                    checksums.TryGetProperty("sha256", out var hash))
                    sha256 = hash.GetString();
                if (string.IsNullOrWhiteSpace(download) || string.IsNullOrWhiteSpace(version))
                    throw new InvalidDataException("authlib-injector latest.json 缺少字段。");
                return new Latest(download, version, sha256);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                first ??= ex;
            }
        }
        throw new InvalidOperationException("无法获取 authlib-injector 下载信息。", first);
    }

    private static bool HashMatches(string file, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || !File.Exists(file)) return true;
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZip(string file)
    {
        if (!File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
    }

    private sealed record Latest(string DownloadUrl, string Version, string? Sha256);
}
