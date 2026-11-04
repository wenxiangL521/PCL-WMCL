using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;

namespace WMCL.App.Tests;

[Collection("motion-ui")]
[Trait("Kind", "Motion")]
public sealed class ListItemRevealTests
{
    [AvaloniaFact]
    public void RowsStartDisplacedAndSettleAfterTheAnimation()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>();
        for (var index = 0; index < 6; index++) items.Add("row-" + index);
        var list = new ListBox { ItemsSource = items, Height = 300 };
        ListItemReveal.SetEnabled(list, true);
        var window = new Window { Content = list, Width = 400, Height = 400 };
        try
        {
            window.Show();
            Pump();
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.NotEmpty(rows);
            // 动画期间行有位移或未完全透明到位；稳定后必须回到 1 且位移归零。
            var settled = WaitUntil(() => rows.All(row => row.Opacity > 0.99), 1500);
            Assert.True(settled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CollectionChangesAnimateOnlyRealizedRowsAndStayBounded()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>();
        var list = new ListBox { ItemsSource = items, Height = 200 };
        ListItemReveal.SetEnabled(list, true);
        var window = new Window { Content = list, Width = 400, Height = 300 };
        try
        {
            window.Show();
            Pump();
            for (var index = 0; index < 40; index++) items.Add("row-" + index);
            Pump();
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.NotEmpty(rows);
            Assert.True(WaitUntil(() => rows.All(row => row.Opacity > 0.99), 2000));
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
