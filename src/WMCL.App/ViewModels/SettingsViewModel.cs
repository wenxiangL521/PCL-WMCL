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

public partial class SettingsViewModel : ViewModelBase
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
        _proxyUrl = services.Settings.ProxyUrl;
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
        _updateManifestUrl = services.Settings.UpdateManifestUrl;
        _microsoftClientId = services.Settings.MicrosoftClientId;
        _theme = ToThemeLabel(services.Settings.Theme);
        _accentHue = services.Settings.AccentHue;
        _accentSaturation = services.Settings.AccentSaturation;
        _accentLightAdjust = services.Settings.AccentLightAdjust;
        if (ThemeService.IsWindowsBlueOnly)
        {
            _accentHue = 210;
            _accentSaturation = 85;
            _accentLightAdjust = 0;
        }
        _curseForgeApiKey = services.Settings.CurseForgeApiKey;
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
        nameof(DownloadSpeedLimitMbps) or nameof(DownloadThreads) or
        nameof(CustomLogoPath) or nameof(CustomHomePagePath) or nameof(MusicFolder) or
        nameof(MusicVolume) or nameof(MusicStart) or nameof(MusicPauseOnGame) or
        nameof(BackgroundImagePath) or nameof(BackgroundImageFolder) or
        nameof(BackgroundColorOverlay) or nameof(BackgroundImageOpacity) or
        nameof(BackgroundImageBlur) or nameof(PlayerName) or nameof(JavaPath) or
        nameof(Mirror) or nameof(UpdateManifestUrl) or nameof(MicrosoftClientId) or
        nameof(Theme) or nameof(AccentHue) or nameof(AccentSaturation) or
        nameof(AccentLightAdjust) or nameof(CurseForgeApiKey);

    public ObservableCollection<string> Mirrors { get; } = ["auto", "official", "bmclapi"];
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
    public ObservableCollection<string> JavaInstallVersions { get; } = ["8", "17", "21"];

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
    private string _proxyUrl = "";

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
    private string _microsoftClientId = "";

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
    private string _curseForgeApiKey = "";

    [ObservableProperty]
    private string _curseForgeTestStatus = "";

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

    partial void OnAccentHueChanged(double value)
    {
        if (ThemeService.IsWindowsBlueOnly)
        {
            if (value != 210) AccentHue = 210;
            return;
        }
        PreviewTheme();
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

    public void SetSection(string key)
    {
        ShowLaunchSettings = key == "settings-launch";
        ShowJavaSettings = key == "settings-java";
        ShowPersonalization = key == "settings-ui";
        ShowOtherSettings = key == "settings-other";
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
        _services.Settings.ProxyUrl = ProxyUrl.Trim();
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
        _services.Settings.UpdateManifestUrl = UpdateManifestUrl.Trim();
        _services.Settings.MicrosoftClientId = MicrosoftClientId.Trim();
        _services.Settings.Theme = FromThemeLabel(Theme);
        _services.Settings.AccentHue = (int)AccentHue;
        _services.Settings.AccentSaturation = (int)AccentSaturation;
        _services.Settings.AccentLightAdjust = (int)AccentLightAdjust;
        var enteredKey = CurseForgeApiKey.Trim();
        var effectiveKey = string.IsNullOrWhiteSpace(enteredKey)
            ? WmclEnvironment.Get("CURSEFORGE_API_KEY") ?? ""
            : enteredKey;
        _services.Settings.CurseForgeApiKey = enteredKey;
        _services.CurseForge.ApiKey = effectiveKey;
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
            var runtime = await _services.JavaProvisioner.EnsureAsync(
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
    private async Task TestCurseForgeKeyAsync()
    {
        CurseForgeTestStatus = "正在验证 CurseForge Key…";
        try
        {
            var key = CurseForgeApiKey.Trim();
            if (string.IsNullOrWhiteSpace(key))
                key = WmclEnvironment.Get("CURSEFORGE_API_KEY") ?? "";
            _services.CurseForge.ApiKey = key;
            await _services.CurseForge.TestKeyAsync();
            CurseForgeTestStatus = "CurseForge Key 可用";
        }
        catch (Exception ex)
        {
            CurseForgeTestStatus = "CurseForge Key 验证失败：" + ex.Message;
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
}


public sealed record ThemePreset(string Name, int Hue, int Saturation, int LightAdjust);
