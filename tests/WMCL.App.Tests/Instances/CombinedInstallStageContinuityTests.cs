using System.Collections.Specialized;
using System.Text.Json;
using System.Threading.Channels;
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
using WMCL.Download;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class CombinedInstallStageContinuityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-stage-continuity-" + Guid.NewGuid().ToString("N"));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealInstallStagesKeepTheSameVisibleContainerWhileCatalogRefreshesRun(bool cancel)
    {
        Directory.CreateDirectory(_root);
        var phases = Channel.CreateUnbounded<Phase>();
        var catalogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogRelease = new TaskCompletionSource<IReadOnlyList<VersionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var selected = new VersionItem { Id = "1.20.1" };
        var download = new DownloadViewModel(services, _ =>
        {
            catalogEntered.TrySetResult();
            return catalogRelease.Task;
        })
        { SelectedVersion = selected };
        var mods = InstallTestData.CreateMods(services);
        var install = new InstallViewModel(download, mods, services,
            root => new Backend(root, phases.Writer),
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        install.ShowSelectedVersion();
        install.SelectedBaseLoader = "Fabric";
        install.InstallFabricApi = false;
        install.InstallOptiFabric = false;
        install.InstanceName = "Stable";
        using var instance = new InstanceViewModel(services);
        using var tasks = new TaskManagerViewModel(download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), instance, install,
            historyFilePath: Path.Combine(_root, "task-history.json"));
        var view = new TaskManagerView { DataContext = tasks };
        var window = new Window { Content = view, Width = 668, Height = 500 };
        window.Show();
        var execution = install.InstallCombinedCommand.ExecuteAsync(null);
        Task? refresh = null;
        try
        {
            var first = await phases.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            tasks.Refresh();
            await PumpAsync();
            var entry = Assert.Single(tasks.Tasks);
            Assert.Equal("组合安装", entry.Title);
            var list = view.FindControl<ListBox>("TaskList")!;
            list.SelectedItem = entry;
            var container = Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>());
            var row = Named<Grid>(view, "TaskRow");
            var details = Named<Expander>(view, "TaskDetails");
            details.IsExpanded = false;
            await PumpAsync();
            var rowY = Position(row, view).Y;
            var rowHeight = row.Bounds.Height;
            var hidden = 0;
            var rebound = 0;
            var detached = 0;
            var taskCollectionChanges = 0;
            container.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.IsVisibleProperty && !container.IsVisible) hidden++;
                if (args.Property == StyledElement.DataContextProperty || args.Property == ContentControl.ContentProperty) rebound++;
            };
            container.DetachedFromVisualTree += (_, _) => detached++;
            tasks.Tasks.CollectionChanged += (_, args) =>
            {
                if (args.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove or
                    NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Move)
                    taskCollectionChanges++;
            };

            refresh = install.RefreshVersionCatalogCommand.ExecuteAsync(null);
            await catalogEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            tasks.Refresh();
            Assert.False(download.CanCancel);
            Assert.False(download.CanPause);
            AssertStable();
            var initialProgress = install.CombinedProgress;
            Assert.Equal(initialProgress, tasks.OverallProgress, 10);
            catalogRelease.SetResult([selected]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            tasks.Refresh();
            AssertStable();
            Assert.Empty(tasks.History);
            first.Release.TrySetResult();

            for (var phaseIndex = 1; phaseIndex < 7; phaseIndex++)
            {
                var phase = await phases.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                await PumpAsync();
                tasks.Refresh();
                await PumpAsync();
                AssertStable();
                DownloadMonitor.Reset();
                await PumpAsync();
                AssertStable();
                if (cancel && phaseIndex == 3)
                {
                    entry.Cancel!();
                    phase.Release.TrySetResult();
                    break;
                }
                phase.Release.TrySetResult();
            }
            Assert.Equal(0, taskCollectionChanges);
            Assert.Equal(0, hidden);
            Assert.Equal(0, rebound);
            Assert.Equal(0, detached);
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
            await PumpAsync();
            tasks.Refresh();
            Assert.Empty(tasks.Tasks);
            Assert.False(install.IsCombinedBusy);
            var finished = Assert.Single(tasks.History);
            Assert.Equal("安装 Stable", finished.Title);
            Assert.Equal(cancel ? TaskTerminalState.Canceled : TaskTerminalState.Completed, finished.State);
            Assert.Equal(!cancel, Directory.Exists(Path.Combine(_root, "versions", "Stable")));

            void AssertStable()
            {
                Assert.True(install.IsCombinedBusy);
                Assert.Same(entry, Assert.Single(tasks.Tasks));
                Assert.Same(container, Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>()));
                Assert.Same(row, Named<Grid>(view, "TaskRow"));
                Assert.Same(entry, container.DataContext);
                Assert.True(container.IsVisible);
                Assert.True(container.IsEffectivelyVisible);
                Assert.True(row.IsEffectivelyVisible);
                Assert.Equal(1, container.Opacity);
                Assert.Equal(1, row.Opacity);
                Assert.Equal(rowY, Position(row, view).Y, 3);
                Assert.Equal(rowHeight, row.Bounds.Height, 3);
                Assert.Equal(install.CombinedProgress, tasks.OverallProgress, 10);
                Assert.True(tasks.HasTransferStats);
            }
        }
        finally
        {
            catalogRelease.TrySetResult([selected]);
            install.CancelCombinedCommand.Execute(null);
            while (phases.Reader.TryRead(out var phase)) phase.Release.TrySetResult();
            if (refresh is not null) await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
            DownloadMonitor.Reset();
        }
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static Point Position(Control control, Visual target) => control.TranslatePoint(default, target)!.Value;

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(5);
        }
    }

    private sealed record Phase(string Name)
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Backend(string root, ChannelWriter<Phase> phases) : ICombinedInstallBackend
    {
        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(root, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"), JsonSerializer.Serialize(new
            {
                id = plan.Minecraft,
                clientVersion = plan.Minecraft,
                mainClass = "net.minecraft.client.main.Main",
                libraries = Array.Empty<object>()
            }), ct);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".jar"), "client", ct);
            await Step("获取版本信息", 0.01, progress, ct);
            await Step("下载游戏文件", 0.8, progress, ct);
            await Step("读取资源索引", 0.6, progress, ct);
            await Step("下载资源文件", 0.24, progress, ct);
        }

        public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot,
            IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(stagingRoot, "versions", "fabric-profile");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "fabric-profile.json"), JsonSerializer.Serialize(new
            {
                id = "fabric-profile",
                inheritsFrom = plan.Minecraft,
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                libraries = Array.Empty<object>()
            }), ct);
            await Step("下载 Fabric Loader", 0.5, progress, ct);
            return "fabric-profile";
        }

        public async Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct)
        {
            await Step("补全游戏文件", 0.4, progress, ct);
            await Step("解压运行库", 0.95, progress, ct);
        }

        private async Task Step(string name, double percent, IProgress<JobProgress> progress, CancellationToken ct)
        {
            progress.Report(new JobProgress(name, percent, Detail: new string('a', 40)));
            DownloadMonitor.Report(new DownloadProgress(5147, 663, 1000, 240, 4.9 * 1024 * 1024, name, 32));
            var phase = new Phase(name);
            await phases.WriteAsync(phase, ct);
            await phase.Release.Task.WaitAsync(ct);
        }

        public Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) =>
            throw new InvalidOperationException("unexpected OptiFine selection");

        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory,
            IProgress<JobProgress> progress, CancellationToken ct) =>
            throw new InvalidOperationException("unexpected API selection");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
