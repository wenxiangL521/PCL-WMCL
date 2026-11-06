using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Download;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.Features.Tasks;

public enum TaskTerminalState
{
    Running,
    Completed,
    Failed,
    Canceled,
    Ended
}

public sealed record TaskEntry(
    string Title,
    string Detail,
    double Progress,
    string PercentText,
    Action? Cancel,
    IReadOnlyList<TaskEntry>? Children = null,
    double Weight = 1,
    Func<string?>? FinalStatus = null,
    TaskTerminalState State = TaskTerminalState.Running,
    string? TaskId = null,
    TaskFailureInfo? Failure = null,
    TaskRetryPayload? Retry = null,
    IReadOnlyList<TaskLogEntry>? Logs = null,
    DateTimeOffset? FinishedAt = null) : INotifyPropertyChanged
{
    public string Detail { get; set; } = Detail;
    public double Progress { get; set; } = Progress;
    public string PercentText { get; set; } = PercentText;
    public Action? Cancel { get; set; } = Cancel;
    public IReadOnlyList<TaskEntry>? Children { get; set; } = Children;
    public double Weight { get; set; } = Weight;
    public Func<string?>? FinalStatus { get; set; } = FinalStatus;
    public TaskTerminalState State { get; set; } = State;
    public TaskFailureInfo? Failure { get; set; } = Failure;
    public TaskRetryPayload? Retry { get; set; } = Retry;
    public IReadOnlyList<TaskLogEntry>? Logs { get; set; } = Logs;
    public DateTimeOffset? FinishedAt { get; set; } = FinishedAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateFrom(TaskEntry next)
    {
        UpdateValue(Detail, next.Detail, value => Detail = value, nameof(Detail));
        UpdateValue(Progress, next.Progress, value => Progress = value, nameof(Progress));
        UpdateValue(PercentText, next.PercentText, value => PercentText = value, nameof(PercentText));
        UpdateValue(Cancel, next.Cancel, value => Cancel = value, nameof(Cancel), nameof(HasCancel));
        UpdateValue(Weight, next.Weight, value => Weight = value, nameof(Weight));
        UpdateValue(FinalStatus, next.FinalStatus, value => FinalStatus = value, nameof(FinalStatus));
        UpdateValue(State, next.State, value => State = value, nameof(State), nameof(IsTerminal), nameof(StateGlyph), nameof(StateText));
        UpdateValue(Failure, next.Failure, value => Failure = value, nameof(Failure), nameof(HasFailure));
        UpdateValue(Retry, next.Retry, value => Retry = value, nameof(Retry), nameof(IsRetryable));
        UpdateValue(FinishedAt, next.FinishedAt, value => FinishedAt = value, nameof(FinishedAt));
        if (!(Logs ?? []).SequenceEqual(next.Logs ?? []))
            UpdateValue(Logs, next.Logs, value => Logs = value, nameof(Logs), nameof(LogCount), nameof(HasLogs));

        var hadChildren = HasChildren;
        if (Children is ObservableCollection<TaskEntry> children)
            Synchronize(children, next.Children ?? []);
        else if (next.HasChildren)
        {
            Children = new ObservableCollection<TaskEntry>();
            Synchronize((ObservableCollection<TaskEntry>)Children, next.Children!);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Children)));
        }
        if (hadChildren != HasChildren)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
    }

    private void UpdateValue<T>(T current, T next, Action<T> assign, string propertyName, params string[] dependentProperties)
    {
        if (EqualityComparer<T>.Default.Equals(current, next)) return;
        assign(next);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        foreach (var dependent in dependentProperties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(dependent));
    }

    internal static void Synchronize(ObservableCollection<TaskEntry> target, IReadOnlyList<TaskEntry> source)
    {
        for (var index = target.Count - 1; index >= 0; index--)
            if (!source.Any(next => SameIdentity(target[index], next)))
                target.RemoveAt(index);

        for (var index = 0; index < source.Count; index++)
        {
            var next = source[index];
            var existingIndex = -1;
            for (var candidate = index; candidate < target.Count; candidate++)
            {
                if (!SameIdentity(target[candidate], next)) continue;
                existingIndex = candidate;
                break;
            }
            if (existingIndex < 0)
            {
                var entry = next with { Children = null };
                entry.UpdateFrom(next);
                target.Insert(index, entry);
            }
            else
            {
                if (existingIndex != index) target.Move(existingIndex, index);
                target[index].UpdateFrom(next);
            }
        }
    }

    private static bool SameIdentity(TaskEntry current, TaskEntry next) =>
        current.TaskId == next.TaskId && string.Equals(current.Title, next.Title, StringComparison.Ordinal);

    public bool HasCancel => Cancel is not null;
    public bool HasChildren => Children is { Count: > 0 };
    public bool IsTerminal => State != TaskTerminalState.Running;
    public bool HasLogs => LogCount > 0;
    public bool HasFailure => Failure is not null;
    public bool IsRetryable => Retry is not null;
    public int LogCount => Logs?.Count ?? 0;
    public string StateGlyph => State switch
    {
        TaskTerminalState.Completed => "✓",
        TaskTerminalState.Failed => "!",
        TaskTerminalState.Canceled => "×",
        TaskTerminalState.Ended => "·",
        _ => ""
    };
    public string StateText => State switch
    {
        TaskTerminalState.Completed => "完成",
        TaskTerminalState.Failed => "失败",
        TaskTerminalState.Canceled => "已取消",
        TaskTerminalState.Ended => "结束",
        _ => "进行中"
    };
}

