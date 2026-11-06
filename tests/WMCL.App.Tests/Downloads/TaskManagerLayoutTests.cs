using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Download;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

public sealed class TaskManagerLayoutTests
{
    [AvaloniaTheory]
    [InlineData(340)]
    [InlineData(560)]
    [InlineData(960)]
    public async Task ResourceProgressKeepsRowColumnsSelectionAndExpandedDetailsStable(double width)
    {
        using var fixture = new Fixture();
        fixture.Runs.MaxLogsPerRun = 3;
        fixture.Download.Status = "";
        fixture.Download.ProgressText = "a.jar";
        fixture.Download.Progress = 1;
        fixture.Download.IsBusy = true;
        fixture.Download.CanCancel = true;
        fixture.Tasks.Refresh();
        var view = new TaskManagerView { DataContext = fixture.Tasks };
        var window = new Window { Content = view, Width = width, Height = 650 };
        try
        {
            window.Show();
            Pump();
            var list = view.FindControl<ListBox>("TaskList")!;
            var entry = Assert.Single(fixture.Tasks.Tasks);
            list.SelectedItem = entry;
            var details = Named<Expander>(view, "TaskDetails");
            details.IsExpanded = true;
            Pump();
            await SettleExpansionAsync(details);
            var container = Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>());
            var row = Named<Grid>(view, "TaskRow");
            var detail = Named<TextBlock>(view, "TaskDetail");
            var percent = Named<TextBlock>(view, "TaskPercent");
            var cancel = Named<Button>(view, "TaskCancel");
            var height = row.Bounds.Height;
            var detailHeight = detail.Bounds.Height;
            var percentX = Position(percent, view).X;
            var cancelX = Position(cancel, view).X;
            var changes = 0;
            fixture.Tasks.Tasks.CollectionChanged += (_, _) => changes++;
            var resources = new[]
            {
                "0/00/0000000000000000000000000000000000000000",
                "下载中文资源：" + new string('资', 160) + ".jar",
                "短名",
                "assets/objects/ff/" + new string('f', 400),
                "第一行资源名\r\n第二行：" + new string('长', 120),
                "",
                "b.jar"
            };
            var progress = new[] { 0d, 9d, 10d, 99d, 100d, 25d, 50d };
            for (var index = 0; index < resources.Length; index++)
            {
                fixture.Download.ProgressText = resources[index];
                fixture.Download.Progress = progress[index];
                fixture.Tasks.Refresh();
                Pump();

                Assert.Same(entry, Assert.Single(fixture.Tasks.Tasks));
                Assert.Same(entry, list.SelectedItem);
                Assert.Same(container, Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>()));
                Assert.Same(details, Named<Expander>(view, "TaskDetails"));
                Assert.True(details.IsExpanded);
                Assert.Equal(resources[index], detail.Text);
                Assert.Equal(resources[index], ToolTip.GetTip(detail));
                Assert.Equal(entry.PercentText, percent.Text);
                Assert.Equal(progress[index], entry.Progress);
                Assert.Equal(height, row.Bounds.Height, 3);
                Assert.Equal(detailHeight, detail.Bounds.Height, 3);
                Assert.Equal(percentX, Position(percent, view).X, 3);
                Assert.Equal(cancelX, Position(cancel, view).X, 3);
                Assert.Equal(56, percent.Bounds.Width, 3);
                Assert.Equal(108, cancel.Bounds.Width, 3);
                Assert.InRange(Position(cancel, view).X + cancel.Bounds.Width, 0, view.Bounds.Width);
                Assert.True(detail.Bounds.Width > 0);
                Assert.True(Position(detail, view).X + detail.Bounds.Width <= percentX);
                Assert.Equal(TextWrapping.NoWrap, detail.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, detail.TextTrimming);
                Assert.Equal(1, detail.MaxLines);
                if (index == 1 || index == resources.Length - 1)
                    Capture(window, $"task-{width}-{(index == 1 ? "long" : "short")}");
            }

