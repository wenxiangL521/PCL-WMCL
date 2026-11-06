using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Update;

namespace WMCL.App.Features.Settings;

public partial class UpdateViewModel : ViewModelBase
{
    private UpdateInfo? _pendingUpdate;
    private PreparedUpdate? _preparedUpdate;
    private CancellationTokenSource? _updateCts;

    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check;

    public UpdateViewModel(AppServices services)
        : this(services, null)
    {
    }

    /// <summary>测试可注入检查来源，避免依赖真实 GitHub 请求。</summary>
    internal UpdateViewModel(
        AppServices services,
        Func<CancellationToken, Task<UpdateCheckResult>>? check)
    {
        _check = check ?? (ct => UpdateFeed.QueryAsync(
            AppInfo.Version, services.Settings.UpdateChannel == "beta", ct));
    }

    public string CurrentVersion => $"v{AppInfo.Version} · {AppInfo.FullName}";
    public string Runtime =>
        $".NET {Environment.Version} · Avalonia {typeof(Avalonia.Controls.Control).Assembly.GetName().Version?.ToString(3)}";

    [ObservableProperty]
    private string _status = "填写更新源后检查更新";

    [ObservableProperty]
    private bool _canDownloadUpdate;

    [ObservableProperty]
    private bool _canInstallUpdate;

    [ObservableProperty]
    private bool _canCancelUpdate;

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
        CanCancelUpdate = IsBusy && _updateCts is not null;
    }

    [RelayCommand]
    private void Cancel()
    {
        _updateCts?.Cancel();
        Status = "正在取消更新操作…";
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _pendingUpdate = null;
        _preparedUpdate = null;
        var cancellation = new CancellationTokenSource();
        _updateCts = cancellation;
        UpdateCommands();
        Status = "正在检查更新…";
        try
        {
            var result = await _check(cancellation.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } info:
                    _pendingUpdate = info;
                    Status = $"发现新版本 v{info.Version}"
                             + (string.IsNullOrWhiteSpace(info.Notes) ? "" : $"：{info.Notes}");
                    break;
                case UpdateCheckStatus.UpToDate:
                    Status = $"已是最新版本（v{AppInfo.Version}）";
                    break;
                case UpdateCheckStatus.MissingUrl:
                    Status = "更新清单无效：" + (result.Error ?? "缺少下载地址");
                    break;
                case UpdateCheckStatus.InvalidManifest:
                    Status = "更新清单无效：" + (result.Error ?? "内容无法解析");
                    break;
                case UpdateCheckStatus.IncomparableVersion:
                    Status = "无法检查更新：" + (result.Error ?? "版本号不可比较");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "已取消检查更新";
        }
        catch (HttpRequestException ex)
        {
            Status = "检查更新失败：网络请求失败（" + ex.Message + "）";
        }
        catch (Exception ex)
        {
            Status = "检查更新失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_updateCts, cancellation)) _updateCts = null;
            cancellation.Dispose();
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
        var cancellation = new CancellationTokenSource();
        _updateCts = cancellation;
        UpdateCommands();
        Status = $"正在下载 v{_pendingUpdate.Version}…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var path = await UpdateService.DownloadAsync(_pendingUpdate, progress, cancellation.Token);
            Progress = 100;
            if (UpdateService.CanApplyInPlace)
            {
                _preparedUpdate = await UpdateService.PrepareAsync(
                    path, _pendingUpdate.Version, cancellation.Token);
                Status = $"更新包已就绪（v{_pendingUpdate.Version}），点击“安装并重启”。";
            }
            else
            {
                Status = $"更新包已下载：{path}。当前运行形态不支持自动替换，请手动覆盖。";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "已取消下载更新";
        }
        catch (InvalidDataException ex)
        {
            Status = "更新包校验失败：" + ex.Message;
        }
        catch (HttpRequestException ex)
        {
            Status = "下载更新失败：网络请求失败（" + ex.Message + "）";
        }
        catch (Exception ex)
        {
            Status = "下载更新失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_updateCts, cancellation)) _updateCts = null;
            cancellation.Dispose();
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
