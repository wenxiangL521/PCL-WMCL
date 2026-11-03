using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.Jobs;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class CatalogLoadingTaskIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-catalog-tasks-" + Guid.NewGuid().ToString("N"));

    public CatalogLoadingTaskIsolationTests() => Directory.CreateDirectory(_root);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogRequestsNeverInsertBeforeAnActualCombinedInstallOrCreateHistory(bool fail)
    {
        var services = Services();
        var versions = new Gate<IReadOnlyList<VersionItem>>();
        var loaders = new Gate<IReadOnlyList<FabricLoaderInfo>>();
        var forge = new Gate<IReadOnlyList<string>>();
        var download = new DownloadViewModel(services, () => versions.LoadAsync(fail))
        {
            SelectedVersion = new VersionItem { Id = "1.20.1" }
        };
        var mods = new ModsViewModel(services, (_, _) => loaders.LoadAsync(fail),
            (_, _) => forge.LoadAsync(fail), _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]));
        var backend = new WaitingBackend();
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services,
            _ => backend, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        install.ShowSelectedVersion();
        using var tasks = CreateTasks(services, download, mods, install);
        var installing = install.InstallCombinedCommand.ExecuteAsync(null);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        install.CombinedProgress = 42;
        tasks.Refresh();
        var entry = Assert.Single(tasks.Tasks);
        var taskId = entry.TaskId;
        var changes = 0;
        tasks.Tasks.CollectionChanged += (_, _) => changes++;
        var view = new TaskManagerView { DataContext = tasks };
        var window = new Window { Content = view, Width = 668, Height = 650 };
        Task? loadingVersions = null;
        Task? loadingMods = null;
        try
        {
            window.Show();
            Pump();
            var container = Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>());
            var row = Assert.Single(view.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "TaskRow");
            var list = view.FindControl<ListBox>("TaskList")!;
            list.SelectedItem = entry;
            var initialY = row.TranslatePoint(default, view)!.Value.Y;
            loadingVersions = download.LoadAsync();
            loadingMods = mods.PrepareGameVersionAsync("1.20.1");
            await Task.WhenAll(versions.Entered.Task, loaders.Entered.Task, forge.Entered.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(download.IsBusy);
            Assert.False(download.CanCancel);
            Assert.False(download.CanPause);
            Assert.True(mods.IsBusy);
            Assert.False(mods.CanCancel);
            Assert.True(mods.IsForgelikeBusy);
            Assert.False(mods.IsInstallingForgelike);
            AssertStable();

            versions.Release.TrySetResult([new VersionItem { Id = "1.20.1" }]);
            await loadingVersions.WaitAsync(TimeSpan.FromSeconds(5));
            AssertStable();
            loaders.Release.TrySetResult([new FabricLoaderInfo("0.16.0", true)]);
            await loaders.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertStable();
            forge.Release.TrySetResult(["47.2.0"]);
            await loadingMods.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(download.IsBusy);
            Assert.False(download.CanCancel);
            Assert.False(download.CanPause);
            Assert.False(mods.IsBusy);
            Assert.False(mods.CanCancel);
            Assert.False(mods.IsForgelikeBusy);
            Assert.False(mods.IsInstallingForgelike);
            AssertStable();
            Assert.Equal(0, changes);

            void AssertStable()
            {
                tasks.Refresh();
                Pump();
                Assert.True(install.IsCombinedBusy);
                Assert.Same(entry, Assert.Single(tasks.Tasks));
                Assert.Equal("组合安装", entry.Title);
                Assert.Equal(taskId, entry.TaskId);
                Assert.Same(entry, list.SelectedItem);
                Assert.Equal(42, tasks.OverallProgress);
                Assert.Equal("1 个任务 · 42%", tasks.OverallText);
                Assert.Empty(tasks.History);
                Assert.True(tasks.HasTransferStats);
                Assert.Same(container, Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>()));
                Assert.Same(row, Assert.Single(view.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "TaskRow"));
                Assert.Equal(initialY, row.TranslatePoint(default, view)!.Value.Y, 3);
            }
        }
        finally
        {
            versions.Release.TrySetResult([]);
            loaders.Release.TrySetResult([]);
            forge.Release.TrySetResult([]);
            if (loadingVersions is not null) await loadingVersions;
            if (loadingMods is not null) await loadingMods;
            install.CancelCombinedCommand.Execute(null);
            await installing.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CatalogOnlyBusyFlagsDoNotActivateTaskOrTransferIndicators()
    {
        var services = Services();
        var versions = new Gate<IReadOnlyList<VersionItem>>();
        var loaders = new Gate<IReadOnlyList<FabricLoaderInfo>>();
        var forge = new Gate<IReadOnlyList<string>>();
        var download = new DownloadViewModel(services, () => versions.LoadAsync(false));
        var mods = new ModsViewModel(services, (_, _) => loaders.LoadAsync(false),
            (_, _) => forge.LoadAsync(false), _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]));
        var install = new InstallViewModel(download, mods, services);
        using var tasks = CreateTasks(services, download, mods, install);
        var pendingVersions = download.LoadAsync();
        var pendingMods = mods.PrepareGameVersionAsync("1.20.1");
        try
        {
            Assert.Empty(tasks.Tasks);
            Assert.False(tasks.HasTasks);
            Assert.False(tasks.HasTransferStats);
            Assert.Empty(tasks.History);
        }
        finally
        {
            versions.Release.TrySetResult([]);
            loaders.Release.TrySetResult([]);
            forge.Release.TrySetResult([]);
            await Task.WhenAll(pendingVersions, pendingMods).WaitAsync(TimeSpan.FromSeconds(5));
        }
        tasks.Refresh();
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.History);
        Assert.False(tasks.HasTransferStats);
    }

    [AvaloniaTheory]
    [InlineData("Fabric")]
    [InlineData("Forge")]
    public async Task ActualLoaderInstallStillCreatesAnImmediatelyCancelableTask(string kind)
    {
        var services = Services();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = false;
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true)]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>(["47.2.0"]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]),
            installComponent: async (_, _, _, cancellation) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
                catch (OperationCanceledException) { canceled = true; throw; }
                return "unreachable";
            });
        await mods.PrepareGameVersionAsync("1.20.1");
        var download = new DownloadViewModel(services);
        using var tasks = CreateTasks(services, download, mods, new InstallViewModel(download, mods, services));
        var command = kind == "Fabric" ? mods.InstallFabricCommand : mods.InstallForgelikeCommand;
        var pending = command.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var entry = Assert.Single(tasks.Tasks);
            Assert.Equal(kind == "Fabric" ? "安装 Fabric" : "安装 Forge / NeoForge", entry.Title);
            Assert.True(entry.HasCancel);
            Assert.True(tasks.HasTransferStats);
            Assert.Equal(kind == "Forge", mods.IsInstallingForgelike);
            entry.Cancel!();
        }
        finally
        {
            if (kind == "Fabric") mods.CancelCommand.Execute(null);
            else mods.CancelForgelikeCommand.Execute(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(canceled);
        Assert.False(mods.IsInstallingForgelike);
        Assert.Empty(tasks.Tasks);
        Assert.Equal(TaskTerminalState.Canceled, Assert.Single(tasks.History).State);
    }

    [AvaloniaFact]
    public async Task MainWindowIgnoresLoaderCatalogsForTaskStateAndNavigation()
    {
        var loaders = new Gate<IReadOnlyList<FabricLoaderInfo>>();
        var forge = new Gate<IReadOnlyList<string>>();
        using var main = new MainWindowViewModel(Services(), createMods: services => new ModsViewModel(services,
            (_, _) => loaders.LoadAsync(false), (_, _) => forge.LoadAsync(false),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([])));
        var page = main.CurrentPage;
        var pending = main.Mods.PrepareGameVersionAsync("1.20.1");
        try
        {
            Assert.False(main.IsTaskActive);
            Assert.Equal("", main.TaskText);
            main.OpenTaskCommand.Execute(null);
            Assert.Same(page, main.CurrentPage);
            main.Install.CombinedProgress = 42;
            main.Install.IsCombinedBusy = true;
            Assert.True(main.IsTaskActive);
            Assert.Equal("组合安装", main.TaskText);
            Assert.Equal(42, main.TaskProgress);
        }
        finally
        {
            loaders.Release.TrySetResult([]);
            forge.Release.TrySetResult([]);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            main.Install.IsCombinedBusy = false;
        }
        Assert.False(main.IsTaskActive);
    }

    [Fact]
    public async Task FinishedCatalogPreservesManualBusyAndPauseCommandState()
    {
        var download = new DownloadViewModel(Services(), () => Task.FromResult<IReadOnlyList<VersionItem>>([]));
        await download.LoadAsync();
        download.IsBusy = true;
        Assert.True(download.CanCancel);
        Assert.True(download.CanPause);
        download.CanCancel = false;
        Assert.False(download.CanCancel);
        download.CanCancel = true;
        download.PauseCommand.Execute(null);
        Assert.True(download.IsPaused);
        Assert.False(download.CanCancel);
        Assert.False(download.CanPause);
        Assert.True(download.CanResume);
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });

    private TaskManagerViewModel CreateTasks(AppServices services, DownloadViewModel download, ModsViewModel mods, InstallViewModel install) =>
        new(download, mods, new ModpackViewModel(services), new InstanceContentViewModel(services),
            new InstanceViewModel(services), install, historyFilePath: Path.Combine(_root, "history-" + Guid.NewGuid().ToString("N") + ".json"));

    private static void Pump()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private sealed class Gate<T>
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<T> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> LoadAsync(bool fail)
        {
            Entered.TrySetResult();
            try
            {
                var result = await Release.Task;
                if (fail) throw new IOException("catalog fixture failure");
                return result;
            }
            finally { Finished.TrySetResult(); }
        }
    }

    private sealed class WaitingBackend : ICombinedInstallBackend
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken cancellation)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        }

        public Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
