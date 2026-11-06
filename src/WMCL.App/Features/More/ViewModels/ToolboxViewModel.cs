using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Features.More;

public partial class ToolboxViewModel : ViewModelBase
{
    private readonly IUserSettingsProvider _services;
    // 使用全局客户端：属性每次求值，ProxyManager.Apply 重置后自动采用新代理；
    // 自建实例会在构造时固化代理设置、运行时不跟随变化。超时由各请求的 CTS 控制。
    private HttpClient Http => WMCL.Core.Http.Http.Client;
    private CancellationTokenSource? _downloadCts;

    public ToolboxViewModel(IUserSettingsProvider services)
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

    // ── 皮肤预览 ──
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
