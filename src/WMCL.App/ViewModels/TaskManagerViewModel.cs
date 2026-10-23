using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using WMCL.Download;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

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
    TaskTerminalState State = TaskTerminalState.Running)
{
    public bool HasCancel => Cancel is not null;
    public bool HasChildren => Children is { Count: > 0 };
    public bool IsTerminal => State != TaskTerminalState.Running;
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

public partial class TaskManagerViewModel : ViewModelBase
{
    private readonly DownloadViewModel _download;
    private readonly ModsViewModel _mods;
    private readonly ModpackViewModel _modpack;
    private readonly InstanceContentViewModel _content;
    private readonly InstanceViewModel _instance;
    private readonly InstallViewModel _install;
    private readonly ResourceFavoriteBatchViewModel? _favoriteBatch;
    private readonly LaunchViewModel? _launch;

    public TaskManagerViewModel(
        DownloadViewModel download,
        ModsViewModel mods,
        ModpackViewModel modpack,
        InstanceContentViewModel content,
        InstanceViewModel instance,
        InstallViewModel install,
        ResourceFavoriteBatchViewModel? favoriteBatch = null,
        LaunchViewModel? launch = null)
    {
        _download = download;
        _mods = mods;
        _modpack = modpack;
        _content = content;
        _instance = instance;
        _install = install;
        install.TaskFinished += (_, task) =>
        {
            AddHistory(task);
            Refresh();
        };
        mods.ModUpdateTaskFinished += (_, task) =>
        {
            AddHistory(task);
            Refresh();
        };
        _favoriteBatch = favoriteBatch;
        _launch = launch;
        if (favoriteBatch is not null) favoriteBatch.PropertyChanged += (_, _) => Refresh();
        if (launch is not null) launch.PropertyChanged += (_, _) => Refresh();

        foreach (var source in new INotifyPropertyChanged[]
                 {
                     download, mods, modpack, content, instance, install
                 })
            source.PropertyChanged += (_, _) => Refresh();

        DownloadMonitor.Updated += OnTransferUpdated;
        Refresh();
    }

    public ObservableCollection<TaskEntry> Tasks { get; } = [];
    public ObservableCollection<TaskEntry> History { get; } = [];
    private Dictionary<string, TaskEntry> _previousTasks = new(StringComparer.Ordinal);

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

    public void Refresh()
    {
        var tasks = new List<TaskEntry>();

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

        if (_mods.IsBusy)
            tasks.Add(new TaskEntry(
                "安装 Fabric",
                Detail(_mods.ProgressText, _mods.Status),
                _mods.Progress,
                Percent(_mods.Progress),
                () => _mods.CancelCommand.Execute(null),
                null,
                1,
                () => _mods.Status));

        if (_mods.IsForgelikeBusy)
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

        var currentTitles = tasks.Select(task => task.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var old in _previousTasks.Values)
        {
            if (currentTitles.Contains(old.Title)) continue;
            if (old.Title == "组合安装") continue;
            if (old.Title.StartsWith("Mod 更新：", StringComparison.Ordinal)) continue;
            AddHistory(CompleteTask(old));
        }
        _previousTasks = tasks.ToDictionary(task => task.Title, StringComparer.Ordinal);
        HasHistory = History.Count > 0;

        Tasks.Clear();
        foreach (var task in tasks) Tasks.Add(task);

        HasTasks = tasks.Count > 0;
        var hasDownload = tasks.Any(task =>
            task.Title.Contains("下载", StringComparison.Ordinal) ||
            task.Title.Contains("安装 / 校验", StringComparison.Ordinal));
        if (!hasDownload)
        {
            HasTransferStats = false;
            TransferSpeedText = "";
            RemainingFilesText = "";
            ThreadsText = "";
        }
        var totalWeight = tasks.Sum(task => task.Weight);
        OverallProgress = totalWeight <= 0
            ? 0
            : tasks.Sum(task => task.Progress * task.Weight) / totalWeight;
        OverallText = tasks.Count == 0
            ? "没有进行中的任务"
            : $"{tasks.Count} 个任务 · {OverallProgress:0}%";
    }

    private void OnTransferUpdated(object? sender, TransferSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HasTransferStats = _download.IsBusy || snapshot.BytesPerSecond > 0 ||
                               snapshot.RemainingFiles > 0;
            TransferSpeedText = FormatSpeed(snapshot.BytesPerSecond);
            RemainingFilesText = $"剩余 {snapshot.RemainingFiles} 个文件";
            ThreadsText = snapshot.MaxThreads > 0 ? $"{snapshot.MaxThreads} 线程" : "";
        });
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond switch
        {
            >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:0.0} KB/s",
            > 0 => $"{bytesPerSecond:0} B/s",
            _ => "0 B/s"
        };

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

    private void AddHistory(TaskEntry task)
    {
        if (task.State == TaskTerminalState.Running)
            task = task with { State = TerminalState(task.PercentText, task.Progress) };
        History.Insert(0, task);
        while (History.Count > 20) History.RemoveAt(History.Count - 1);
        HasHistory = true;
    }

    [RelayCommand]
    private void CancelTask(TaskEntry? task)
    {
        task?.Cancel?.Invoke();
        Refresh();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        HasHistory = false;
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var task in Tasks) task.Cancel?.Invoke();
        Refresh();
    }

    private static string Detail(string? progressText, string? status) =>
        !string.IsNullOrWhiteSpace(progressText)
            ? progressText
            : status ?? "";

    private static string Percent(double progress) =>
        progress > 0 ? $"{progress:0}%" : "…";
}
