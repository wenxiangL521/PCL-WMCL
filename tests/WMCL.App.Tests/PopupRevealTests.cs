using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.App.Tests;

[Collection("motion-ui")]
[Trait("Kind", "Motion")]
public sealed class PopupRevealTests
{
    [AvaloniaFact]
    public void DropDownFadesInInsteadOfAppearingInstantly()
    {
        WMCL.App.Animation.PopupReveal.Initialize();
        var combo = new ComboBox { ItemsSource = new[] { "Java 8", "Java 17", "Java 21" }, Width = 240 };
        var window = new Window { Content = combo, Width = 400, Height = 200 };
        try
        {
            window.Show();
            Pump();
            combo.IsDropDownOpen = true;
            var border = WaitForBorder(combo, 1000);
            Assert.NotNull(border);
            // 动画开始时会从透明/位移过渡，不会一出现就是终态。
            var sawShortHeight = false;
            var start = Environment.TickCount64;
            while (Environment.TickCount64 - start < 600)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                if (border!.Bounds.Height < 40) sawShortHeight = true;
                if (sawShortHeight && double.IsNaN(border.Height)) break;
                Thread.Sleep(10);
            }
            // 弹层应当从很矮的高度长到完整高度，而不是一出现就是终态。
            Assert.True(sawShortHeight);
            Assert.True(border!.Bounds.Height > 40);
            Assert.True(border.Opacity > 0.99);
            Assert.Equal(double.NaN, border.Height);
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

    private static Border? WaitForBorder(ComboBox combo, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var border = combo.GetVisualDescendants().OfType<Popup>()
                .Select(popup => popup.Child).OfType<Border>()
                .FirstOrDefault(item => item.Name == "PopupBorder");
            if (border is not null) return border;
            Thread.Sleep(10);
        }
        return null;
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
