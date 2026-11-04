using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Motion")]
public sealed class GameManageRevealTests
{
    [AvaloniaFact]
    public async Task SwitchingSelectedVersionReplaysTheDetailReveal()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "game-manage-" + Guid.NewGuid().ToString("N"));
        foreach (var id in new[] { "A", "B" })
        {
            var directory = Path.Combine(root, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"),
                "{\"id\":\"" + id + "\",\"clientVersion\":\"1.21.1\",\"libraries\":[],\"arguments\":{\"game\":[],\"jvm\":[]}}");
        }
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new GameManageViewModel(services);
        await model.LoadAsync();
        Assert.True(model.Versions.Count >= 2);
        var view = new GameManageView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            Pump();
            var panel = view.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "VersionDetailPanel");
            Assert.Equal(1, panel.Opacity);
            model.SelectedVersion = model.Versions[1];
            Pump();
            // 切换后详情重新入场：透明度和位移都会变化，随后回到终态。
            Assert.True(WaitUntil(() => panel.Opacity < 0.99, 800), "详情没有播放入场动画");
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
