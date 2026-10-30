using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class DownloadViewModel : ViewModelBase, ITaskRetrySource
{
    private readonly AppServices _services;
    private readonly Func<bool, Task<IReadOnlyList<VersionItem>>> _loadVersions;
    private readonly List<VersionItem> _all = [];
    private CancellationTokenSource? _cts;
    private Task? _loadTask;
    private int _installRequest;
    private bool _batchInstall;
    internal bool PreserveSelection { get; set; }
    internal bool? IsOfficialVersion(string id) => _all.Count == 0 ? null : _all.Any(version => version.Id == id);

    /// <summary>最近一次失败任务的堆栈载荷（先于 Status 赋值，供任务中心在 Refresh 时消费）。</summary>
    public TaskFailureInfo? LastTaskFailure { get; private set; }

    private TaskRetryPayload? _lastRetryPayload;

    public TaskRetryPayload? CaptureRetryPayload() => _lastRetryPayload;

    public void ClearTaskFailure()
    {
        LastTaskFailure = null;
        _lastRetryPayload = null;
    }

    private VersionItem? FindVersion(string id) =>
        _all.FirstOrDefault(version => string.Equals(version.Id, id, StringComparison.Ordinal));

    public async Task<bool> RetryAsync(TaskRetryPayload payload)
    {
        if (payload.Kind is not (TaskRetryKind.VersionInstall or TaskRetryKind.VersionVerify
            or TaskRetryKind.BatchInstall))
            return false;
        if (IsBusy)
        {
            Status = "重试失败：已有任务进行中";
            return false;
        }
        switch (payload.Kind)
        {
            case TaskRetryKind.VersionInstall:
            case TaskRetryKind.VersionVerify:
                {
                    if (payload.VersionId is not { } id) return false;
                    var version = FindVersion(id);
                    if (version is null)
                    {
                        Status = $"重试失败：版本 {id} 不在当前列表，请重新选择";
                        return false;
                    }
                    await InstallCoreAsync(version, payload.Kind == TaskRetryKind.VersionVerify);
                    return true;
                }
            case TaskRetryKind.BatchInstall:
                {
                    if (!payload.Options.TryGetValue("versions", out var list)) return false;
                    var versions = list
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(FindVersion)
                        .OfType<VersionItem>()
                        .ToList();
                    if (versions.Count == 0)
                    {
                        Status = "重试失败：批量安装的版本均已不在列表，请重新选择";
                        return false;
                    }
                    SelectedVersions.Clear();
                    foreach (var version in versions) SelectedVersions.Add(version);
                    await InstallBatchAsync();
                    return true;
                }
            default:
                return false;
        }
    }

    public DownloadViewModel(AppServices services) : this(services,
        forceRefresh => new VersionCatalog(services.Repository).LoadAsync(int.MaxValue, officialOnly: true, forceRefresh: forceRefresh))
    {
    }

    internal DownloadViewModel(AppServices services, Func<Task<IReadOnlyList<VersionItem>>> loadVersions)
        : this(services, _ => loadVersions())
    {
    }

    internal DownloadViewModel(AppServices services, Func<bool, Task<IReadOnlyList<VersionItem>>> loadVersions)
    {
        _services = services;
        _loadVersions = loadVersions;
    }

    internal async Task<IReadOnlyList<VersionItem>> LoadInstanceCatalogAsync() =>
        await _loadVersions(true) ?? throw new InvalidDataException("Minecraft 版本列表为空");

    internal void AcceptInstanceCatalog(IReadOnlyList<VersionItem> versions)
    {
        _all.Clear();
        _all.AddRange(versions);
        ApplyFilter();
    }

    public ObservableCollection<VersionItem> Versions { get; } = [];
    public ObservableCollection<VersionItem> SelectedVersions { get; } = [];
    public ObservableCollection<string> Filters { get; } = ["全部", "正式版", "快照"];
    public bool HasVersions => Versions.Count > 0;
    public bool HasSelection => SelectedVersions.Count > 0;
    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";
    public string SelectedVersionInfo => SelectedVersion is null
        ? "从版本列表选择 Minecraft 版本"
        : SelectedVersion.Subtitle + (SelectedVersion.IsInstalled ? " · 已安装" : "");
    public string GameDirectory => _services.Settings.GameDirectory;

    [ObservableProperty]
    private string _selectedFilter = "全部";

    [ObservableProperty]
    private bool _hideInstalled;

    partial void OnHideInstalledChanged(bool value) => ApplyFilter();

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private string _loadError = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canVerify;

    public async Task LoadAsync()
    {
        if (_loadTask is { } pending)
        {
            await pending;
            return;
        }
        if (IsBusy) return;
        var loading = LoadCatalogAsync();
        _loadTask = loading;
        try { await loading; }
        finally { _loadTask = null; }
    }

    private async Task LoadCatalogAsync()
    {
        IsBusy = true;
        LoadError = "";
        Status = "正在获取可下载版本…";
        try
        {
            var items = await _loadVersions(false);
            _all.Clear();
            _all.AddRange(items);
            ApplyFilter();
            Status = $"共 {items.Count} 个版本，选择后点击安装";
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
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

    public void RefreshSelectedVersionInfo()
    {
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(SelectedVersionInfo));
        UpdateCommands();
    }

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedVersion is not null;
        CanVerify = !IsBusy && SelectedVersion is not null;
        CanPause = IsBusy && !IsPaused;
        CanResume = IsPaused;
        CanCancel = IsBusy && !IsPaused;
        CanInstallBatch = !IsBusy && SelectedVersions.Count > 0;
    }

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canInstallBatch;

    public bool HasLatestVersion => Versions.Any(v => v.Type == "release" && v.IsLatest);

    [RelayCommand]
    private Task InstallLatestAsync()
    {
        var latest = Versions.FirstOrDefault(v => v.Type == "release" && v.IsLatest)
                     ?? Versions.FirstOrDefault(v => v.Type == "release");
        return InstallCoreAsync(latest, verify: false);
    }

    [RelayCommand]
    private async Task InstallAllAsync()
    {
        if (IsBusy) return;
        var targets = Versions.Where(v => !v.IsInstalled).ToList();
        if (targets.Count == 0) return;
        SelectedVersions.Clear();
        foreach (var v in targets)
            SelectedVersions.Add(v);
        await InstallBatchAsync();
    }

    [RelayCommand]
    private void ClearSelection() => SelectedVersions.Clear();

    private void ApplyFilter()
    {
        Versions.Clear();
        foreach (var item in _all.Where(MatchesFilter))
            Versions.Add(item);

        if (SelectedVersion is null || (!PreserveSelection && !Versions.Contains(SelectedVersion)))
        {
            SelectedVersion = Versions.FirstOrDefault(version => version.Id == SelectedVersion?.Id)
                              ?? Versions.FirstOrDefault(v => v.Type == "release" && v.IsLatest)
                              ?? Versions.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasVersions));
        OnPropertyChanged(nameof(HasLatestVersion));
    }

    private bool MatchesFilter(VersionItem item) => SelectedFilter switch
    {
        "正式版" => item.Type == "release",
        "快照" => item.Type == "snapshot",
        _ => true
    } && (!HideInstalled || !item.IsInstalled);

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private Task InstallAsync() => InstallCoreAsync(SelectedVersion, verify: false);

    [RelayCommand]
    private Task VerifyAsync() => InstallCoreAsync(SelectedVersion, verify: true);

    [RelayCommand]
    private async Task InstallBatchAsync()
    {
        if (SelectedVersions.Count == 0 || IsBusy) return;
        var request = ++_installRequest;
        _batchInstall = true;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Status = $"正在批量安装 {SelectedVersions.Count} 个版本…";
        try
        {
            var root = _services.Settings.GameDirectory;
            using var operation = _services.GameOperations.Acquire(root, "批量安装版本");
            var queue = SelectedVersions.ToList();
            for (var i = 0; i < queue.Count; i++)
            {
                var version = queue[i];
                _cts.Token.ThrowIfCancellationRequested();
                Status = $"正在安装 {version.Id}（{i + 1}/{queue.Count}）…";
                var progress = new Progress<JobProgress>(update =>
                {
                    Progress = (update.Percent ?? 0) * 100;
                    ProgressText = FormatProgress(update);
                    if (update.BytesPerSecond is > 0)
                        SpeedText = $"{update.BytesPerSecond / 1048576.0:0.0} MB/s";
                });
                await _services.Installer.InstallAsync(
                    version.Id,
                    new InstallOptions
                    {
                        Mirror = _services.Mirror,
                        MaxConcurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64)
                    },
                    progress,
                    _cts.Token);
                version.IsInstalled = true;
            }
            Status = $"批量安装完成：{queue.Count} 个版本";
            Progress = 100;
            OnPropertyChanged(nameof(HasLatestVersion));
        }
        catch (OperationCanceledException) when (request == _installRequest)
        {
            Status = "批量安装已取消";
        }
        catch (Exception ex) when (request == _installRequest)
        {
            LastTaskFailure = TaskFailureInfo.From(ex, "批量安装版本");
            _lastRetryPayload = TaskRetryPayload.Create(
                TaskRetryKind.BatchInstall, _services.Settings.GameDirectory,
                options: new Dictionary<string, string>
                { ["versions"] = string.Join(",", SelectedVersions.Select(v => v.Id)) });
            Status = "批量安装失败：" + ex.Message;
        }
        finally
        {
            if (request == _installRequest)
            {
                // 仅取消路径清理 .part（暂停路径保留续传资产）。
                if (!IsPaused) PartFileCleaner.CleanPartFiles(_services.Settings.GameDirectory);
                IsBusy = false;
                CanCancel = false;
                Progress = 0;
                ProgressText = "";
                SpeedText = "";
                SelectedVersions.Clear();
                _cts?.Dispose();
                _cts = null;
            }
            UpdateCommands();
        }
    }

    private async Task InstallCoreAsync(VersionItem? target, bool verify)
    {
        if (target is null || IsBusy) return;
        var request = ++_installRequest;
        _batchInstall = false;
        var root = _services.Settings.GameDirectory;

        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = verify ? $"正在校验 {target.Id}…" : $"正在安装 {target.Id}…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
                if (update.BytesPerSecond is > 0)
                    SpeedText = $"{update.BytesPerSecond / 1048576.0:0.0} MB/s";
                else
                    SpeedText = "";
            });

            using var operation = _services.GameOperations.Acquire(root, verify ? "校验游戏" : "安装游戏");
            var result = await _services.Installer.InstallAsync(
                target.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64),
                    VerifyExisting = verify
                },
                progress,
                _cts.Token);

            target.IsInstalled = true;
            Status = verify
                ? $"校验完成：{result.Files} 个文件，{FormatBytes(result.Bytes)}"
                : $"安装完成：{result.Files} 个文件，{FormatBytes(result.Bytes)}";
            Progress = 100;
        }
        catch (OperationCanceledException) when (request == _installRequest)
        {
            Status = IsPaused ? "已暂停" : (verify ? "已取消校验" : "已取消安装");
        }
        catch (Exception ex) when (request == _installRequest)
        {
            LastTaskFailure = TaskFailureInfo.From(ex, verify ? "校验游戏" : "安装游戏");
            _lastRetryPayload = TaskRetryPayload.Create(
                verify ? TaskRetryKind.VersionVerify : TaskRetryKind.VersionInstall,
                root, target.Id,
                options: new Dictionary<string, string>
                {
                    ["mirror"] = _services.Mirror.ToString(),
                    ["threads"] = _services.Settings.DownloadThreads.ToString()
                });
            Status = (verify ? "校验失败：" : "安装失败：") + ex.Message;
        }
        finally
        {
            if (request == _installRequest)
            {
                // 仅取消路径清理 .part（暂停路径保留续传资产）；清理失败不阻塞状态复位。
                if (!IsPaused) PartFileCleaner.CleanPartFiles(root);
                if (!IsPaused)
                {
                    IsBusy = false;
                    CanCancel = false;
                    Progress = 0;
                    ProgressText = "";
                    SpeedText = "";
                }
                _cts?.Dispose();
                _cts = null;
            }
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is null) return;
        _cts.Cancel();
        Status = "正在取消…";
    }

    [ObservableProperty]
    private bool _isPaused;

    partial void OnIsPausedChanged(bool value) => UpdateCommands();

    [RelayCommand]
    private void Pause()
    {
        if (!IsBusy || IsPaused) return;
        _cts?.Cancel();
        IsPaused = true;
        Status = "已暂停";
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (!IsPaused) return;
        IsPaused = false;
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
        Status = "正在继续…";
        // 暂停前是批量安装时，恢复批量；否则恢复单个版本安装。
        if (_batchInstall)
        {
            // 暂停前已装完的版本 IsInstalled 已实时置位，跳过它们从剩余版本继续。
            foreach (var done in SelectedVersions.Where(v => v.IsInstalled).ToList())
                SelectedVersions.Remove(done);
            if (SelectedVersions.Count == 0)
            {
                Status = "批量安装完成";
                UpdateCommands();
                return;
            }
            await InstallBatchAsync();
        }
        else
        {
            await InstallCoreAsync(SelectedVersion, verify: false);
        }
    }

    private static string FormatProgress(JobProgress progress) =>
        ProgressFormatter.Format(progress);

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
