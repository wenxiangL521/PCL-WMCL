using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Update;

namespace WMCL.App.Features.More;

public partial class AboutViewModel : ViewModelBase, IDisposable
{
    private readonly IUserSettingsProvider _services;
    private UpdateInfo? _pendingUpdate;
    private PreparedUpdate? _preparedUpdate;
    private CancellationTokenSource? _updateCts;

    public AboutViewModel(IUserSettingsProvider services)
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
        + "Plain Craft Launcher (PCL) WMCL 是第三方独立重写版本，由 wenxiangL 维护，与 PCL 官方无隶属关系。";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _canDownloadUpdate;

    [ObservableProperty]
    private bool _canInstallUpdate;

    [ObservableProperty]
    private bool _canCancelUpdate;

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
    private void OpenPclCe()
    {
        OpenUrl("https://github.com/PCL-Community/PCL-CE");
        Status = "已在浏览器中打开 PCL-CE 仓库";
    }

    [RelayCommand]
    private void OpenPclMe()
    {
        OpenUrl("https://github.com/TheUnknownThing/PCL-ME");
        Status = "已在浏览器中打开 PCL-ME 仓库";
    }

    [RelayCommand]
    private void OpenLogs() => OpenPath(WmclPaths.LogsDirectory);

    [RelayCommand]
    private void CancelUpdate()
    {
        _updateCts?.Cancel();
        UpdateStatus = "正在取消更新操作…";
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsUpdating) return;
        IsUpdating = true;
        CanDownloadUpdate = false;
        var cancellation = new CancellationTokenSource();
        _updateCts = cancellation;
        CanCancelUpdate = true;
        UpdateStatus = "正在检查更新…";
        try
        {
            var includePrerelease = _services.Settings.UpdateChannel == "beta";
            var result = await UpdateFeed.QueryAsync(
                AppInfo.Version, includePrerelease, cancellation.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } info:
                    _pendingUpdate = info;
                    CanDownloadUpdate = true;
                    UpdateStatus = $"发现新版本 v{info.Version}"
                                   + (string.IsNullOrWhiteSpace(info.Notes) ? "" : $"：{info.Notes}");
                    break;
                case UpdateCheckStatus.UpToDate:
                    UpdateStatus = $"已是最新版本（v{AppInfo.Version}）";
                    break;
                case UpdateCheckStatus.MissingUrl:
                case UpdateCheckStatus.InvalidManifest:
                    UpdateStatus = "更新清单无效：" + (result.Error ?? "内容无法解析");
                    break;
                case UpdateCheckStatus.IncomparableVersion:
                    UpdateStatus = "无法检查更新：" + (result.Error ?? "版本号不可比较");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            UpdateStatus = "已取消检查更新";
        }
        catch (HttpRequestException ex)
        {
            UpdateStatus = "检查更新失败：网络请求失败（" + ex.Message + "）";
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_updateCts, cancellation)) _updateCts = null;
            cancellation.Dispose();
            IsUpdating = false;
            CanCancelUpdate = false;
            CanDownloadUpdate = _pendingUpdate is not null;
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
        var cancellation = new CancellationTokenSource();
        _updateCts = cancellation;
        CanCancelUpdate = true;
        UpdateStatus = $"正在下载 v{_pendingUpdate.Version}…";

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                UpdateProgress = (update.Percent ?? 0) * 100;
                UpdateProgressText = update.Detail ?? update.Stage;
            });
            var path = await UpdateService.DownloadAsync(
                _pendingUpdate, progress, cancellation.Token);
            UpdateProgress = 100;

            if (UpdateService.CanApplyInPlace)
            {
                UpdateStatus = "正在准备自动更新…";
                _preparedUpdate = await UpdateService.PrepareAsync(
                    path, _pendingUpdate.Version, cancellation.Token);
                CanInstallUpdate = true;
                UpdateStatus = $"更新包已就绪（v{_pendingUpdate.Version}），点击“安装更新并重启”。";
            }
            else
            {
                UpdateStatus = $"更新包已下载：{path}。当前运行形态不支持自动替换，请手动覆盖后重启。";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            UpdateStatus = "已取消下载更新";
        }
        catch (InvalidDataException ex)
        {
            UpdateStatus = "更新包校验失败：" + ex.Message;
        }
        catch (HttpRequestException ex)
        {
            UpdateStatus = "下载更新失败：网络请求失败（" + ex.Message + "）";
        }
        catch (Exception ex)
        {
            UpdateStatus = "下载更新失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_updateCts, cancellation)) _updateCts = null;
            cancellation.Dispose();
            IsUpdating = false;
            CanCancelUpdate = false;
            CanDownloadUpdate = _pendingUpdate is not null;
            CanInstallUpdate = _preparedUpdate is not null;
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

    /// <summary>按设置里的更新模式执行：仅提示 / 自动下载 / 自动下载并安装。</summary>
    public async Task AutoUpdateAsync()
    {
        var mode = _services.Settings.UpdateMode;
        if (mode == "off") return;

        await CheckUpdateAsync();
        if (_pendingUpdate is null) return;
        if (mode == "prompt")
        {
            UpdateStatus += "（可在“设置 → 更新方式”改为自动下载）";
            return;
        }

        await DownloadUpdateAsync();
        if (_preparedUpdate is null) return;
        if (mode == "auto") await InstallUpdateAsync();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            ExternalLauncher.Open(url);
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
            ExternalLauncher.Open(path);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;
    }
}
