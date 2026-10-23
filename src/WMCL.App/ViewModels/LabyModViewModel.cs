using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.Core.Jobs;
using WMCL.Modding.Forgelike;

namespace WMCL.App.ViewModels;

public partial class LabyModViewModel : ViewModelBase
{
    public const string Website = "https://labymod.net";

    private readonly Func<CancellationToken, Task<IReadOnlyList<LabyModVersionInfo>>> _load;
    private readonly Func<LabyModVersionInfo, string, IProgress<JobProgress>?, CancellationToken, Task<string>> _download;

    public LabyModViewModel(LabyModService? service = null)
    {
        var actual = service ?? new LabyModService();
        _load = actual.GetVersionsAsync;
        _download = actual.DownloadInstallerAsync;
    }

    internal LabyModViewModel(
        Func<CancellationToken, Task<IReadOnlyList<LabyModVersionInfo>>> load,
        Func<LabyModVersionInfo, string, IProgress<JobProgress>?, CancellationToken, Task<string>> download)
    {
        _load = load;
        _download = download;
    }

    public ObservableCollection<LabyModVersionInfo> Versions { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = "正在读取 LabyMod 版本…";

    [ObservableProperty]
    private string _downloadStatus = "";

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在读取 LabyMod 版本…";
        try
        {
            var versions = await _load(CancellationToken.None);
            Versions.Clear();
            foreach (var version in versions) Versions.Add(version);
            Status = Versions.Count == 0
                ? "没有可用的 LabyMod 版本"
                : "LabyMod 正式版 / 快照版安装器";
        }
        catch (Exception ex)
        {
            Status = "读取 LabyMod 版本失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadInstallerAsync(LabyModVersionInfo version, string destination)
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
