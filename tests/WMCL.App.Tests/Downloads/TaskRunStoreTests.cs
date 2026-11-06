using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>逐任务运行存储的回归测试：环形淘汰、并发安全、快照隔离。</summary>
[Trait("Kind", "Unit")]
public sealed class TaskRunStoreTests
{
    private static TaskLogEntry Entry(int index, string stage = "下载") =>
        new(DateTimeOffset.Now, TaskLogLevel.Info, stage, $"行 {index}");

    [Fact]
    public void AppendLog_EvictsOldest_WhenCapacityExceeded()
    {
        var store = new TaskRunStore { MaxLogsPerRun = 5 };

        for (var i = 0; i < 12; i++)
            store.AppendLog("run", Entry(i));

        var logs = store.GetLogs("run");
        Assert.Equal(5, logs.Count);
        Assert.Equal("行 7", logs[0].Message);
        Assert.Equal("行 11", logs[^1].Message);
    }

    [Fact]
    public async Task AppendLog_ConcurrentWrites_KeepCapacityWithoutLoss()
    {
        var store = new TaskRunStore { MaxLogsPerRun = 500 };
        const int threads = 4;
        const int perThread = 1000;
        var barrier = new Barrier(threads);
        var failures = new List<Exception>();

        var workers = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < perThread; i++)
            {
                try
                {
                    store.AppendLog("shared", Entry(t * perThread + i));
                }
                catch (Exception ex)
                {
                    lock (failures) failures.Add(ex);
                }
            }
        })).ToArray();
        await Task.WhenAll(workers);

        Assert.Empty(failures);
        var logs = store.GetLogs("shared");
        Assert.Equal(500, logs.Count);
        var written = new HashSet<string>(
            Enumerable.Range(0, threads * perThread).Select(i => $"行 {i}"));
        Assert.All(logs, log => Assert.Contains(log.Message, written));
    }

    [Fact]
    public void GetLogs_ReturnsSnapshot_IndependentOfLaterAppends()
    {
        var store = new TaskRunStore();
        store.AppendLog("run", Entry(1));
        store.AppendLog("run", Entry(2));

        var snapshot = store.GetLogs("run");
        store.AppendLog("run", Entry(3));

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(3, store.GetLogs("run").Count);
    }

    [Fact]
    public void GetLogs_ReturnsEmpty_ForUnknownRun()
    {
        var store = new TaskRunStore();

        Assert.Empty(store.GetLogs("missing"));
    }

    [Fact]
    public void ClearRun_RemovesLogsAndFailure()
    {
        var store = new TaskRunStore();
        store.AppendLog("run", Entry(1));
        store.CaptureFailure("run", new TaskFailureInfo("Exception", "boom", "", DateTimeOffset.Now));

        store.ClearRun("run");

        Assert.Empty(store.GetLogs("run"));
        Assert.Null(store.GetFailure("run"));
    }

    [Fact]
    public void CapacityOne_KeepsOnlyLatest()
    {
        var store = new TaskRunStore { MaxLogsPerRun = 1 };

        store.AppendLog("run", Entry(1));
        store.AppendLog("run", Entry(2));
        store.AppendLog("run", Entry(3));

        var logs = store.GetLogs("run");
        Assert.Single(logs);
        Assert.Equal("行 3", logs[0].Message);
    }

    [Fact]
    public void CaptureFailure_RetrievesLatest()
    {
        var store = new TaskRunStore();
        var first = new TaskFailureInfo("IOException", "disk full", "", DateTimeOffset.Now);
        var second = new TaskFailureInfo("WebException", "timeout", "", DateTimeOffset.Now);

        store.CaptureFailure("run", first);
        store.CaptureFailure("run", second);

        Assert.Equal("WebException", store.GetFailure("run")?.ExceptionType);
    }

    [Fact]
    public void ClearAll_RemovesEveryRun()
    {
        var store = new TaskRunStore();
        store.AppendLog("a", Entry(1));
        store.AppendLog("b", Entry(2));

        store.ClearAll();

        Assert.Empty(store.GetLogs("a"));
        Assert.Empty(store.GetLogs("b"));
    }

    [Fact]
    public void EmptyRunKey_IsIgnored()
    {
        var store = new TaskRunStore();

        store.AppendLog("", Entry(1));
        store.CaptureFailure("", new TaskFailureInfo("E", "m", "", DateTimeOffset.Now));

        Assert.Empty(store.GetLogs(""));
        Assert.Null(store.GetFailure(""));
    }
}
