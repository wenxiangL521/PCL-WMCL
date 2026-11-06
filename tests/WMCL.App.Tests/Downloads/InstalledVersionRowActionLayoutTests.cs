using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class InstalledVersionRowActionLayoutTests
{
    private const string TargetId = "A very long instance title that must never push favorite settings and more icons out of the row";

    [AvaloniaTheory]
    [InlineData("light", 0.8)]
    [InlineData("light", 1)]
    [InlineData("light", 1.5)]
    [InlineData("dark", 0.8)]
    [InlineData("dark", 1)]
    [InlineData("dark", 1.5)]
    public async Task FixedActionSlotsStayCenteredAndClickableAcrossParentScales(string theme, double scale)
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        PclInteractionMotion.Initialize();
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        ThemeService.Apply(theme);
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "row-actions-" + Guid.NewGuid().ToString("N"));
        var current = new VersionItem { Id = "Current", IsInstalled = true, Category = "vanilla" };
        var target = new VersionItem
        {
            Id = TargetId,
            IsInstalled = true,
            Category = "vanilla",
            Description = "A long description and custom suffix are clipped before the fixed action area",
            BaseGameVersion = "1.21.1",
            IsBaseGameVersionReliable = true
        };
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services, loadVersions: _ => Task.FromResult<IReadOnlyList<VersionItem>>([current, target]));
        var closeCount = 0;
        var requests = new List<string>();
        var openedPaths = new List<string>();
        var selection = new VersionSelectViewModel(launch, services, () => closeCount++, () => { }, openedPaths.Add);
        selection.VersionSettingsRequested += (_, id) => requests.Add(id);
        await launch.LoadAsync();
        launch.SelectedVersion = current;
        var view = new VersionSelectView { DataContext = selection, Width = 850, Height = 500 };
        // Simulate the parent's whole-page scale, not a local font change. The logical
        // page remains the requested 850x500 and physical hit coordinates include scale.
        var scaled = new Border
        {
            Width = 850,
            Height = 500,
            Child = view,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new ScaleTransform(scale, scale),
            RenderTransformOrigin = RelativePoint.TopLeft
        };
        var window = new Window { Content = scaled, Width = 850 * scale, Height = 500 * scale };
        try
        {
            window.Show();
            await PumpAsync();
            var row = Row(view, target);
            var layout = row.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "VersionRowLayout");
            var actions = row.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Classes.Contains("rowActions"));
            var slots = Actions(row);
            Assert.Equal(3, slots.Length);
            Assert.Equal(102, actions.Bounds.Width, 6);
            Assert.Equal(6, actions.Spacing);
            AssertCenteredLayout(layout, actions, slots);
            var text = layout.Children.OfType<StackPanel>().Single(panel => Grid.GetColumn(panel) == 2);
            var beforeText = text.Bounds;
            var beforeActions = actions.Bounds;
            Assert.True(text.Bounds.Width >= 100);
            Assert.True(text.Bounds.Right <= actions.Bounds.X + 0.01);

            var favorite = slots.Single(button => button.Classes.Contains("favorite"));
            Send(window, "MouseMove", Center(favorite, window), RawInputModifiers.None);
            await PumpAsync();
            Assert.Equal(1, actions.Opacity, 3);
            Assert.Equal(beforeText, text.Bounds);
            Assert.Equal(beforeActions, actions.Bounds);
            AssertCenteredLayout(layout, actions, slots);
            Assert.Equal(Matrix.Identity, row.RenderTransform?.Value ?? Matrix.Identity);
            Capture(window, theme + "-row-actions-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Click(window, favorite);
            await PumpAsync();
            Assert.True(target.IsFavorite);
            Assert.Same(current, launch.SelectedVersion);
            Assert.Equal(0, closeCount);
            row = Row(view, target);
            slots = Actions(row);
            var gear = slots.Single(button => Equals(ToolTip.GetTip(button), "版本设置"));
            Click(window, gear);
            await PumpAsync();
            Assert.Equal([TargetId], requests);
            Assert.Same(current, launch.SelectedVersion);
            Assert.Equal(0, closeCount);

            var more = slots.Single(button => button.Flyout is MenuFlyout);
            Click(window, more);
            await PumpAsync();
            var menu = Assert.IsType<MenuFlyout>(more.Flyout);
            Assert.True(menu.IsOpen);
            Assert.Same(more, menu.Target);
            Assert.Equal(PlacementMode.BottomEdgeAlignedRight, menu.Placement);
            var menuSettings = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "版本设置"));
            var presenter = menuSettings.FindAncestorOfType<MenuFlyoutPresenter>();
            Assert.NotNull(presenter);
            var popupRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(presenter));
            Click(popupRoot, menuSettings);
            await PumpAsync();
            Assert.Equal([TargetId, TargetId], requests);
            Assert.False(menu.IsOpen);
            Assert.Same(current, launch.SelectedVersion);
            Assert.Equal(0, closeCount);
            Assert.Empty(openedPaths);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previousTheme == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previousTheme;
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    private static void AssertCenteredLayout(Grid row, StackPanel actions, Button[] buttons)
    {
        foreach (var button in buttons)
        {
            Assert.Equal(30, button.Bounds.Width, 6);
            Assert.Equal(30, button.Bounds.Height, 6);
            var rowCenter = button.TranslatePoint(new Point(15, 15), row)!.Value;
            Assert.Equal(21, rowCenter.Y, 6);
            var icon = Assert.IsAssignableFrom<Control>(button.Content);
            Assert.Equal(16, icon.Bounds.Width, 6);
            Assert.Equal(16, icon.Bounds.Height, 6);
            var iconCenter = icon.TranslatePoint(new Point(8, 8), button)!.Value;
            Assert.Equal(15, iconCenter.X, 6);
            Assert.Equal(15, iconCenter.Y, 6);
        }
        for (var i = 1; i < buttons.Length; i++)
            Assert.Equal(6, buttons[i].Bounds.Left - buttons[i - 1].Bounds.Right, 6);
        Assert.Equal(30, actions.Bounds.Height, 6);
    }
    private static Button[] Actions(ListBoxItem row) => row.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("rowAction") && button.IsEffectivelyVisible).ToArray();
    private static ListBoxItem Row(Control view, VersionItem version) => Assert.Single(view.GetVisualDescendants().OfType<ListBoxItem>(),
        item => ReferenceEquals(item.DataContext, version));
    private static Point Center(Control control, Visual relativeTo) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;
    private static void Click(TopLevel root, Control control)
    {
        var point = Center(control, root);
        Send(root, "MouseMove", point, RawInputModifiers.None);
        Send(root, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Send(root, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }
    private static void Send(TopLevel root, string method, params object?[] arguments) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(root.PlatformImpl, arguments);
    private static async Task PumpAsync()
    {
        for (var index = 0; index < 8; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }
    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
