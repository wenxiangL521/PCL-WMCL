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
using WMCL.UI.Controls;

namespace WMCL.App.Tests;

[Trait("Kind", "Motion")]
public sealed class ExpanderAnimationTests
{
    [AvaloniaTheory]
    [InlineData("taskSection", "light")]
    [InlineData("taskSection", "dark")]
    [InlineData("cardSection", "light")]
    [InlineData("cardSection", "dark")]
    public async Task PointerAndKeyboardAnimateLayoutHeightInBothDirections(string sectionClass, string theme)
    {
        using var fixture = new Fixture(sectionClass, theme);
        var collapsed = fixture.Section.Bounds.Height;
        var belowCollapsed = fixture.Below.Bounds.Y;
        Assert.False(fixture.Body.IsVisible);
        Assert.Equal(1, fixture.Body.Opacity);
        Assert.True(fixture.Reveal.ClipToBounds);
        Assert.NotNull(fixture.Section.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "TaskSectionSurface"));

        Click(fixture.Window, fixture.Header);
        Pump();
        Assert.True(fixture.Section.IsExpanded);
        await Until(() => fixture.Section.Bounds.Height > collapsed);
        var opening = fixture.Section.Bounds.Height;
        Assert.True(fixture.Body.IsVisible);
        Assert.True(fixture.Divider.IsVisible);
        Assert.Equal(opening - collapsed, fixture.Below.Bounds.Y - belowCollapsed, 5);
        Assert.Equal(Matrix.Identity, fixture.Body.RenderTransform?.Value ?? Matrix.Identity);
        Capture(fixture.Window, $"{theme}-{sectionClass}-opening");
        await Settle();
        var expanded = fixture.Section.Bounds.Height;
        Assert.True(collapsed < opening && opening < expanded,
            $"Expected collapsed ({collapsed}) < intermediate ({opening}) < expanded ({expanded}).");
        Assert.Equal(fixture.Content.Height + 23, expanded - collapsed, 5);
        Assert.Equal(expanded - collapsed, fixture.Below.Bounds.Y - belowCollapsed, 5);
        Capture(fixture.Window, $"{theme}-{sectionClass}-expanded");

