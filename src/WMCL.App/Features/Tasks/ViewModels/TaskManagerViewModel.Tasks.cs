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

public partial class TaskManagerViewModel
{
    private void AddHistory(TaskEntry task)
    {
        if (task.State == TaskTerminalState.Running)
            task = task with { State = TerminalState(task.PercentText, task.Progress) };
        task = task with { FinishedAt = DateTimeOffset.Now };
        History.Insert(0, task);
        while (History.Count > 20) History.RemoveAt(History.Count - 1);
        HasHistory = true;
        ApplyHistoryFilter();
        SaveHistory();
    }

    [RelayCommand]
    private void CancelTask(TaskEntry? task)
    {
        task?.Cancel?.Invoke();
        Refresh();
    }

    /// <summary>按任务标题找到失败上报源，读取并一次性消费失败堆栈与重试快照。</summary>
    private (TaskFailureInfo? Failure, TaskRetryPayload? Retry) CaptureOutcome(string title)
    {
        ITaskRetrySource? source = title switch
        {
            "安装 / 校验 Minecraft" => _download,
            "安装 Fabric" or "安装 Forge / NeoForge" or "安装 OptiFine" or "安装 Mod 与必需前置" => _mods,
            "安装整合包" => _modpack,
            "下载资源" => _content,
            "批量安装收藏" => _favoriteBatch,
            _ => null
        };
        if (source is null) return (null, null);
        var failure = source.LastTaskFailure;
        var retry = source.CaptureRetryPayload();
        source.ClearTaskFailure();
        return (failure, retry);
    }

    private ITaskRetrySource? ResolveRetrySource(TaskRetryPayload payload) => payload.Kind switch
    {
        TaskRetryKind.VersionInstall or TaskRetryKind.VersionVerify or TaskRetryKind.BatchInstall => _download,
        TaskRetryKind.ModInstall => _mods,
        TaskRetryKind.ModpackInstall => _modpack,
        TaskRetryKind.CombinedInstall => _install,
        TaskRetryKind.ResourceInstall when payload.Options.TryGetValue("source", out var source)
            && source == "favorites" => _favoriteBatch,
        TaskRetryKind.ResourceInstall => _content,
        _ => null
    };

    [RelayCommand]
    private async Task RetryTaskAsync(TaskEntry? task)
    {
        if (task?.Retry is not { } payload) return;
        var source = ResolveRetrySource(payload);
        if (source is null) return;
        await source.RetryAsync(payload);
        Refresh();
    }

