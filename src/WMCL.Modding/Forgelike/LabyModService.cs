using System.Net;
using System.Text.Json;
using WMCL.Core.Jobs;

namespace WMCL.Modding.Forgelike;

public sealed record LabyModVersionInfo(
    string Channel,
    string Version,
    string InstallerUrl,
    string InstallerFileName)
{
    public string DisplayName => Channel == "production" ? "正式版" : "快照版";
    public string Detail => $"LabyMod {Version}";
}

/// <summary>
/// CE 的 LabyMod 下载页固定查询 production/snapshot 两个 manifest，
/// 只保存对应通道的 Java 安装器，不在启动器内创建实例。
/// </summary>
public sealed class LabyModService
{
    private const string ProductionManifest =
        "https://releases.r2.labymod.net/api/v1/manifest/production/latest.json";
    private const string SnapshotManifest =
        "https://releases.r2.labymod.net/api/v1/manifest/snapshot/latest.json";
    private readonly HttpClient _http;

    public LabyModService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
    }

    public async Task<IReadOnlyList<LabyModVersionInfo>> GetVersionsAsync(
        CancellationToken ct = default)
    {
        var production = ReadManifest(
            await _http.GetStringAsync(ProductionManifest, ct).ConfigureAwait(false),
            "production");
        var snapshot = ReadManifest(
            await _http.GetStringAsync(SnapshotManifest, ct).ConfigureAwait(false),
            "snapshot");
        return [production, snapshot];
    }

    public static LabyModVersionInfo ReadManifest(string json, string channel)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.TryGetProperty("labyModVersion", out var versionElement)
            ? versionElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException($"LabyMod {channel} manifest 缺少 labyModVersion。");
        return new LabyModVersionInfo(
            channel,
            version,
            $"https://releases.labymod.net/api/v1/installer/{channel}/java",
            $"LabyMod4{char.ToUpperInvariant(channel[0])}{channel[1..]}Installer.jar");
    }

    public async Task<string> DownloadInstallerAsync(
        LabyModVersionInfo version,
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
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    progress?.Report(new JobProgress("下载 LabyMod 安装器",
                        total > 0 ? (double)readTotal / total : null, null, null, version.InstallerFileName));
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(part, destination, true);
            progress?.Report(new JobProgress("LabyMod 安装器已下载", 1));
            return destination;
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }
}
