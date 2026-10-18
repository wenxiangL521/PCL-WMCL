using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Core.Update;

namespace PCL.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private UpdateInfo? _pendingUpdate;

    public AboutViewModel(AppServices services)
    {
        _services = services;
    }

    public string Version => $"{AppInfo.Version} · M7";
    public string Runtime => $".NET {Environment.Version} · Avalonia 11.3.10";

    public string Description =>
        "以原版 Plain Craft Launcher 2 为规格的 Linux 优先重写。"
        + "当前版本已支持原版 Minecraft 的安装、校验、Java 选择与启动，"
        + "以及 Fabric 与 Modrinth。";

    public string Attribution =>
        "参考项目：Plain Craft Launcher 2（作者：龙腾猫跃，Hex-Dragon/PCL2）。"
        + "本项目为非官方重写，与原作者无隶属关系。";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _canDownloadUpdate;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    private string _updateProgressText = "";

    [RelayCommand]
    private void OpenProject()
    {
        OpenUrl("https://github.com/Hex-Dragon/PCL2");
        Status = "已在浏览器中打开原版仓库";
    }

    [RelayCommand]
    private void OpenLogs() => OpenPath(PclPaths.LogsDirectory);

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var url = _services.Settings.UpdateManifestUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            UpdateStatus = "未配置更新源：请在“设置 → 更新源”填写 latest.json 地址";
            return;
        }

        IsUpdating = true;
        CanDownloadUpdate = false;
        UpdateStatus = "正在检查更新…";
        try
        {
            var info = await UpdateService.CheckAsync(url, AppInfo.Version);
            if (info is null)
            {
                UpdateStatus = $"已是最新版本（v{AppInfo.Version}）";
                return;
            }

            _pendingUpdate = info;
            CanDownloadUpdate = true;
            UpdateStatus = $"发现新版本 v{info.Version}"
                           + (string.IsNullOrWhiteSpace(info.Notes) ? "" : $"：{info.Notes}");
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate is null || IsUpdating) return;

        IsUpdating = true;
        CanDownloadUpdate = false;
        UpdateProgress = 0;
        UpdateProgressText = "";
        UpdateStatus = $"正在下载 v{_pendingUpdate.Version}…";

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                UpdateProgress = (update.Percent ?? 0) * 100;
                UpdateProgressText = update.Detail ?? update.Stage;
            });
            var path = await UpdateService.DownloadAsync(_pendingUpdate, progress);
            UpdateStatus = $"更新包已下载：{path}。解压覆盖后重启即可完成更新。";
            UpdateProgress = 100;
        }
        catch (Exception ex)
        {
            UpdateStatus = "下载更新失败：" + ex.Message;
        }
        finally
        {
            IsUpdating = false;
            UpdateProgress = 0;
            UpdateProgressText = "";
        }
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
