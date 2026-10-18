using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using PCL.Core;
using PCL.Core.Jobs;

namespace PCL.App.Services;

/// <summary>
/// Terracotta（陶瓦联机）进程封装。通过 HMCL 模式启动并调用本地 HTTP API：
/// 房主 /state/scanning，客人 /state/guesting，/state 查询状态。
/// Terracotta 基于 EasyTier，按 AGPL-3.0 发布；本程序仅通过进程接口调用未修改的二进制。
/// </summary>
public sealed class TerracottaService : IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/burningtnt/Terracotta/releases/latest";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public TerracottaService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    private Process? _process;
    private string? _binary;
    private int _port;
    private string? _version;

    public bool IsRunning => _process is { HasExited: false } && _port > 0;
    public int Port => _port;
    public string? Version => _version;

    public async Task EnsureAsync(
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_binary is not null && File.Exists(_binary)) return;
        var overridePath = Environment.GetEnvironmentVariable("PCL_TERRACOTTA_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            _binary = overridePath;
            _version = "local";
            return;
        }

        progress?.Report(new JobProgress("获取陶瓦联机版本", 0.02));
        using var response = await _http.GetAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var assetName = SelectAssetName();
        var assetUrl = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? "",
                Url = asset.GetProperty("browser_download_url").GetString() ?? ""
            })
            .FirstOrDefault(asset => asset.Name.Contains(assetName, StringComparison.OrdinalIgnoreCase))
            ?.Url;
        if (string.IsNullOrWhiteSpace(assetUrl))
            throw new InvalidOperationException("没有找到适用于当前平台的陶瓦发行包。");

        var directory = Path.Combine(PclPaths.CacheDirectory, "terracotta", tag);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory,
            OperatingSystem.IsWindows() ? "terracotta.exe" : "terracotta");
        if (!File.Exists(target))
        {
            progress?.Report(new JobProgress("下载陶瓦联机", 0.1, Detail: assetName));
            var archive = Path.Combine(directory, "package.tar.gz");
            await using (var input = await _http.GetStreamAsync(assetUrl, ct).ConfigureAwait(false))
            await using (var output = File.Create(archive))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("解压陶瓦联机", 0.2));
            var extract = Path.Combine(directory, "extract");
            if (Directory.Exists(extract)) Directory.Delete(extract, recursive: true);
            Directory.CreateDirectory(extract);
            await using (var compressed = File.OpenRead(archive))
            await using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                await TarFile.ExtractToDirectoryAsync(gzip, extract, overwriteFiles: true, ct)
                    .ConfigureAwait(false);
            var executable = Directory.EnumerateFiles(extract, "*", SearchOption.AllDirectories)
                .FirstOrDefault(file => Path.GetFileName(file)
                    .StartsWith("terracotta", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("陶瓦发行包中没有找到可执行文件。");
            File.Copy(executable, target, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Delete(archive);
            Directory.Delete(extract, recursive: true);
        }
        _binary = target;
        _version = tag;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        await EnsureAsync(null, ct).ConfigureAwait(false);
        var portFile = Path.Combine(Path.GetTempPath(),
            "pcl-terracotta-" + Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo(_binary!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--hmcl");
        startInfo.ArgumentList.Add(portFile);
        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException("无法启动陶瓦联机进程。");
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException("陶瓦联机进程提前退出。");
            if (File.Exists(portFile))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(portFile, ct)
                        .ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("port", out var portElement) &&
                        portElement.TryGetInt32(out var port) && port > 0)
                    {
                        _port = port;
                        File.Delete(portFile);
                        return;
                    }
                }
                catch (JsonException)
                {
                    // 文件可能仍在写入
                }
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("陶瓦联机启动超时（未收到端口）。");
    }

    public async Task<JsonElement> GetStateAsync(CancellationToken ct = default)
    {
        if (_port <= 0) throw new InvalidOperationException("陶瓦联机尚未启动。");
        using var response = await _http.GetAsync(
            $"http://127.0.0.1:{_port}/state", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    public async Task<string> HostAsync(
        string? room,
        string? player = null,
        CancellationToken ct = default)
    {
        await PostStateAsync("scanning", room, player, ct).ConfigureAwait(false);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var state = await GetStateAsync(ct).ConfigureAwait(false);
            var name = state.GetProperty("state").GetString();
            if (name == "host-ok") return state.GetProperty("room").GetString() ?? room ?? "";
            if (name == "exception")
                throw new InvalidOperationException("陶瓦房主进入异常状态（" + ExceptionName(state) + "）。");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("陶瓦房主启动超时；请确认已打开 Minecraft 局域网世界。");
    }

    public async Task<string> JoinAsync(
        string room,
        string? player = null,
        CancellationToken ct = default)
    {
        await PostStateAsync("guesting", room, player, ct).ConfigureAwait(false);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var state = await GetStateAsync(ct).ConfigureAwait(false);
            var name = state.GetProperty("state").GetString();
            if (name == "guest-ok") return state.GetProperty("url").GetString() ?? "127.0.0.1";
            if (name == "exception")
                throw new InvalidOperationException("陶瓦客人进入异常状态（" + ExceptionName(state) + "）。");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("陶瓦连接超时；请确认房主已创建房间且房间码一致。");
    }

    public async Task StopAsync()
    {
        if (_port > 0)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _http.GetAsync($"http://127.0.0.1:{_port}/panic?peaceful=true", cts.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 进程可能已退出
            }
        }
        try
        {
            if (_process is { HasExited: false })
            {
                await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            _process?.Kill(entireProcessTree: true);
        }
        _process?.Dispose();
        _process = null;
        _port = 0;
    }

    private async Task PostStateAsync(
        string endpoint, string? room, string? player, CancellationToken ct)
    {
        var url = $"http://127.0.0.1:{_port}/state/{endpoint}";
        if (!string.IsNullOrWhiteSpace(room))
            url += "?room=" + Uri.EscapeDataString(room);
        if (!string.IsNullOrWhiteSpace(player))
            url += (url.Contains('?') ? "&" : "?") + "player=" + Uri.EscapeDataString(player);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string ExceptionName(JsonElement state) => state.TryGetProperty("type", out var type)
        ? type.GetInt32().ToString()
        : "unknown";

    private static string SelectAssetName()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "armv7",
            _ => "x86_64"
        };
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "linux";
        return $"{os}-{arch}-pkg.tar.gz";
    }

    public void Dispose()
    {
        _process?.Dispose();
        _http.Dispose();
    }
}