/// <summary>任务历史记录的可序列化 DTO（v2）。不包含委托（Cancel/FinalStatus）。</summary>
internal sealed class TaskHistoryRecord
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    [JsonPropertyName("progress")] public double Progress { get; set; }
    [JsonPropertyName("percent")] public string PercentText { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("weight")] public double Weight { get; set; } = 1;
    [JsonPropertyName("children")] public List<TaskHistoryRecord>? Children { get; set; }
    [JsonPropertyName("taskId")] public string? TaskId { get; set; }
    [JsonPropertyName("finishedAt")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("failure")] public TaskFailureRecord? Failure { get; set; }
    [JsonPropertyName("logs")] public List<TaskLogRecord>? Logs { get; set; }
    [JsonPropertyName("retry")] public TaskRetryRecord? Retry { get; set; }

    public TaskEntry ToTaskEntry() => new(
        Title, Detail, Progress, PercentText,
        Cancel: null,
        Children: Children?.Select(c => c.ToTaskEntry()).ToList(),
        Weight,
        FinalStatus: null,
        State: ParseState(State),
        TaskId: TaskId,
        Failure: Failure?.ToTaskFailureInfo(),
        Retry: Retry?.ToTaskRetryPayload(),
        Logs: Logs?.Select(l => l.ToTaskLogEntry()).ToList());

    internal static TaskTerminalState ParseState(string state) => state switch
    {
        "Completed" => TaskTerminalState.Completed,
        "Failed" => TaskTerminalState.Failed,
        "Canceled" => TaskTerminalState.Canceled,
        "Ended" => TaskTerminalState.Ended,
        _ => TaskTerminalState.Running
    };
}

internal sealed class TaskFailureRecord
{
    [JsonPropertyName("exceptionType")] public string ExceptionType { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("stackTrace")] public string StackTrace { get; set; } = "";
    [JsonPropertyName("capturedAt")] public DateTimeOffset CapturedAt { get; set; }

    public TaskFailureInfo ToTaskFailureInfo() =>
        new(ExceptionType, Message, StackTrace, CapturedAt);

    public static TaskFailureRecord From(TaskFailureInfo failure) => new()
    {
        ExceptionType = failure.ExceptionType,
        Message = failure.Message,
        StackTrace = failure.StackTrace,
        CapturedAt = failure.CapturedAt
    };
}

internal sealed class TaskLogRecord
{
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("level")] public string Level { get; set; } = "Info";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";

    public TaskLogEntry ToTaskLogEntry() =>
        new(Timestamp, ParseLevel(Level), Stage, Message);

    private static TaskLogLevel ParseLevel(string level) => level switch
    {
        "Warn" => TaskLogLevel.Warn,
        "Error" => TaskLogLevel.Error,
        _ => TaskLogLevel.Info
    };

    public static TaskLogRecord From(TaskLogEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        Level = entry.Level.ToString(),
        Stage = entry.Stage,
        Message = entry.Message
    };
}

