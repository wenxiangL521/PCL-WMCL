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

public sealed class SidebarMotionScopeTests
{
    [AvaloniaTheory]
    [InlineData("light", "download")]
    [InlineData("dark", "download")]
    [InlineData("light", "settings")]
    [InlineData("dark", "settings")]
    public async Task MainSidebarRestoresHoverFramesWithoutChangingSelectionOrLayout(string theme, string page)
    {
        PclInteractionMotion.Initialize();
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        ThemeService.Apply(theme);
        using var main = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        main.NavigateTo(page);
        var window = new MainWindow { DataContext = main };
        try
        {
            window.Show();
            await AdvanceAsync(300);
            var sidebar = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), list => list.Classes.Contains("subTabs"));
            var item = sidebar.GetVisualDescendants().OfType<ListBoxItem>().First(control => !control.IsSelected);
            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>()
                .First(control => control.Name == "PART_ContentPresenter");
            var selected = sidebar.SelectedItem;
            var layout = item.Bounds;
            var outside = new Point(window.Width - 25, window.Height - 25);
            var target = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Move(window, outside);
                Move(window, target);
                Assert.True(item.IsPointerOver);
                Assert.InRange(Scale(presenter), 0.8, 0.99);
                Assert.Same(selected, sidebar.SelectedItem);
                Assert.Equal(layout, item.Bounds);
                await AdvanceAsync(240);
                Assert.Equal(1, Scale(presenter), 6);
                Move(window, outside);
                Assert.Equal(1, Scale(presenter), 6);
            }
            Move(window, target);
            Assert.True(Scale(presenter) < 1);
            for (var frame = 0; frame < 4; frame++)
            {
                await AdvanceAsync(20);
                var current = Scale(presenter);
                Send(window, "MouseMove", outside, RawInputModifiers.None);
                Assert.Equal(current, Scale(presenter), 6);
                Send(window, "MouseMove", target, RawInputModifiers.None);
                Assert.Equal(current, Scale(presenter), 6);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.True(Scale(presenter) >= current);
                Assert.Same(selected, sidebar.SelectedItem);
                Assert.Equal(layout, item.Bounds);
            }
            Move(window, outside);
            var beforeFinish = Scale(presenter);
            await AdvanceAsync(150);
            Assert.True(Scale(presenter) >= beforeFinish);
            Assert.Equal(1, Scale(presenter), 6);
            Move(window, target);
            Assert.InRange(Scale(presenter), 0.8, 0.99);
            await AdvanceAsync(240);
            Assert.Equal(1, Scale(presenter), 6);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previousTheme == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previousTheme;
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [AvaloniaFact]
    public async Task MovingBetweenSidebarRowsKeepsEachExistingAnimationRunning()
    {
        PclInteractionMotion.Initialize();
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        var list = new ListBox { ItemsSource = new[] { "first", "second", "third" }, SelectedIndex = 0 };
        list.Classes.Add("subTabs");
        var window = new Window { Content = list, Width = 220, Height = 200 };
        try
        {
            window.Show();
            await AdvanceAsync(20);
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().Where(item => !item.IsSelected).ToArray();
            Assert.Equal(2, rows.Length);
            var presenters = rows.Select(item => item.GetVisualDescendants().OfType<ContentPresenter>()
                .First(control => control.Name == "PART_ContentPresenter")).ToArray();
            var points = rows.Select(item => item.TranslatePoint(
                new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value).ToArray();
            Move(window, new Point(215, 195));
            Move(window, points[0]);
            Assert.InRange(Scale(presenters[0]), 0.8, 0.99);
            await AdvanceAsync(20);
            var firstScale = Scale(presenters[0]);
            Send(window, "MouseMove", points[1], RawInputModifiers.None);
            Assert.Equal(firstScale, Scale(presenters[0]), 6);
            Assert.InRange(Scale(presenters[1]), 0.8, 0.99);
            for (var pass = 0; pass < 3; pass++)
            {
                await AdvanceAsync(20);
                var before = presenters.Select(Scale).ToArray();
                Send(window, "MouseMove", points[pass % 2], RawInputModifiers.None);
                for (var index = 0; index < presenters.Length; index++)
                    Assert.Equal(before[index], Scale(presenters[index]), 6);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                for (var index = 0; index < presenters.Length; index++)
                    Assert.True(Scale(presenters[index]) >= before[index]);
            }
            await AdvanceAsync(240);
            Assert.All(presenters, presenter => Assert.Equal(1, Scale(presenter), 6));
            Assert.Equal(0, list.SelectedIndex);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    [AvaloniaFact]
    public async Task SidebarHonorsReducedMotionWithoutRemovingItsSelectionBehaviour()
    {
        PclInteractionMotion.Initialize();
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var list = new ListBox { ItemsSource = new[] { "first", "second" }, SelectedIndex = 0 };
        list.Classes.Add("subTabs");
        var window = new Window { Content = list, Width = 180, Height = 150 };
        try
        {
            window.Show();
            await AdvanceAsync(20);
            var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(control => !control.IsSelected);
            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>()
                .First(control => control.Name == "PART_ContentPresenter");
            var target = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            Move(window, new Point(175, 145));
            Move(window, target);
            Assert.True(item.IsPointerOver);
            Assert.Equal(1, Scale(presenter), 6);
            Send(window, "MouseDown", target, MouseButton.Left, RawInputModifiers.None);
            Send(window, "MouseUp", target, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, list.SelectedIndex);
            await AdvanceAsync(240);
            Assert.Equal(1, Scale(presenter), 6);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    private static double Scale(Control control) => (control.RenderTransform?.Value ?? Matrix.Identity).M11;

    private static void Move(Window window, Point point)
    {
        Send(window, "MouseMove", point, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static async Task AdvanceAsync(int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 20)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Send(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);
}
