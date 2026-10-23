using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class LogsViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wmcl-logs-" + Guid.NewGuid().ToString("N"));

    public LogsViewModelTests() => Directory.CreateDirectory(_directory);

    private string Write(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "log content");
        return path;
    }

    private LogsViewModel Create(Func<string, string, Task<bool>> confirm, Action<string>? open = null) =>
        new(new AppServices(new AppSettings { GameDirectory = _directory }, saveSettings: _ => { }),
            _directory, confirm, open);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedDeletionKeepsConfirmedTargetWhenSelectionChanges(bool clearSelection)
    {
        var first = Write("first.log");
        var second = Write("second.txt");
        var confirmation = new TaskCompletionSource<bool>();
        string? message = null;
        var model = Create((_, text) => { message = text; return confirmation.Task; });
        model.Refresh();
        model.SelectedLog = model.Logs.Single(log => log.Path == first);
        var deletion = model.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.Contains("first.log", message);
        model.SelectedLog = clearSelection ? null : model.Logs.Single(log => log.Path == second);
        confirmation.SetResult(true);
        await deletion;
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal("已删除 first.log", model.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledConfirmationPreservesFiles(bool batch)
    {
        var path = Write("keep.log");
        var model = Create((_, _) => Task.FromResult(false));
        model.Refresh();
        await (batch ? model.ClearOldCommand : model.DeleteSelectedCommand).ExecuteAsync(null);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task BatchDeletionPreservesFilesAddedWhileConfirmationIsPending()
    {
        var first = Write("first.LOG");
        var second = Write("second.txt");
        var unrelated = Write("keep.zip");
        var confirmation = new TaskCompletionSource<bool>();
        string? message = null;
        var model = Create((_, text) => { message = text; return confirmation.Task; });
        model.Refresh();
        var deletion = model.ClearOldCommand.ExecuteAsync(null);
        Assert.Contains("2 个", message);
        var added = Write("new.log");
        model.Refresh();
        confirmation.SetResult(true);
        await deletion;
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(added));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(added, Assert.Single(model.Logs).Path);
        Assert.Equal("已清理 2 个日志", model.Status);
    }

    [Fact]
    public async Task BatchDeletionContinuesAfterAnUndeletableTarget()
    {
        var replaced = Write("replaced.log");
        var removable = Write("removable.txt");
        var confirmation = new TaskCompletionSource<bool>();
        var model = Create((_, _) => confirmation.Task);
        model.Refresh();
        var deletion = model.ClearOldCommand.ExecuteAsync(null);
        File.Delete(replaced);
        Directory.CreateDirectory(replaced);
        model.Logs.Clear();
        confirmation.SetResult(true);
        await deletion;
        Assert.True(Directory.Exists(replaced));
        Assert.False(File.Exists(removable));
        Assert.Equal("已清理 1 个日志，跳过 1 个无法删除的文件", model.Status);
    }

    [Fact]
    public async Task BatchDeletionDoesNotCountAlreadyRemovedFiles()
    {
        var removed = Write("removed.log");
        Write("remaining.txt");
        var confirmation = new TaskCompletionSource<bool>();
        var model = Create((_, _) => confirmation.Task);
        model.Refresh();
        var deletion = model.ClearOldCommand.ExecuteAsync(null);
        File.Delete(removed);
        confirmation.SetResult(true);
        await deletion;
        Assert.Equal("已清理 1 个日志", model.Status);
        Assert.Empty(model.Logs);
    }

    [Fact]
    public void OpenCommandsPreserveExactPathsAndSurfaceFailures()
    {
        var path = Write("中文 log & file.txt");
        var opened = new List<string>();
        var model = Create((_, _) => Task.FromResult(false), opened.Add);
        model.Refresh();
        model.OpenSelectedCommand.Execute(null);
        model.OpenFolderCommand.Execute(null);
        Assert.Equal(new[] { path, _directory }, opened);
        var failing = Create((_, _) => Task.FromResult(false), _ => throw new IOException("open failed"));
        failing.Refresh();
        failing.OpenSelectedCommand.Execute(null);
        Assert.Equal("打开失败：open failed", failing.Status);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
