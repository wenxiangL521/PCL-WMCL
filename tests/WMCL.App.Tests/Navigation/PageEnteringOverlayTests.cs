using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class PageEnteringOverlayTests
{
    [AvaloniaFact]
    public async Task OverlayAppearsInTheSameTickAsNavigationAndHidesAfterLoad()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "page-enter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var main = new MainWindowViewModel(services);
        var window = new MainWindow { DataContext = main, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await main.InitializeAsync();
            Dispatcher.UIThread.RunJobs();
            main.NavigateTo("mods");
            // 导航调用返回时遮罩已经处于显示状态，不能等数据开始加载才出现
            Assert.True(main.IsPageEntering);
            Dispatcher.UIThread.RunJobs();
            var overlay = window.GetVisualDescendants().OfType<LoadingOverlay>()
                .Single(item => ReferenceEquals(item.DataContext, main));
            Assert.True(overlay.IsEffectivelyVisible);
            Assert.True(overlay.GetVisualDescendants().OfType<PclLoadingIndicator>().Single().IsEffectivelyVisible);

            var start = Environment.TickCount64;
            while (main.IsPageEntering && Environment.TickCount64 - start < 5000)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(main.IsPageEntering);
            Assert.False(overlay.IsRunning);
            var indicator = overlay.GetVisualDescendants().OfType<PclLoadingIndicator>().Single();
            var hideStart = Environment.TickCount64;
            while (indicator.IsEffectivelyVisible && Environment.TickCount64 - hideStart < 2000)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(indicator.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LaunchPageAlsoShowsTheEnteringOverlay()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "page-enter-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var main = new MainWindowViewModel(services);
        var window = new MainWindow { DataContext = main, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            await main.InitializeAsync();
            main.NavigateTo("mods");
            var start = Environment.TickCount64;
            while (main.IsPageEntering && Environment.TickCount64 - start < 5000)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            main.NavigateTo("launch");
            Assert.True(main.IsPageEntering);
            Dispatcher.UIThread.RunJobs();
            var overlay = window.GetVisualDescendants().OfType<LoadingOverlay>()
                .Single(item => ReferenceEquals(item.DataContext, main));
            Assert.True(overlay.IsRunning);
            var launchStart = Environment.TickCount64;
            while (main.IsPageEntering && Environment.TickCount64 - launchStart < 5000)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(main.IsPageEntering);
        }
        finally { window.Close(); }
    }
}
