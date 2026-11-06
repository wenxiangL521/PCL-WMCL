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
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

[Trait("Kind", "Motion")]
public sealed class SectionTransitionAndToolboxTests
{
    [AvaloniaTheory]
    [InlineData("launch", "settings-java")]
    [InlineData("settings-launch", "settings-java")]
    [InlineData("settings-java", "settings-games")]
    [InlineData("settings-java", "settings-launch")]
    public async Task SectionAndPageSwitchesAlwaysRunTheContentTransition(string from, string to)
    {
        var previousMotion = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "0");
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        model.NavigateTo(from);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 600 };
        try
        {
            window.Show();
            await PumpAsync();
            var host = window.FindControl<Avalonia.Controls.TransitioningContentControl>("PageHost")!;
            var transition = host.PageTransition;
            Assert.NotNull(transition);
            var restarts = 0;
            host.PropertyChanged += (_, args) =>
            {
                if (args.Property == Avalonia.Controls.ContentControl.ContentProperty) restarts++;
            };
            model.NavigateTo(to);
            await PumpAsync();
            // Settings sections share one SettingsViewModel, so they clear the content first
            // and restore it: two content changes that drive the framework CrossFade.
            static bool Shared(string key) => key is "settings-launch" or "settings-java" or "settings-ui" or "settings-other";
            var sharedSettingsPage = Shared(from) && Shared(to);
            Assert.True(restarts >= (sharedSettingsPage ? 2 : 1),
                $"{from}->{to} changed content {restarts} time(s), expected {(sharedSettingsPage ? 2 : 1)}+");
            Assert.NotNull(host.Content);
            Assert.Equal(to, model.SelectedSubTab!.Key);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previousMotion);
        }
    }

    [AvaloniaFact]
    public async Task ClickingSettingsSidebarBetweenSharedPageSectionsRecreatesContent()
    {
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        model.NavigateTo("settings-launch");
        var window = new MainWindow { DataContext = model, Width = 900, Height = 600 };
        try
        {
            window.Show();
            await PumpAsync();
            var host = window.FindControl<Avalonia.Controls.TransitioningContentControl>("PageHost")!;
            var changes = 0;
            host.PropertyChanged += (_, args) =>
            {
                if (args.Property == Avalonia.Controls.ContentControl.ContentProperty) changes++;
            };
            foreach (var key in new[] { "settings-java", "settings-ui", "settings-other", "settings-launch" })
            {
                var tab = model.SubTabs.First(item => item.Key == key);
                var before = changes;
                model.SelectedSubTab = tab;
                await PumpAsync();
                Assert.True(changes > before, $"switching to {key} did not restart the content");
                Assert.Same(model.Settings, model.CurrentPage);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PushedPagesHideTheGlobalTopTabBar()
    {
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        var window = new MainWindow { DataContext = model, Width = 900, Height = 600 };
        try
        {
            window.Show();
            await PumpAsync();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(),
                list => list.Classes.Contains("topTabs"));
            Assert.True(tabs.IsEffectivelyVisible);
            model.OpenVersionSelect();
            await PumpAsync();
            Assert.False(tabs.IsEffectivelyVisible);
            Assert.True(model.CanGoBack);
            model.GoBackCommand.Execute(null);
            await PumpAsync();
            Assert.True(tabs.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ManageJavaButtonIsBoundToTheJavaSectionCommand()
    {
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        model.NavigateTo("settings-ui");
        model.Settings.ManageJavaCommand.Execute(null);
        Assert.Equal("settings-java", model.SelectedSubTab!.Key);
        Assert.True(model.Settings.ShowJavaSettings);
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task ToolboxGroupsSingleButtonToolsIntoSharedRows(string theme)
    {
        ThemeService.Apply(theme);
        var previous = Application.Current!.RequestedThemeVariant;
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "toolbox-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var model = new ToolboxViewModel(services);
        var view = new ToolboxView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var rows = view.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("toolRow")).ToArray();
            Assert.Equal(7, rows.Length);
            Assert.All(rows, row => Assert.True(row.Background is null ||
                row.Background is ISolidColorBrush { Color.A: 0 },
                "tool rows must not paint blue slabs"));
            foreach (var action in new[] { "开始清理", "+1 启动", "创建", "抽奖", "Ping", "测速", "开始对比" })
                Assert.Contains(rows, row => row.GetVisualDescendants().OfType<Button>()
                    .Any(button => Equals(button.Content, action)));
            // Grouped utilities must not each occupy a full standalone card any more.
            var cards = view.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("card") && border.IsEffectivelyVisible).ToArray();
            Assert.InRange(cards.Length, 5, 6);
            var titles = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Classes.Contains("cardTitle")).Select(text => text.Text).ToArray();
            Assert.Contains("实用工具", titles);
            Assert.Contains("网络诊断", titles);
            Assert.DoesNotContain("清理垃圾", titles);
            var screenshot = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(screenshot))
            {
                Directory.CreateDirectory(screenshot);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(screenshot, $"{theme}-toolbox-grouped.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previous == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaFact]
    public async Task GroupedToolsStillInvokeTheirCommands()
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "toolbox-actions-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var model = new ToolboxViewModel(services);
        var view = new ToolboxView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var before = model.LaunchCountStatus;
            var increment = view.GetVisualDescendants().OfType<Button>()
                .First(button => Equals(button.Content, "+1 启动"));
            Assert.Same(model.IncrementLaunchCountCommand, increment.Command);
            increment.Command!.Execute(null);
            await PumpAsync();
            Assert.NotEqual(before, model.LaunchCountStatus);
            var lottery = view.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "抽奖"));
            Assert.Same(model.RunLotteryCommand, lottery.Command);
            lottery.Command!.Execute(null);
            await PumpAsync();
            Assert.False(string.IsNullOrWhiteSpace(model.LotteryResult));
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var headless = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
        headless.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [point, Avalonia.Input.RawInputModifiers.None]);
        headless.GetMethod("MouseDown")!.Invoke(window.PlatformImpl, [point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None]);
        headless.GetMethod("MouseUp")!.Invoke(window.PlatformImpl, [point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None]);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 6; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
        }
    }
}
