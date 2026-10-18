using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Update;

namespace WMCL.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private UpdateInfo? _pendingUpdate;
    private PreparedUpdate? _preparedUpdate;

    public AboutViewModel(AppServices services)
    {
        _services = services;
    }

    public string Version => $"{AppInfo.Version} · {AppInfo.FullName}";
    public string Runtime => $".NET {Environment.Version} · Avalonia 11.3.10";

    public string Description =>
        "以原版 Plain Craft Launcher 2 为规格的 Linux 优先重写。"
        + "已支持原版/Fabric/Forge/NeoForge/OptiFine、Modrinth/CurseForge、"
        + "整合包、资源/光影/存档、实例管理与自动更新。";

    public string Attribution =>
        "原版 PCL 作者：龙腾猫跃（LTCat）。"
        + "PCL (WMCL) 是第三方独立重写版本，由 wenxiangL 维护，与 PCL 官方无隶属关系。";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _canDownloadUpdate;

    [ObservableProperty]
    private bool _canInstallUpdate;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    private string _updateProgressText = "";

    [RelayCommand]
    private void OpenSponsor() =>
        OpenUrl("https://meloong.com/afd/a/LTCat");

    [RelayCommand]
    private void OpenPurchase() =>
        OpenUrl("https://www.minecraft.net/zh-hans/store/minecraft-java-bedrock-edition-pc");

    [RelayCommand]
    private void OpenProject()
    {
        OpenUrl("https://github.com/Hex-Dragon/PCL2");
        Status = "已在浏览器中打开原版仓库";
    }

    [RelayCommand]
    private void OpenLogs() => OpenPath(WmclPaths.LogsDirectory);

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
            UpdateProgress = 100;

            if (UpdateService.CanApplyInPlace)
            {
                UpdateStatus = "正在准备自动更新…";
                _preparedUpdate = await UpdateService.PrepareAsync(path, _pendingUpdate.Version);
                CanInstallUpdate = true;
                UpdateStatus = $"更新包已就绪（v{_pendingUpdate.Version}），点击“安装更新并重启”。";
            }
            else
            {
                UpdateStatus = $"更新包已下载：{path}。当前运行形态不支持自动替换，请手动覆盖后重启。";
            }
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

    [RelayCommand]
    private Task InstallUpdateAsync()
    {
        if (_preparedUpdate is null || IsUpdating) return Task.CompletedTask;

        try
        {
            UpdateStatus = "正在重启以完成更新…";
            UpdateService.ApplyAndRestart(_preparedUpdate);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            UpdateStatus = "安装更新失败：" + ex.Message;
        }

        return Task.CompletedTask;
    }

    public async Task AutoUpdateAsync(string manifestUrl)
    {
        _services.Settings.UpdateManifestUrl = manifestUrl;
        await CheckUpdateAsync();
        if (_pendingUpdate is null) return;

        await DownloadUpdateAsync();
        if (_preparedUpdate is not null)
            await InstallUpdateAsync();
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
