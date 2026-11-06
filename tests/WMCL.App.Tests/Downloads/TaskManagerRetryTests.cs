using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

/// <summary>失败上报与重试的回归测试：失败堆栈入历史、重试快照、目标失效分派。</summary>
[Trait("Kind", "Headless")]
public sealed class TaskManagerRetryTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;
    private readonly string _root;

    public TaskManagerRetryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-taskretry-" + Guid.NewGuid().ToString("N"));
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

    private TaskManagerViewModel Create(out DownloadViewModel download, out AppServices services)
    {
        services = new AppServices(new AppSettings { GameDirectory = _root });
        download = new DownloadViewModel(services);
        var mods = new ModsViewModel(services);
        return new TaskManagerViewModel(
            download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services),
            new InstallViewModel(download, mods, services),
            historyFilePath: Path.Combine(_dataDir, "history.json"));
    }

    [AvaloniaFact]
    public async Task FailedInstall_ReportsFailureAndRetryableHistory()
    {
        var tasks = Create(out var download, out var services);
        download.AcceptInstanceCatalog(new[]
        {
            new VersionItem { Id = "1.21.1" }
        });
        download.SelectedVersion = download.Versions.First(v => v.Id == "1.21.1");

        using (services.GameOperations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }

        // 源 VM 属性变化触发的自动 Refresh 已完成差集终态化并一次性消费失败上报。
        tasks.Refresh();
        var history = Assert.Single(tasks.History);
        Assert.Equal(TaskTerminalState.Failed, history.State);
        Assert.True(history.IsRetryable);
        Assert.NotNull(history.Failure);
        Assert.NotNull(history.TaskId);
        Assert.Equal(TaskRetryKind.VersionInstall, history.Retry!.Kind);
        Assert.Equal("1.21.1", history.Retry.VersionId);
        Assert.Null(download.LastTaskFailure);
        Assert.Null(download.CaptureRetryPayload());
    }

    [AvaloniaFact]
    public async Task RetryAsync_ReturnsFalseForMissingVersion()
    {
        Create(out var download, out _);

        var payload = TaskRetryPayload.Create(TaskRetryKind.VersionInstall, _root, "9.9.9-不存在");
        var accepted = await download.RetryAsync(payload);

        Assert.False(accepted);
        Assert.Contains("不在当前列表", download.Status);
    }

    [AvaloniaFact]
    public async Task RetryAsync_ReturnsFalseForUnsupportedKind()
    {
        Create(out var download, out _);

        var payload = TaskRetryPayload.Create(TaskRetryKind.ModpackInstall, _root);
        Assert.False(await download.RetryAsync(payload));

        var combined = TaskRetryPayload.Create(TaskRetryKind.CombinedInstall, _root);
        Assert.False(await download.RetryAsync(combined));
    }

    [AvaloniaFact]
    public async Task RetryAsync_ReturnsFalseWhenBusy()
    {
        Create(out var download, out _);
        download.IsBusy = true;

        var payload = TaskRetryPayload.Create(TaskRetryKind.VersionInstall, _root, "1.21.1");
        Assert.False(await download.RetryAsync(payload));
        Assert.Contains("已有任务进行中", download.Status);
    }

    [AvaloniaFact]
    public async Task RetryTaskCommand_RetriesWithOriginalParameters()
    {
        var tasks = Create(out var download, out var services);
        download.AcceptInstanceCatalog(new[]
        {
            new VersionItem { Id = "1.21.1" }
        });
        download.SelectedVersion = download.Versions.First(v => v.Id == "1.21.1");

        // 全程保持目录占用，两次安装都在 Acquire 处失败，不触网。
        using (services.GameOperations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
            tasks.Refresh();
            var history = Assert.Single(tasks.History);
            Assert.True(history.IsRetryable);
            Assert.Equal("1.21.1", history.Retry!.VersionId);

            await tasks.RetryTaskCommand.ExecuteAsync(history);

            Assert.Equal(2, tasks.History.Count);
            Assert.Equal(TaskTerminalState.Failed, tasks.History[0].State);
            Assert.Contains("安装失败", tasks.History[0].Detail);
            Assert.Equal("1.21.1", tasks.History[0].Retry!.VersionId);
        }
    }

    [AvaloniaFact]
    public async Task FailedBatchInstall_CapturesVersionList()
    {
        var tasks = Create(out var download, out var services);
        download.AcceptInstanceCatalog(new[]
        {
            new VersionItem { Id = "1.21.1" },
            new VersionItem { Id = "1.20.1" }
        });
        foreach (var id in new[] { "1.21.1", "1.20.1" })
            download.SelectedVersions.Add(download.Versions.First(v => v.Id == id));

        using (services.GameOperations.Acquire(_root, "测试占用"))
        {
            await download.InstallBatchCommand.ExecuteAsync(null);
        }

        tasks.Refresh();
        var history = Assert.Single(tasks.History);
        Assert.Equal(TaskTerminalState.Failed, history.State);
        Assert.Equal(TaskRetryKind.BatchInstall, history.Retry!.Kind);
        Assert.Contains("1.21.1", history.Retry.Options["versions"]);
        Assert.Contains("1.20.1", history.Retry.Options["versions"]);
    }
}
