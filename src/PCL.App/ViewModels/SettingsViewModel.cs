using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Launch.Java;

namespace PCL.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _gameDirectory = services.Settings.GameDirectory;
        _memoryMb = services.Settings.MemoryMb;
        _windowWidth = services.Settings.Width;
        _windowHeight = services.Settings.Height;
        _globalJvmArgs = services.Settings.ExtraJvmArgs;
        _globalGameArgs = services.Settings.ExtraGameArgs;
        _proxyUrl = services.Settings.ProxyUrl;
        _customLogoPath = services.Settings.CustomLogoPath ?? "";
        _musicFolder = services.Settings.MusicFolder ?? "";
        _musicVolume = services.Settings.MusicVolume;
        _musicStart = services.Settings.MusicStart;
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
        _curseForgeApiKey = services.Settings.CurseForgeApiKey;
    }

    public ObservableCollection<string> Mirrors { get; } = ["auto", "official", "bmclapi"];
    public ObservableCollection<string> Themes { get; } = ["浅色", "深色", "跟随系统"];
    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = [];
    public string ConfigPath => PclPaths.ConfigFile;

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
    private string _proxyUrl = "";

    [ObservableProperty]
    private string _customLogoPath = "";

    [ObservableProperty]
    private string _musicFolder = "";

    [ObservableProperty]
    private decimal _musicVolume = 70;

    [ObservableProperty]
    private bool _musicStart;

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
    private string _curseForgeApiKey = "";

    [ObservableProperty]
    private JavaRuntime? _selectedJavaRuntime;

    partial void OnSelectedJavaRuntimeChanged(JavaRuntime? value)
    {
        if (value is not null) JavaPath = value.Path;
    }

    partial void OnThemeChanged(string value) => PreviewTheme();

    partial void OnAccentHueChanged(double value) => PreviewTheme();

    partial void OnAccentSaturationChanged(double value) => PreviewTheme();

    partial void OnAccentLightAdjustChanged(double value) => PreviewTheme();

    private void PreviewTheme() =>
        ThemeService.Apply(
            FromThemeLabel(Theme), (int)AccentHue, (int)AccentSaturation, (int)AccentLightAdjust);

    [ObservableProperty]
    private bool _showLaunchSettings = true;

    [ObservableProperty]
    private bool _showPersonalization;

    [ObservableProperty]
    private bool _showOtherSettings;

    public void SetSection(string key)
    {
        ShowLaunchSettings = key == "settings-launch";
        ShowPersonalization = key == "settings-ui";
        ShowOtherSettings = key == "settings-other";
    }

    [ObservableProperty]
    private string _status = "修改后点击保存";

    [RelayCommand]
    private void Save()
    {
        _services.Settings.GameDirectory = PclPaths.Expand(GameDirectory);
        _services.Settings.MemoryMb = (int)MemoryMb;
        _services.Settings.Width = Math.Max(320, (int)WindowWidth);
        _services.Settings.Height = Math.Max(240, (int)WindowHeight);
        _services.Settings.ExtraJvmArgs = GlobalJvmArgs.Trim();
        _services.Settings.ExtraGameArgs = GlobalGameArgs.Trim();
        _services.Settings.ProxyUrl = ProxyUrl.Trim();
        _services.Settings.CustomLogoPath = CustomLogoPath.Trim();
        _services.Settings.MusicFolder = MusicFolder.Trim();
        _services.Settings.MusicVolume = Math.Clamp((int)MusicVolume, 0, 100);
        _services.Settings.MusicStart = MusicStart;
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
        _services.Settings.CurseForgeApiKey = CurseForgeApiKey.Trim();
        _services.CurseForge.ApiKey = _services.Settings.CurseForgeApiKey;
        ThemeService.Apply(
            _services.Settings.Theme,
            _services.Settings.AccentHue,
            _services.Settings.AccentSaturation,
            _services.Settings.AccentLightAdjust);
        _services.SaveSettings();

        GameDirectory = _services.Settings.GameDirectory;
        Status = "已保存。游戏目录修改需重启启动器生效。";
    }

    public async Task LoadAsync()
    {
        if (JavaRuntimes.Count == 0)
            await DetectJavaAsync();
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
    private void OpenConfigFolder() => OpenPath(PclPaths.ConfigDirectory);

    [RelayCommand]
    private void OpenGameFolder()
    {
        var path = PclPaths.Expand(GameDirectory);
        if (Directory.Exists(path)) OpenPath(path);
    }

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
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
                startInfo = new ProcessStartInfo("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
                startInfo = new ProcessStartInfo("open", path);
            else
                startInfo = new ProcessStartInfo("xdg-open", path);

            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // ignore
        }
    }
}
