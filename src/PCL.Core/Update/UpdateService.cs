using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PCL.Core.Http;
using PCL.Core.Jobs;
using PCL.Core.Logging;

namespace PCL.Core.Update;

public sealed record UpdateInfo(
    string Version,
    string Url,
    string? Sha256 = null,
    string? Notes = null,
    long? Size = null);

/// <summary>
/// 自研更新清单（latest.json）。原版 PCL 开源代码中更新逻辑为空实现，因此这里定义自己的格式：
/// { "version": "0.1.1", "url": "https://.../PCL-linux-x64.tar.gz", "sha256": "...", "notes": "..." }
/// </summary>
public static class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static UpdateInfo? ParseManifest(string json) =>
        JsonSerializer.Deserialize<UpdateInfo>(json, JsonOptions);

    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParseVersion(candidate, out var candidateVersion) ||
            !TryParseVersion(current, out var currentVersion))
            return false;
        return candidateVersion > currentVersion;
    }

    public static async Task<UpdateInfo?> CheckAsync(
        string manifestUrl,
        string currentVersion,
        CancellationToken ct = default)
    {
        var json = await PCL.Core.Http.Http
            .GetStringAsync(manifestUrl, ct, TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        var info = ParseManifest(json);
        if (info is null || string.IsNullOrWhiteSpace(info.Url)) return null;
        return IsNewer(info.Version, currentVersion) ? info : null;
    }

    public static async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var directory = Path.Combine(PclPaths.CacheDirectory, "updates");
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(new Uri(info.Url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "PCL-update.tar.gz";
        var destination = Path.Combine(directory, fileName);
        var part = destination + ".part";

        using var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");

        using var response = await client
            .GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.Size ?? 0;
        await using var output = new FileStream(
            part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[81920];
        long downloaded = 0;
        var clock = Stopwatch.StartNew();
        long lastReport = 0;

        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;

            if (clock.ElapsedMilliseconds - lastReport >= 200)
            {
                var speed = clock.ElapsedMilliseconds > 0
                    ? downloaded * 1000 / clock.ElapsedMilliseconds
                    : 0;
                progress?.Report(new JobProgress(
                    "下载更新",
                    total > 0 ? (double)downloaded / total : null,
                    speed,
                    null,
                    $"{downloaded / 1048576.0:0.0} / {total / 1048576.0:0.0} MB"));
                lastReport = clock.ElapsedMilliseconds;
            }
        }

        await output.FlushAsync(ct).ConfigureAwait(false);
        await output.DisposeAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            await using var check = File.OpenRead(part);
            var actual = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException(
                    $"更新包校验失败：期望 {info.Sha256}，实际 {actual}");
            }
        }

        File.Move(part, destination, overwrite: true);
        Log.Info($"更新包已下载：{destination}");
        return destination;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }
}