            Assert.Equal(0, changes);
            Assert.Equal(3, entry.LogCount);
            Assert.Equal("b.jar", entry.Logs![^1].Message);
            Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "b.jar");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(340)]
    [InlineData(800)]
    public async Task ChildOnlyResourceChangesUpdateInPlaceAndKeepLayoutStable(double width)
    {
        using var fixture = new Fixture();
        var stage = new InstallStage("原版游戏") { Detail = "a.jar", Progress = 1 };
        fixture.Install.Stages.Clear();
        fixture.Install.Stages.Add(stage);
        fixture.Install.CombinedProgressText = "安装资源";
        fixture.Install.IsCombinedBusy = true;
        fixture.Tasks.Refresh();
        var view = new TaskManagerView { DataContext = fixture.Tasks };
        var window = new Window { Content = view, Width = width, Height = 650 };
        try
        {
            window.Show();
            Pump();
            var entry = Assert.Single(fixture.Tasks.Tasks);
            var child = Assert.Single(entry.Children!);
            var children = entry.Children;
            var details = Named<Expander>(view, "TaskDetails");
            details.IsExpanded = true;
            view.FindControl<ListBox>("TaskList")!.SelectedItem = entry;
            Pump();
            await SettleExpansionAsync(details);
            var row = Named<Grid>(view, "TaskRow");
            var childRow = Named<Grid>(view, "ChildTaskRow");
            var detail = Named<TextBlock>(view, "ChildTaskDetail");
            var percent = Named<TextBlock>(view, "ChildTaskPercent");
            var cancel = Named<Button>(view, "TaskCancel");
            var rowHeight = row.Bounds.Height;
            var childHeight = childRow.Bounds.Height;
            var percentX = Position(percent, view).X;
            var cancelX = Position(cancel, view).X;
            var originalDetail = entry.Detail;
            var originalProgress = entry.Progress;
            var originalLogCount = entry.LogCount;
            var resources = new[]
            {
                new string('f', 320),
                "中文资源\n包含第二行\r\n第三行" + new string('资', 100),
                "x",
                ""
            };
            for (var index = 0; index < resources.Length; index++)
            {
                stage.Detail = resources[index];
                stage.Progress = (index + 1) * 25;
                fixture.Tasks.Refresh();
                Pump();

                Assert.Same(entry, Assert.Single(fixture.Tasks.Tasks));
                Assert.Same(children, entry.Children);
                Assert.Same(child, Assert.Single(entry.Children!));
                Assert.Same(childRow, Named<Grid>(view, "ChildTaskRow"));
                Assert.Equal(resources[index], detail.Text);
                Assert.Equal(resources[index], ToolTip.GetTip(detail));
                Assert.Equal(stage.PercentText, percent.Text);
                Assert.Equal(stage.Progress, child.Progress);
                Assert.Equal(originalDetail, entry.Detail);
                Assert.Equal(originalProgress, entry.Progress);
                Assert.Equal(originalLogCount, entry.LogCount);
                Assert.Equal(rowHeight, row.Bounds.Height, 3);
                Assert.Equal(childHeight, childRow.Bounds.Height, 3);
                Assert.Equal(percentX, Position(percent, view).X, 3);
                Assert.Equal(cancelX, Position(cancel, view).X, 3);
                Assert.True(detail.Bounds.Width > 0);
                Assert.True(Position(detail, view).X + detail.Bounds.Width <= percentX);
                Assert.Same(details, Named<Expander>(view, "TaskDetails"));
                Assert.True(details.IsExpanded);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(340)]
    [InlineData(668)]
    [InlineData(960)]
    // 布局时序敏感，分片并发下不稳定，放入 CI 串行组
    [Trait("Kind", "UiInteraction")]
    public void CombinedInstallTransferStatisticsNeverCollapseDuringProgressOrStageChanges(double width)
    {
        using var fixture = new Fixture();
        var stage = new InstallStage("原版游戏") { Detail = "等待下载" };
        fixture.Install.Stages.Clear();
        fixture.Install.Stages.Add(stage);
        fixture.Install.IsCombinedBusy = true;
        fixture.Tasks.Refresh();
        var view = new TaskManagerView { DataContext = fixture.Tasks };
        var window = new Window { Content = view, Width = width, Height = 650 };
        try
        {
            window.Show();
            Pump();
            var statistics = view.FindControl<Grid>("TransferStatsRow")!;
            var row = Named<Grid>(view, "TaskRow");
            var entry = Assert.Single(fixture.Tasks.Tasks);
            var cancel = Named<Button>(view, "TaskCancel");
            var initialY = Position(row, view).Y;
            var cancelY = Position(cancel, view).Y;
            var statsHeight = statistics.Bounds.Height;
            Assert.True(fixture.Tasks.HasTransferStats);
            Assert.True(statistics.IsVisible);
            Assert.True(statsHeight >= 18);
            Assert.Equal("等待传输", fixture.Tasks.RemainingFilesText);
            var visibilityChanges = 0;
            fixture.Tasks.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TaskManagerViewModel.HasTransferStats)) visibilityChanges++;
            };
            var samples = new[]
            {
                new DownloadProgress(5147, 663, 10000, 2400, 4.9 * 1024 * 1024, new string('a', 40), 32),
                new DownloadProgress(5147, 664, 10000, 2600, 0, "next-resource", 32),
                new DownloadProgress(5147, 5147, 10000, 10000, 8 * 1024 * 1024, "last-resource", 32),
                new DownloadProgress(0, 0, 0, 0, 0, null),
                new DownloadProgress(1, 0, 100, 1, 25, "loader.jar", 1)
            };
            foreach (var sample in samples)
            {
                DownloadMonitor.Report(sample);
                Pump();
                AssertStable();
                fixture.Install.CombinedProgressText = sample.CurrentFile ?? "写入安装清单";
                stage.Detail = sample.CurrentFile ?? "等待";
                fixture.Tasks.Refresh();
                Pump();
                AssertStable();
                DownloadMonitor.Reset();
                Pump();
                AssertStable();
                Assert.Equal(ProgressFormatter.FormatSpeed(0), fixture.Tasks.TransferSpeedText);
            }
            Assert.Equal(0, visibilityChanges);
            DownloadMonitor.Report(samples[0]);
            fixture.Install.IsCombinedBusy = false;
            fixture.Tasks.Refresh();
            Pump();
            Assert.False(fixture.Tasks.HasTransferStats);
            Assert.False(statistics.IsVisible);
            Assert.Equal("", fixture.Tasks.TransferSpeedText);
            Assert.Equal("", fixture.Tasks.RemainingFilesText);
            Assert.Equal("", fixture.Tasks.ThreadsText);
            DownloadMonitor.Report(samples[0]);
            Pump();
            Assert.False(fixture.Tasks.HasTransferStats);
            Assert.Equal("", fixture.Tasks.TransferSpeedText);

            void AssertStable()
            {
                Assert.True(fixture.Tasks.HasTransferStats);
                Assert.True(statistics.IsVisible);
                Assert.Equal(statsHeight, statistics.Bounds.Height, 3);
                Assert.Equal(initialY, Position(row, view).Y, 3);
                Assert.Equal(cancelY, Position(cancel, view).Y, 3);
                Assert.Same(entry, Assert.Single(fixture.Tasks.Tasks));
            }
        }
        finally
        {
            window.Close();
            DownloadMonitor.Reset();
        }
    }

    [AvaloniaFact]
    public void TransferStatisticsStayVisibleUntilTheLastDownloadCapableTaskEnds()
    {
        using var fixture = new Fixture();
        fixture.Install.IsCombinedBusy = true;
        fixture.Download.IsBusy = true;
        fixture.Download.CanCancel = true;
        fixture.Tasks.Refresh();
        Assert.True(fixture.Tasks.HasTransferStats);
        fixture.Install.IsCombinedBusy = false;
        fixture.Tasks.Refresh();
        DownloadMonitor.Reset();
        Pump();
        Assert.True(fixture.Tasks.HasTransferStats);
        fixture.Download.IsBusy = false;
        fixture.Download.CanCancel = false;
        fixture.Tasks.Refresh();
        Assert.False(fixture.Tasks.HasTransferStats);
        DownloadMonitor.Report(new DownloadProgress(10, 1, 100, 10, 1024, "late.jar", 4));
        Pump();
        Assert.False(fixture.Tasks.HasTransferStats);
        Assert.Equal("", fixture.Tasks.TransferSpeedText);
        DownloadMonitor.Reset();
    }

    [Fact]
    public void InPlaceUpdatesRetainSameCountLogChangesAndLatestActions()
    {
        var entries = new ObservableCollection<TaskEntry>();
        var firstCancelCount = 0;
        var nextCancelCount = 0;
        var firstLog = new TaskLogEntry(DateTimeOffset.UnixEpoch, TaskLogLevel.Info, "下载", "old");
        var snapshot = new TaskEntry("下载", "old", 1, "1%", () => firstCancelCount++, TaskId: "run", Logs: [firstLog]);
        TaskEntry.Synchronize(entries, [snapshot]);
        var entry = Assert.Single(entries);
        Assert.NotSame(snapshot, entry);
        var changed = new List<string?>();
        entry.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var failure = new TaskFailureInfo("IOException", "失败", "stack", DateTimeOffset.UnixEpoch);
        var nextLog = firstLog with { Message = "new" };
        TaskEntry.Synchronize(entries,
            [new TaskEntry("下载", "new", 100, "失败", () => nextCancelCount++, TaskId: "run",
                Failure: failure, State: TaskTerminalState.Failed, Logs: [nextLog])]);

        Assert.Same(entry, Assert.Single(entries));
        Assert.Equal("new", Assert.Single(entry.Logs!).Message);
        Assert.Equal("old", snapshot.Detail);
        Assert.Equal(1, snapshot.Progress);
        Assert.Equal("old", Assert.Single(snapshot.Logs!).Message);
        Assert.Contains(nameof(TaskEntry.Logs), changed);
        Assert.Contains(nameof(TaskEntry.HasFailure), changed);
        Assert.Contains(nameof(TaskEntry.StateText), changed);
        Assert.Same(failure, entry.Failure);
        entry.Cancel!();
        Assert.Equal(0, firstCancelCount);
        Assert.Equal(1, nextCancelCount);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_TASK_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static Point Position(Control control, Visual target) => control.TranslatePoint(default, target)!.Value;

    private static async Task SettleExpansionAsync(Expander section)
    {
        var last = double.NaN;
        for (var frame = 0; frame < 80; frame++)
        {
            await Task.Delay(10);
            Pump();
            var height = section.Bounds.Height;
            if (!double.IsNaN(last) && Math.Abs(height - last) < 0.01) return;
            last = height;
        }
    }

    private static void Pump()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "task-layout-" + Guid.NewGuid().ToString("N"));
        public DownloadViewModel Download { get; }
        public InstallViewModel Install { get; }
        public TaskManagerViewModel Tasks { get; }
        public TaskRunStore Runs { get; } = new();

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var services = new AppServices(new AppSettings { GameDirectory = _root });
            Download = new DownloadViewModel(services);
            var mods = new ModsViewModel(services);
            Install = new InstallViewModel(Download, mods, services);
            Tasks = new TaskManagerViewModel(Download, mods, new ModpackViewModel(services),
                new InstanceContentViewModel(services), new InstanceViewModel(services), Install,
                historyFilePath: Path.Combine(_root, "history.json"), runs: Runs);
        }

        public void Dispose()
        {
            Tasks.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
