using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using System.Text.Json;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed partial class ModUpdateTargetTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecliningTheOriginalWarningDoesNotStartEitherUpdateOrRememberAcknowledgement(bool selected)
    {
        using var fixture = new Fixture { ConfirmUpdate = (_, _) => Task.FromResult(false) };
        var model = await fixture.CreateModelAsync();
        var completed = new List<TaskEntry>();
        model.ModUpdateTaskFinished += (_, task) => completed.Add(task);
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        var saves = fixture.SettingsSaves;
        await (selected ? model.UpdateSelectedModCommand : model.UpdateAllModsCommand).ExecuteAsync(null);
        Assert.Equal(1, fixture.Confirmations);
        Assert.Contains("永久损坏存档", fixture.LastWarning);
        Assert.Contains("整合包", fixture.LastWarning);
        Assert.Contains("回收站", fixture.LastWarning);
        Assert.False(fixture.Services.Settings.HintUpdateModWarningAcknowledged);
        Assert.Equal(saves, fixture.SettingsSaves);
        Assert.False(model.IsUpdatingMods);
        Assert.False(model.IsCheckingUpdates);
        Assert.Empty(completed);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
    }

    [AvaloniaTheory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 0)]
    [InlineData(14, true, 0)]
    [InlineData(15, true, 1)]
    public async Task WarningMemoryAndFifteenFileThresholdMatchTheOriginal(int count, bool acknowledged, int expectedWarnings)
    {
        using var fixture = new Fixture();
        for (var index = 1; index < count; index++)
            File.WriteAllText(Path.Combine(fixture.Directory(fixture.Root, "first"), $"extra-{index}.jar"), "old extra " + index);
        fixture.Services.Settings.HintUpdateModWarningAcknowledged = acknowledged;
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        var saves = fixture.SettingsSaves;
        await model.UpdateAllModsCommand.ExecuteAsync(null);
        Assert.Equal(expectedWarnings, fixture.Confirmations);
        Assert.Equal(saves + expectedWarnings, fixture.SettingsSaves);
        Assert.True(fixture.Services.Settings.HintUpdateModWarningAcknowledged);
        Assert.True(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(fixture.Services.Settings))!.HintUpdateModWarningAcknowledged);
        Assert.Contains("已更新", model.UpdateStatus);
        Assert.Equal(count, System.IO.Directory.EnumerateFiles(fixture.RecycledDirectory, "*.jar", SearchOption.AllDirectories).Count());
        Assert.False(model.IsUpdatingMods);
        Assert.Equal(1, fixture.Downloads);
    }

    [AvaloniaTheory]
    [InlineData("root")]
    [InlineData("instance")]
    [InlineData("empty")]
    public async Task SwitchingTargetDuringConfirmationRejectsTheFrozenUpdateWithoutSavingWarningMemory(string change)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture { ConfirmUpdate = (_, _) => { entered.TrySetResult(); return answer.Task; } };
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        var updating = model.UpdateAllModsCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.CanUpdateAllMods);
        Assert.False(model.IsUpdatingMods);
        if (change == "root") fixture.Services.SetGameDirectory(fixture.OtherRoot);
        else if (change == "instance") await model.SelectModInstanceAsync("second");
        else model.SelectedModInstance = null;
        answer.SetResult(true);
        await updating;
        Assert.False(fixture.Services.Settings.HintUpdateModWarningAcknowledged);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
    }

    [AvaloniaFact]
    public async Task FailedWarningPersistenceKeepsTheWarningUnacknowledgedAndDoesNotStartTheTask()
    {
        using var fixture = new Fixture { FailSavingSettings = true };
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        await model.UpdateAllModsCommand.ExecuteAsync(null);
        Assert.Contains("fixture settings failure", model.UpdateStatus);
        Assert.False(fixture.Services.Settings.HintUpdateModWarningAcknowledged);
        Assert.False(model.IsUpdatingMods);
        Assert.Equal(0, fixture.Downloads);
        fixture.AssertOriginalFiles();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskCenterCancellationUsesTheFrozenTargetAndRecordsOneTerminalResult(bool switchRoot)
    {
        using var fixture = new Fixture { DelayDownload = true };
        var model = await fixture.CreateModelAsync();
        var tasks = Tasks(fixture, model);
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        var updating = model.UpdateAllModsCommand.ExecuteAsync(null);
        await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var active = Assert.Single(tasks.Tasks);
        Assert.Equal("Mod 更新：first", active.Title);
        Assert.True(active.HasCancel);
        if (switchRoot)
        {
            fixture.Services.SetGameDirectory(fixture.OtherRoot);
            await model.SelectModInstanceAsync("second");
        }
        tasks.CancelTaskCommand.Execute(active);
        await updating.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(tasks.Tasks);
        var history = Assert.Single(tasks.History);
        Assert.Equal("Mod 更新：first", history.Title);
        Assert.Equal("已取消", history.PercentText);
        Assert.False(history.HasCancel);
        tasks.Refresh();
        tasks.Refresh();
        Assert.Single(tasks.History);
        tasks.ClearHistoryCommand.Execute(null);
        tasks.Refresh();
        Assert.Empty(tasks.History);
        fixture.AssertOriginalFiles();
        Assert.False(System.IO.Directory.Exists(fixture.RecycledDirectory));
        Assert.Null(fixture.Services.GameOperations.GetActiveOperation(fixture.Root));
        if (switchRoot) Assert.Equal("", model.UpdateStatus);
    }

    [AvaloniaTheory]
    [InlineData("success", "完成")]
    [InlineData("dependency-failure", "失败")]
    [InlineData("recycle-failure", "完成（备份保留）")]
    public async Task TaskCenterRecordsActualCompletionAndKeepsOldFilesRecoverable(string scenario, string terminal)
    {
        using var fixture = new Fixture { MissingDependency = scenario == "dependency-failure", FailRecycling = scenario == "recycle-failure" };
        var model = await fixture.CreateModelAsync();
        var tasks = Tasks(fixture, model);
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        await model.UpdateSelectedModCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(tasks.Tasks);
        var history = Assert.Single(tasks.History);
        Assert.Equal(terminal, history.PercentText);
        Assert.False(history.HasCancel);
        tasks.Refresh();
        Assert.Single(tasks.History);
        if (scenario == "dependency-failure")
        {
            fixture.AssertOriginalFiles();
            Assert.False(System.IO.Directory.Exists(fixture.RecycledDirectory));
        }
        else
        {
            Assert.Equal(100, history.Progress);
            Assert.False(File.Exists(fixture.OldFile(fixture.Root, "first")));
            var backupRoot = scenario == "success" ? fixture.RecycledDirectory : fixture.Directory(fixture.Root, "first");
            var backup = Assert.Single(System.IO.Directory.EnumerateFiles(backupRoot, "old.jar", SearchOption.AllDirectories));
            Assert.Equal("old first", File.ReadAllText(backup));
            if (scenario == "recycle-failure")
            {
                Assert.Contains("原始备份保留于", history.Detail);
                Assert.Contains("fixture recycle failure", model.UpdateStatus);
            }
            else Assert.Contains("回收站", history.Detail);
        }
    }

    private static TaskManagerViewModel Tasks(Fixture fixture, ModsViewModel model)
    {
        var download = new DownloadViewModel(fixture.Services);
        var install = new InstallViewModel(download, model, fixture.Services);
        return new TaskManagerViewModel(download, model, new ModpackViewModel(fixture.Services),
            new InstanceContentViewModel(fixture.Services), new InstanceViewModel(fixture.Services), install);
    }
}
