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
    }

    public ObservableCollection<string> Mirrors { get; } = ["auto", "official", "bmclapi"];
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
    private string _status = "修改后点击保存";

    [RelayCommand]
    private void Save()
    {
        _services.Settings.GameDirectory = PclPaths.Expand(GameDirectory);
        _services.Settings.MemoryMb = (int)MemoryMb;
        _services.Settings.PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? "Steve" : PlayerName;
        _services.Settings.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
        _services.Settings.Mirror = Mirror;
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
                ? "未发现 Java，请手动填写路径或安装 Java 21"
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
