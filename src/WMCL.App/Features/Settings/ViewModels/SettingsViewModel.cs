using WMCL.Link;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.Services.Platform;
using WMCL.App.Services.Infrastructure;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.Features.Settings;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    public event EventHandler? JavaManagementRequested;

    [RelayCommand]
    private void ManageJava() => JavaManagementRequested?.Invoke(this, EventArgs.Empty);

    private readonly IEnvironmentServices _services;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _autoSaveReady;
    private bool _suppressAutoSave;

    public SettingsViewModel(AppServices services, Func<string, string, Task<bool>>? confirm = null)
    {
        _confirm = confirm ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        _services = services;
        BuildHiddenFeatureGroups();
        services.Features.Changed += (_, _) =>
        {
            ShowFeatureHiding = services.Features.IsVisible(FeatureCatalog.FeatureHiding);
            foreach (var group in HiddenFeatureGroups)
                foreach (var item in group.Items) item.Refresh();
        };
        _gameDirectory = services.Settings.GameDirectory;
        _memoryMb = services.Settings.MemoryMb;
        _windowWidth = services.Settings.Width;
        _windowHeight = services.Settings.Height;
        _globalJvmArgs = services.Settings.ExtraJvmArgs;
        _globalGameArgs = services.Settings.ExtraGameArgs;
        _preLaunchCommand = services.Settings.PreLaunchCommand;
        _preLaunchWait = services.Settings.PreLaunchWait;
        _afterLaunch = ToAfterLaunchLabel(services.Settings.LauncherAfterLaunch);
        _closeBehavior = ToCloseBehaviorLabel(services.Settings.CloseBehavior);
        _priority = ToPriorityLabel(services.Settings.ProcessPriority);
        _isolation = ToIsolationLabel(services.Settings.IsolationMode);
        _highPerformanceGpu = services.Settings.HighPerformanceGpu;
        _gpuIndex = services.Settings.GpuIndex ?? "";
        _gpuManualIndex = _gpuIndex;
        _proxyUrl = services.Settings.ProxyUrl;
        _proxyMode = string.IsNullOrWhiteSpace(services.Settings.ProxyMode) ? "none" : services.Settings.ProxyMode;
        _proxyUsername = services.Settings.ProxyUsername ?? "";
        _proxyPassword = services.Settings.ProxyPassword ?? "";
        _proxyBypassList = string.IsNullOrWhiteSpace(services.Settings.ProxyBypassList)
            ? "localhost,127.0.0.1,::1"
            : services.Settings.ProxyBypassList;
        EnableTrash = services.Settings.EnableTrash;
        TrashAutoCleanDays = services.Settings.TrashAutoCleanDays;
        ProxyStatus = ProxyManager.Describe();
        _downloadSpeedLimitMbps = (decimal)services.Settings.DownloadSpeedLimitMbps;
        _downloadThreads = Math.Clamp(services.Settings.DownloadThreads, 1, 64);
        _customLogoPath = services.Settings.CustomLogoPath ?? "";
        _customLogoText = services.Settings.CustomLogoText ?? "";
        _customHomePagePath = services.Settings.CustomHomePagePath ?? "";
        _musicFolder = services.Settings.MusicFolder ?? "";
        _musicVolume = services.Settings.MusicVolume;
        _musicStart = services.Settings.MusicStart;
        _musicPauseOnGame = services.Settings.MusicPauseOnGame;
        _backgroundImagePath = services.Settings.BackgroundImagePath ?? "";
        _backgroundImageFolder = services.Settings.BackgroundImageFolder ?? "";
        _backgroundColorOverlay = services.Settings.BackgroundColorOverlay;
        _backgroundImageOpacity = services.Settings.BackgroundImageOpacity;
        _backgroundImageBlur = services.Settings.BackgroundImageBlur;
        _playerName = services.Settings.PlayerName;
        _javaPath = services.Settings.JavaPath ?? "";
        _mirror = services.Settings.Mirror;
        _theme = ToThemeLabel(services.Settings.Theme);
        _updateChannel = ToChannelLabel(services.Settings.UpdateChannel);
        _updateMode = ToModeLabel(services.Settings.UpdateMode);
        UiFonts.Add(UiFontService.DefaultLabel);
        foreach (var font in UiFontService.InstalledFonts()) UiFonts.Add(font);
        _uiFont = string.IsNullOrWhiteSpace(services.Settings.UiFontFamily)
            ? UiFontService.DefaultLabel
            : services.Settings.UiFontFamily;
        _accentHue = services.Settings.AccentHue;
        _accentSaturation = services.Settings.AccentSaturation;
        _accentLightAdjust = services.Settings.AccentLightAdjust;
        if (ThemeService.IsWindowsBlueOnly)
        {
            _accentHue = 210;
            _accentSaturation = 85;
            _accentLightAdjust = 0;
        }
        FontScale = services.Settings.FontScale;
        _applicationOpacity = Math.Clamp(services.Settings.ApplicationOpacity, 0, 100);
        _windowBackdropBlur = services.Settings.WindowBackdropBlur;
        _shortcutTab1 = "Ctrl+1";
        _shortcutTab2 = "Ctrl+2";
        _shortcutTab3 = "Ctrl+3";
        _shortcutTab4 = "Ctrl+4";
        _shortcutTab5 = "Ctrl+5";
        _shortcutSearch = "Ctrl+F";
        ApplyShortcuts(services.Settings.CustomShortcuts);
        SelectMatchingPreset();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            RunSave(applyGameDirectory: false);
        };
        _autoSaveReady = true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_autoSaveReady || _suppressAutoSave || !ShouldAutoSave(e.PropertyName)) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private static bool ShouldAutoSave(string? property) => property is
        nameof(MemoryMb) or nameof(WindowWidth) or nameof(WindowHeight) or
        nameof(GlobalJvmArgs) or nameof(GlobalGameArgs) or nameof(PreLaunchCommand) or
        nameof(PreLaunchWait) or nameof(AfterLaunch) or nameof(Priority) or
        nameof(Isolation) or nameof(HighPerformanceGpu) or nameof(ProxyUrl) or
        nameof(ProxyMode) or nameof(ProxyUsername) or nameof(ProxyPassword) or
        nameof(ProxyBypassList) or
        nameof(EnableTrash) or nameof(TrashAutoCleanDays) or
        nameof(DownloadSpeedLimitMbps) or nameof(DownloadThreads) or
        nameof(CustomLogoPath) or nameof(CustomLogoText) or nameof(CustomHomePagePath) or nameof(MusicFolder) or
        nameof(MusicVolume) or nameof(MusicStart) or nameof(MusicPauseOnGame) or
        nameof(BackgroundImagePath) or nameof(BackgroundImageFolder) or
        nameof(BackgroundColorOverlay) or nameof(BackgroundImageOpacity) or
        nameof(BackgroundImageBlur) or nameof(PlayerName) or nameof(JavaPath) or
        nameof(Mirror) or nameof(UpdateChannel) or nameof(UpdateMode) or
        nameof(Theme) or nameof(UiFont) or nameof(AccentHue) or nameof(AccentSaturation) or
        nameof(AccentLightAdjust) or
        nameof(FontScale) or nameof(ApplicationOpacity) or nameof(WindowBackdropBlur) or nameof(ShortcutTab1) or nameof(ShortcutTab2) or
        nameof(ShortcutTab3) or nameof(ShortcutTab4) or nameof(ShortcutTab5) or
        nameof(ShortcutSearch);

    public ObservableCollection<string> Mirrors { get; } = ["auto", "official", "bmclapi"];

    public ObservableCollection<string> UpdateChannels { get; } = ["稳定版", "测试版"];

    public ObservableCollection<string> UpdateModes { get; } =
        ["不检查更新", "仅提示", "自动下载并提示", "自动下载并安装"];

    [ObservableProperty]
    private string _updateChannel = "稳定版";

    [ObservableProperty]
    private string _updateMode = "仅提示";

    private static string ToChannelLabel(string value) =>
        value == "beta" ? "测试版" : "稳定版";

    private static string FromChannelLabel(string label) =>
        label == "测试版" ? "beta" : "stable";

    private static string ToModeLabel(string value) => value switch
    {
        "off" => "不检查更新",
        "download" => "自动下载并提示",
        "auto" => "自动下载并安装",
        _ => "仅提示"
    };

    private static string FromModeLabel(string label) => label switch
    {
        "不检查更新" => "off",
        "自动下载并提示" => "download",
        "自动下载并安装" => "auto",
        _ => "prompt"
    };

    /// <summary>界面字体候选：默认项 + 系统已安装字体。</summary>
    public ObservableCollection<string> UiFonts { get; } = [];

    [ObservableProperty]
    private string _uiFont = UiFontService.DefaultLabel;
    public ObservableCollection<string> Themes { get; } = ["浅色", "深色", "跟随系统"];

    public bool CanCustomizeAccent => !ThemeService.IsWindowsBlueOnly;
    public bool IsWindowsThemeRestricted => ThemeService.IsWindowsBlueOnly;
    public ObservableCollection<ThemePreset> ThemePresets { get; } =
    [
        new("自定义", 210, 85, 0),
        new("龙猫蓝", 210, 85, 0),
        new("甜柠青", 180, 75, 0),
        new("小草绿", 130, 65, 0),
        new("菠萝黄", 45, 85, 0),
        new("橡木棕", 30, 55, -5),
        new("玄素黑", 0, 0, 0),
        new("铁杆粉", 330, 75, 0),
        new("神秘紫", 270, 75, 0),
        new("秋仪金", 40, 80, 0),
        new("活跃橙", 25, 85, 0),
        new("跳票红", 0, 75, 0),
        new("极客蓝", 225, 85, 0)
    ];
    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = [];
    public ObservableCollection<string> JavaInstallVersions { get; } = ["8", "11", "17", "21", "25"];

    [ObservableProperty]
    private string _javaInstallVersion = "21";
    public string ConfigPath => WmclPaths.ConfigFile;

    [ObservableProperty]
    private string _gameDirectory = "";

    [ObservableProperty]
    private decimal _memoryMb = 4096;

    [ObservableProperty]
    private decimal _windowWidth = 854;

    [ObservableProperty]
    private decimal _windowHeight = 480;

    [ObservableProperty]
    private string _globalJvmArgs = "";

    [ObservableProperty]
    private string _globalGameArgs = "";

    [ObservableProperty]
    private string _preLaunchCommand = "";

    [ObservableProperty]
    private bool _preLaunchWait = true;

    [ObservableProperty]
    private string _priority = "普通";

    [ObservableProperty]
    private string _afterLaunch = "保持打开";

    [ObservableProperty]
    private string _closeBehavior = "隐藏到托盘";

    public ObservableCollection<string> CloseBehaviorOptions { get; } = ["隐藏到托盘", "退出程序"];

    internal static string ToCloseBehaviorLabel(string? value) =>
        string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase) ? "退出程序" : "隐藏到托盘";

    internal static string ToCloseBehaviorValue(string? label) =>
        string.Equals(label, "退出程序", StringComparison.Ordinal) ? "exit" : "hide";

    [ObservableProperty]
    private string _isolation = "自动";

    public ObservableCollection<string> IsolationOptions { get; } = ["自动", "隔离", "不隔离"];

    [ObservableProperty]
    private bool _highPerformanceGpu;

    [ObservableProperty]
    private string _gpuIndex = "";

    /// <summary>检测到的 GPU 列表（仅 Linux）。为空时显示手动输入。</summary>
    public ObservableCollection<GpuDetector.GpuDevice> GpuDevices { get; } = [];

    /// <summary>下拉框选中的 GPU 设备。</summary>
    [ObservableProperty]
    private GpuDetector.GpuDevice? _selectedGpuDevice;

    partial void OnSelectedGpuDeviceChanged(GpuDetector.GpuDevice? value)
    {
        if (value is not null)
            GpuIndex = value.Index;
    }

    /// <summary>GPU 手动选择输入框文本。</summary>
    [ObservableProperty]
    private string _gpuManualIndex = "";

    public bool HasGpuDevices => GpuDevices.Count > 0;

    partial void OnHighPerformanceGpuChanged(bool value)
    {
        if (value) DetectGpus();
    }


    public void SetSection(string key)
    {
        ShowLaunchSettings = key == "settings-launch";
        ShowJavaSettings = key == "settings-java";
        ShowPersonalization = key == "settings-ui";
        ShowOtherSettings = key == "settings-other";
        // 自己也能被隐藏，但必须留一个入口恢复；F12 或“其他”页都能再打开。
        ShowFeatureHiding = _services.Features.IsVisible(Services.FeatureCatalog.FeatureHiding);
    }

    [ObservableProperty]
    private string _status = "修改后自动保存；游戏目录需点击“应用目录”";

    [RelayCommand]
    private void Save() => RunSave(applyGameDirectory: true);

    private void RunSave(bool applyGameDirectory)
    {
        _autoSaveTimer.Stop();
        _suppressAutoSave = true;
        try
        {
            SaveCore(applyGameDirectory);
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private void SaveCore(bool applyGameDirectory)
    {
        if (applyGameDirectory)
        {
            var expandedGameDirectory = WmclPaths.Expand(GameDirectory);
            if (!string.Equals(expandedGameDirectory, _services.Settings.GameDirectory,
                    StringComparison.OrdinalIgnoreCase))
                _services.SetGameDirectory(expandedGameDirectory);
        }
        var applyTheme = _services.Settings.Theme != FromThemeLabel(Theme) ||
                         _services.Settings.AccentHue != (int)AccentHue ||
                         _services.Settings.AccentSaturation != (int)AccentSaturation ||
                         _services.Settings.AccentLightAdjust != (int)AccentLightAdjust;
        _services.Settings.MemoryMb = (int)MemoryMb;
        _services.Settings.Width = Math.Max(320, (int)WindowWidth);
        _services.Settings.Height = Math.Max(240, (int)WindowHeight);
        _services.Settings.ExtraJvmArgs = GlobalJvmArgs.Trim();
        _services.Settings.ExtraGameArgs = GlobalGameArgs.Trim();
        _services.Settings.PreLaunchCommand = PreLaunchCommand.Trim();
        _services.Settings.PreLaunchWait = PreLaunchWait;
        _services.Settings.LauncherAfterLaunch = ToAfterLaunchValue(AfterLaunch);
        _services.Settings.CloseBehavior = ToCloseBehaviorValue(CloseBehavior);
        _services.Settings.ProcessPriority = FromPriorityLabel(Priority);
        _services.Settings.IsolationMode = FromIsolationLabel(Isolation);
        _services.Settings.HighPerformanceGpu = HighPerformanceGpu;
        _services.Settings.GpuIndex = GpuIndex.Trim();
        _services.Settings.ProxyUrl = ProxyUrl.Trim();
        _services.Settings.ProxyMode = ProxyMode;
        _services.Settings.ProxyUsername = ProxyUsername.Trim();
        _services.Settings.ProxyPassword = ProxyPassword;
        _services.Settings.ProxyBypassList = ProxyBypassList.Trim();
        _services.Settings.EnableTrash = EnableTrash;
        _services.Settings.TrashAutoCleanDays = Math.Clamp(TrashAutoCleanDays, 0, 365);
        ProxyManager.Apply(new ProxyConfig(
            ProxyManager.ParseMode(ProxyMode), ProxyUrl, ProxyUsername, ProxyPassword, ProxyBypassList));
        ProxyStatus = ProxyManager.Describe();
        _services.Settings.DownloadSpeedLimitMbps = (double)Math.Max(0, DownloadSpeedLimitMbps);
        DownloadThrottle.SetLimit((long)((double)Math.Max(0, DownloadSpeedLimitMbps) * 1024 * 1024));
        _services.Settings.DownloadThreads = Math.Clamp((int)DownloadThreads, 1, 64);
        _services.Settings.CustomLogoPath = CustomLogoPath.Trim();
        _services.Settings.CustomLogoText = CustomLogoText.Trim();
        _services.Settings.CustomHomePagePath = CustomHomePagePath.Trim();
        _services.Settings.MusicFolder = MusicFolder.Trim();
        _services.Settings.MusicVolume = Math.Clamp((int)MusicVolume, 0, 100);
        _services.Settings.MusicStart = MusicStart;
        _services.Settings.MusicPauseOnGame = MusicPauseOnGame;
        _services.Settings.BackgroundImagePath = BackgroundImagePath.Trim();
        _services.Settings.BackgroundImageFolder = BackgroundImageFolder.Trim();
        _services.Settings.BackgroundColorOverlay = BackgroundColorOverlay;
        _services.Settings.BackgroundImageOpacity = Math.Clamp((int)BackgroundImageOpacity, 0, 100);
        _services.Settings.BackgroundImageBlur = Math.Clamp((int)BackgroundImageBlur, 0, 30);
        _services.Settings.PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? "Steve" : PlayerName;
        _services.Settings.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
        _services.Settings.Mirror = Mirror;
        _services.Settings.FontScale = Math.Clamp(FontScale, 0.8, 1.5);
        _services.Settings.ApplicationOpacity = Math.Clamp((int)ApplicationOpacity, 0, 100);
        _services.Settings.WindowBackdropBlur = WindowBackdropBlur;
        _services.Settings.CustomShortcuts = System.Text.Json.JsonSerializer.Serialize(new
        {
            Tab1 = ShortcutTab1,
            Tab2 = ShortcutTab2,
            Tab3 = ShortcutTab3,
            Tab4 = ShortcutTab4,
            Tab5 = ShortcutTab5,
            Search = ShortcutSearch
        });
        _services.Settings.UpdateChannel = FromChannelLabel(UpdateChannel);
        _services.Settings.UpdateMode = FromModeLabel(UpdateMode);
        _services.Settings.Theme = FromThemeLabel(Theme);
        _services.Settings.UiFontFamily =
            UiFont == UiFontService.DefaultLabel ? "" : UiFont;
        _services.Settings.AccentHue = (int)AccentHue;
        _services.Settings.AccentSaturation = (int)AccentSaturation;
        _services.Settings.AccentLightAdjust = (int)AccentLightAdjust;
        UiFontService.Apply(_services.Settings);
        if (applyTheme)
            ThemeService.Apply(
                _services.Settings.Theme,
                _services.Settings.AccentHue,
                _services.Settings.AccentSaturation,
                _services.Settings.AccentLightAdjust);
        _services.SaveSettings();

        if (applyGameDirectory) GameDirectory = _services.Settings.GameDirectory;
        Status = applyGameDirectory ? "已保存。" : "已自动保存。";
    }

    public void RefreshGameDirectory() =>
        GameDirectory = _services.Settings.GameDirectory;

    public async Task LoadAsync()
    {
        if (JavaRuntimes.Count == 0)
            await DetectJavaAsync();
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        var path = WmclPaths.Expand(GameDirectory);
        if (Directory.Exists(path)) OpenPath(path);
    }

    public ObservableCollection<string> AfterLaunchOptions { get; } = ["保持打开", "隐藏到后台", "关闭启动器"];

    internal static string ToAfterLaunchLabel(string? value) => value switch
    {
        "hide" => "隐藏到后台",
        "close" => "关闭启动器",
        _ => "保持打开"
    };

    internal static string ToAfterLaunchValue(string? label) => label switch
    {
        "隐藏到后台" => "hide",
        "关闭启动器" => "close",
        _ => "keep"
    };

    public ObservableCollection<string> PriorityOptions { get; } =
        ["普通", "高于普通", "高", "低于普通", "空闲"];

    private static string ToIsolationLabel(string? mode) => IsolationPolicy.Normalize(mode) switch
    {
        IsolationPolicy.Isolated => "隔离",
        IsolationPolicy.Global => "不隔离",
        _ => "自动"
    };

    private static string FromIsolationLabel(string? label) => label switch
    {
        "隔离" => IsolationPolicy.Isolated,
        "不隔离" => IsolationPolicy.Global,
        _ => IsolationPolicy.Auto
    };

    private static string ToPriorityLabel(string? priority) => priority?.ToLowerInvariant() switch
    {
        "abovenormal" => "高于普通",
        "high" => "高",
        "belownormal" => "低于普通",
        "idle" => "空闲",
        _ => "普通"
    };

    private static string FromPriorityLabel(string? label) => label switch
    {
        "高于普通" => "aboveNormal",
        "高" => "high",
        "低于普通" => "belowNormal",
        "空闲" => "idle",
        _ => "normal"
    };

    private static string ToThemeLabel(string? theme) => ThemeService.Normalize(theme) switch
    {
        "dark" => "深色",
        "system" => "跟随系统",
        _ => "浅色"
    };

    private static string FromThemeLabel(string? label) => label switch
    {
        "深色" => "dark",
        "跟随系统" => "system",
        _ => "light"
    };

    private static void OpenPath(string path)
    {
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // ignore
        }
    }

    // ── 配置导入/导出 ──

    [ObservableProperty]
    private string _configStatus = "";
}

