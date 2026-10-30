using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

/// <summary>任务中心日志单点采集的回归测试：TaskId 分配、差分追加、终态冻结、日志脱敏。</summary>
public sealed class TaskManagerLogCaptureTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;
    private readonly string _root;

    public TaskManagerLogCaptureTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-tasklog-" + Guid.NewGuid().ToString("N"));
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

    private TaskManagerViewModel Create(out DownloadViewModel download, out TaskRunStore runs)
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        download = new DownloadViewModel(services);
        var mods = new ModsViewModel(services);
        runs = new TaskRunStore();
        return new TaskManagerViewModel(
            download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services),
            new InstallViewModel(download, mods, services),
            historyFilePath: Path.Combine(_dataDir, "history.json"),
            runs: runs);
    }

    [AvaloniaFact]
    public void Refresh_AssignsTaskIdAndCapturesLogs()
    {
        var tasks = Create(out var download, out var runs);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();

        var entry = Assert.Single(tasks.Tasks);
        Assert.NotNull(entry.TaskId);
        Assert.True(entry.HasLogs);
        Assert.Equal(1, entry.LogCount);
        Assert.Contains("下载 50%", runs.GetLogs(entry.TaskId!)[0].Message);

        download.ProgressText = "下载 75%";
        tasks.Refresh();

        var updated = Assert.Single(tasks.Tasks);
        Assert.Equal(entry.TaskId, updated.TaskId);
        Assert.Equal(2, updated.LogCount);
    }

    [AvaloniaFact]
    public void Refresh_DoesNotAppendWhenDetailUnchanged()
    {
        var tasks = Create(out var download, out var runs);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();
        var taskId = Assert.Single(tasks.Tasks).TaskId!;

        tasks.Refresh();
        tasks.Refresh();

        Assert.Equal(1, runs.GetLogCount(taskId));
    }

    [AvaloniaFact]
    public void Refresh_FreezesLogsAfterTaskDisappears()
    {
        var tasks = Create(out var download, out var runs);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();
        var taskId = Assert.Single(tasks.Tasks).TaskId!;
        Assert.Equal(1, runs.GetLogCount(taskId));

        download.IsBusy = false;
        download.CanCancel = false;
        tasks.Refresh();

        Assert.Empty(tasks.Tasks);
        var history = Assert.Single(tasks.History);
        Assert.Equal(taskId, history.TaskId);

        var frozen = runs.GetLogCount(taskId);
        download.ProgressText = "下载 99%";
        tasks.Refresh();
        Assert.Equal(frozen, runs.GetLogCount(taskId));
    }

    [AvaloniaFact]
    public void Refresh_AssignsNewTaskIdWhenSameTitleReappears()
    {
        var tasks = Create(out var download, out var runs);
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();
        var first = Assert.Single(tasks.Tasks).TaskId;

        download.IsBusy = false;
        download.CanCancel = false;
        tasks.Refresh();

        download.ProgressText = "重新下载 10%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();

        var second = Assert.Single(tasks.Tasks).TaskId;
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [AvaloniaFact]
    public void Refresh_RedactsSensitivePatternsInLogs()
    {
        var tasks = Create(out var download, out var runs);
        download.ProgressText = "下载 http://user:secret@mirror.example/x 重试";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();

        var entry = Assert.Single(tasks.Tasks);
        var message = runs.GetLogs(entry.TaskId!)[0].Message;
        Assert.DoesNotContain("secret", message);
        Assert.Contains("***", message);
        Assert.Contains("重试", message);
    }

    [AvaloniaFact]
    public void BuildFailureCopyText_ContainsRedactedStackAndType()
    {
        var failure = new TaskFailureInfo(
            "System.Net.WebException",
            "远程服务器返回错误 proxyPassword=topsecret",
            "   at Download() in Downloader.cs:line 88",
            DateTimeOffset.Now);
        const string title = "安装 1.21.1";

        var text = TaskManagerViewModel.BuildFailureCopyText(title, failure);

        Assert.Contains(title, text);
        Assert.Contains("System.Net.WebException", text);
        Assert.Contains("Downloader.cs:line 88", text);
        Assert.DoesNotContain("topsecret", text);
        Assert.Contains("***", text);
    }
}
