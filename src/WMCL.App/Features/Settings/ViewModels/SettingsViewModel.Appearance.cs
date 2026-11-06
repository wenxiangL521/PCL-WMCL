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

public partial class SettingsViewModel
{
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
    private string _customLogoText = "";

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
}
