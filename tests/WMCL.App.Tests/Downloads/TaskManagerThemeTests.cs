using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class TaskManagerThemeTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task TaskControlsAndExpandedSectionsShareThePclCardStyle(string theme)
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        using var fixture = new Fixture();
        ThemeService.Apply(theme);
        var view = new TaskManagerView { DataContext = fixture.Tasks };
        var window = new Window { Content = view, Width = 668, Height = 900 };
        try
        {
            window.Show();
            Pump();
            var history = view.FindControl<Expander>("RecentTasksSection")!;
            var detail = Assert.Single(view.GetVisualDescendants().OfType<Expander>(), section => section.Name == "TaskDetails");
            var logToggle = view.FindControl<ToggleButton>("LogToggle")!;
            var clearLogs = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "清空"));
            Assert.False(clearLogs.IsEnabled);
            Assert.Equal((byte)0, BrushColor(Presenter(clearLogs).Background).A);
            Assert.Equal(30, logToggle.Bounds.Height);
            Assert.Equal(new CornerRadius(3), logToggle.CornerRadius);
            Capture(window, theme + "-collapsed");

            Click(window, Header(history));
            Pump();
            Assert.True(history.IsExpanded);
            AssertSection(history);
            var historyActions = history.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("taskAction") && button.IsEffectivelyVisible).ToArray();
            Assert.Contains(historyActions, button => Equals(button.Content, "重试"));
            Assert.Equal(2, historyActions.Count(button => Equals(button.Content, "复制失败信息")));
            foreach (var button in historyActions)
            {
                Assert.Equal(30, button.Height);
                Assert.Equal(new CornerRadius(3), button.CornerRadius);
                Assert.Equal(new Thickness(1), button.BorderThickness);
            }
            var filter = Assert.Single(history.GetVisualDescendants().OfType<ComboBox>());
            Assert.Equal(30, filter.Bounds.Height);
            filter.SelectedItem = "已失败";
            Pump();
            Assert.Equal(2, fixture.Tasks.FilteredHistory.Count);
            filter.SelectedItem = "全部";
            Pump();
            Capture(window, theme + "-history");

            Click(window, Header(detail));
            Pump();
            Assert.True(detail.IsExpanded);
            AssertSection(detail);
            await SettleExpansionAsync(detail);
            fixture.Tasks.AppendLog("[下载] 正在连接资源服务器");
            Click(window, logToggle);
            Pump();
            Assert.True(fixture.Tasks.ShowLogs);
            Assert.True(logToggle.IsChecked);
            Assert.True(view.FindControl<Border>("RunLogSurface")!.IsVisible);
            Assert.Equal("隐藏日志", logToggle.Content);
            Assert.True(clearLogs.IsEnabled);
            Capture(window, theme + "-expanded");

            Header(detail).Focus();
            Send(window, "KeyPress", Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Send(window, "KeyRelease", Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Pump();
            Assert.False(detail.IsExpanded);
            for (var frame = 0; frame < 60 && Body(detail).IsVisible; frame++)
            {
                await Task.Delay(10);
                Pump();
            }
            Assert.False(Body(detail).IsVisible);
            Click(window, logToggle);
            Pump();
            Assert.False(fixture.Tasks.ShowLogs);
            Assert.Equal("显示日志", logToggle.Content);
            var clearPoint = clearLogs.TranslatePoint(new Point(20, clearLogs.Bounds.Height / 2), window)!.Value;
            Send(window, "MouseMove", clearPoint, RawInputModifiers.None);
            Send(window, "MouseDown", clearPoint, MouseButton.Left, RawInputModifiers.None);
            Pump();
            Assert.True(clearLogs.IsPressed);
            var pressed = Assert.IsAssignableFrom<ISolidColorBrush>(view.FindResource(view.ActualThemeVariant, "WmclBlue7"));
            await WaitForBackground(Presenter(clearLogs), pressed.Color);
            Send(window, "MouseUp", clearPoint, MouseButton.Left, RawInputModifiers.None);
            Pump();
            Assert.False(fixture.Tasks.HasLogs);
            await WaitForBackground(Presenter(clearLogs), Colors.Transparent);
            fixture.Tasks.ClearHistoryCommand.Execute(null);
            Pump();
            Assert.False(history.IsVisible);
            Assert.Empty(fixture.Tasks.History);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previousTheme == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }

    private static void AssertSection(Expander section)
    {
        var surface = Assert.Single(section.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "TaskSectionSurface" && border.FindAncestorOfType<Expander>() == section);
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(section.FindResource(section.ActualThemeVariant, "WmclCard"));
        Assert.Equal(expected.Color, BrushColor(surface.Background));
        Assert.Equal(new CornerRadius(5), surface.CornerRadius);
        Assert.Equal(36, Header(section).Bounds.Height);
        var accent = Assert.IsAssignableFrom<ISolidColorBrush>(section.FindResource(section.ActualThemeVariant, "WmclTaskAccent"));
        Assert.Equal(accent.Color, BrushColor(Header(section).Foreground));
        Assert.True(Contrast(accent.Color, expected.Color) >= 4.5);
        Assert.Equal(new Thickness(0), Header(section).BorderThickness);
        Assert.True(Body(section).IsVisible);
        Assert.Equal((byte)0, BrushColor(Body(section).Background).A);
    }

    private static ContentPresenter Body(Expander section) => Assert.Single(
        section.GetVisualDescendants().OfType<ContentPresenter>(),
        presenter => presenter.Name == "PART_ContentPresenter" && presenter.TemplatedParent == section);

    private static ToggleButton Header(Expander section) => Assert.Single(
        section.GetVisualDescendants().OfType<ToggleButton>(),
        toggle => toggle.Name == "TaskSectionHeader" && toggle.FindAncestorOfType<Expander>() == section);

    private static ContentPresenter Presenter(Button button) => Assert.Single(
        button.GetVisualDescendants().OfType<ContentPresenter>(),
        presenter => presenter.Name == "PART_ContentPresenter" && presenter.TemplatedParent == button);

    private static Color BrushColor(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte component)
        {
            var normalized = component / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static async Task WaitForBackground(ContentPresenter presenter, Color expected)
    {
        for (var frame = 0; frame < 60 && BrushColor(presenter.Background) != expected; frame++)
        {
            await Task.Delay(20);
            Pump();
        }
        Assert.Equal(expected, BrushColor(presenter.Background));
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_TASK_STYLE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        Pump();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

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

    private static void Click(Window window, Control control)
    {
        var target = control.TranslatePoint(new Point(20, control.Bounds.Height / 2), window)!.Value;
        Send(window, "MouseMove", target, RawInputModifiers.None);
        Send(window, "MouseDown", target, MouseButton.Left, RawInputModifiers.None);
        Send(window, "MouseUp", target, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Send(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "task-theme-" + Guid.NewGuid().ToString("N"));
        private readonly InstanceViewModel _instance;
        public TaskManagerViewModel Tasks { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
            var download = new DownloadViewModel(services);
            var mods = InstallTestData.CreateMods(services);
            var install = new InstallViewModel(download, mods, services)
            {
                IsCombinedBusy = true,
                CombinedProgress = 24,
                CombinedProgressText = "下载资源文件 24% 663/5147 client-resource"
            };
            install.Stages.Add(new InstallStage("原版游戏") { Detail = "663/5147 client-resource", Progress = 24 });
            install.Stages.Add(new InstallStage("安装清单"));
            install.Stages.Add(new InstallStage("应用安装更改"));
            _instance = new InstanceViewModel(services);
            Tasks = new TaskManagerViewModel(download, mods, new ModpackViewModel(services),
                new InstanceContentViewModel(services), _instance, install,
                historyFilePath: Path.Combine(_root, "history.json"));
            var failure = new TaskFailureInfo("IOException", "共享依赖已存在且内容不同，未覆盖：assets/indexes/34.json",
                "System.IO.IOException: example stack trace", DateTimeOffset.UtcNow);
            Tasks.History.Add(new TaskEntry("安装 26.3", "组合安装失败：" + failure.Message, 16, "失败", null,
                State: TaskTerminalState.Failed, Failure: failure,
                Retry: TaskRetryPayload.Create(TaskRetryKind.CombinedInstall, _root)));
            Tasks.History.Add(new TaskEntry("安装 26.3", "已取消组合安装，原有文件未更改", 10, "已取消", null,
                State: TaskTerminalState.Canceled));
            Tasks.History.Add(new TaskEntry("安装 Fabric", "安装失败：下载连接中断", 20, "失败", null,
                State: TaskTerminalState.Failed, Failure: failure));
            Tasks.HasHistory = true;
            Tasks.RefreshHistoryCommand.Execute(null);
        }

        public void Dispose()
        {
            Tasks.Dispose();
            _instance.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
