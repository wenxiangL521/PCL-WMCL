using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Update;

namespace WMCL.App.ViewModels;

public partial class UpdateViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private UpdateInfo? _pendingUpdate;
    private PreparedUpdate? _preparedUpdate;

    public UpdateViewModel(AppServices services)
    {
        _services = services;
        _manifestUrl = services.Settings.UpdateManifestUrl;
    }

    public string CurrentVersion => $"v{AppInfo.Version} · {AppInfo.FullName}";
    public string Runtime => $".NET {Environment.Version} · Avalonia 11.3.10";

    [ObservableProperty]
    private string _manifestUrl = "";

    [ObservableProperty]
    private string _status = "填写更新源后检查更新";

    [ObservableProperty]
    private bool _canDownloadUpdate;

    [ObservableProperty]
    private bool _canInstallUpdate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanDownloadUpdate = !IsBusy && _pendingUpdate is not null;
        CanInstallUpdate = !IsBusy && _preparedUpdate is not null;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        var url = ManifestUrl.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            Status = "未配置更新源：请填写 latest.json 地址";
            return;
        }

        _services.Settings.UpdateManifestUrl = url;
        _services.SaveSettings();
        IsBusy = true;
        _pendingUpdate = null;
        _preparedUpdate = null;
        UpdateCommands();
        Status = "正在检查更新…";
        try
        {
            var info = await UpdateService.CheckAsync(url, AppInfo.Version);
            if (info is null)
            {
                Status = $"已是最新版本（v{AppInfo.Version}）";
                return;
            }

            _pendingUpdate = info;
            Status = $"发现新版本 v{info.Version}"
                     + (string.IsNullOrWhiteSpace(info.Notes) ? "" : $"：{info.Notes}");
        }
        catch (Exception ex)
        {
            Status = "检查更新失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_pendingUpdate is null || IsBusy) return;
        IsBusy = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在下载 v{_pendingUpdate.Version}…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var path = await UpdateService.DownloadAsync(_pendingUpdate, progress);
            Progress = 100;
            if (UpdateService.CanApplyInPlace)
            {
                _preparedUpdate = await UpdateService.PrepareAsync(path, _pendingUpdate.Version);
                Status = $"更新包已就绪（v{_pendingUpdate.Version}），点击“安装并重启”。";
            }
            else
            {
                Status = $"更新包已下载：{path}。当前运行形态不支持自动替换，请手动覆盖。";
            }
        }
        catch (Exception ex)
        {
            Status = "下载更新失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            ProgressText = "";
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Install()
    {
        if (_preparedUpdate is null || IsBusy) return;
        try
        {
            Status = "正在重启以完成更新…";
            UpdateService.ApplyAndRestart(_preparedUpdate);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Status = "安装更新失败：" + ex.Message;
        }
    }
}
