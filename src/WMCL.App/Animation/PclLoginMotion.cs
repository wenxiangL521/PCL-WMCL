using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace WMCL.App.Animation;

/// <summary>登录方式子页的 PCL2 透明度切换；不改导航和账户状态提交时机。</summary>
internal static class PclLoginMotion
{
    private static readonly ConditionalWeakTable<Control, CancellationTokenSource> Cancellations = [];

    public static void Show(ContentControl host, Control accountPanel, Control content)
    {
        host.Content = content;
        accountPanel.IsVisible = false;
        host.IsVisible = true;
        FadeIn(host);
    }

    public static void Hide(ContentControl host, Control accountPanel)
    {
        host.Content = null;
        host.IsVisible = false;
        accountPanel.IsVisible = true;
        FadeIn(accountPanel);
    }

    private static void FadeIn(Control control)
    {
        Cancel(control);
        control.Opacity = Motion.ReducedMotion ? 1 : 0;
        if (Motion.ReducedMotion) return;
        var cancellation = new CancellationTokenSource();
        Cancellations.Add(control, cancellation);
        _ = RunAsync(control, cancellation.Token);
    }

    private static async Task RunAsync(Control control, CancellationToken cancellation)
    {
        try
        {
            await PclMotionClock.RunAsync(100, progress =>
                control.Opacity = PclEasing.In(progress, 3), cancellation);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Cancel(control);
        }
    }

    private static void Cancel(Control control)
    {
        if (!Cancellations.TryGetValue(control, out var cancellation)) return;
        cancellation.Cancel();
        cancellation.Dispose();
        Cancellations.Remove(control);
    }
}