internal sealed class TaskRetryRecord
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("gameRoot")] public string GameRoot { get; set; } = "";
    [JsonPropertyName("versionId")] public string? VersionId { get; set; }
    [JsonPropertyName("resourceProjectId")] public string? ResourceProjectId { get; set; }
    [JsonPropertyName("options")] public Dictionary<string, string>? Options { get; set; }

    public TaskRetryPayload? ToTaskRetryPayload()
    {
        if (!Enum.TryParse<TaskRetryKind>(Kind, out var kind)) return null;
        return new TaskRetryPayload(kind, GameRoot, VersionId, ResourceProjectId,
            Options ?? new Dictionary<string, string>());
    }

    public static TaskRetryRecord From(TaskRetryPayload payload) => new()
    {
        Kind = payload.Kind.ToString(),
        GameRoot = payload.GameRoot,
        VersionId = payload.VersionId,
        ResourceProjectId = payload.ResourceProjectId,
        Options = payload.Options.ToDictionary(pair => pair.Key, pair => pair.Value)
    };
}

/// <summary>v2 历史文件结构：带 schemaVersion 的对象；v1 是裸数组（兼容读取）。</summary>
internal sealed class TaskHistoryDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 2;
    [JsonPropertyName("records")] public List<TaskHistoryRecord> Records { get; set; } = [];
}

public partial class TaskManagerViewModel : ViewModelBase, IDisposable
{
    private readonly DownloadViewModel _download;
    private readonly ModsViewModel _mods;
    private readonly ModpackViewModel _modpack;
    private readonly InstanceContentViewModel _content;
    private readonly InstanceViewModel _instance;
    private readonly InstallViewModel _install;
    private ResourceFavoriteBatchViewModel? _favoriteBatch;
    private LaunchViewModel? _launch;
    private PropertyChangedEventHandler? _favoriteBatchHandler;
    private PropertyChangedEventHandler? _launchHandler;
    private EventHandler<TaskEntry>? _installTaskFinishedHandler;
    private EventHandler<TaskEntry>? _modsModUpdateTaskFinishedHandler;
    private readonly List<PropertyChangedEventHandler> _sourceHandlers = [];
    private bool _disposed;
    private readonly TaskRunStore _runs;
    private readonly Dictionary<string, string> _lastLogDetail = new(StringComparer.Ordinal);
    private long _taskSequence;

    public TaskManagerViewModel(
        DownloadViewModel download,
        ModsViewModel mods,
        ModpackViewModel modpack,
        InstanceContentViewModel content,
        InstanceViewModel instance,
        InstallViewModel install,
        ResourceFavoriteBatchViewModel? favoriteBatch = null,
        LaunchViewModel? launch = null,
        string? historyFilePath = null,
        TaskRunStore? runs = null,
        AppServices? services = null)
    {
        _historyFile = historyFilePath
            ?? Path.Combine(WmclPaths.DataDirectory, "task-history.json");
        _runs = runs ?? services?.TaskRunStore ?? new TaskRunStore();
        _download = download;
        _mods = mods;
        _modpack = modpack;
        _content = content;
        _instance = instance;
        _install = install;
        _installTaskFinishedHandler = (_, task) =>
        {
            var failure = _install.LastTaskFailure;
            var retry = _install.CaptureRetryPayload();
            _install.ClearTaskFailure();
            if (failure is not null)
                task = task with { Failure = failure, Retry = retry, State = TaskTerminalState.Failed, PercentText = "失败" };
            else if (retry is not null)
                task = task with { Retry = retry };
            AddHistory(task);
            Refresh();
        };
        install.TaskFinished += _installTaskFinishedHandler;
        _modsModUpdateTaskFinishedHandler = (_, task) =>
        {
            var failure = _mods.LastTaskFailure;
            var retry = _mods.CaptureRetryPayload();
            _mods.ClearTaskFailure();
            if (failure is not null)
                task = task with { Failure = failure, Retry = retry, State = TaskTerminalState.Failed, PercentText = "失败" };
            else if (retry is not null)
                task = task with { Retry = retry };
            AddHistory(task);
            Refresh();
        };
        mods.ModUpdateTaskFinished += _modsModUpdateTaskFinishedHandler;
        _favoriteBatch = favoriteBatch;
        _launch = launch;
        if (favoriteBatch is not null)
        {
            _favoriteBatchHandler = (_, _) => Refresh();
            favoriteBatch.PropertyChanged += _favoriteBatchHandler;
        }
        if (launch is not null)
        {
            _launchHandler = (_, e) =>
            {
                if (e.PropertyName == nameof(LaunchViewModel.Log))
                    SyncLaunchLog();
                Refresh();
            };
            launch.PropertyChanged += _launchHandler;
            SyncLaunchLog();
        }

        foreach (var source in new INotifyPropertyChanged[]
                 {
                     download, mods, modpack, content, instance, install
                 })
        {
            var handler = new PropertyChangedEventHandler((_, e) => ScheduleRefresh(e.PropertyName));
            source.PropertyChanged += handler;
            _sourceHandlers.Add(handler);
        }

        DownloadMonitor.Updated += OnTransferUpdated;
        LoadHistory();
        Refresh();
    }

