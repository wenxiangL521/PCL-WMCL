using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Minecraft;

namespace PCL.App.Services;

/// <summary>
/// 手动 EasyTier 联机：下载官方 core，使用网络名/密钥 + 公共中继建立虚拟网络。
/// 注意：这不是 PCL2 官方房间服务（开源版无法获取官方房间配置）。
/// </summary>
public sealed class EasyTierService : IDisposable
{
    private const string LatestRelease =
        "https://api.github.com/repos/EasyTier/EasyTier/releases/latest";

    private readonly HttpClient _http;
    private Process? _process;
    private int _rpcPort;

    public EasyTierService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public bool IsRunning => _process is { HasExited: false };
    public string CorePath { get; private set; } = "";
    public string CliPath { get; private set; } = "";

    public event Action<string>? LogReceived;

    public sealed record NodeInfo(
        string? VirtualIp,
        string? PublicIp,
        int? NatType,
        string? Hostname,
        long? PeerId);

    public sealed record PeerTestResult(
        string Address,
        bool Success,
        long LatencyMs,
        string? Error);

    public static async Task<PeerTestResult> TestPeerAsync(
        string peer,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var value = peer.Trim();
        if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            value = value["tcp://".Length..];
        else if (value.Contains("://", StringComparison.Ordinal))
            return new PeerTestResult(peer, false, 0, "仅测试 tcp:// 中继");
        var (host, port) = ServerPingService.SplitAddress(value);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(4));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new PeerTestResult(peer, true, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PeerTestResult(peer, false, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }

    public async Task EnsureCoreAsync(
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (File.Exists(CorePath) && File.Exists(CliPath)) return;
        progress?.Report(new JobProgress("获取 EasyTier 版本", 0.02));
        var json = await _http.GetStringAsync(LatestRelease, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var asset = SelectAsset(root);
        if (asset is null)
            throw new InvalidOperationException("没有找到适用于当前平台的 EasyTier 发布包。");

        var directory = Path.Combine(PclPaths.CacheDirectory, "easytier", tag);
        Directory.CreateDirectory(directory);
        CorePath = Path.Combine(directory, ExecutableName("easytier-core"));
        CliPath = Path.Combine(directory, ExecutableName("easytier-cli"));
        if (File.Exists(CorePath) && File.Exists(CliPath)) return;

        var zip = Path.Combine(directory, "easytier.zip");
        progress?.Report(new JobProgress("下载 EasyTier", 0.1));
        await using (var source = await _http.GetStreamAsync(asset, ct).ConfigureAwait(false))
        await using (var destination = File.Create(zip))
            await source.CopyToAsync(destination, ct).ConfigureAwait(false);

        progress?.Report(new JobProgress("解压 EasyTier", 0.7));
        var temp = Path.Combine(directory, "extract-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zip, temp);
        var core = Directory.EnumerateFiles(temp, ExecutableName("easytier-core"), SearchOption.AllDirectories)
            .FirstOrDefault()
                   ?? throw new InvalidDataException("EasyTier 压缩包中缺少 easytier-core。");
        var cli = Directory.EnumerateFiles(temp, ExecutableName("easytier-cli"), SearchOption.AllDirectories)
            .FirstOrDefault()
                  ?? throw new InvalidDataException("EasyTier 压缩包中缺少 easytier-cli。");
        File.Copy(core, CorePath, overwrite: true);
        File.Copy(cli, CliPath, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(CorePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(CliPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        Directory.Delete(temp, recursive: true);
        File.Delete(zip);
    }

    public async Task StartAsync(
        string roomCode,
        string peer,
        CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException("EasyTier 已在运行。");
        if (string.IsNullOrWhiteSpace(roomCode))
            throw new InvalidOperationException("房间码不能为空。");
        if (!File.Exists(CorePath))
            throw new InvalidOperationException("EasyTier 尚未准备好。");

        var networkName = "pcl-" + roomCode.Trim();
        var secret = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("PCL-Linux-EasyTier:" + roomCode.Trim())))[..32];
        _rpcPort = FindFreePort();
        var listener = FindFreePort();

        var startInfo = new ProcessStartInfo(CorePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(CorePath)!
        };
        foreach (var argument in new[]
                 {
                     "--network-name", networkName,
                     "--network-secret", secret,
                     "--listeners", listener.ToString(),
                     "--rpc-portal", $"127.0.0.1:{_rpcPort}",
                     "-d",
                     "--hostname", "pcl-" + Environment.MachineName.ToLowerInvariant(),
                     "--private-mode", "true"
                 })
            startInfo.ArgumentList.Add(argument);
        var peers = peer.Split([',', ';', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var item in peers)
        {
            startInfo.ArgumentList.Add("--peers");
            startInfo.ArgumentList.Add(item);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(e.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("无法启动 EasyTier。");
        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await Task.Delay(300, ct).ConfigureAwait(false);
        if (process.HasExited)
        {
            var exit = process.ExitCode;
            _process = null;
            throw new InvalidOperationException(
                $"EasyTier 启动后立即退出（退出码 {exit}），通常是缺少 TUN/管理员权限。");
        }
    }

    public async Task<NodeInfo?> TryGetNodeInfoAsync(CancellationToken ct = default)
    {
        var json = await RunCliAsync(["-o", "json", "-p", $"127.0.0.1:{_rpcPort}", "node"], ct)
            .ConfigureAwait(false);
        return ParseNodeInfo(json);
    }

    public async Task<string> TryGetPeersAsync(CancellationToken ct = default) =>
        await RunCliAsync(["-p", $"127.0.0.1:{_rpcPort}", "peer"], ct).ConfigureAwait(false);

    public static NodeInfo? ParseNodeInfo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? virtualIp = root.TryGetProperty("ipv4_addr", out var ip)
                ? ip.GetString()
                : null;
            string? hostname = root.TryGetProperty("hostname", out var host)
                ? host.GetString()
                : null;
            long? peerId = root.TryGetProperty("peer_id", out var peer) &&
                           peer.TryGetInt64(out var id)
                ? id
                : null;
            string? publicIp = null;
            int? natType = null;
            if (root.TryGetProperty("stun_info", out var stun) &&
                stun.ValueKind == JsonValueKind.Object)
            {
                if (stun.TryGetProperty("public_ip", out var publicIps) &&
                    publicIps.ValueKind == JsonValueKind.Array)
                {
                    var first = publicIps.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.String)
                        publicIp = first.GetString();
                }
                if (stun.TryGetProperty("udp_nat_type", out var nat) &&
                    nat.TryGetInt32(out var natValue))
                    natType = natValue;
            }
            return new NodeInfo(
                string.IsNullOrWhiteSpace(virtualIp) ? null : virtualIp,
                publicIp, natType, hostname, peerId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> RunCliAsync(
        IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (!IsRunning || !File.Exists(CliPath)) return "";
        try
        {
            var startInfo = new ProcessStartInfo(CliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return "";
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(ct)).ConfigureAwait(false);
            var result = await stdout.ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(result) ? await stderr.ConfigureAwait(false) : result;
        }
        catch
        {
            return "";
        }
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        process?.Dispose();
    }

    public void Dispose() => Stop();

    private static string? SelectAsset(JsonElement root)
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        var arch = architecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            _ => "x86_64"
        };
        var platform = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos" : "linux";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.Contains($"easytier-{platform}-{arch}", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        }
        return null;
    }

    private static string ExecutableName(string name) =>
        OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
