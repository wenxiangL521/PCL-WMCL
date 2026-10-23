using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using AvaloniaShapePath = Avalonia.Controls.Shapes.Path;

namespace WMCL.App.Tests;

public sealed class InstalledVersionCardTests
{
    [AvaloniaFact]
    public async Task CardAnimatesHeightAndChevronAndReversesFromCurrentFrame()
    {
        var model = Create(Version("1.20.1"), Item("b1.7.3", type: "old_beta"));
        var group = model.Groups.Single(item => item.Title == "不常用版本");
        var view = new VersionSelectView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync(5);
            var card = Card(view, group);
            var clip = card.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "ContentClip");
            var chevron = card.GetVisualDescendants().OfType<AvaloniaShapePath>()
                .Single(path => Math.Abs(path.Width - 10) < 0.1);
            var transform = Assert.IsType<RotateTransform>(chevron.RenderTransform);

            Assert.False(group.IsExpanded);
            Assert.Equal(0, clip.Height);
            Assert.Equal(0, transform.Angle);

            group.ToggleCommand.Execute(null);
            // The first frame must not jump to the final state, even if a loaded
            // test run delays the dispatcher timer before the next assertion.
            Assert.Equal(0, transform.Angle);
            Assert.Equal(0, clip.Height);
            await PumpAsync(18);
            Assert.Equal(180, transform.Angle, 3);
            Assert.Equal(60, clip.Height, 3);

            group.ToggleCommand.Execute(null);
            Assert.Equal(180, transform.Angle);
            Assert.Equal(60, clip.Height, 3);
            await PumpAsync(18);
            Assert.Equal(0, transform.Angle, 3);
            Assert.Equal(0, clip.Height, 3);
            Assert.False(clip.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RowUsesPclIconTwoLineInfoAndRevealsThreeInlineActions()
    {
        var model = Create(Version("1.20.1"));
        var view = new VersionSelectView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync(5);
            var row = Row(view, "1.20.1");
            var image = Assert.Single(row.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(image.Source);
            Assert.Equal(31, image.Bounds.Width, 1);
            Assert.Equal(32, image.Bounds.Height, 1);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "1.20.1");
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "正式版 1.20.1");

            var actions = row.GetVisualDescendants().OfType<StackPanel>()
                .Single(panel => panel.Classes.Contains("rowActions"));
            Assert.Equal(0, actions.Opacity);
            var buttons = actions.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible).ToList();
            Assert.Equal(3, buttons.Count);
            Assert.Contains(buttons, button => button.Classes.Contains("favorite"));
            Assert.All(buttons, button =>
            {
                Assert.Equal(25, button.Bounds.Width, 1);
                Assert.Equal(25, button.Bounds.Height, 1);
                Assert.Equal(0, button.BorderThickness.Left);
            });

            Pointer(window, "MouseMove", row);
            await PumpAsync(12);
            Assert.Equal(1, actions.Opacity, 2);
        }
        finally { window.Close(); }
    }

    private static VersionSelectViewModel Create(params VersionItem[] versions)
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-card-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services);
        foreach (var version in versions) launch.InstalledVersions.Add(version);
        return new VersionSelectViewModel(launch, services, () => { }, () => { });
    }

    private static VersionItem Version(string id) => new()
    {
        Id = id,
        Category = "vanilla",
        Type = "release",
        ReleaseTime = "2024-01-01T00:00:00Z",
        BaseGameVersion = id,
        IsBaseGameVersionReliable = true,
        IsInstalled = true
    };

    private static VersionItem Item(string id, string category = "vanilla", string type = "release") => new()
    {
        Id = id,
        Category = category,
        Type = type,
        IsInstalled = true
    };

    private static InstalledVersionCard Card(Control view, VersionGroup group) =>
        view.GetVisualDescendants().OfType<InstalledVersionCard>()
            .Single(card => ReferenceEquals(card.DataContext, group));

    private static ListBoxItem Row(Control view, string id) =>
        view.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is VersionItem version && version.Id == id);

    private static void Pointer(Window window, string method, Control control)
    {
        var point = control.TranslatePoint(
            new Point(Math.Max(1, control.Bounds.Width / 2), control.Bounds.Height / 2), window)!.Value;
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, [point, RawInputModifiers.None]);
    }

    private static async Task PumpAsync(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
        }
    }
}
