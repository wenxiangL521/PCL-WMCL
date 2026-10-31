using System.Diagnostics;

namespace WMCL.App.Animation;

/// <summary>短交互动画的统一时钟：可取消、可从当前值接续，减弱动效时直接落到终值。</summary>
internal static class PclMotionClock
{
    public static async Task RunAsync(
        double durationMilliseconds,
        Action<double> apply,
        CancellationToken cancellation = default)
    {
        if (Motion.ReducedMotion || durationMilliseconds <= 0)
        {
            apply(1);
            return;
        }

        var clock = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var progress = PclEasing.Clamp(clock.Elapsed.TotalMilliseconds / durationMilliseconds);
            apply(progress);
            if (progress >= 1) return;
            await Task.Delay(10, cancellation);
        }
    }
}
