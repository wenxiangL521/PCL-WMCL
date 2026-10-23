using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Forgelike;

namespace WMCL.App.ViewModels;

public partial class CleanroomViewModel : ViewModelBase
{
    public const string Website = "https://cleanroommc.com/zh/";

    public event EventHandler<string>? VersionInstalled;

    private readonly Func<CancellationToken, Task<IReadOnlyList<CleanroomVersionInfo>>> _load;
    private readonly Func<CleanroomVersionInfo, string, IProgress<JobProgress>?, CancellationToken, Task<string>> _download;
    private readonly Func<CleanroomVersionInfo, IProgress<JobProgress>?, IProgress<string>?, CancellationToken, Task<string>>? _install;
    private CancellationTokenSource? _installCts;

    public CleanroomViewModel(AppServices? services = null, CleanroomService? service = null)
    {
        var actual = service ?? new CleanroomService();
        _load = actual.GetVersionsAsync;
        _download = actual.DownloadInstallerAsync;
        if (services is not null)
            _install = (version, progress, log, ct) => InstallWithServicesAsync(services, version, progress, log, ct);
    }

    internal CleanroomViewModel(
        Func<CancellationToken, Task<IReadOnlyList<CleanroomVersionInfo>>> load,
        Func<CleanroomVersionInfo, string, IProgress<JobProgress>?, CancellationToken, Task<string>> download,
        Func<CleanroomVersionInfo, IProgress<JobProgress>?, IProgress<string>?, CancellationToken, Task<string>>? install = null)
    {
        _load = load;
        _download = download;
        _install = install;
    }

    private static async Task<string> InstallWithServicesAsync(
        AppServices services, CleanroomVersionInfo version,
        IProgress<JobProgress>? progress, IProgress<string>? log, CancellationToken ct)
    {
        var root = services.Settings.GameDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("请先在设置中选择游戏目录。");
        var java = await services.JavaProvisioner.EnsureAsync(
            new JavaRequirement(new Version(25, 0, 0)), progress, ct);
        return await new ForgelikeInstaller(new VersionRepository(root)).InstallAsync(
            ForgelikeKind.Cleanroom, "1.12.2", version.Tag, java.Path,
            progress, log, ct);
    }

    public ObservableCollection<CleanroomVersionInfo> Versions { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = "正在读取 Cleanroom 版本…";

    [ObservableProperty]
    private string _downloadStatus = "";

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _installStatus = "";

    public bool CanInstall => _install is not null && !IsInstalling;

    partial void OnIsInstallingChanged(bool value) =>
        OnPropertyChanged(nameof(CanInstall));

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在读取 Cleanroom 版本…";
        try
        {
            var versions = await _load(CancellationToken.None);
            Versions.Clear();
            foreach (var version in versions) Versions.Add(version);
            Status = Versions.Count == 0
                ? "没有可用的 Cleanroom 版本"
                : $"{Versions.Count} 个 Cleanroom 版本 · 仅支持 Minecraft 1.12.2";
        }
        catch (Exception ex)
        {
            Status = "读取 Cleanroom 版本失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallAsync(CleanroomVersionInfo version)
    {
        if (_install is null || IsInstalling) return;
        IsInstalling = true;
        InstallStatus = $"正在安装 Cleanroom {version.Tag}（Java 25）…";
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        var ct = _installCts.Token;
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                InstallStatus = update.Detail ?? update.Stage;
            });
            var log = new Progress<string>(line => InstallStatus = line);
            var id = await _install(version, progress, log, ct);
            Progress = 100;
            InstallStatus = "Cleanroom 已安装：" + id;
            VersionInstalled?.Invoke(this, id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            InstallStatus = "已取消 Cleanroom 安装";
        }
        catch (Exception ex)
        {
            InstallStatus = "Cleanroom 安装失败：" + ex.Message;
        }
        finally
        {
            _installCts?.Dispose();
            _installCts = null;
            IsInstalling = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _installCts?.Cancel();
        InstallStatus = "正在取消 Cleanroom 安装…";
    }

    public async Task DownloadInstallerAsync(CleanroomVersionInfo version, string destination)
    {
        if (IsDownloading) return;
        IsDownloading = true;
        Progress = 0;
        DownloadStatus = $"正在下载 {version.InstallerFileName}…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                DownloadStatus = update.Detail ?? update.Stage;
            });
            var path = await _download(version, destination, progress, CancellationToken.None);
            Progress = 100;
            DownloadStatus = "安装器已保存：" + path;
        }
        catch (Exception ex)
        {
            DownloadStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsDownloading = false;
            Progress = 0;
        }
    }
}
