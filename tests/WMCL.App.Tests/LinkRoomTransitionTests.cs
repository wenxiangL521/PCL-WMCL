using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class LinkRoomTransitionTests
{
    [AvaloniaFact]
    public void EnteringTheRoomPanelPlaysTheRevealAnimation()
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "link-room-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var connection = new LinkViewModel(services);
        var view = new LinkView { DataContext = connection };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            Pump();
            var panel = view.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "RoomPanel");
            Assert.False(panel.IsVisible);
            Assert.True(connection.IsIdle);
            connection.IsRunning = true;
            Pump();
            Assert.True(panel.IsVisible);
            // 入场动画会先把透明度压低再回到 1。
            Assert.True(WaitUntil(() => panel.Opacity < 0.99, 800), "房间面板没有播放入场动画");
            Assert.True(WaitUntil(() => panel.Opacity > 0.99, 1500));
        }
        finally { window.Close(); }
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
