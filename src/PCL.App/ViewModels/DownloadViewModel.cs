using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core.Jobs;
using PCL.Minecraft;

namespace PCL.App.ViewModels;

public partial class DownloadViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly VersionCatalog _catalog;
    private readonly List<VersionItem> _all = [];
    private CancellationTokenSource? _cts;

    public DownloadViewModel(AppServices services)
    {
        _services = services;
        _catalog = new VersionCatalog(services.Repository);
    }

    public ObservableCollection<VersionItem> Versions { get; } = [];
    public ObservableCollection<string> Filters { get; } = ["全部", "正式版", "快照"];
    public bool HasVersions => Versions.Count > 0;
    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";
    public string SelectedVersionInfo => SelectedVersion is null
        ? "从左侧选择一个版本"
        : $"{SelectedVersion.TypeText} · {SelectedVersion.ReleaseTime ?? "未知时间"}";
    public string GameDirectory => _services.Settings.GameDirectory;

    [ObservableProperty]
    private string _selectedFilter = "全部";

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _canCancel;

    public async Task LoadAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Status = "正在获取可下载版本…";
        try
        {
            var items = await _catalog.LoadAsync();
            _all.Clear();
            _all.AddRange(items);
            ApplyFilter();
            Status = $"共 {items.Count} 个版本，选择后点击安装";
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(SelectedVersionInfo));
        UpdateCommands();
    }

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedVersion is { IsInstalled: false };
    }

    private void ApplyFilter()
    {
        Versions.Clear();
        foreach (var item in _all.Where(MatchesFilter))
            Versions.Add(item);

        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
        {
            SelectedVersion = Versions.FirstOrDefault(v => v.Type == "release" && v.IsLatest)
                              ?? Versions.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasVersions));
    }

    private bool MatchesFilter(VersionItem item) => SelectedFilter switch
    {
        "正式版" => item.Type == "release",
        "快照" => item.Type == "snapshot",
        _ => true
    };

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedVersion is null || IsBusy) return;

        var target = SelectedVersion;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {target.Id}…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });

            var result = await _services.Installer.InstallAsync(
                target.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = 16
                },
                progress,
                _cts.Token);

            target.IsInstalled = true;
            Status = $"安装完成：{result.Files} 个文件，{FormatBytes(result.Bytes)}";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            Status = "已取消安装";
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            Progress = 0;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "正在取消…";
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0.0}%";
        var speed = progress.BytesPerSecond is > 0
            ? $" · {progress.BytesPerSecond / 1048576.0:0.0} MB/s"
            : "";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : $" · {progress.Detail}";
        return $"{progress.Stage} {percent}{speed}{detail}".Trim();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.0} {units[unit]}";
    }
}
