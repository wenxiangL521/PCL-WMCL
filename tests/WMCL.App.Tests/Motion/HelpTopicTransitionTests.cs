using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Motion")]
public sealed class HelpTopicTransitionTests
{
    [AvaloniaFact]
    public void SwitchingHelpTopicReplaysTheArticleEntryAnimation()
    {
        var model = new HelpViewModel();
        var view = new HelpView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            Pump();
            var host = view.GetVisualDescendants().OfType<StackPanel>()
                .Single(item => item.Name == "ArticleHost");
            Assert.True(host.IsVisible);

            // 切换到另一个主题：正文应当先变淡再回到终态
            var target = model.FilteredTopics.First(topic => !ReferenceEquals(topic, model.SelectedTopic));
            model.SelectedTopic = target;
            Assert.True(WaitUntil(() => host.Opacity < 0.99, 900), "切换主题时没有播放动画");
            Assert.True(WaitUntil(() => host.Opacity > 0.99, 1500));
        }
        finally
        {
            window.Close();
        }
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
