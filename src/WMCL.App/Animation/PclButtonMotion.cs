
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using WMCL.App.Controls;

namespace WMCL.App.Animation;

/// <summary>普通按钮与图标按钮的 PCL2 按压缩放；颜色过渡仍由样式负责。</summary>
internal static class PclButtonMotion
{
    private static readonly ConditionalWeakTable<Control, State> States = [];
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Button.IsPressedProperty.Changed.AddClassHandler<Button>((button, _) => OnPressedChanged(button));
        Button.IsPointerOverProperty.Changed.AddClassHandler<Button>((button, _) => OnPointerChanged(button));
    }

    internal static double PressScaleAt(double start, double elapsedMilliseconds) =>
        start + (0.955 - start) * PclEasing.Out(elapsedMilliseconds / 80, 5)
        - 0.01 * PclEasing.Out(elapsedMilliseconds / 700, 3);

    private static void OnPressedChanged(Button button)
    {
        if (Excluded(button)) return;
        Animate(button, button.IsPressed ? AnimationKind.Press : AnimationKind.Release);
    }

    private static void OnPointerChanged(Button button)
    {
        if (Excluded(button) || button.IsPointerOver || !button.IsPressed) return;
        Animate(button, AnimationKind.Leave);
    }

    private static bool Excluded(Button button) =>
        button is ToggleButton or DownloadActionButton or ComponentActionButton
        || button.Classes.Contains("titleButton") || button.Classes.Contains("backButton")
        || button.Classes.Contains("catalogEntry") || button.Classes.Contains("componentEntry");

    private static void Animate(Button button, AnimationKind kind)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(part => part.Name == "PART_ContentPresenter");
        if (presenter is null) return;
        var state = States.GetOrCreateValue(presenter);
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = new CancellationTokenSource();
        var token = state.Cancellation.Token;
        var scale = (presenter.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
        var profile = button.Classes.Contains("taskButton") ? Profile.Extra
            : button.Classes.Contains("iconButton") ? Profile.Icon : Profile.Normal;
        if (Motion.ReducedMotion || button.GetVisualRoot() is null)
        {
            presenter.RenderTransform = new ScaleTransform(1, 1);
            return;
        }

        _ = RunAsync(presenter, scale, profile, kind, token);
    }

    private static async Task RunAsync(
        Control presenter,
        double start,
        Profile profile,
        AnimationKind kind,
        CancellationToken cancellation)
    {
        try
        {
            var duration = kind switch
            {
                AnimationKind.Press when profile == Profile.Normal => 700,
                AnimationKind.Press when profile == Profile.Icon => 400,
                AnimationKind.Press => 800,
                AnimationKind.Release when profile == Profile.Icon => 250,
                AnimationKind.Release when profile == Profile.Normal => 310,
                AnimationKind.Release => 300,
                AnimationKind.Leave when profile == Profile.Normal => 800,
                AnimationKind.Leave when profile == Profile.Icon => 250,
                _ => 500
            };
            await PclMotionClock.RunAsync(duration, progress =>
            {
                var milliseconds = progress * duration;
                var scale = kind switch
                {
                    AnimationKind.Press when profile == Profile.Extra =>
                        start + (0.85 - start) * PclEasing.Out(milliseconds / 800, 4)
                        - 0.05 * PclEasing.Out(milliseconds / 60, 3),
                    AnimationKind.Press when profile == Profile.Icon =>
                        start + (0.8 - start) * PclEasing.Out(milliseconds / 400, 4),
                    AnimationKind.Press => PressScaleAt(start, milliseconds),
                    AnimationKind.Release when profile == Profile.Extra =>
                        start + (1 - start) * PclEasing.OutBack(milliseconds / 300, 3),
                    AnimationKind.Release when profile == Profile.Icon =>
                        start + (1.05 - start) * PclEasing.OutBack(milliseconds / 250, 2)
                        - 0.05 * PclEasing.Out(milliseconds / 250, 4),
                    AnimationKind.Release => start + (1 - start) * PclEasing.Out((milliseconds - 10) / 300, 3),
                    _ => start + (1 - start) * PclEasing.Out(milliseconds / duration, profile == Profile.Normal ? 4 : 3)
                };
                presenter.RenderTransform = new ScaleTransform(scale, scale);
            }, cancellation);
        }
        catch (OperationCanceledException) { }
    }

    private enum AnimationKind { Press, Release, Leave }
    private enum Profile { Normal, Icon, Extra }

    private sealed class State
    {
        public CancellationTokenSource? Cancellation;
    }
}
