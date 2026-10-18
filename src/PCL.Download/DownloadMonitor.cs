namespace PCL.Download;

public sealed record TransferSnapshot(
    double BytesPerSecond,
    int RemainingFiles,
    int MaxThreads,
    string? CurrentFile);

/// <summary>下载器的全局轻量统计，供任务管理页显示速度/剩余文件/线程。</summary>
public static class DownloadMonitor
{
    private static readonly object Gate = new();
    private static TransferSnapshot _current = new(0, 0, 0, null);

    public static event EventHandler<TransferSnapshot>? Updated;

    public static TransferSnapshot Current
    {
        get
        {
            lock (Gate) return _current;
        }
    }

    public static void Report(DownloadProgress progress)
    {
        var snapshot = new TransferSnapshot(
            progress.BytesPerSecond,
            Math.Max(0, progress.TotalFiles - progress.CompletedFiles),
            progress.MaxThreads,
            progress.CurrentFile);
        lock (Gate) _current = snapshot;
        Updated?.Invoke(null, snapshot);
    }

    public static void Reset()
    {
        var snapshot = new TransferSnapshot(0, 0, 0, null);
        lock (Gate) _current = snapshot;
        Updated?.Invoke(null, snapshot);
    }
}
