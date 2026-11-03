using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class TaskRowHoverMotionTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task PointerHoverAndPressNeverScaleTheRunningTask(string theme)
    {
        PclInteractionMotion.Initialize();
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "task-hover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var download = new DownloadViewModel(services);
        var mods = InstallTestData.CreateMods(services);
        var install = new InstallViewModel(download, mods, services)
        {
            IsCombinedBusy = true,
            CombinedProgressText = "下载资源文件",
            CombinedProgress = 24
        };
        install.Stages.Add(new InstallStage("原版游戏") { Detail = new string('a', 40), Progress = 24 });
        using var instance = new InstanceViewModel(services);
        using var tasks = new TaskManagerViewModel(download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), instance, install, historyFilePath: Path.Combine(root, "history.json"));
        var view = new TaskManagerView { DataContext = tasks };
        var window = new Window { Content = view, Width = 668, Height = 500 };
        try
        {
            window.Show();
            Pump();
            var container = Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>());
            var presenter = Presenter(container);
            var row = Assert.Single(view.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "TaskRow");
            var position = row.TranslatePoint(default, window)!.Value;
            var target = row.TranslatePoint(new Point(20, 10), window)!.Value;
            var outside = new Point(window.Width - 2, window.Height - 2);
            for (var round = 0; round < 3; round++)
            {
                Send(window, "MouseMove", outside, RawInputModifiers.None);
                Send(window, "MouseMove", target, RawInputModifiers.None);
                Pump();
                Assert.True(container.IsPointerOver);
                AssertUnscaled(container, presenter);
                for (var frame = 0; frame < 12; frame++)
                {
                    install.CombinedProgressText = "resource-" + frame;
                    tasks.Refresh();
                    await Task.Delay(20);
                    Pump();
                    AssertUnscaled(container, presenter);
                    Assert.Equal(position, row.TranslatePoint(default, window)!.Value);
                }
                Send(window, "MouseDown", target, MouseButton.Left, RawInputModifiers.None);
                Pump();
                AssertUnscaled(container, presenter);
                Send(window, "MouseUp", target, MouseButton.Left, RawInputModifiers.None);
                Send(window, "MouseMove", outside, RawInputModifiers.None);
                await Task.Delay(30);
                Pump();
                AssertUnscaled(container, presenter);
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = previousTheme;
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [AvaloniaFact]
    public async Task OrdinaryRowsNeverJumpToReducedSizeOnHoverOrAfterLeaving()
    {
        PclInteractionMotion.Initialize();
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        var list = new ListBox { ItemsSource = new[] { "ordinary row" }, Height = 100, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = list, Width = 400, Height = 300 };
        try
        {
            window.Show();
            Pump();
            var item = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>());
            var presenter = Presenter(item);
            var target = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            Send(window, "MouseMove", new Point(390, 290), RawInputModifiers.None);
            Send(window, "MouseMove", target, RawInputModifiers.None);
            Pump();
            Assert.True(item.IsPointerOver);
            AssertUnscaled(item, presenter);
            Send(window, "MouseMove", new Point(390, 290), RawInputModifiers.None);
            for (var frame = 0; frame < 12; frame++)
            {
                await Task.Delay(20);
                Pump();
                Assert.False(item.IsPointerOver);
                AssertUnscaled(item, presenter);
            }
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    private static ContentPresenter Presenter(ListBoxItem item) => item.GetVisualDescendants()
        .OfType<ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");

    private static void AssertUnscaled(params Control[] controls)
    {
        foreach (var control in controls)
        {
            var matrix = control.RenderTransform?.Value ?? Matrix.Identity;
            Assert.Equal(1, matrix.M11, 6);
            Assert.Equal(1, matrix.M22, 6);
        }
    }

    private static void Send(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
