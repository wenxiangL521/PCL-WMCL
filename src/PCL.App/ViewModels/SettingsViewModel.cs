using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;

namespace PCL.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _gameDirectory = services.Settings.GameDirectory;
        _memoryMb = services.Settings.MemoryMb;
        _playerName = services.Settings.PlayerName;
        _javaPath = services.Settings.JavaPath ?? "";
        _mirror = services.Settings.Mirror;
        _updateManifestUrl = services.Settings.UpdateManifestUrl;
        _microsoftClientId = services.Settings.MicrosoftClientId;
        _theme = ToThemeLabel(services.Settings.Theme);
        _curseForgeApiKey = services.Settings.CurseForgeApiKey;
    }

    public ObservableCollection<string> Mirrors { get; } = ["auto", "official", "bmclapi"];
    public ObservableCollection<string> Themes { get; } = ["浅色", "深色", "跟随系统"];
    public string ConfigPath => PclPaths.ConfigFile;

    [ObservableProperty]
    private string _gameDirectory = "";

    [ObservableProperty]
    private decimal _memoryMb = 4096;

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
    private string _curseForgeApiKey = "";

    partial void OnThemeChanged(string value) =>
        ThemeService.Apply(FromThemeLabel(value));

    [ObservableProperty]
    private string _status = "修改后点击保存";

    [RelayCommand]
    private void Save()
    {
        _services.Settings.GameDirectory = PclPaths.Expand(GameDirectory);
        _services.Settings.MemoryMb = (int)MemoryMb;
        _services.Settings.PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? "Steve" : PlayerName;
        _services.Settings.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
        _services.Settings.Mirror = Mirror;
        _services.Settings.UpdateManifestUrl = UpdateManifestUrl.Trim();
        _services.Settings.MicrosoftClientId = MicrosoftClientId.Trim();
        _services.Settings.Theme = FromThemeLabel(Theme);
        _services.Settings.CurseForgeApiKey = CurseForgeApiKey.Trim();
        _services.CurseForge.ApiKey = _services.Settings.CurseForgeApiKey;
        ThemeService.Apply(_services.Settings.Theme);
        _services.SaveSettings();

        GameDirectory = _services.Settings.GameDirectory;
        Status = "已保存。游戏目录修改需重启启动器生效。";
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在扫描 Java…";
        try
        {
            var runtimes = await _services.JavaLocator.DiscoverAsync(forceRefresh: true);
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
