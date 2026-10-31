using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace WMCL.App.Animation;

/// <summary>任务球等浮动按钮的 PCL2 MyExtraButton 显示/隐藏动画。</summary>
internal static class PclExtraButtonMotion
{
    private static readonly ConditionalWeakTable<Control, CancellationTokenSource> Cancellations = [];

    public static async Task SetVisibleAsync(Border button, bool show)
    {
        Cancel(button);
        if (Motion.ReducedMotion)
        {
            button.IsVisible = show;
            button.RenderTransform = new ScaleTransform(show ? 1 : 0, show ? 1 : 0);
            return;
        }

        var cancellation = new CancellationTokenSource();
        Cancellations.Add(button, cancellation);
        try
        {
            var start = (button.RenderTransform as ScaleTransform)?.ScaleX ?? (show ? 0 : 1);
            if (show) button.IsVisible = true;
            await PclMotionClock.RunAsync(show ? 560 : 100, progress =>
            {
                var milliseconds = progress * (show ? 560 : 100);
                var scale = show
                    ? start + (0.3 - start) * PclEasing.Out((milliseconds - 60) / 500, 2)
                        + 0.7 * PclEasing.OutBack((milliseconds - 60) / 500, 2)
                    : start * (1 - PclEasing.In(progress, 2));
                button.RenderTransform = new ScaleTransform(scale, scale);
            }, cancellation.Token);
            if (!show) button.IsVisible = false;
        }
        catch (OperationCanceledException) { }
        finally
        {
            Cancel(button);
        }
    }

    private static void Cancel(Control button)
    {
        if (!Cancellations.TryGetValue(button, out var cancellation)) return;
        cancellation.Cancel();
        cancellation.Dispose();
        Cancellations.Remove(button);
    }
}
