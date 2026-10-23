using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class DownloadActionButtonTests
{
    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task InstallPageBindsTheRealActionAndKeepsCatalogBusyAndNameGuards(string theme, int width)
    {
        ThemeService.Apply(theme);
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-download-action-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<WMCL.Modding.Fabric.FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>([]));
        var model = new InstallViewModel(download, mods, services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        model.ShowSelectedVersion();
        var view = new InstallView { DataContext = model };
        var window = new Window { Width = width, Height = 500, Content = view };
        window.Show();
        try
        {
            await PumpAsync(100);
            var button = view.FindControl<DownloadActionButton>("StartDownloadButton")!;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.Equal("开始下载", button.Content);
            Assert.Same(model.InstallCombinedCommand, button.Command);
            Assert.True(button.Show);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal(500, scroll.Bounds.Height);
            var lastCard = view.FindControl<InstallComponentCard>("OptiFineComponentCard")!;
            lastCard.BringIntoView();
            await PumpAsync(60);
            var lastBottom = lastCard.TranslatePoint(new Point(0, lastCard.Bounds.Height), window)!.Value.Y;
            Assert.True(lastBottom <= button.TranslatePoint(default, window)!.Value.Y);
            model.InstanceName = "";
            Assert.False(button.IsEffectivelyEnabled);
            model.InstanceName = "1.20.1";
            Assert.True(button.IsEffectivelyEnabled);
            model.IsCombinedBusy = true;
            Assert.False(button.IsEffectivelyEnabled);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
                item => Equals(item.Content, "取消安装"));
            model.IsCombinedBusy = false;
            model.IsChoosingVersion = true;
            Assert.False(button.Show);
            Assert.False(button.IsHitTestVisible);
            await PumpAsync(300);
            Assert.Equal(0, Part<Grid>(button, "ShowPanel").Opacity);
            model.IsChoosingVersion = false;
            await PumpAsync(500);
            Assert.Equal(1, Part<Grid>(button, "ShowPanel").Opacity);
            Assert.True(button.IsEffectivelyEnabled);
        }
        finally { window.Close(); ThemeService.Apply("light"); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task LayoutThemeAndDisabledStateUseTheExtraTextButtonTemplate(string theme)
    {
        ThemeService.Apply(theme);
        var button = new DownloadActionButton { Content = "开始下载", Show = true };
        var window = Open(button);
        try
        {
            await PumpAsync(60);
            var show = Part<Grid>(button, "ShowPanel");
            var press = Part<Grid>(button, "PressPanel");
            var icon = Part<Avalonia.Controls.Shapes.Path>(button, "DownloadIcon");
            var color = Part<Border>(button, "ColorBorder");
            Assert.Equal(52, button.Bounds.Height);
            Assert.Equal(new Thickness(20), button.Margin);
            Assert.Equal(42, press.Bounds.Height);
            Assert.Equal(new Thickness(0, 10, 0, 0), press.Margin);
            Assert.Equal(16, button.FontSize);
            Assert.Equal(18, icon.Bounds.Height);
            Assert.NotNull(icon.Data);
            Assert.Equal("开始下载", Part<TextBlock>(button, "ActionText").Text);
            Assert.Equal(Resource("WmclBlue8"), ColorOf(Part<TextBlock>(button, "ActionText").Foreground));
            Assert.Equal(new CornerRadius(20.8), color.CornerRadius);
            Assert.Equal(1, show.Opacity);
            Assert.Equal(Resource("WmclBlue3"), ColorOf(color.Background));
            Move(window, Center(window, button));
            await PumpAsync(200);
            Assert.Equal(Resource("WmclBlue4"), ColorOf(color.Background));
            button.IsEnabled = false;
            await PumpAsync(200);
            Assert.Equal(Resource("WmclTextFaint"), ColorOf(color.Background));
            Assert.Equal(1, Scale(press));
        }
        finally { window.Close(); ThemeService.Apply("light"); }
    }

    [AvaloniaFact]
    public async Task RevealAndHideHaveIntermediateFramesAndHiddenButtonsCannotReceiveInput()
    {
        var clicks = 0;
        var button = new DownloadActionButton { Content = "开始下载", Command = new RelayCommand(() => clicks++) };
        var window = Open(button);
        try
        {
            await PumpAsync(30);
            var show = Part<Grid>(button, "ShowPanel");
            Assert.Equal(0, show.Opacity);
            Assert.False(button.IsHitTestVisible);
            Assert.False(button.Focusable);
            Click(window, button);
            Assert.Equal(0, clicks);
            button.Show = true;
            await PumpAsync(90);
            Assert.InRange(show.Opacity, 0.01, 0.99);
            Assert.InRange(Scale(show), 0.01, 1.1);
            await PumpAsync(450);
            Assert.Equal(1, Scale(show));
            Assert.Equal(1, show.Opacity);
            button.Show = false;
            Assert.False(button.IsHitTestVisible);
            Assert.False(button.Focusable);
            await PumpAsync(40);
            Assert.InRange(Scale(show), 0.01, 0.99);
            await PumpAsync(130);
            Assert.Equal(0, Scale(show));
            Assert.Equal(0, show.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MouseKeyboardAndDragCancellationExecuteOnlyRealActivations()
    {
        var clicks = 0;
        var button = new DownloadActionButton { Content = "开始下载", Show = true, Command = new RelayCommand(() => clicks++) };
        var window = Open(button);
        try
        {
            await PumpAsync(50);
            Click(window, button, MouseButton.Right);
            Click(window, button, MouseButton.Middle);
            Assert.Equal(0, clicks);
            Click(window, button);
            Assert.Equal(1, clicks);
            button.Focus(NavigationMethod.Tab);
            Press(window, Key.Space);
            Press(window, Key.Enter);
            Assert.Equal(3, clicks);
            var point = Center(window, button);
            Move(window, point);
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            Move(window, new Point(3, 3));
            Move(window, point);
            Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(3, clicks);
            Click(window, button);
            Assert.Equal(4, clicks);
            button.IsEnabled = false;
            Click(window, button);
            Assert.Equal(4, clicks);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PressContractsAndReleaseOrLeaveRestoresTheVisualWithoutChangingTheHitArea()
    {
        var button = new DownloadActionButton { Content = "开始下载", Show = true };
        var window = Open(button);
        try
        {
            await PumpAsync(50);
            var press = Part<Grid>(button, "PressPanel");
            var point = Center(window, button);
            var bounds = button.Bounds;
            Move(window, point);
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(120);
            Assert.InRange(Scale(press), 0.8, 0.99);
            Assert.Equal(bounds, button.Bounds);
            Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(350);
            Assert.Equal(1, Scale(press));
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(100);
            Move(window, new Point(3, 3));
            Input(window, "MouseUp", new Point(3, 3), MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(550);
            Assert.Equal(1, Scale(press));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RapidReversalAndDetachReattachDoNotLeaveInvisibleOrPressedState()
    {
        var button = new DownloadActionButton { Content = "开始下载" };
        var window = Open(button);
        try
        {
            await PumpAsync(30);
            button.Show = true;
            await PumpAsync(100);
            button.Show = false;
            await PumpAsync(35);
            button.Show = true;
            await PumpAsync(500);
            Assert.Equal(1, Scale(Part<Grid>(button, "ShowPanel")));
            Assert.Equal(1, Part<Grid>(button, "ShowPanel").Opacity);
            var point = Center(window, button);
            Move(window, point);
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(60);
            window.Content = null;
            Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
            button.Show = false;
            window.Content = button;
            await PumpAsync(30);
            Assert.Equal(0, Part<Grid>(button, "ShowPanel").Opacity);
            button.Show = true;
            await PumpAsync(500);
            Assert.Equal(1, Scale(Part<Grid>(button, "PressPanel")));
            Assert.Equal(1, Part<Grid>(button, "ShowPanel").Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionSnapsRevealPressAndHide()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var button = new DownloadActionButton { Content = "开始下载" };
        var window = Open(button);
        try
        {
            await PumpAsync(30);
            button.Show = true;
            Assert.Equal(1, Part<Grid>(button, "ShowPanel").Opacity);
            var point = Center(window, button);
            Move(window, point);
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, Scale(Part<Grid>(button, "PressPanel")));
            button.Show = false;
            Assert.Equal(0, Part<Grid>(button, "ShowPanel").Opacity);
            Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
        }
        finally { window.Close(); Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous); }
    }

    private static Window Open(DownloadActionButton button)
    {
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        var window = new Window { Width = 520, Height = 300, Content = button };
        window.Show();
        return window;
    }

    private static T Part<T>(Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);
    private static double Scale(Control control) => ((ScaleTransform)control.RenderTransform!).ScaleX;
    private static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
    private static Color Resource(string key) => ColorOf((IBrush)Application.Current!.Resources[key]!);
    private static Point Center(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Input(Window window, string method, params object?[] args) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, args);
    private static void Move(Window window, Point point) => Input(window, "MouseMove", point, RawInputModifiers.None);
    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        var point = Center(window, control);
        Move(window, point);
        Input(window, "MouseDown", point, button, RawInputModifiers.None);
        Input(window, "MouseUp", point, button, RawInputModifiers.None);
    }

    private static void Press(Window window, Key key)
    {
        Input(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        Input(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static async Task PumpAsync(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        } while (DateTime.UtcNow < deadline);
    }
}
