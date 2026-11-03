using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.Jobs;

namespace WMCL.App.Tests;

public sealed class LaunchFileVerificationInteractionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-launch-verify-ui-" + Guid.NewGuid().ToString("N"));

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("directory-changed")]
    public async Task FileVerificationRunsBeforeJavaAndBlocksLaunchUntilItFinishes(string outcome)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        IProgress<JobProgress>? capturedProgress = null;
        var services = CreateServices(async (root, id, options, progress, ct) =>
        {
            calls++;
            Assert.Equal(_root, root);
            Assert.Equal("Pack", id);
            Assert.True(options.VerifyExisting);
            capturedProgress = progress;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            if (outcome == "failure") throw new IOException("下载服务器不可用");
            progress?.Report(new JobProgress("校验完成", 1));
        });
        using var main = new MainWindowViewModel(services, loadVersions: _ => Task.FromResult<IReadOnlyList<VersionItem>>([]));
        main.Tasks.ClearHistoryCommand.Execute(null);
        var launch = main.Launch;
        launch.SelectedVersion = new VersionItem { Id = "Pack", IsInstalled = true };
        var execution = launch.LaunchCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            capturedProgress!.Report(new JobProgress("修复游戏资源", 0.37, Detail: "objects/long-resource-name"));
            Dispatcher.UIThread.RunJobs();
            main.Tasks.Refresh();
            Assert.True(launch.IsCheckingGameFiles);
            Assert.True(launch.IsBusy);
            Assert.True(launch.CanCancel);
            Assert.False(launch.CanLaunch);
            Assert.False(launch.IsGameRunning);
            Assert.True(main.IsTaskActive);
            Assert.Equal("启动前校验游戏文件", main.TaskText);
            Assert.Equal(37, launch.Progress);
            Assert.True(Assert.Single(main.Tasks.Tasks, task => task.Title == "启动前校验游戏文件").HasCancel);
            Assert.Equal(1, calls);
            if (outcome == "cancel") launch.CancelCommand.Execute(null);
            else
            {
                if (outcome == "directory-changed") services.Settings.GameDirectory = Path.Combine(_root, "other");
                release.SetResult();
            }
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            main.Tasks.Refresh();
            Assert.False(launch.IsCheckingGameFiles);
            Assert.False(launch.IsBusy);
            Assert.False(launch.CanCancel);
            Assert.False(launch.IsGameRunning);
            Assert.Equal(0, launch.Progress);
            Assert.Equal("", launch.ProgressText);
            Assert.DoesNotContain(main.Tasks.Tasks, task => task.Title == "启动前校验游戏文件");
            var history = Assert.Single(main.Tasks.History, task => task.Title == "启动前校验游戏文件");
            Assert.Equal(outcome == "failure" ? TaskTerminalState.Failed
                : outcome == "cancel" ? TaskTerminalState.Canceled : TaskTerminalState.Completed, history.State);
            if (outcome == "success")
            {
                Assert.False(launch.HasGameFileCheckError);
                Assert.Contains("版本继承出现循环", launch.Status);
                Assert.Contains("游戏文件检查与补全完成", launch.Log);
            }
            else if (outcome == "failure")
            {
                Assert.True(launch.HasGameFileCheckError);
                Assert.Contains("下载服务器不可用", launch.GameFileCheckError);
                Assert.DoesNotContain("版本继承出现循环", launch.Status);
            }
            else if (outcome == "cancel")
            {
                Assert.False(launch.HasGameFileCheckError);
                Assert.Equal("已取消游戏文件校验，未启动游戏", launch.Status);
            }
            else Assert.Contains("游戏目录已切换", launch.Status);
            capturedProgress.Report(new JobProgress("迟到的进度", 0.9));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, launch.Progress);
        }
        finally
        {
            release.TrySetResult();
            if (!execution.IsCompleted) launch.CancelCommand.Execute(null);
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaFact]
    public async Task FailedVerificationCanBeRetriedAndChangingTargetClearsItsError()
    {
        var calls = 0;
        var services = CreateServices((_, _, _, _, _) =>
        {
            calls++;
            return calls == 1 ? Task.FromException(new IOException("first attempt failed")) : Task.CompletedTask;
        });
        var launch = new LaunchViewModel(services) { SelectedVersion = new VersionItem { Id = "Pack", IsInstalled = true } };
        await launch.LaunchCommand.ExecuteAsync(null);
        Assert.True(launch.HasGameFileCheckError);
        Assert.True(launch.CanLaunch);
        await launch.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
        Assert.False(launch.HasGameFileCheckError);
        Assert.Contains("版本继承出现循环", launch.Status);
        launch.GameFileCheckError = "old target failure";
        launch.SelectedVersion = new VersionItem { Id = "Other", IsInstalled = true };
        Assert.False(launch.HasGameFileCheckError);
    }

    private AppServices CreateServices(Func<string, string, WMCL.Minecraft.InstallOptions, IProgress<JobProgress>?, CancellationToken, Task> check)
    {
        var directory = Path.Combine(_root, "versions", "Pack");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Pack.json"), """{"id":"Pack","inheritsFrom":"Pack"}""");
        return new AppServices(new AppSettings
        {
            GameDirectory = _root,
            AccountType = "offline",
            MinecraftPurchaseNoticeAcknowledged = true
        }, saveSettings: _ => { }, ensureGameFiles: check);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
