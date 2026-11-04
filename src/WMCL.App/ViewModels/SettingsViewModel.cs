using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    public event EventHandler? JavaManagementRequested;

    [RelayCommand]
    private void ManageJava() => JavaManagementRequested?.Invoke(this, EventArgs.Empty);

    private readonly AppServices _services;
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
        _hideLauncherOnGame = services.Settings.HideLauncherOnGame;
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
        nameof(PreLaunchWait) or nameof(HideLauncherOnGame) or nameof(Priority) or
        nameof(Isolation) or nameof(HighPerformanceGpu) or nameof(ProxyUrl) or
        nameof(ProxyMode) or nameof(ProxyUsername) or nameof(ProxyPassword) or
        nameof(ProxyBypassList) or
        nameof(EnableTrash) or nameof(TrashAutoCleanDays) or
        nameof(DownloadSpeedLimitMbps) or nameof(DownloadThreads) or
        nameof(CustomLogoPath) or nameof(CustomHomePagePath) or nameof(MusicFolder) or
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
    private bool _hideLauncherOnGame;

    [ObservableProperty]
    private string _priority = "普通";

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

    private void DetectGpus()
    {
        GpuDevices.Clear();
        if (!OperatingSystem.IsLinux()) return;
        foreach (var device in GpuDetector.Detect())
            GpuDevices.Add(device);
    }

    [RelayCommand]
    private void RefreshGpuList() => DetectGpus();

    [RelayCommand]
    private void ApplyGpuManual()
    {
        GpuIndex = GpuManualIndex.Trim();
    }

    partial void OnGpuIndexChanged(string value)
    {
        // 同步手动输入框
        GpuManualIndex = value;
    }

    [ObservableProperty]
    private string _proxyUrl = "";

    [ObservableProperty]
    private string _proxyMode = "none";

    [ObservableProperty]
    private string _proxyUsername = "";

    [ObservableProperty]
    private string _proxyPassword = "";

    [ObservableProperty]
    private string _proxyBypassList = "localhost,127.0.0.1,::1";

    [ObservableProperty]
    private bool _enableTrash = true;

    [ObservableProperty]
    private int _trashAutoCleanDays = 30;

    [ObservableProperty]
    private string _proxyStatus = "";

    public List<string> ProxyModes { get; } = ["none", "system", "custom"];

    [RelayCommand]
    private async Task DetectSystemProxyAsync()
    {
        ProxyStatus = "正在检测系统代理…";
        var detected = await ProxyManager.DetectSystemProxyAsync();
        // 检测结果写回 ProxyManager：SystemProxy 模式依赖该值，
        // 此前只把结果拼进状态文本、从未回存，导致系统代理模式静默直连。
        ProxyManager.SystemProxyAddress = ProxyManager.NormalizeAddress(detected);
        ProxyStatus = detected is null
            ? "未检测到系统代理"
            : $"已检测到系统代理：{detected}";
    }

    [ObservableProperty]
    private decimal _downloadSpeedLimitMbps;

    [ObservableProperty]
    private decimal _downloadThreads = 16;

    [ObservableProperty]
    private string _customLogoPath = "";

    [ObservableProperty]
    private string _customHomePagePath = "";

    [ObservableProperty]
    private string _musicFolder = "";

    [ObservableProperty]
    private decimal _musicVolume = 70;

    [ObservableProperty]
    private bool _musicStart;

    [ObservableProperty]
    private bool _musicPauseOnGame = true;

    [ObservableProperty]
    private string _backgroundImagePath = "";

    [ObservableProperty]
    private decimal _backgroundImageOpacity = 30;

    [ObservableProperty]
    private decimal _backgroundImageBlur;

    [ObservableProperty]
    private string _backgroundImageFolder = "";

    [ObservableProperty]
    private bool _backgroundColorOverlay;

    [ObservableProperty]
    private string _playerName = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _mirror = "auto";

    [ObservableProperty]
    private string _updateManifestUrl = "";

    [ObservableProperty]
    private string _theme = "浅色";

    [ObservableProperty]
    private double _accentHue = 210;

    [ObservableProperty]
    private double _accentSaturation = 85;

    [ObservableProperty]
    private double _accentLightAdjust;

    [ObservableProperty]
    private ThemePreset? _selectedThemePreset;

    private bool _applyingPreset;

    [ObservableProperty]
    private double _fontScale = 1.0;

    [ObservableProperty]
    private decimal _applicationOpacity = 100;

    [ObservableProperty]
    private bool _windowBackdropBlur;

    [ObservableProperty]
    private string _windowEffectsStatus = "";

    [ObservableProperty]
    private string _windowEffectsBackendStatus = "";

    public bool IsNiriSession => OperatingSystem.IsLinux() &&
        ((Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':').Any(value => value.Equals("niri", StringComparison.OrdinalIgnoreCase)) ||
         !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NIRI_SOCKET")));
    public string WindowEffectsHelp => IsNiriSession
        ? "透明度滑到最左仍有 40% 窗口不透明度，配合 Niri 模糊可保持可读。更改开关后点击“应用 Niri 规则”；会先确认并备份，仅更新 WMCL 专用规则，不修改其他应用。"
        : "透明度滑到最左仍有 40% 窗口不透明度。桌面背景模糊取决于窗口系统支持，不会把文字本身模糊；不支持时会缺少背景模糊。";
    public string NiriWindowRule => NiriWindowRuleService.BuildRule(WindowBackdropBlur);

    partial void OnWindowBackdropBlurChanged(bool value)
    {
        OnPropertyChanged(nameof(NiriWindowRule));
        WindowEffectsStatus = IsNiriSession ? "应用请求已更新；Niri 全局规则可能覆盖请求，请应用专用规则。" : "已更新系统背景模糊请求。";
    }

    [RelayCommand]
    private async Task ApplyNiriEffectsAsync(CancellationToken cancellationToken)
    {
        if (!IsNiriSession) { WindowEffectsStatus = "当前不是 Niri 会话。"; return; }
        var path = NiriWindowRuleService.GetConfigPath();
        var blur = WindowBackdropBlur;
        if (!await _confirm("应用 Niri 窗口特效", $"将在以下配置中仅更新 WMCL 的模糊规则，原文件会备份，其他规则保留：\n{path}\n\n模糊：{(blur ? "开启" : "关闭")}。是否继续？")) return;
        if (blur != WindowBackdropBlur) { WindowEffectsStatus = "选项已改变，请重新确认应用。"; return; }
        try
        {
            var backup = await new NiriWindowRuleService().ApplyAsync(path, blur, cancellationToken);
            WindowEffectsStatus = "Niri 专用规则已校验并保存，合成器会自动重载。备份：" + backup;
        }
        catch (OperationCanceledException) { WindowEffectsStatus = "已取消 Niri 配置，原文件未覆盖。"; }
        catch (Exception exception) { WindowEffectsStatus = "应用 Niri 规则失败：" + exception.Message + "；可复制规则后手动配置。"; }
    }

    [ObservableProperty]
    private string _shortcutTab1 = "Ctrl+1";
    [ObservableProperty]
    private string _shortcutTab2 = "Ctrl+2";
    [ObservableProperty]
    private string _shortcutTab3 = "Ctrl+3";
    [ObservableProperty]
    private string _shortcutTab4 = "Ctrl+4";
    [ObservableProperty]
    private string _shortcutTab5 = "Ctrl+5";
    [ObservableProperty]
    private string _shortcutSearch = "Ctrl+F";

    [ObservableProperty]
    private JavaRuntime? _selectedJavaRuntime;

    [ObservableProperty]
    private string _javaInstallStatus = "";

    [ObservableProperty]
    private bool _isInstallingJava;

    [ObservableProperty]
    private double _javaInstallProgress;

    private CancellationTokenSource? _javaInstallCts;

    partial void OnSelectedJavaRuntimeChanged(JavaRuntime? value)
    {
        if (value is not null) JavaPath = value.Path;
    }

    partial void OnThemeChanged(string value) => PreviewTheme();

    partial void OnFontScaleChanged(double value) => OnPropertyChanged(nameof(WindowSummary));
    partial void OnApplicationOpacityChanged(decimal value) => OnPropertyChanged(nameof(WindowSummary));
    partial void OnBackgroundImagePathChanged(string value) => OnPropertyChanged(nameof(BackgroundSummary));
    partial void OnBackgroundImageFolderChanged(string value) => OnPropertyChanged(nameof(BackgroundSummary));

    partial void OnUiFontChanged(string value) =>
        UiFontService.Apply(ResolveUiFontChain(value));

    partial void OnAccentHueChanged(double value)
    {
        if (ThemeService.IsWindowsBlueOnly)
        {
            if (value != 210) AccentHue = 210;
            return;
        }
        PreviewTheme();
        OnPropertyChanged(nameof(AccentSummary));
        if (!_applyingPreset) SelectMatchingPreset();
    }

    partial void OnAccentSaturationChanged(double value)
    {
        if (ThemeService.IsWindowsBlueOnly)
        {
            if (value != 85) AccentSaturation = 85;
            return;
        }
        PreviewTheme();
        OnPropertyChanged(nameof(AccentSummary));
        if (!_applyingPreset) SelectMatchingPreset();
    }

    partial void OnAccentLightAdjustChanged(double value)
    {
        if (ThemeService.IsWindowsBlueOnly)
        {
            if (value != 0) AccentLightAdjust = 0;
            return;
        }
        PreviewTheme();
        OnPropertyChanged(nameof(AccentSummary));
        if (!_applyingPreset) SelectMatchingPreset();
    }

    partial void OnSelectedThemePresetChanged(ThemePreset? value)
    {
        if (value is null || value.Name == "自定义") return;
        if ((int)AccentHue == value.Hue &&
            (int)AccentSaturation == value.Saturation &&
            (int)AccentLightAdjust == value.LightAdjust) return;
        _applyingPreset = true;
        try
        {
            AccentHue = value.Hue;
            AccentSaturation = value.Saturation;
            AccentLightAdjust = value.LightAdjust;
        }
        finally
        {
            _applyingPreset = false;
        }
        SelectMatchingPreset();
    }

    public string AccentSummary =>
        $"色相 {(int)AccentHue}° · 饱和度 {(int)AccentSaturation}% · 亮度 {(int)AccentLightAdjust:+0;-0;0}";

    public string WindowSummary =>
        $"缩放 {FontScale:P0} · 透明度 {(int)ApplicationOpacity}%";

    public string BackgroundSummary =>
        string.IsNullOrWhiteSpace(BackgroundImageFolder)
            ? string.IsNullOrWhiteSpace(BackgroundImagePath) ? "默认渐变" : Path.GetFileName(BackgroundImagePath)
            : Path.GetFileName(BackgroundImageFolder) + "（随机）";

    private static string ResolveUiFontChain(string label) =>
        label == UiFontService.DefaultLabel || string.IsNullOrWhiteSpace(label)
            ? UiFontService.DefaultChain
            : label + ", " + string.Join(", ", UiFontService.ChineseFallbacks);

    private void SelectMatchingPreset()
    {
        var match = ThemePresets.FirstOrDefault(preset =>
            preset.Name != "自定义" &&
            preset.Hue == (int)AccentHue &&
            preset.Saturation == (int)AccentSaturation &&
            preset.LightAdjust == (int)AccentLightAdjust);
        SelectedThemePreset = match ?? ThemePresets[0];
    }

    private void PreviewTheme() =>
        ThemeService.Apply(
            FromThemeLabel(Theme), (int)AccentHue, (int)AccentSaturation, (int)AccentLightAdjust);

    [ObservableProperty]
    private bool _showLaunchSettings = true;

    [ObservableProperty]
    private bool _showJavaSettings;

    [ObservableProperty]
    private bool _showPersonalization;

    [ObservableProperty]
    private bool _showOtherSettings;

    [ObservableProperty]
    private bool _showFeatureHiding = true;

    [ObservableProperty]
    private string _featureHidingStatus = "修改后立即生效；按住 F12 可临时显示被隐藏的功能。";

    public ObservableCollection<HiddenFeatureGroup> HiddenFeatureGroups { get; } = [];

    private void BuildHiddenFeatureGroups()
    {
        HiddenFeatureGroups.Clear();
        foreach (var group in FeatureCatalog.Groups)
            HiddenFeatureGroups.Add(new HiddenFeatureGroup(group, FeatureCatalog.All
                .Where(feature => feature.Group == group)
                .Select(feature => new HiddenFeatureItem(feature, _services.Features))
                .ToArray()));
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
        _services.Settings.HideLauncherOnGame = HideLauncherOnGame;
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
    private async Task InstallJavaAsync()
    {
        if (IsInstallingJava) return;
        if (!int.TryParse(JavaInstallVersion, out var major)) major = 21;
        IsInstallingJava = true;
        JavaInstallProgress = 0;
        JavaInstallStatus = $"正在准备 Java {major}…";
        _javaInstallCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                JavaInstallProgress = (update.Percent ?? 0) * 100;
                JavaInstallStatus = $"{update.Stage} {JavaInstallProgress:0}%";
            });
            var runtime = await _services.JavaProvisioner.EnsureExactAsync(
                major, progress, _javaInstallCts.Token);
            JavaPath = runtime.Path;
            JavaInstallStatus = $"Java {runtime.MajorVersion} 已就绪：{runtime.Path}";
        }
        catch (OperationCanceledException)
        {
            JavaInstallStatus = "已取消 Java 安装";
        }
        catch (Exception ex)
        {
            JavaInstallStatus = "Java 安装失败：" + ex.Message;
        }
        finally
        {
            IsInstallingJava = false;
            JavaInstallProgress = 0;
            _javaInstallCts?.Dispose();
            _javaInstallCts = null;
        }
    }

    [RelayCommand]
    private void CancelJavaInstall() => _javaInstallCts?.Cancel();

    [RelayCommand]
    private async Task CleanJavaAsync()
    {
        if (IsInstallingJava) return;
        if (!await _confirm(
                "清理 Java", "删除本启动器下载的 mojang-* Java 运行时？系统 Java 不受影响。"))
            return;
        if (IsInstallingJava)
        {
            JavaInstallStatus = "Java 正在安装，请等待结束后再清理。";
            return;
        }
        try
        {
            using (var operation = _services.GameOperations.AcquireExclusive("清理 Java"))
            {
                var removed = _services.JavaProvisioner.RemoveDownloaded();
                JavaInstallStatus = $"已清理 {removed} 个下载的 Java 运行时";
            }
            await DetectJavaAsync();
        }
        catch (Exception exception)
        {
            JavaInstallStatus = "清理 Java 失败：" + exception.Message;
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在扫描 Java…";
        try
        {
            var runtimes = await _services.JavaLocator.DiscoverAsync(forceRefresh: true);
            JavaRuntimes.Clear();
            foreach (var runtime in runtimes) JavaRuntimes.Add(runtime);
            SelectedJavaRuntime = runtimes.FirstOrDefault(runtime =>
                                      string.Equals(runtime.Path, JavaPath, StringComparison.Ordinal));
            Status = runtimes.Count == 0
                ? "未发现 Java；启动时会自动下载 Mojang Runtime，也可手动填写路径"
                : "已发现：" + string.Join("，", runtimes.Select(r => $"Java {r.MajorVersion}"));
        }
        catch (Exception ex)
        {
            Status = "扫描失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void UseAutomaticJava()
    {
        JavaPath = "";
        SelectedJavaRuntime = null;
        Status = "Java 已设为自动：启动时按版本发现或下载 Mojang Runtime";
    }

    [RelayCommand]
    private void OpenConfigFolder() => OpenPath(WmclPaths.ConfigDirectory);

    [RelayCommand]
    private void OpenGameFolder()
    {
        var path = WmclPaths.Expand(GameDirectory);
        if (Directory.Exists(path)) OpenPath(path);
    }

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

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        try
        {
            var storage = GetStorageProvider();
            if (storage is null)
            {
                ConfigStatus = "无法访问文件选择器";
                return;
            }
            var suggested = $"WMCL-Config-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出配置",
                SuggestedFileName = suggested,
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            if (file is null) return;
            var outputPath = file.Path.LocalPath;
            SettingsService.ExportToFile(_services.Settings, outputPath);
            ConfigStatus = $"已导出配置到：{outputPath}";
        }
        catch (Exception ex)
        {
            ConfigStatus = "导出配置失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        try
        {
            var storage = GetStorageProvider();
            if (storage is null)
            {
                ConfigStatus = "无法访问文件选择器";
                return;
            }
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择配置文件",
                AllowMultiple = false,
                FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0) return;
            var imported = SettingsService.ImportFromFile(files[0].Path.LocalPath);
            // 重新加载到当前 VM（Settings 是 get-only，LoadFromSettings 用反射复制）
            LoadFromSettings(imported);
            ConfigStatus = "配置已导入并已应用（游戏目录保持不变；启动命令、Java/JVM 参数、自定义主页、外部程序及更新源等执行类设置不导入，请在本机手动配置）";
        }
        catch (Exception ex)
        {
            ConfigStatus = "导入配置失败：" + ex.Message;
        }
    }

    /// <summary>获取当前窗口的 StorageProvider（无文件选择器环境返回 null）。</summary>
    private static Avalonia.Platform.Storage.IStorageProvider? GetStorageProvider()
    {
        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
        { MainWindow: { } win }
            ? win
            : null;
        return Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.StorageProvider;
    }

    private void LoadFromSettings(AppSettings settings)
    {
        AppSettingsImportPolicy.Validate(settings);
        var wasSuppressed = _suppressAutoSave;
        _suppressAutoSave = true;
        _autoSaveTimer.Stop();
        try
        {
            // Settings 是 get-only，用反射把所有字段复制到现有对象
            // 跳过游戏目录相关属性，避免导入配置时意外改变游戏目录
            foreach (var prop in typeof(AppSettings).GetProperties())
            {
                if (AppSettingsImportPolicy.IsProtectedProperty(prop.Name)) continue;
                if (prop.Name is "GameDirectory" or "GameFolders" or "GameFolderNames"
                    or "FavoriteVersions" or "HiddenVersions"
                    or "FavoriteDownloadComponents" or "ResourceFavoriteFolders"
                    or "FavoriteVersionsByFolder" or "HiddenVersionsByFolder")
                    continue;
                // 导入的敏感字段为空时（导出已脱敏）保留本地已配置的值。
                if (prop.Name is "ProxyPassword" && string.IsNullOrWhiteSpace(prop.GetValue(settings) as string))
                    continue;
                if (prop.CanRead && prop.CanWrite)
                    prop.SetValue(_services.Settings, prop.GetValue(settings));
            }
            // 重新绑定 VM 属性
            GameDirectory = _services.Settings.GameDirectory;
            MemoryMb = _services.Settings.MemoryMb;
            WindowWidth = _services.Settings.Width;
            WindowHeight = _services.Settings.Height;
            GlobalJvmArgs = _services.Settings.ExtraJvmArgs;
            GlobalGameArgs = _services.Settings.ExtraGameArgs;
            PreLaunchCommand = _services.Settings.PreLaunchCommand;
            PreLaunchWait = _services.Settings.PreLaunchWait;
            PlayerName = _services.Settings.PlayerName;
            HideLauncherOnGame = _services.Settings.HideLauncherOnGame;
            Priority = ToPriorityLabel(_services.Settings.ProcessPriority);
            Isolation = ToIsolationLabel(_services.Settings.IsolationMode);
            HighPerformanceGpu = _services.Settings.HighPerformanceGpu;
            GpuIndex = _services.Settings.GpuIndex ?? "";
            ProxyUrl = _services.Settings.ProxyUrl ?? "";
            ProxyMode = string.IsNullOrWhiteSpace(_services.Settings.ProxyMode) ? "none" : _services.Settings.ProxyMode;
            ProxyUsername = _services.Settings.ProxyUsername ?? "";
            ProxyPassword = _services.Settings.ProxyPassword ?? "";
            ProxyBypassList = string.IsNullOrWhiteSpace(_services.Settings.ProxyBypassList)
                ? "localhost,127.0.0.1,::1"
                : _services.Settings.ProxyBypassList;
            ProxyStatus = ProxyManager.Describe();
            EnableTrash = _services.Settings.EnableTrash;
            TrashAutoCleanDays = _services.Settings.TrashAutoCleanDays;
            DownloadSpeedLimitMbps = (decimal)_services.Settings.DownloadSpeedLimitMbps;
            DownloadThreads = (decimal)_services.Settings.DownloadThreads;
            CustomLogoPath = _services.Settings.CustomLogoPath ?? "";
            CustomHomePagePath = _services.Settings.CustomHomePagePath ?? "";
            MusicFolder = _services.Settings.MusicFolder ?? "";
            MusicVolume = (decimal)_services.Settings.MusicVolume;
            MusicStart = _services.Settings.MusicStart;
            MusicPauseOnGame = _services.Settings.MusicPauseOnGame;
            BackgroundImagePath = _services.Settings.BackgroundImagePath ?? "";
            BackgroundImageFolder = _services.Settings.BackgroundImageFolder ?? "";
            BackgroundColorOverlay = _services.Settings.BackgroundColorOverlay;
            BackgroundImageOpacity = (decimal)_services.Settings.BackgroundImageOpacity;
            BackgroundImageBlur = (decimal)_services.Settings.BackgroundImageBlur;
            JavaPath = _services.Settings.JavaPath ?? "";
            Mirror = _services.Settings.Mirror;
            UpdateChannel = ToChannelLabel(_services.Settings.UpdateChannel);
            UpdateMode = ToModeLabel(_services.Settings.UpdateMode);
            Theme = ToThemeLabel(_services.Settings.Theme);
            UiFont = string.IsNullOrWhiteSpace(_services.Settings.UiFontFamily)
                ? UiFontService.DefaultLabel
                : _services.Settings.UiFontFamily;
            AccentHue = _services.Settings.AccentHue;
            AccentSaturation = _services.Settings.AccentSaturation;
            AccentLightAdjust = _services.Settings.AccentLightAdjust;
            FontScale = _services.Settings.FontScale;
            ApplicationOpacity = Math.Clamp(_services.Settings.ApplicationOpacity, 0, 100);
            WindowBackdropBlur = _services.Settings.WindowBackdropBlur;
            ApplyShortcuts(_services.Settings.CustomShortcuts);
            ConfigStatus = "配置已导入";
            Status = ConfigStatus;
        }
        finally
        {
            _suppressAutoSave = wasSuppressed;
        }
        RunSave(applyGameDirectory: false);
    }

    private void ApplyShortcuts(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            var dto = System.Text.Json.JsonSerializer.Deserialize<ShortcutDto>(json);
            if (dto is null) return;
            ShortcutTab1 = dto.Tab1;
            ShortcutTab2 = dto.Tab2;
            ShortcutTab3 = dto.Tab3;
            ShortcutTab4 = dto.Tab4;
            ShortcutTab5 = dto.Tab5;
            ShortcutSearch = dto.Search;
        }
        catch { /* 保留默认快捷键 */ }
    }

    private sealed record ShortcutDto(
        string Tab1, string Tab2, string Tab3, string Tab4, string Tab5, string Search);

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _javaInstallCts?.Cancel();
        _javaInstallCts?.Dispose();
        _javaInstallCts = null;
    }
}


public sealed record ThemePreset(string Name, int Hue, int Saturation, int LightAdjust);

/// <summary>“功能隐藏”分组中的一项，勾选即写入设置。</summary>
public partial class HiddenFeatureItem(FeatureDefinition definition, FeatureVisibility features) : ObservableObject
{
    public string Key => definition.Key;
    public string Label => definition.Label;

    public bool IsHidden
    {
        get => features.IsHidden(Key);
        set
        {
            if (value == IsHidden) return;
            features.SetHidden(Key, value);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsHidden));
}

public sealed record HiddenFeatureGroup(string Title, IReadOnlyList<HiddenFeatureItem> Items);
