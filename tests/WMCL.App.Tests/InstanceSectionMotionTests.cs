using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class InstanceSectionMotionTests
{
    [AvaloniaFact]
    public async Task EmbeddedSectionAnimatesOnlyAfterLoadingFinishes()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "section-motion-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "versions", "Pack");
        var mods = Path.Combine(instance, "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.21.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}],"arguments":{"game":[],"jvm":[]}}""");
        for (var index = 0; index < 40; index++)
            File.WriteAllText(Path.Combine(mods, $"mod-{index}.jar"), "fixture");
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var main = new MainWindowViewModel(services);
        main.Instance.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        var view = new InstanceView { DataContext = main.Instance };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            Pump();
            main.NavigateTo("instance");
            main.Instance.SelectedSectionIndex = 3;
            Pump();
            var host = view.GetVisualDescendants().OfType<Control>().Single(item => item.Name == "EmbeddedPageHost");
            Assert.True(main.Instance.IsSectionPreparing);
            Assert.False(host.IsVisible);
            await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(20));
            Pump();
            Assert.True(host.IsVisible);
            Assert.True(WaitUntil(() => host.Opacity < 0.99, 800), "加载完成后没有播放切换动画");
            Assert.True(WaitUntil(() => host.Opacity > 0.99, 1500));
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