    public ObservableCollection<TaskEntry> Tasks { get; } = [];
    public ObservableCollection<TaskEntry> History { get; } = [];
    private Dictionary<string, TaskEntry> _previousTasks = new(StringComparer.Ordinal);
    private readonly string _historyFile;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallText = "没有进行中的任务";

    [ObservableProperty]
    private bool _hasTasks;

    [ObservableProperty]
    private string _transferSpeedText = "";

    [ObservableProperty]
    private string _remainingFilesText = "";

    [ObservableProperty]
    private string _threadsText = "";

    [ObservableProperty]
    private bool _hasTransferStats;

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private string _historyStatus = "";

    [ObservableProperty]
    private string _historyFilter = "全部";

    public List<string> HistoryFilters { get; } = ["全部", "已完成", "已失败"];

    public ObservableCollection<TaskEntry> FilteredHistory { get; } = [];

    partial void OnHistoryFilterChanged(string value) => ApplyHistoryFilter();

    private void ApplyHistoryFilter()
    {
        FilteredHistory.Clear();
        foreach (var entry in History)
        {
            if (HistoryFilter == "全部"
                || (HistoryFilter == "已完成" && entry.State == TaskTerminalState.Completed)
                || (HistoryFilter == "已失败" && entry.State == TaskTerminalState.Failed))
                FilteredHistory.Add(entry);
        }
        OnPropertyChanged(nameof(HasFilteredHistory));
    }

    public bool HasFilteredHistory => FilteredHistory.Count > 0;

    [RelayCommand]
    private void RefreshHistory()
    {
        ApplyHistoryFilter();
        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>实时日志面板。收集启动日志与任务输出。</summary>
    public ObservableCollection<string> LogLines { get; } = [];
    private const int MaxLogLines = 200;

    [ObservableProperty]
    private bool _showLogs;

    [ObservableProperty]
    private string _logStatus = "";

    public string LogToggleText => ShowLogs ? "隐藏日志" : "显示日志";

    partial void OnShowLogsChanged(bool value)
    {
        OnPropertyChanged(nameof(LogToggleText));
        OnPropertyChanged(nameof(HasLogs));
    }

    public bool HasLogs => LogLines.Count > 0;

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogLines.Add(line);
        while (LogLines.Count > MaxLogLines) LogLines.RemoveAt(0);
        OnPropertyChanged(nameof(HasLogs));
        LogStatus = $"日志 {LogLines.Count} 行";
    }

