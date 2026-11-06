using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LoadingOverlayTests
{
    private static Control Indicator(LoadingOverlay overlay) =>
        overlay.GetVisualDescendants().OfType<PclLoadingIndicator>().Single();

    [AvaloniaFact]
    public void IndicatorIsCenteredAndHiddenWhenIdle()
    {
        var overlay = new LoadingOverlay { IsRunning = true };
        var window = new Window { Content = overlay, Width = 600, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var indicator = Indicator(overlay);
            var center = indicator.TranslatePoint(
                new Point(indicator.Bounds.Width / 2, indicator.Bounds.Height / 2), overlay)!.Value;
            Assert.InRange(center.X, overlay.Bounds.Width / 2 - 2, overlay.Bounds.Width / 2 + 2);
            Assert.InRange(center.Y, overlay.Bounds.Height / 2 - 2, overlay.Bounds.Height / 2 + 2);

            overlay.IsRunning = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Indicator(overlay).IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }
}
