using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App.ViewModels;

public partial class ToolboxViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public ToolboxViewModel(AppServices services)
    {
        _services = services;
    }

    public string LogsDirectory => WmclPaths.LogsDirectory;
    public string GameDirectory => _services.Settings.GameDirectory;
    public string ConfigDirectory => WmclPaths.ConfigDirectory;

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private void OpenLogs() => Open(WmclPaths.LogsDirectory);

    [RelayCommand]
    private void OpenGame() => Open(_services.Settings.GameDirectory);

    [RelayCommand]
    private void OpenConfig() => Open(WmclPaths.ConfigDirectory);

    private void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? path : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : path,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
            Status = "已打开：" + path;
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }
}
