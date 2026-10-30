using System.Collections.Concurrent;

namespace WMCL.App.Services;

/// <summary>
/// 逐任务运行存储：按 runKey 分桶的日志环形缓冲（默认单任务 500 条，
/// 超容量 O(1) 淘汰最旧一条）与最近一次失败信息。全方法锁保护，不向调用方抛出。
/// </summary>
public sealed class TaskRunStore
{
    private sealed class TaskRun
    {
        internal readonly Queue<TaskLogEntry> Logs = new();
        internal TaskFailureInfo? Failure;
    }

    private readonly ConcurrentDictionary<string, TaskRun> _runs = new(StringComparer.Ordinal);

    /// <summary>单任务日志容量上限（&gt;=1）。</summary>
    public int MaxLogsPerRun { get; set; } = 500;

    public void AppendLog(string runKey, TaskLogEntry entry)
    {
        if (string.IsNullOrEmpty(runKey)) return;
        try
        {
            var run = _runs.GetOrAdd(runKey, _ => new TaskRun());
            lock (run)
            {
                run.Logs.Enqueue(entry);
                while (run.Logs.Count > Math.Max(1, MaxLogsPerRun))
                    run.Logs.Dequeue();
            }
        }
        catch
        {
            // 日志写入失败不中断任务流程
        }
    }

    /// <summary>返回容量内最新日志的只读快照（副本，与后续写入互不影响）。</summary>
    public IReadOnlyList<TaskLogEntry> GetLogs(string runKey)
    {
        if (string.IsNullOrEmpty(runKey)) return [];
        if (!_runs.TryGetValue(runKey, out var run)) return [];
        lock (run) return run.Logs.ToList();
    }

    /// <summary>返回当前日志条数（轻量查询，不复制列表）。</summary>
    public int GetLogCount(string runKey)
    {
        if (string.IsNullOrEmpty(runKey)) return 0;
        if (!_runs.TryGetValue(runKey, out var run)) return 0;
        lock (run) return run.Logs.Count;
    }

    public void CaptureFailure(string runKey, TaskFailureInfo failure)
    {
        if (string.IsNullOrEmpty(runKey)) return;
        try
        {
            var run = _runs.GetOrAdd(runKey, _ => new TaskRun());
            lock (run) run.Failure = failure;
        }
        catch
        {
            // 失败信息写入失败不中断任务流程
        }
    }

    public TaskFailureInfo? GetFailure(string runKey)
    {
        if (string.IsNullOrEmpty(runKey)) return null;
        if (!_runs.TryGetValue(runKey, out var run)) return null;
        lock (run) return run.Failure;
    }

    public void ClearRun(string runKey)
    {
        if (string.IsNullOrEmpty(runKey)) return;
        _runs.TryRemove(runKey, out _);
    }

    public void ClearAll() => _runs.Clear();
}
