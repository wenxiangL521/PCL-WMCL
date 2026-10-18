using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string Version => "0.1.0 · M6";
    public string Runtime => $".NET {Environment.Version} · Avalonia 11.3.10";

    public string Description =>
        "以原版 Plain Craft Launcher 2 为规格的 Linux 优先重写。"
        + "当前版本已支持原版 Minecraft 的安装、校验、Java 选择与启动。";

    public string Attribution =>
        "参考项目：Plain Craft Launcher 2（作者：龙腾猫跃，Hex-Dragon/PCL2）。"
        + "本项目为非官方重写，与原作者无隶属关系。";

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private void OpenProject()
    {
        OpenUrl("https://github.com/Hex-Dragon/PCL2");
        Status = "已在浏览器中打开原版仓库";
    }

    [RelayCommand]
    private void OpenLogs()
    {
        OpenPath(PCL.Core.PclPaths.LogsDirectory);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo("xdg-open", url) { UseShellExecute = false };
            Process.Start(startInfo);
        }
        catch
        {
            // ignore
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", path)
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", path)
                    : new ProcessStartInfo("xdg-open", path);
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // ignore
        }
    }
}