        fixture.Header.Focus();
        SendKey(fixture.Window, Key.Space);
        Pump();
        Assert.False(fixture.Section.IsExpanded);
        await Until(() => fixture.Section.Bounds.Height < expanded);
        var closing = fixture.Section.Bounds.Height;
        Assert.True(collapsed < closing && closing < expanded,
            $"Expected collapsed ({collapsed}) < intermediate ({closing}) < expanded ({expanded}).");
        Assert.True(fixture.Body.IsVisible);
        Assert.True(fixture.Divider.IsVisible);
        Assert.Equal(closing - collapsed, fixture.Below.Bounds.Y - belowCollapsed, 5);
        Capture(fixture.Window, $"{theme}-{sectionClass}-closing");
        await Settle();
        Assert.Equal(collapsed, fixture.Section.Bounds.Height, 5);
        Assert.Equal(belowCollapsed, fixture.Below.Bounds.Y, 5);
        Assert.False(fixture.Body.IsVisible);
        Assert.False(fixture.Divider.IsVisible);
        Capture(fixture.Window, $"{theme}-{sectionClass}-collapsed");
    }

    [AvaloniaFact]
    public async Task RapidPointerReversalsContinueFromCurrentHeightWithoutJumping()
    {
        using var fixture = new Fixture();
        var collapsed = fixture.Section.Bounds.Height;
        Click(fixture.Window, fixture.Header);
        Pump();
        await Until(() => fixture.Reveal.Bounds.Height > 0);
        for (var reversal = 0; reversal < 3; reversal++)
        {
            var beforeClose = fixture.Section.Bounds.Height;
            Click(fixture.Window, fixture.Header);
            fixture.Window.UpdateLayout();
            Assert.False(fixture.Section.IsExpanded);
            Assert.Equal(beforeClose, fixture.Section.Bounds.Height, 5);
            await Until(() => fixture.Section.Bounds.Height < beforeClose);
            var beforeOpen = fixture.Section.Bounds.Height;
            Assert.True(beforeOpen > collapsed);
            Click(fixture.Window, fixture.Header);
            fixture.Window.UpdateLayout();
            Assert.True(fixture.Section.IsExpanded);
            Assert.Equal(beforeOpen, fixture.Section.Bounds.Height, 5);
            await Until(() => fixture.Section.Bounds.Height > beforeOpen);
        }
        await Settle();
        Assert.Equal(fixture.Content.Height + 23, fixture.Section.Bounds.Height - collapsed, 5);
        Assert.True(fixture.Body.IsVisible);
    }

    [AvaloniaFact]
    public async Task DynamicContentHeightIsRemeasuredWhileExpandedCollapsedAndAnimating()
    {
        using var fixture = new Fixture(expanded: true);
        var initial = fixture.Section.Bounds.Height;
        fixture.Content.Height += 100;
        Pump();
        Assert.Equal(initial + 100, fixture.Section.Bounds.Height, 5);
        Assert.Equal(fixture.Section.Bounds.Bottom, fixture.Below.Bounds.Y, 5);

        Click(fixture.Window, fixture.Header);
        Pump();
        await Settle();
        var collapsed = fixture.Section.Bounds.Height;
        fixture.Content.Height += 100;
        Pump();
        Assert.Equal(collapsed, fixture.Section.Bounds.Height, 5);
        Click(fixture.Window, fixture.Header);
        Pump();
        await Until(() => fixture.Reveal.Bounds.Height > 0);
        var intermediate = fixture.Section.Bounds.Height;
        fixture.Content.Height += 60;
        fixture.Window.UpdateLayout();
        Assert.Equal(intermediate, fixture.Section.Bounds.Height, 5);
        await Settle();
        Assert.Equal(fixture.Content.Height + 23, fixture.Section.Bounds.Height - collapsed, 5);
        Assert.Equal(fixture.Content.Height, fixture.Content.Bounds.Height, 5);
        Assert.Equal(fixture.Section.Bounds.Bottom, fixture.Below.Bounds.Y, 5);

        var replacement = new Border { Height = 120, Background = Brushes.CadetBlue };
        fixture.Section.Content = replacement;
        Pump();
        Assert.Equal(143, fixture.Section.Bounds.Height - collapsed, 5);
        Assert.Equal(120, replacement.Bounds.Height, 5);
    }

    [AvaloniaFact]
    public async Task ReducedMotionAppliesBothTerminalLayoutsImmediately()
    {
        using var fixture = new Fixture(reducedMotion: true);
        var collapsed = fixture.Section.Bounds.Height;
        Click(fixture.Window, fixture.Header);
        Pump();
        var expanded = fixture.Section.Bounds.Height;
        Assert.Equal(fixture.Content.Height + 23, expanded - collapsed, 5);
        Assert.True(fixture.Body.IsVisible);
        Assert.True(fixture.Divider.IsVisible);
        await Settle();
        Assert.Equal(expanded, fixture.Section.Bounds.Height, 5);
        fixture.Header.Focus();
        SendKey(fixture.Window, Key.Space);
        Pump();
        Assert.Equal(collapsed, fixture.Section.Bounds.Height, 5);
        Assert.False(fixture.Body.IsVisible);
        Assert.False(fixture.Divider.IsVisible);
        await Settle();
        Assert.Equal(collapsed, fixture.Section.Bounds.Height, 5);
    }

    [AvaloniaFact]
    public async Task EnablingReducedMotionDuringAnimationSnapsToTheRequestedTerminalHeight()
    {
        using var fixture = new Fixture();
        var collapsed = fixture.Section.Bounds.Height;
        Click(fixture.Window, fixture.Header);
        Pump();
        await Until(() => fixture.Reveal.Bounds.Height > 0);
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        await Until(() => Math.Abs(fixture.Section.Bounds.Height - collapsed - fixture.Content.Height - 23) < 0.01);
        var terminal = fixture.Section.Bounds.Height;
        await Settle();
        Assert.Equal(terminal, fixture.Section.Bounds.Height, 5);
    }

    [AvaloniaFact]
    public async Task DetachCancelsOldFramesAndReattachUsesTheCurrentExpansionState()
    {
        using var fixture = new Fixture();
        Click(fixture.Window, fixture.Header);
        Pump();
        await Until(() => fixture.Reveal.Bounds.Height > 0);
        var reveal = fixture.Reveal;
        var body = fixture.Body;
        fixture.Window.Content = null;
        Pump();
        fixture.Section.IsExpanded = false;
        reveal.Measure(new Size(460, double.PositiveInfinity));
        reveal.Arrange(new Rect(0, 0, 460, reveal.DesiredSize.Height));
        Assert.Equal(0, reveal.Bounds.Height);
        Assert.False(reveal.IsContentVisible);
        Assert.False(body.IsVisible);
        var changes = 0;
        void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs args) => changes++;
        reveal.PropertyChanged += OnChanged;
        try
        {
            await Settle();
            Assert.Equal(0, changes);
            Assert.Equal(0, reveal.Bounds.Height);
        }
        finally
        {
            reveal.PropertyChanged -= OnChanged;
        }

        fixture.Section.IsExpanded = false;
        fixture.Window.Content = fixture.Panel;
        Pump();
        Assert.Equal(0, reveal.Bounds.Height);
        Assert.False(body.IsVisible);
        Click(fixture.Window, fixture.Header);
        Pump();
        await Until(() => reveal.Bounds.Height > 0);
        var intermediate = reveal.Bounds.Height;
        await Settle();
        Assert.True(intermediate < reveal.Bounds.Height);
        Assert.Equal(fixture.Content.Height + 23, reveal.Bounds.Height, 5);
        Assert.True(body.IsVisible);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var frame = 0; frame < 100 && !condition(); frame++)
        {
            await Task.Delay(10);
            Pump();
        }
        Assert.True(condition(), "Expected animation state was not reached.");
    }

    private static async Task Settle()
    {
        for (var frame = 0; frame < 25; frame++)
        {
            await Task.Delay(10);
            Pump();
        }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Send(window, "MouseMove", point, RawInputModifiers.None);
        Send(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Send(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void SendKey(Window window, Key key)
    {
        Send(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        Send(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Send(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_EXPANDER_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? _previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        private readonly ThemeVariant? _previousTheme = Application.Current!.RequestedThemeVariant;

        public Fixture(string sectionClass = "taskSection", string theme = "light", bool expanded = false, bool reducedMotion = false)
        {
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", reducedMotion ? "1" : "0");
            Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Content = new Border { Height = 240, Background = Brushes.CadetBlue };
            Section = new Expander { Header = "维护工具", Content = Content, IsExpanded = expanded };
            Section.Classes.Add(sectionClass);
            Below = new Border { Height = 24, Background = Brushes.Coral };
            Panel = new StackPanel { Children = { Section, Below } };
            Window = new Window { Content = Panel, Width = 500, Height = 900 };
            Window.Show();
            Pump();
        }

        public Window Window { get; }
        public StackPanel Panel { get; }
        public Expander Section { get; }
        public Border Content { get; }
        public Border Below { get; }
        public ExpanderReveal Reveal => Section.GetVisualDescendants().OfType<ExpanderReveal>().Single();
        public ToggleButton Header => Section.GetVisualDescendants().OfType<ToggleButton>()
            .Single(control => control.Name == "TaskSectionHeader");
        public ContentPresenter Body => Section.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter" && control.TemplatedParent == Section);
        public Border Divider => Section.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Name == "TaskSectionDivider");

        public void Dispose()
        {
            Window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", _previousMotion);
            Application.Current!.RequestedThemeVariant = _previousTheme;
        }
    }
}
