using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Controls;
using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class PclLoadingIndicatorTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task OnlyTheFailedIndicatorCanRetryWithMouseOrKeyboard(string theme)
    {
        ThemeService.Apply(theme);
        var attempts = 0;
        var indicator = new PclLoadingIndicator { IsRunning = true, Command = new RelayCommand(() => attempts++) };
        var window = new Window { Content = indicator, Width = 400, Height = 200 };
        window.Show();
        try
        {
            await Pump(30);
            var button = indicator.FindControl<Button>("RetryButton")!;
            var pickaxe = indicator.FindControl<Avalonia.Controls.Shapes.Path>("Pickaxe")!;
            var iconPanel = Assert.IsType<Grid>(pickaxe.Parent);
            Assert.Equal(60, iconPanel.Bounds.Width);
            Assert.Equal(47, iconPanel.Bounds.Height);
            Assert.False(button.IsEffectivelyEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Click(window, point);
            Assert.Equal(0, attempts);
            indicator.IsRunning = false;
            indicator.Error = "无法读取版本信息";
            await Pump(330);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal("无法读取版本信息", indicator.FindControl<TextBlock>("LoadingText")!.Text);
            Assert.True(indicator.FindControl<Avalonia.Controls.Shapes.Path>("ErrorIcon")!.IsVisible);
            var expectedBrush = Assert.IsAssignableFrom<ISolidColorBrush>(indicator.FindResource(indicator.ActualThemeVariant, "WmclDanger"));
            Assert.Equal(expectedBrush.Color, Assert.IsAssignableFrom<ISolidColorBrush>(pickaxe.Stroke).Color);
            point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Click(window, point);
            Assert.Equal(1, attempts);
            button.Focus();
            Input(window, "KeyPress", Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Input(window, "KeyRelease", Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(2, attempts);
            indicator.IsRunning = true;
            await Pump(150);
            Assert.False(button.IsEffectivelyEnabled);
            Assert.Equal(0, indicator.FindControl<Avalonia.Controls.Shapes.Path>("ErrorIcon")!.Opacity);
            Assert.Equal("正在加载", indicator.FindControl<TextBlock>("LoadingText")!.Text);
            Click(window, point);
            Assert.Equal(2, attempts);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnimationStopsOnDetachAndReducedMotionKeepsAStablePickaxe()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", null);
        var indicator = new PclLoadingIndicator { IsRunning = true };
        var window = new Window { Content = indicator, Width = 400, Height = 200 };
        window.Show();
        try
        {
            await Pump(450);
            var rotation = (RotateTransform)indicator.FindControl<Avalonia.Controls.Shapes.Path>("Pickaxe")!.RenderTransform!;
            Assert.NotEqual(55, rotation.Angle);
            window.Content = null;
            var detachedAngle = rotation.Angle;
            await Pump(100);
            Assert.Equal(detachedAngle, rotation.Angle);
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
            window.Content = indicator;
            await Pump(450);
            Assert.Equal(55, rotation.Angle);
            Assert.Equal(0, indicator.FindControl<Avalonia.Controls.Shapes.Path>("LeftFragment")!.Opacity);
            Assert.Equal(0, indicator.FindControl<Avalonia.Controls.Shapes.Path>("RightFragment")!.Opacity);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorColorPrecedesTheDelayedIconAndRetryReversesFromTheCurrentFrame(bool swinging)
    {
        var indicator = new PclLoadingIndicator { IsRunning = swinging };
        var window = new Window { Content = indicator, Width = 400, Height = 200 };
        window.Show();
        try
        {
            await Pump(50);
            var icon = indicator.FindControl<Avalonia.Controls.Shapes.Path>("ErrorIcon")!;
            var scale = Assert.IsType<ScaleTransform>(icon.RenderTransform);
            var brush = Assert.IsType<SolidColorBrush>(indicator.FindControl<TextBlock>("LoadingText")!.Foreground);
            var original = brush.Color;
            indicator.IsRunning = false;
            indicator.Error = "加载失败";
            Assert.Equal(0, icon.Opacity);
            await Pump(120);
            Assert.NotEqual(original, brush.Color);
            Assert.Equal(0, icon.Opacity);
            await Pump(330);
            if (swinging) Assert.Equal(0, icon.Opacity);
            else Assert.True(icon.Opacity > 0);
            await Pump(750);
            Assert.Equal(1, icon.Opacity);
            Assert.Equal(1, scale.ScaleX, 4);
            var beforeRetry = scale.ScaleX;
            indicator.IsRunning = true;
            Assert.Equal(beforeRetry, scale.ScaleX);
            Assert.Equal(1, icon.Opacity);
            await Pump(50);
            Assert.InRange(icon.Opacity, 0.05, 0.95);
            Assert.InRange(scale.ScaleX, 0.5, 1);
            await Pump(300);
            Assert.Equal(0, icon.Opacity);
            Assert.Equal(0.5, scale.ScaleX, 4);
            Assert.Equal(original, brush.Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RapidErrorRetryErrorAndDetachDoNotLeaveAnOldTransitionRunning()
    {
        var indicator = new PclLoadingIndicator();
        var window = new Window { Content = indicator, Width = 400, Height = 200 };
        window.Show();
        try
        {
            indicator.Error = "第一次失败";
            await Pump(450);
            var icon = indicator.FindControl<Avalonia.Controls.Shapes.Path>("ErrorIcon")!;
            var scale = Assert.IsType<ScaleTransform>(icon.RenderTransform);
            indicator.IsRunning = true;
            await Pump(40);
            indicator.IsRunning = false;
            indicator.Error = "再次失败";
            var current = icon.Opacity;
            await Pump(150);
            Assert.Equal(current, icon.Opacity, 2);
            window.Content = null;
            var stoppedOpacity = icon.Opacity;
            var stoppedScale = scale.ScaleX;
            await Pump(800);
            Assert.Equal(stoppedOpacity, icon.Opacity);
            Assert.Equal(stoppedScale, scale.ScaleX);
            window.Content = indicator;
            await Pump(30);
            Assert.Equal(1, icon.Opacity);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal("再次失败", indicator.FindControl<TextBlock>("LoadingText")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, "正在加载 - 0%")]
    [InlineData(0.7, "正在加载 - 70%")]
    [InlineData(0.999, "正在加载 - 99%")]
    [InlineData(1, "正在加载 - 100%")]
    [InlineData(2, "正在加载 - 100%")]
    [InlineData(-1, "正在加载 - 0%")]
    [InlineData(double.NaN, "正在加载")]
    public void ProgressUsesTheOriginalTextAndErrorsReplaceThePercentage(double progress, string expected)
    {
        var indicator = new PclLoadingIndicator { IsRunning = true, Progress = progress };
        var text = indicator.FindControl<TextBlock>("LoadingText")!;
        Assert.Equal(expected, text.Text);
        indicator.IsRunning = false;
        indicator.Error = "无法加载目录";
        Assert.Equal("无法加载目录", text.Text);
        indicator.Progress = 1;
        Assert.Equal("无法加载目录", text.Text);
    }

    private static void Click(Window window, Point point)
    {
        Input(window, "MouseMove", point, RawInputModifiers.None);
        Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Input(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        } while (DateTime.UtcNow < until);
    }
}