    /// <summary>分配新的任务运行键：时间戳 + 递增序号 + 标题（同标题重现必然得到新键）。</summary>
    private string NewTaskId(string title) =>
        $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Interlocked.Increment(ref _taskSequence):000000}-{title}";

    /// <summary>对进行中任务按 Detail 差分追加日志；无变化不追加（终态任务不会进入此路径，日志自然冻结）。</summary>
    private void AppendTaskLog(string taskId, TaskEntry task)
    {
        if (string.IsNullOrWhiteSpace(task.Detail)) return;
        if (_lastLogDetail.TryGetValue(taskId, out var last) &&
            string.Equals(last, task.Detail, StringComparison.Ordinal))
            return;
        _runs.AppendLog(taskId, new TaskLogEntry(
            DateTimeOffset.Now, TaskLogLevel.Info, task.Title,
            SensitiveTextRedactor.Redact(task.Detail)));
        _lastLogDetail[taskId] = task.Detail;
    }

    internal static string BuildFailureCopyText(string title, TaskFailureInfo failure)
    {
        var redacted = failure.Redacted();
        return $"【{title}】失败于 {redacted.CapturedAt:yyyy-MM-dd HH:mm:ss}\n"
            + $"{redacted.ExceptionType}: {redacted.Message}\n\n{redacted.StackTrace}";
    }

    [RelayCommand]
    private void CopyFailureInfo(TaskEntry? task)
    {
        if (task?.Failure is not { } failure) return;
        var text = BuildFailureCopyText(task.Title, failure);
        WMCL.App.Services.ClipboardService.TrySetText(text);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        foreach (var entry in History)
            if (entry.TaskId is not null) _runs.ClearRun(entry.TaskId);
        History.Clear();
        FilteredHistory.Clear();
        HasHistory = false;
        HistoryStatus = "";
        TryDeleteHistoryFile();
    }

    [RelayCommand]
    private void CancelAll()
    {
        // 快照列表，避免取消操作触发 Refresh 时集合被修改
        foreach (var task in Tasks.ToList()) task.Cancel?.Invoke();
        Refresh();
    }

    /// <summary>把历史记录序列化到磁盘（v2 结构 + 临时文件原子替换，防止写盘中断产生半文件）。</summary>
    private void SaveHistory()
    {
        try
        {
            var document = new TaskHistoryDocument
            {
                SchemaVersion = 2,
                Records = History.Select(ToRecord).ToList()
            };
            WmclPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(document, JsonOptions);
            var temporary = _historyFile + ".tmp";
            File.WriteAllText(temporary, json);
            FileUtil.MoveReplace(temporary, _historyFile);
        }
        catch
        {
            // 持久化失败不影响任务管理功能
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 从磁盘加载历史记录（启动时调用）。兼容 v1 裸数组与 v2 对象结构；
    /// 读取到的 Running 记录强制改写为 Canceled（进程被强杀未走 Dispose 的兜底）。
    /// </summary>
    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFile)) return;
            var json = File.ReadAllText(_historyFile);
            List<TaskHistoryRecord>? records = null;
            if (json.TrimStart().StartsWith('{'))
                records = JsonSerializer.Deserialize<TaskHistoryDocument>(json)?.Records;
            else
                records = JsonSerializer.Deserialize<List<TaskHistoryRecord>>(json);
            if (records is null) return;
            foreach (var record in records)
            {
                var entry = record.ToTaskEntry();
                if (entry.State == TaskTerminalState.Running)
                    entry = entry with { State = TaskTerminalState.Canceled, PercentText = "已取消" };
                History.Add(entry);
            }
            HasHistory = History.Count > 0;
            ApplyHistoryFilter();
        }
        catch
        {
            // 加载失败使用空历史，不阻塞启动；置空态提示供视图展示。
            HistoryStatus = "历史不可用，已重置";
        }
    }

    private void TryDeleteHistoryFile()
    {
        try
        {
            if (File.Exists(_historyFile)) File.Delete(_historyFile);
        }
        catch
        {
            // 删除失败不影响功能
        }
    }

    /// <summary>单条历史记录持久化的日志条数上限（取最新 N 条，超限标注）。</summary>
    private const int MaxPersistedLogs = 64;

    private static TaskHistoryRecord ToRecord(TaskEntry task)
    {
        List<TaskLogRecord>? logs = null;
        if (task.Logs is { Count: > 0 })
        {
            var truncated = Math.Max(0, task.Logs.Count - MaxPersistedLogs);
            logs = new List<TaskLogRecord>();
            if (truncated > 0)
                logs.Add(new TaskLogRecord
                {
                    Timestamp = task.Logs[0].Timestamp,
                    Level = "Warn",
                    Stage = task.Title,
                    Message = $"…已截断早期 {truncated} 条日志…"
                });
            foreach (var entry in task.Logs.Skip(Math.Max(0, task.Logs.Count - MaxPersistedLogs)))
                logs.Add(TaskLogRecord.From(entry));
        }
        return new TaskHistoryRecord
        {
            Title = task.Title,
            Detail = task.Detail,
            Progress = task.Progress,
            PercentText = task.PercentText,
            State = task.State.ToString(),
            Weight = task.Weight,
            Children = task.Children?.Select(ToRecord).ToList(),
            TaskId = task.TaskId,
            FinishedAt = task.FinishedAt,
            Failure = task.Failure is { } failure ? TaskFailureRecord.From(failure) : null,
            Logs = logs,
            Retry = task.Retry is { } retry ? TaskRetryRecord.From(retry) : null
        };
    }

    private static string Detail(string? progressText, string? status) =>
        !string.IsNullOrWhiteSpace(progressText)
            ? progressText
            : status ?? "";

    private static string Percent(double progress) =>
        progress > 0 ? $"{progress:0}%" : "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_favoriteBatch is not null && _favoriteBatchHandler is not null)
            _favoriteBatch.PropertyChanged -= _favoriteBatchHandler;
        if (_launch is not null && _launchHandler is not null)
            _launch.PropertyChanged -= _launchHandler;
        if (_installTaskFinishedHandler is not null)
            _install.TaskFinished -= _installTaskFinishedHandler;
        if (_modsModUpdateTaskFinishedHandler is not null)
            _mods.ModUpdateTaskFinished -= _modsModUpdateTaskFinishedHandler;
        foreach (var handler in _sourceHandlers)
        {
            // 从所有源取消订阅
            foreach (var source in new INotifyPropertyChanged[]
                     {
                         _download, _mods, _modpack, _content, _instance, _install
                     })
                source.PropertyChanged -= handler;
        }
        _sourceHandlers.Clear();
        DownloadMonitor.Updated -= OnTransferUpdated;
        _refreshTimer?.Stop();
        _refreshTimer = null;
        _refreshPending = false;
        try
        {
            // 退出时进行中任务终态化为已取消并持久化（spec §6.2-1）。
            foreach (var task in _previousTasks.Values)
            {
                if (task.State != TaskTerminalState.Running) continue;
                var canceled = CompleteTask(task) with
                {
                    State = TaskTerminalState.Canceled,
                    PercentText = "已取消"
                };
                History.Insert(0, canceled);
            }
            while (History.Count > 20) History.RemoveAt(History.Count - 1);
            if (History.Count > 0 || File.Exists(_historyFile))
                SaveHistory();
        }
        catch
        {
            // 退出终态化失败不影响退订已完成
        }
    }
}