    /// <summary>同步 LaunchViewModel 的 Log 到日志面板。</summary>
    /// <remarks>
    /// Log 是追加式属性（每次追加一行并整体重新赋值触发 PropertyChanged），
    /// 因此按行数做增量同步：只追加新增行。此前每次变更都把全文重灌一遍，
    /// 一次启动 N 行日志会触发 O(N²) 追加并使面板内容重复滚动。
    /// </remarks>
    private void SyncLaunchLog()
    {
        if (_launch is null) return;
        var log = _launch.Log;
        if (string.IsNullOrWhiteSpace(log)) return;
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < _launchLogSyncedLines)
        {
            // 日志被重置（新一轮启动 / 用户清空 / 超长截断）：面板从头同步。
            LogLines.Clear();
            _launchLogSyncedLines = 0;
        }
        for (var i = _launchLogSyncedLines; i < lines.Length; i++)
            AppendLog(lines[i].TrimEnd('\r'));
        _launchLogSyncedLines = lines.Length;
    }

    /// <summary>已同步到日志面板的 Log 行数（增量同步游标）。</summary>
    private int _launchLogSyncedLines;

    [RelayCommand]
    private void ClearLogs()
    {
        LogLines.Clear();
        LogStatus = "";
        OnPropertyChanged(nameof(HasLogs));
    }

    public void Refresh()
    {
        var tasks = new List<TaskEntry>();

        if (_launch is { IsCheckingGameFiles: true })
            tasks.Add(new TaskEntry("启动前校验游戏文件", Detail(_launch.ProgressText, _launch.Status),
                _launch.Progress, Percent(_launch.Progress), () => _launch.CancelCommand.Execute(null),
                FinalStatus: () => _launch.GameFileCheckOutcome));

        if (_launch is { IsCheckingModpackUpdate: true })
            tasks.Add(new TaskEntry("启动前检查整合包", Detail(_launch.ProgressText, _launch.Status),
                _launch.Progress, Percent(_launch.Progress), () => _launch.CancelCommand.Execute(null),
                FinalStatus: () => _launch.Status));

        if (_download.IsBusy && _download.CanCancel)
            tasks.Add(new TaskEntry(
                "安装 / 校验 Minecraft",
                Detail(_download.ProgressText, _download.Status),
                _download.Progress,
                Percent(_download.Progress),
                () => _download.CancelCommand.Execute(null),
                null,
                3,
                () => _download.Status));

        if (_mods.IsBusy && _mods.CanCancel)
            tasks.Add(new TaskEntry(
                "安装 Fabric",
                Detail(_mods.ProgressText, _mods.Status),
                _mods.Progress,
                Percent(_mods.Progress),
                () => _mods.CancelCommand.Execute(null),
                null,
                1,
                () => _mods.Status));

        if (_mods.IsInstallingForgelike)
            tasks.Add(new TaskEntry(
                "安装 Forge / NeoForge",
                Detail(_mods.ForgelikeProgressText, _mods.ForgelikeStatus),
                _mods.ForgelikeProgress,
                Percent(_mods.ForgelikeProgress),
                () => _mods.CancelForgelikeCommand.Execute(null),
                null,
                2,
                () => _mods.ForgelikeStatus));

        if (_mods.IsOptiFineBusy)
            tasks.Add(new TaskEntry(
                "安装 OptiFine",
                Detail(_mods.OptiFineProgressText, _mods.OptiFineStatus),
                _mods.OptiFineProgress,
                Percent(_mods.OptiFineProgress),
                () => _mods.CancelOptiFineCommand.Execute(null),
                null,
                1,
                () => _mods.OptiFineStatus));

        if (_install.IsSavingComponentInstaller)
            tasks.Add(new TaskEntry("下载安装器：" + _install.ComponentInstallerName, _install.ComponentInstallerStatus,
                _install.ComponentInstallerProgress, Percent(_install.ComponentInstallerProgress),
                () => _install.CancelComponentRowActionsCommand.Execute(null),
                FinalStatus: () => _install.ComponentInstallerStatus));

        if (_install.IsCombinedBusy)
            tasks.Add(new TaskEntry(
                "组合安装",
                Detail(_install.CombinedProgressText, _install.CombinedStatus),
                _install.CombinedProgress,
                Percent(_install.CombinedProgress),
                () => _install.CancelCombinedCommand.Execute(null),
                _install.Stages.Select(stage => new TaskEntry(
                    stage.Title,
                    stage.Detail,
                    stage.Progress,
                    stage.PercentText,
                    null)).ToList(),
                3));

        if (_mods.IsInstallingMod)
            tasks.Add(new TaskEntry(
                "安装 Mod 与必需前置",
                Detail(_mods.ModProgressText, _mods.ModStatus),
                _mods.ModProgress,
                Percent(_mods.ModProgress),
                () => _mods.CancelModInstallCommand.Execute(null),
                FinalStatus: () => _mods.ModStatus));

        if (_mods.IsUpdatingMods)
            tasks.Add(new TaskEntry(_mods.ModUpdateTaskTitle, Detail(_mods.ModUpdateProgressText, _mods.ModUpdateTaskStatus),
                _mods.ModUpdateProgress, Percent(_mods.ModUpdateProgress), () => _mods.CancelModUpdatesCommand.Execute(null)));

        if (_favoriteBatch is { IsInstalling: true })
            tasks.Add(new TaskEntry("批量安装收藏", Detail(_favoriteBatch.ProgressText, _favoriteBatch.Status),
                _favoriteBatch.Progress, Percent(_favoriteBatch.Progress), () => _favoriteBatch.CancelCommand.Execute(null),
                FinalStatus: () => _favoriteBatch.Status));

        if (_modpack.IsBusy)
            tasks.Add(new TaskEntry(
                "安装整合包",
                Detail(_modpack.ProgressText, _modpack.Status),
                _modpack.Progress,
                Percent(_modpack.Progress),
                () => _modpack.CancelCommand.Execute(null),
                null,
                3,
                () => _modpack.Status));

        if (_content.IsDownloadBusy)
            tasks.Add(new TaskEntry(
                "下载资源",
                Detail(_content.DownloadProgressText, _content.DownloadStatus),
                _content.DownloadProgress,
                Percent(_content.DownloadProgress),
                () => _content.CancelDownloadCommand.Execute(null),
                null,
                1,
                () => _content.DownloadStatus));

        if (_instance.IsImporting)
            tasks.Add(new TaskEntry(
                "导入实例",
                _instance.Status,
                0,
                "…",
                null,
                FinalStatus: () => _instance.Status));

        var enriched = new List<TaskEntry>(tasks.Count);
        foreach (var task in tasks)
        {
            var taskId = _previousTasks.TryGetValue(task.Title, out var previous) && previous.TaskId is not null
                ? previous.TaskId
                : NewTaskId(task.Title);
            AppendTaskLog(taskId, task);
            enriched.Add(task with { TaskId = taskId, Logs = _runs.GetLogs(taskId) });
        }
        tasks = enriched;

        var currentTitles = tasks.Select(task => task.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var old in _previousTasks.Values)
        {
            if (currentTitles.Contains(old.Title)) continue;
            if (old.Title == "组合安装") continue;
            if (old.Title.StartsWith("Mod 更新：", StringComparison.Ordinal)) continue;
            if (old.TaskId is not null) _lastLogDetail.Remove(old.TaskId);
            var (failure, retry) = CaptureOutcome(old.Title);
            var completed = CompleteTask(old);
            if (failure is not null)
            {
                // 失败上报优先于文本启发式判定（design §2.1.2）
                if (old.TaskId is not null) _runs.CaptureFailure(old.TaskId, failure);
                completed = completed with
                {
                    Failure = failure,
                    Retry = retry,
                    State = TaskTerminalState.Failed,
                    PercentText = "失败"
                };
            }
            else if (retry is not null)
            {
                completed = completed with { Retry = retry };
            }
            AddHistory(completed);
        }
        _previousTasks = new Dictionary<string, TaskEntry>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            // 重复标题时保留第一个，避免 ToDictionary 抛异常
            _previousTasks.TryAdd(task.Title, task);
        }
        HasHistory = History.Count > 0;

        UpdateTasksCollection(tasks);

        HasTasks = tasks.Count > 0;
        UpdateTransferVisibility();
        var totalWeight = tasks.Sum(task => task.Weight);
        OverallProgress = totalWeight <= 0
            ? 0
            : tasks.Sum(task => task.Progress * task.Weight) / totalWeight;
        OverallText = tasks.Count == 0
            ? "没有进行中的任务"
            : $"{tasks.Count} 个任务 · {OverallProgress:0}%";
    }

    /// <summary>
    /// 刷新合并调度：状态转换类属性（IsBusy 等，决定任务出现/消失）立即同步执行，
    /// 进度类高频属性在 100ms 窗口内合并为一次补刷（≤10Hz，design §1.1.2），
    /// 避免短命任务整体落在合并窗口内而丢失终态。
    /// </summary>
    private DispatcherTimer? _refreshTimer;
    private bool _refreshPending;

    private static readonly HashSet<string> ImmediateRefreshProperties = new(StringComparer.Ordinal)
    {
        "IsBusy", "IsPaused", "CanCancel", "IsInstallingForgelike", "IsOptiFineBusy",
        "IsInstalling", "IsInstallingMod", "IsUpdatingMods", "IsDownloadBusy",
        "IsImporting", "IsCombinedBusy", "IsSavingComponentInstaller",
        "IsCheckingModpackUpdate", "IsCheckingGameFiles"
    };

    private void ScheduleRefresh(string? propertyName)
    {
        if (propertyName is not null && ImmediateRefreshProperties.Contains(propertyName))
        {
            _refreshPending = false;
            _refreshTimer?.Stop();
            _refreshTimer = null;
            Refresh();
            return;
        }
        if (_refreshTimer is not null)
        {
            _refreshPending = true;
            return;
        }
        Refresh();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _refreshTimer = null;
            if (!_refreshPending) return;
            _refreshPending = false;
            ScheduleRefresh(null);
        };
        timer.Start();
        _refreshTimer = timer;
    }

    /// <summary>按任务运行键差量同步，进度与子任务更新保留条目、选择和已展开详情。</summary>
    private void UpdateTasksCollection(IReadOnlyList<TaskEntry> tasks) => TaskEntry.Synchronize(Tasks, tasks);

    private bool HasTransferActivity =>
        (_download.IsBusy && _download.CanCancel) || (_mods.IsBusy && _mods.CanCancel) || _mods.IsInstallingForgelike ||
        _mods.IsOptiFineBusy || _mods.IsInstallingMod || _mods.IsUpdatingMods ||
        _install.IsCombinedBusy || _install.IsSavingComponentInstaller || _modpack.IsBusy ||
        _content.IsDownloadBusy || _instance.IsImporting || _favoriteBatch is { IsInstalling: true } ||
        _launch is { IsCheckingGameFiles: true };

    private void UpdateTransferVisibility()
    {
        HasTransferStats = HasTransferActivity;
        if (!HasTransferStats)
        {
            TransferSpeedText = "";
            RemainingFilesText = "";
            ThreadsText = "";
        }
        else if (string.IsNullOrEmpty(TransferSpeedText))
        {
            TransferSpeedText = FormatSpeed(0);
            RemainingFilesText = "等待传输";
        }
    }

    private void OnTransferUpdated(object? sender, TransferSnapshot snapshot)
    {
        UiDispatcher.Post(() =>
        {
            if (_disposed) return;
            UpdateTransferVisibility();
            if (!HasTransferStats) return;
            TransferSpeedText = FormatSpeed(snapshot.BytesPerSecond);
            RemainingFilesText = $"剩余 {snapshot.RemainingFiles} 个文件";
            ThreadsText = snapshot.MaxThreads > 0 ? $"{snapshot.MaxThreads} 线程" : "";
        });
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        ProgressFormatter.FormatSpeed(bytesPerSecond);

    internal static string TerminalOutcome(string? detail, double progress)
    {
        var text = detail ?? "";
        if (text.Contains("取消", StringComparison.Ordinal)) return "已取消";
        if (text.Contains("失败", StringComparison.Ordinal) ||
            text.Contains("错误", StringComparison.Ordinal) ||
            text.Contains("异常", StringComparison.Ordinal)) return "失败";
        if (text.Contains("完成", StringComparison.Ordinal) ||
            text.Contains("已安装", StringComparison.Ordinal) ||
            text.Contains("已下载", StringComparison.Ordinal) ||
            text.Contains("已导入", StringComparison.Ordinal) ||
            progress >= 99.5) return "完成";
        return "结束";
    }

    private static TaskEntry CompleteTask(TaskEntry task)
    {
        var status = task.FinalStatus?.Invoke();
        return task with
        {
            Detail = string.IsNullOrWhiteSpace(status) ? task.Detail : status,
            PercentText = TerminalOutcome(status, task.Progress),
            State = TerminalState(status, task.Progress),
            Cancel = null,
            FinalStatus = null
        };
    }

    internal static TaskTerminalState TerminalState(string? detail, double progress)
    {
        var text = detail ?? "";
        if (text.Contains("取消", StringComparison.Ordinal)) return TaskTerminalState.Canceled;
        if (text.Contains("失败", StringComparison.Ordinal) ||
            text.Contains("错误", StringComparison.Ordinal) ||
            text.Contains("异常", StringComparison.Ordinal)) return TaskTerminalState.Failed;
        if (text.Contains("完成", StringComparison.Ordinal) ||
            text.Contains("已安装", StringComparison.Ordinal) ||
            text.Contains("已下载", StringComparison.Ordinal) ||
            text.Contains("已导入", StringComparison.Ordinal) ||
            progress >= 99.5) return TaskTerminalState.Completed;
        return TaskTerminalState.Ended;
    }

}
