using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

/// <summary>任务中心刷新节流与差量更新的回归测试：合并调度 ≤10Hz、条目引用稳定、增删正确。</summary>
[Trait("Kind", "Headless")]
public sealed class TaskManagerRefreshThrottleTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;
    private readonly string _root;

    public TaskManagerRefreshThrottleTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-throttle-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_dataDir, "game");
        Directory.CreateDirectory(_root);
        _oldXdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdgData);
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch
        {
        }
    }

    private TaskManagerViewModel Create(out DownloadViewModel download)
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        download = new DownloadViewModel(services);
        var mods = new ModsViewModel(services);
        return new TaskManagerViewModel(
            download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services),
            new InstallViewModel(download, mods, services),
            historyFilePath: Path.Combine(_dataDir, "history.json"));
    }

    private static async Task FlushRefreshWindowAsync()
    {
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task ProgressBursts_WithinWindow_CollapseToSingleRefresh()
    {
        var tasks = Create(out var download);
        var events = 0;
        tasks.Tasks.CollectionChanged += (_, _) => events++;

        download.ProgressText = "下载 1%";
        download.IsBusy = true;
        download.CanCancel = true;
        // 状态转换立即刷新：任务出现（1 次 Add 事件）。
        Assert.Single(tasks.Tasks);
        Assert.Equal(1, events);

        for (var i = 0; i < 15; i++)
            download.ProgressText = $"下载 {i + 2}%";
        Assert.Equal(1, events);

        // 窗口过后补刷一次，最终状态与逐次刷新一致（无丢失终任务）。
        await FlushRefreshWindowAsync();
        Assert.Equal(1, events);
        Assert.Single(tasks.Tasks);
        Assert.Contains("下载 16%", tasks.Tasks[0].Detail);
    }

    [AvaloniaFact]
    public void UnchangedTask_KeepsInstanceReference_AcrossRefreshes()
    {
        var tasks = Create(out var download);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        var first = Assert.Single(tasks.Tasks);

        tasks.Refresh();
        tasks.Refresh();
        tasks.Refresh();

        Assert.Same(first, tasks.Tasks[0]);
    }

    [AvaloniaFact]
    public void ChangedTask_KeepsInstanceAndNotifiesBindings()
    {
        using var tasks = Create(out var download);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        var first = Assert.Single(tasks.Tasks);
        var changes = new List<string?>();
        first.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var collectionChanges = 0;
        tasks.Tasks.CollectionChanged += (_, _) => collectionChanges++;

        download.ProgressText = "下载 75%";
        download.Progress = 75;
        tasks.Refresh();

        Assert.Same(first, tasks.Tasks[0]);
        Assert.Contains("下载 75%", first.Detail);
        Assert.Equal(75, first.Progress);
        Assert.Equal("75%", first.PercentText);
        Assert.Contains(nameof(TaskEntry.Detail), changes);
        Assert.Contains(nameof(TaskEntry.Progress), changes);
        Assert.Contains(nameof(TaskEntry.PercentText), changes);
        Assert.Equal(0, collectionChanges);
    }

    [AvaloniaFact]
    public void DisappearedTask_IsRemovedFromCollection()
    {
        var tasks = Create(out var download);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        Assert.Single(tasks.Tasks);

        download.IsBusy = false;
        download.CanCancel = false;

        Assert.Empty(tasks.Tasks);
        Assert.Single(tasks.History);
    }

    [AvaloniaFact]
    public async Task ShortLivedTask_WithinWindow_StillTerminalizes()
    {
        var tasks = Create(out var download);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        Assert.Single(tasks.Tasks);

        // 全程处于合并窗口内的短状态翻转（进度→消失）不能丢失终态。
        download.ProgressText = "下载 60%";
        download.IsBusy = false;
        download.CanCancel = false;
        await FlushRefreshWindowAsync();

        Assert.Empty(tasks.Tasks);
        Assert.Single(tasks.History);
    }
}
