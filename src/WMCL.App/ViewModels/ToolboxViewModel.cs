using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class ToolboxViewModel : ViewModelBase
{
    private readonly AppServices _services;
    // 使用全局客户端：属性每次求值，ProxyManager.Apply 重置后自动采用新代理；
    // 自建实例会在构造时固化代理设置、运行时不跟随变化。超时由各请求的 CTS 控制。
    private HttpClient Http => WMCL.Core.Http.Http.Client;
    private CancellationTokenSource? _downloadCts;

    public ToolboxViewModel(AppServices services)
    {
        _services = services;
        LoadLaunchCount();
    }

    public string LogsDirectory => WmclPaths.LogsDirectory;
    public string GameDirectory => _services.Settings.GameDirectory;
    public string ConfigDirectory => WmclPaths.ConfigDirectory;

    [ObservableProperty]
    private string _status = "";

    // ── 自定义下载 ──

    [ObservableProperty]
    private string _customDownloadUrl = "";

    [ObservableProperty]
    private string _customDownloadFileName = "";

    [ObservableProperty]
    private string _customDownloadFolder =
        Path.Combine(WmclPaths.DataDirectory, "downloads");

    [ObservableProperty]
    private string _customDownloadStatus = "输入 URL 后下载到指定目录";

    [ObservableProperty]
    private bool _isCustomDownloading;

    [ObservableProperty]
    private double _customDownloadProgress;

    // ── 服务器查询 ──

    [ObservableProperty]
    private string _serverAddress = "";

    [ObservableProperty]
    private string _serverPingStatus = "输入服务器地址后 Ping";

    [ObservableProperty]
    private bool _isServerPinging;

    [ObservableProperty]
    private IReadOnlyList<MotdRun>? _serverMotdRuns;

    public bool HasServerMotdRuns => ServerMotdRuns is { Count: > 0 };

    partial void OnServerMotdRunsChanged(IReadOnlyList<MotdRun>? value) =>
        OnPropertyChanged(nameof(HasServerMotdRuns));

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _serverFavicon;

    public bool HasServerFavicon => ServerFavicon is not null;

    partial void OnServerFaviconChanged(Avalonia.Media.Imaging.Bitmap? value) =>
        OnPropertyChanged(nameof(HasServerFavicon));

    public bool CanPingServer => !IsServerPinging && !string.IsNullOrWhiteSpace(ServerAddress);

    partial void OnIsServerPingingChanged(bool value) =>
        OnPropertyChanged(nameof(CanPingServer));

    partial void OnServerAddressChanged(string value) =>
        OnPropertyChanged(nameof(CanPingServer));

    public bool CanStartCustomDownload =>
        !IsCustomDownloading && !string.IsNullOrWhiteSpace(CustomDownloadUrl);

    partial void OnIsCustomDownloadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanStartCustomDownload));

    partial void OnCustomDownloadUrlChanged(string value) =>
        OnPropertyChanged(nameof(CanStartCustomDownload));

    // ── 清理垃圾 ──

    [ObservableProperty]
    private string _junkStatus = "尚未扫描";

    [ObservableProperty]
    private double _junkProgress;

    [ObservableProperty]
    private bool _isCleaningJunk;

    public bool CanCleanJunk => !IsCleaningJunk;

    partial void OnIsCleaningJunkChanged(bool value) =>
        OnPropertyChanged(nameof(CanCleanJunk));

    // ── 启动计数 ──

    [ObservableProperty]
    private int _launchCount;

    [ObservableProperty]
    private string _launchCountStatus = "";

    // ── 创建快捷方式 ──

    [ObservableProperty]
    private string _shortcutStatus = "";

    // ── 皮肤预览 ──

    [ObservableProperty]
    private string _skinUrl = "https://crafatar.com/skins/";

    [ObservableProperty]
    private Bitmap? _skinPreview;

    public bool HasSkinPreview => SkinPreview is not null;

    partial void OnSkinPreviewChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(HasSkinPreview));

    // ── 抽奖 ──

    [ObservableProperty]
    private string _lotteryStatus = "点击抽奖";

    [ObservableProperty]
    private string _lotteryResult = "";

    // ── 下载测速 ──

    [ObservableProperty]
    private string _speedTestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    [ObservableProperty]
    private string _speedTestStatus = "点击开始测速";

    [ObservableProperty]
    private double _speedTestProgress;

    [ObservableProperty]
    private bool _isSpeedTesting;

    public bool CanStartSpeedTest => !IsSpeedTesting;

    partial void OnIsSpeedTestingChanged(bool value) =>
        OnPropertyChanged(nameof(CanStartSpeedTest));

    [RelayCommand]
    private async Task RunSpeedTestAsync()
    {
        if (!CanStartSpeedTest) return;
        IsSpeedTesting = true;
        SpeedTestProgress = 0;
        SpeedTestStatus = "正在测速…";
        try
        {
            if (!Uri.TryCreate(SpeedTestUrl.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                SpeedTestStatus = "请输入有效的 URL";
                return;
            }

            // 从多个镜像点下载，取平均值
            var results = new List<double>();
            var urls = new[]
            {
                SpeedTestUrl.Trim(),
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"
            };

            for (var i = 0; i < urls.Length; i++)
            {
                if (!Uri.TryCreate(urls[i], UriKind.Absolute, out var testUri) ||
                    testUri.Scheme is not ("http" or "https"))
                    continue;

                SpeedTestProgress = (i + 1) * 100.0 / urls.Length;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var response = await Http.GetAsync(
                        testUri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0;
                    if (total < 1024) continue;

                    await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[65536];
                    long read = 0;
                    var start = DateTime.UtcNow;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        read += count;
                        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3)) break;
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    if (elapsed > 0.1)
                        results.Add(read / elapsed / 1024 / 1024); // MB/s
                }
                catch { /* 跳过失败的测速点 */ }
            }

            if (results.Count > 0)
            {
                var avg = results.Average();
                SpeedTestStatus = $"平均速度：{avg:F2} MB/s（{results.Count} 个测速点）";
            }
            else
            {
                SpeedTestStatus = "测速失败：所有测速点均不可用";
            }
        }
        catch (Exception ex)
        {
            SpeedTestStatus = "测速失败：" + ex.Message;
        }
        finally
        {
            IsSpeedTesting = false;
            SpeedTestProgress = 100;
        }
    }

    // ── 镜像对比测速 ──

    public ObservableCollection<MirrorResult> MirrorResults { get; } = [];

    [ObservableProperty]
    private bool _isMirrorTesting;

    [ObservableProperty]
    private string _mirrorTestStatus = "点击开始镜像对比";

    public bool CanStartMirrorTest => !IsMirrorTesting;

    partial void OnIsMirrorTestingChanged(bool value) =>
        OnPropertyChanged(nameof(CanStartMirrorTest));

    [RelayCommand]
    private async Task RunMirrorTestAsync()
    {
        if (!CanStartMirrorTest) return;
        IsMirrorTesting = true;
        MirrorResults.Clear();
        MirrorTestStatus = "正在解析测试文件…";
        try
        {
            var mirrors = await ResolveMirrorTestTargetsAsync();

            foreach (var (name, url) in mirrors)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var response = await Http.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0;
                    if (total < 1024)
                    {
                        MirrorResults.Add(new MirrorResult(name, url, 0, "文件太小"));
                        continue;
                    }

                    await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[65536];
                    long read = 0;
                    var start = DateTime.UtcNow;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        read += count;
                        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3)) break;
                    }
                    var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    var speed = elapsed > 0 ? read / elapsed / 1024 / 1024 : 0;
                    MirrorResults.Add(new MirrorResult(name, url, speed, ""));
                }
                catch (Exception ex)
                {
                    MirrorResults.Add(new MirrorResult(name, url, 0, ex.Message));
                }
                OnPropertyChanged(nameof(MirrorResults));
            }

            var best = MirrorResults.Where(r => r.Speed > 0).OrderByDescending(r => r.Speed).FirstOrDefault();
            MirrorTestStatus = best is not null
                ? $"最快镜像：{best.Name}（{best.Speed:F2} MB/s）"
                : "所有镜像均不可用";
        }
        catch (Exception ex)
        {
            MirrorTestStatus = "镜像对比失败：" + ex.Message;
        }
        finally
        {
            IsMirrorTesting = false;
        }
    }

    /// <summary>
    /// 解析镜像测速目标。官方 piston-data 的 client.jar 直链包含完整
    /// SHA1 哈希，无法硬编码（此前硬编码的截断哈希是 404）；
    /// 这里从版本清单动态解析。解析失败时回退到两边的版本清单文件
    /// （官方与 BMCLAPI 均镜像该路径），保证对比功能始终可用。
    /// </summary>
    private async Task<List<(string Name, string Url)>> ResolveMirrorTestTargetsAsync()
    {
        const string manifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        const string bmclapiManifestUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var manifest = await Http.GetStringAsync(manifestUrl, cts.Token);
            var versionUrl = ExtractLatestReleaseUrl(manifest);
            if (versionUrl is null) throw new InvalidOperationException("版本清单中没有正式版条目。");
            var versionJson = await Http.GetStringAsync(versionUrl, cts.Token);
            var clientUrl = ExtractClientJarUrl(versionJson);
            if (clientUrl is null) throw new InvalidOperationException("版本 JSON 中没有 client.jar 下载地址。");
            return
            [
                ("官方", clientUrl),
                ("BMCLAPI", "https://bmclapi2.bangbang93.com/version/1.20.1/client.jar")
            ];
        }
        catch
        {
            // 解析失败（离线/接口变更）：退回版本清单文件，两边同路径可公平对比。
            return [("官方", manifestUrl), ("BMCLAPI", bmclapiManifestUrl)];
        }

        static string? ExtractLatestReleaseUrl(string manifestJson)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;
            foreach (var version in versions.EnumerateArray())
            {
                if (version.TryGetProperty("type", out var type) && type.GetString() == "release" &&
                    version.TryGetProperty("url", out var url))
                    return url.GetString();
            }
            return null;
        }

        static string? ExtractClientJarUrl(string versionJson)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(versionJson);
            if (!doc.RootElement.TryGetProperty("downloads", out var downloads)) return null;
            if (!downloads.TryGetProperty("client", out var client)) return null;
            if (!client.TryGetProperty("url", out var url)) return null;
            return url.GetString();
        }
    }

    public sealed record MirrorResult(string Name, string Url, double Speed, string Error);

    [RelayCommand]
    private void OpenLogs() => Open(WmclPaths.LogsDirectory);

    [RelayCommand]
    private void OpenGame() => Open(_services.Settings.GameDirectory);

    [RelayCommand]
    private void OpenConfig() => Open(WmclPaths.ConfigDirectory);

    [RelayCommand]
    private async Task PingServerAsync()
    {
        if (!CanPingServer) return;
        IsServerPinging = true;
        ServerFavicon = null;
        ServerMotdRuns = null;
        ServerPingStatus = "正在 Ping…";
        try
        {
            var result = await ServerPingService.PingAsync(
                ServerAddress.Trim(), timeout: TimeSpan.FromSeconds(5));
            var players = result.Online is not null && result.Max is not null
                ? $"{result.Online}/{result.Max} 人"
                : "人数未知";
            var version = string.IsNullOrWhiteSpace(result.Version) ? "版本未知" : result.Version;
            ServerMotdRuns = result.MotdRuns;
            var sample = result.PlayerSample is { Count: > 0 }
                ? " · 玩家：" + string.Join(", ", result.PlayerSample.Take(6))
                : "";
            ServerPingStatus = $"{players} · {version} · {result.LatencyMs} ms{sample}";
            ServerFavicon = DecodeFavicon(result.Favicon);
        }
        catch (Exception ex)
        {
            ServerPingStatus = "Ping 失败：" + ex.Message;
        }
        finally
        {
            IsServerPinging = false;
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? DecodeFavicon(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task StartCustomDownloadAsync()
    {
        if (!CanStartCustomDownload) return;

        if (!Uri.TryCreate(CustomDownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            CustomDownloadStatus = "请输入有效的 http/https URL";
            return;
        }

        var fileName = string.IsNullOrWhiteSpace(CustomDownloadFileName)
            ? Path.GetFileName(uri.LocalPath)
            : CustomDownloadFileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "download.bin";

        var folder = string.IsNullOrWhiteSpace(CustomDownloadFolder)
            ? Path.Combine(WmclPaths.DataDirectory, "downloads")
            : WmclPaths.Expand(CustomDownloadFolder);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(fileName));

        IsCustomDownloading = true;
        CustomDownloadProgress = 0;
        CustomDownloadStatus = "开始下载…";
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        try
        {
            using var response = await Http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var output = File.Create(destination);
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), _downloadCts.Token);
                read += count;
                if (total is > 0)
                    CustomDownloadProgress = read * 100d / total.Value;
                CustomDownloadStatus = total is > 0
                    ? $"{read / 1024d / 1024d:0.0} / {total.Value / 1024d / 1024d:0.0} MB"
                    : $"{read / 1024d / 1024d:0.0} MB";
            }
            CustomDownloadProgress = 100;
            CustomDownloadStatus = "下载完成：" + destination;
            Status = "自定义下载完成：" + fileName;
        }
        catch (OperationCanceledException)
        {
            CustomDownloadStatus = "已取消下载";
        }
        catch (Exception ex)
        {
            CustomDownloadStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsCustomDownloading = false;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelCustomDownload()
    {
        _downloadCts?.Cancel();
        CustomDownloadStatus = "正在取消…";
    }

    private void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            ExternalLauncher.Open(path);
            Status = "已打开：" + path;
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }

    // ── 清理垃圾 ──

    [RelayCommand]
    private async Task CleanJunkAsync()
    {
        if (!CanCleanJunk) return;
        IsCleaningJunk = true;
        JunkProgress = 0;
        JunkStatus = "正在扫描…";
        try
        {
            var totalBytes = 0L;
            var cleanedBytes = 0L;
            var targets = new List<(string Desc, string Path)>
            {
                ("临时文件", Path.Combine(WmclPaths.DataDirectory, "temp")),
                ("缓存文件", Path.Combine(WmclPaths.DataDirectory, "cache")),
            };

            foreach (var (desc, path) in targets)
            {
                if (!Directory.Exists(path)) continue;
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalBytes += fi.Length;
                        fi.Delete();
                        cleanedBytes += fi.Length;
                    }
                    catch { /* 跳过无法删除的文件 */ }
                }
                JunkProgress += 25;
            }

            // downloads 目录同时是“自定义下载”的默认保存位置（StartCustomDownloadAsync），
            // 不能整体清空；只按缓存语义清理 7 天前的旧文件，避免误删用户刚下载的内容。
            var downloads = Path.Combine(WmclPaths.DataDirectory, "downloads");
            if (Directory.Exists(downloads))
            {
                var staleCutoff = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(downloads, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalBytes += fi.Length;
                        if (fi.LastWriteTime >= staleCutoff) continue;
                        fi.Delete();
                        cleanedBytes += fi.Length;
                    }
                    catch { /* 跳过无法删除的文件 */ }
                }
                JunkProgress += 25;
            }

            // 清理旧日志（保留最近 7 天的）
            var logsDir = WmclPaths.LogsDirectory;
            if (Directory.Exists(logsDir))
            {
                var cutoff = DateTime.Now.AddDays(-7);
                foreach (var logFile in Directory.GetFiles(logsDir, "*.log"))
                {
                    try
                    {
                        var fi = new FileInfo(logFile);
                        if (fi.LastWriteTime < cutoff)
                        {
                            totalBytes += fi.Length;
                            fi.Delete();
                            cleanedBytes += fi.Length;
                        }
                    }
                    catch { /* 跳过 */ }
                }
                JunkProgress = 100;
            }

            JunkStatus = cleanedBytes > 0
                ? $"已清理 {cleanedBytes / 1024 / 1024:F1} MB"
                : "没有可清理的文件";
            Status = JunkStatus;
        }
        catch (Exception ex)
        {
            JunkStatus = "清理失败：" + ex.Message;
        }
        finally
        {
            IsCleaningJunk = false;
        }
    }

    // ── 启动计数 ──

    [RelayCommand]
    private void IncrementLaunchCount()
    {
        LaunchCount++;
        SaveLaunchCount();
        LaunchCountStatus = $"本次启动是第 {LaunchCount} 次";
        Status = LaunchCountStatus;
    }

    private void LoadLaunchCount()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "launch_count.txt");
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var count))
        {
            LaunchCount = count;
        }
        LaunchCountStatus = $"已启动 {LaunchCount} 次";
    }

    private void SaveLaunchCount()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "launch_count.txt");
        File.WriteAllText(path, LaunchCount.ToString());
    }

    // ── 创建快捷方式 ──

    [RelayCommand]
    private void CreateShortcut()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrWhiteSpace(exePath))
            {
                ShortcutStatus = "无法获取程序路径";
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                // 使用 PowerShell 创建桌面快捷方式
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var linkPath = Path.Combine(desktop, "WMCL.lnk");
                var psi = new ProcessStartInfo("powershell", "-Command")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add(
                    $"$ws = New-Object -ComObject WScript.Shell; " +
                    $"$link = $ws.CreateShortcut('{linkPath}'); " +
                    $"$link.TargetPath = '{exePath}'; $link.Save()");
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
                ShortcutStatus = "已在桌面创建快捷方式";
            }
            else if (OperatingSystem.IsMacOS())
            {
                ShortcutStatus = "macOS 请将 .app 拖到应用程序文件夹";
            }
            else
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var linkPath = Path.Combine(desktop, "WMCL.desktop");
                var content = $"[Desktop Entry]\nType=Application\nName=WMCL\nExec={exePath}\nIcon={exePath}\n";
                File.WriteAllText(linkPath, content);
                ShortcutStatus = "已在桌面创建 .desktop 文件";
            }
            Status = ShortcutStatus;
        }
        catch (Exception ex)
        {
            ShortcutStatus = "创建快捷方式失败：" + ex.Message;
            Status = ShortcutStatus;
        }
    }

    // ── 皮肤预览 ──

    [RelayCommand]
    private async Task PreviewSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(SkinUrl)) return;
        try
        {
            using var response = await Http.GetAsync(SkinUrl);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            SkinPreview = new Bitmap(stream);
            Status = "皮肤预览已加载";
        }
        catch (Exception ex)
        {
            Status = "加载皮肤失败：" + ex.Message;
        }
    }

    // ── 抽奖 ──

    [RelayCommand]
    private void RunLottery()
    {
        var prizes = new[]
        {
            "普通石头 ×1", "铁矿 ×1", "金矿 ×1", "钻石 ×1",
            "附魔书 ×1", "末影珍珠 ×1", "青金石 ×2", "红石 ×3",
            "" /* 空 = 谢谢参与 */,
        };
        var result = prizes[Random.Shared.Next(prizes.Length)];
        LotteryResult = result.Length > 0 ? $"恭喜获得：{result}" : "谢谢参与，下次加油！";
        LotteryStatus = LotteryResult;
        Status = LotteryResult;
    }
}
