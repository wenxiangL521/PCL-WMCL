using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

internal static class PclPageAnimation
{
    public static async Task RunAsync(IReadOnlyList<Control> controls, bool entering, CancellationToken cancellation,
        double elapsedMilliseconds = 0, ScrollViewer? scrollViewer = null)
    {
        cancellation.ThrowIfCancellationRequested();
        scrollViewer?.UpdateLayout();
        cancellation.ThrowIfCancellationRequested();
        var scrollBar = FindVerticalScrollBar(scrollViewer);
        var scrollTransform = scrollBar?.RenderTransform as TranslateTransform;
        if (scrollBar is not null && scrollTransform is null)
            scrollBar.RenderTransform = scrollTransform = new TranslateTransform(entering ? 10 : 0, 0);
        var scrollStart = scrollTransform?.X ?? 0;
        var scrollDuration = entering ? 350 : 90;
        var duration = entering ? 350 : 70;
        var stagger = entering ? 25 : 15;
        var total = duration + Math.Max(0, controls.Count - 1) * stagger;
        if (scrollBar is not null) total = Math.Max(total, scrollDuration);
        var transforms = controls.Select(control =>
        {
            var transform = new TranslateTransform();
            control.RenderTransform = transform;
            control.IsHitTestVisible = entering;
            return transform;
        }).ToArray();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var elapsed = Motion.ReducedMotion ? total : elapsedMilliseconds + clock.Elapsed.TotalMilliseconds;
            for (var index = 0; index < controls.Count; index++)
            {
                var local = elapsed - index * stagger;
                if (entering)
                {
                    controls[index].Opacity = 1 - Math.Pow(1 - Progress(local / 100), 2);
                    var fluent = 1 - Math.Pow(1 - Progress(local / 250), 3);
                    var back = Progress(local / 350);
                    transforms[index].Y = -16 + 5 * fluent + 11 * (1 - Math.Pow(1 - back, 1.5) * Math.Cos(1.5 * Math.PI * back));
                }
                else
                {
                    var progress = Progress(local / 70);
                    controls[index].Opacity = 1 - progress;
                    transforms[index].Y = -6 * progress;
                }
            }
            if (scrollTransform is not null)
            {
                var progress = Progress(elapsed / scrollDuration);
                var eased = entering ? 1 - Math.Pow(1 - progress, 3) : Math.Pow(progress, 3);
                scrollTransform.X = scrollStart + ((entering ? 0 : 10) - scrollStart) * eased;
            }
            if (elapsed >= total) return;
            await Task.Delay(10, cancellation);
        }
    }

    internal static ScrollBar? FindVerticalScrollBar(ScrollViewer? viewer) => viewer?.GetVisualDescendants()
        .OfType<ScrollBar>().FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible && bar.Bounds.Height > 0);

    public static void ResetScrollBar(ScrollViewer viewer)
    {
        foreach (var bar in viewer.GetVisualDescendants().OfType<ScrollBar>().Where(bar => bar.Orientation == Orientation.Vertical))
            if (bar.RenderTransform is TranslateTransform translation) translation.X = 0;
    }

    public static void Reset(IEnumerable<Control> controls)
    {
        foreach (var control in controls)
        {
            control.Opacity = 1;
            control.RenderTransform = new TranslateTransform();
            control.IsHitTestVisible = true;
        }
    }

    private static double Progress(double value) => Math.Clamp(value, 0, 1);
}
