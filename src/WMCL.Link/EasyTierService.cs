using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.Link;

/// <summary>
/// 手动 EasyTier 联机：下载官方 core，使用网络名/密钥 + 公共中继建立虚拟网络。
/// 注意：这不是 PCL2 官方房间服务（开源版无法获取官方房间配置）。
/// </summary>
public sealed class EasyTierService : IDisposable
{
    private const string LatestRelease =
        "https://api.github.com/repos/EasyTier/EasyTier/releases/latest";

    public static readonly IReadOnlyList<string> DefaultPeers =
    [
        "tcp://public.easytier.top:11010",
        "tcp://public2.easytier.cn:54321",
        "https://etnode.zkitefly.eu.org/node1",
        "https://etnode.zkitefly.eu.org/node2"
    ];

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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public bool IsRunning => _process is { HasExited: false };
    public string CorePath { get; private set; } = "";
    public string CliPath { get; private set; } = "";

    public event Action<string>? LogReceived;

    public async Task<IReadOnlyList<string>> FetchPublicNodesAsync(
        CancellationToken ct = default)
    {
        var urls = new[]
        {
            "https://uptime.easytier.cn/api/nodes?page=1&per_page=1000",
            "https://easytier.meloong.com/?page=1&per_page=1000"
        };
        foreach (var url in urls)
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var nodes = EasyTierNodeList.Parse(json);
                if (nodes.Count > 0) return nodes;
            }
            catch
            {
                // 尝试下一个源，最后回退内置节点
            }
        }
        return DefaultPeers;
    }

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
        string host;
        int port;
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                string.IsNullOrWhiteSpace(uri.Host))
                return new PeerTestResult(peer, false, 0, "无效的 HTT(S) 节点地址");
            host = uri.Host;
            port = uri.IsDefaultPort
                ? (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80)
                : uri.Port;
        }
        else
        {
            if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                value = value["tcp://".Length..];
            else if (value.Contains("://", StringComparison.Ordinal))
                return new PeerTestResult(peer, false, 0, "仅测试 tcp:// 或 https:// 节点");
            (host, port) = ServerPingService.SplitAddress(value);
        }
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

        var directory = Path.Combine(WmclPaths.CacheDirectory, "easytier", tag);
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

    public Task StartAsync(
        string roomCode,
        string peer,
        CancellationToken ct = default,
        bool latencyFirst = false)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            throw new InvalidOperationException("房间码不能为空。");
        // 房间码按不区分大小写处理，避免 ABC / abc 进入不同网络。
        var normalizedRoom = roomCode.Trim().ToUpperInvariant();
        var secret = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("WMCL-EasyTier:" + normalizedRoom)))[..32];
        return StartCoreAsync("wmcl-" + normalizedRoom, secret, peer, ct, latencyFirst, privateMode: true);
    }

    /// <summary>
    /// 加入外部 EasyTier 网络（例如 Astral 房间）：网络名与密钥由对方给出，不使用 WMCL 房间码派生。
    /// </summary>
    public Task StartExternalAsync(
        string networkName,
        string networkSecret,
        string peer,
        string? virtualIp = null,
        CancellationToken ct = default,
        bool latencyFirst = false,
        string? hostname = null)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            throw new InvalidOperationException("网络名不能为空。");
        return StartCoreAsync(
            networkName.Trim(), networkSecret, peer, ct, latencyFirst,
            privateMode: false, virtualIp: virtualIp, hostname: hostname);
    }

    private async Task StartCoreAsync(
        string networkName,
        string networkSecret,
        string peer,
        CancellationToken ct,
        bool latencyFirst,
        bool privateMode,
        string? virtualIp = null,
        string? hostname = null)
    {
        if (IsRunning) throw new InvalidOperationException("EasyTier 已在运行。");
        if (!File.Exists(CorePath))
            throw new InvalidOperationException("EasyTier 尚未准备好。");
        var elevate = false;
        try
        {
            EnsureTunAvailable();
        }
        catch (InvalidOperationException) when (OperatingSystem.IsLinux() && !ElevationSuppressed)
        {
            // 优先复用 easytier-core 上已授予的 CAP_NET_ADMIN：授权一次后不再弹密码框。
            if (CoreHasNetAdminCapability(CorePath))
            {
                elevate = false;
            }
            else if (PkexecAvailable() && FindOnPath("setcap") is not null &&
                     await GrantCoreCapabilityAsync(CorePath, ct).ConfigureAwait(false))
            {
                // 刚刚授权成功，后续以普通权限启动即可
                elevate = false;
            }
            else if (PkexecAvailable())
            {
                // 授权失败（用户取消/无 setcap）时退回每次提权启动
                elevate = true;
            }
            else
            {
                throw;
            }
        }

        _rpcPort = FindFreePort();
        var listener = FindFreePort();

        var startInfo = new ProcessStartInfo(elevate ? "pkexec" : CorePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(CorePath)!
        };
        if (elevate) startInfo.ArgumentList.Add(CorePath);
        foreach (var argument in new[]
                 {
                     "--network-name", networkName,
                     "--network-secret", networkSecret,
                     "--listeners", listener.ToString(),
                     "--rpc-portal", $"127.0.0.1:{_rpcPort}",
                     "-d",
                     "--hostname", string.IsNullOrWhiteSpace(hostname)
                         ? "wmcl-" + Environment.MachineName.ToLowerInvariant()
                         : hostname.Trim(),
                     "--private-mode", privateMode ? "true" : "false",
                     "--multi-thread",
                     "--enable-kcp-proxy",
                     "--compression", "zstd"
                 })
            startInfo.ArgumentList.Add(argument);
        if (latencyFirst) startInfo.ArgumentList.Add("--latency-first");
        if (!string.IsNullOrWhiteSpace(virtualIp))
        {
            startInfo.ArgumentList.Add("--ipv4");
            startInfo.ArgumentList.Add(virtualIp.Trim());
        }
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
        WritePidFile(process.Id);
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

    internal static bool PkexecAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, "pkexec")));
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>解析 getcap 输出，判断是否已带 cap_net_admin。</summary>
    internal static bool HasNetAdminCapability(string? getcapOutput) =>
        getcapOutput is not null &&
        getcapOutput.Contains("cap_net_admin", StringComparison.Ordinal);

    /// <summary>检查 easytier-core 是否已被授予 CAP_NET_ADMIN（授权一次后长期有效）。</summary>
    public static bool CoreHasNetAdminCapability(string corePath)
    {
        if (!OperatingSystem.IsLinux()) return true;
        var getcap = FindOnPath("getcap");
        if (getcap is null) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(getcap, [corePath])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return HasNetAdminCapability(output);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 通过 pkexec 给 easytier-core 打上 cap_net_admin（只需一次），
    /// 之后启动联机不再弹密码框。
    /// </summary>
    /// <summary>测试/自动化环境下不请求提权，避免弹出 pkexec 密码框。</summary>
    private static bool ElevationSuppressed =>
        string.Equals(Environment.GetEnvironmentVariable("WMCL_NO_ELEVATION"), "1", StringComparison.Ordinal);

    public static async Task<bool> GrantCoreCapabilityAsync(string corePath, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux() || ElevationSuppressed) return false;
        var setcap = FindOnPath("setcap");
        var pkexec = FindOnPath("pkexec");
        if (setcap is null || pkexec is null) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(pkexec, ["setcap", "cap_net_admin+ep", corePath])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 && CoreHasNetAdminCapability(corePath);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureTunAvailable()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (!File.Exists("/dev/net/tun"))
            throw new InvalidOperationException(
                "当前 Linux 没有 /dev/net/tun，EasyTier TUN 不可用。"
                + "请安装/启用 TUN（tun 内核模块）或以具备 CAP_NET_ADMIN 的方式运行；"
                + "也可以直接切换到陶瓦联机。");

        // 文件存在不代表内核 TUN 可用：没有 CAP_NET_ADMIN 或模块异常时
        // TUNSETIFF 会返回 ENODEV/EPERM。这里实际探测一次，避免启动后才超时。
        var fd = NativeOpen("/dev/net/tun", 2 /* O_RDWR */);
        if (fd < 0)
        {
            var openError = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                "EasyTier TUN 不可用：" + TunError(openError) + "。可以切换到陶瓦联机。");
        }
        var buffer = Marshal.AllocHGlobal(64);
        try
        {
            for (var i = 0; i < 64; i++) Marshal.WriteByte(buffer, i, 0);
            var name = "wmcltun" + Environment.ProcessId % 10000;
            var nameBytes = Encoding.ASCII.GetBytes(name);
            Marshal.Copy(nameBytes, 0, buffer, Math.Min(nameBytes.Length, 15));
            // Linux ifreq: name[16] + flags (short) at offset 16.
            Marshal.WriteInt16(buffer, 16, 0x0001 | 0x1000); // IFF_TUN | IFF_NO_PI
            const ulong TunSetIff = 0x400454ca;
            var result = NativeIoctl(fd, TunSetIff, buffer);
            if (result < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException(
                    "EasyTier TUN 不可用：" + TunError(error) + "。可以切换到陶瓦联机。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            NativeClose(fd);
        }
    }

    private static string TunError(int errno) => errno switch
    {
        19 => "TUN 内核模块不可用/未加载（ENODEV）",
        1 or 13 => "缺少 CAP_NET_ADMIN 或设备权限",
        _ => "系统 TUN 不可用（errno " + errno + "）"
    };

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, IntPtr arg);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static int NativeOpen(string path, int flags) => open(path, flags);
    private static int NativeIoctl(int fd, ulong request, IntPtr arg) => ioctl(fd, request, arg);
    private static void NativeClose(int fd) => close(fd);

    /// <summary>
    /// 结束此前被强杀后残留的 easytier-core（带 WMCL 主机名前缀）。
    /// 这些孤儿会一直占用 TUN 与 10.126.126.x 网段，导致后续组网冲突。
    /// </summary>
    public static int CleanupOrphans()
    {
        var killed = 0;
        try
        {
            // 上次运行记录下来的 PID（自定义节点名时也能定位）
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out var recorded))
            {
                try
                {
                    using var process = Process.GetProcessById(recorded);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                    killed++;
                }
                catch
                {
                    // 已退出
                }
                try { File.Delete(PidFile); } catch { }
            }
            if (OperatingSystem.IsLinux())
            {
                foreach (var directory in Directory.EnumerateDirectories("/proc"))
                {
                    var name = Path.GetFileName(directory);
                    if (!int.TryParse(name, out var pid) || pid == Environment.ProcessId) continue;
                    string cmdline;
                    try
                    {
                        cmdline = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ');
                    }
                    catch
                    {
                        continue;
                    }
                    if (!cmdline.Contains("easytier-core", StringComparison.Ordinal)) continue;
                    if (!cmdline.Contains("--hostname wmcl-", StringComparison.Ordinal)) continue;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                        killed++;
                    }
                    catch (Exception exception)
                    {
                        // 进程已退出，或当前环境不允许结束其他会话的进程
                        System.Diagnostics.Trace.WriteLine($"清理残留 EasyTier 失败（pid {pid}）：{exception.Message}");
                    }
                }
            }
        }
        catch
        {
            // 清理失败不影响启动
        }
        return killed;
    }

    private static string PidFile => Path.Combine(WmclPaths.DataDirectory, "easytier.pid");

    private static void WritePidFile(int pid)
    {
        try
        {
            Directory.CreateDirectory(WmclPaths.DataDirectory);
            File.WriteAllText(PidFile, pid.ToString());
        }
        catch
        {
            // 记录失败不影响运行
        }
    }

    public void Stop()
    {
        try { File.Delete(PidFile); } catch { }
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
