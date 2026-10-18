using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaAnimation = Avalonia.Animation.Animation;

namespace WMCL.App.Animation;

/// <summary>
/// 参考 PCL-ME 的 PclModalMotion：弹窗淡入 + 轻微 overshoot。
/// </summary>
public static class ModalMotion
{
    public static async Task AnimateInAsync(Window window)
    {
        window.Opacity = 0;
        window.RenderTransformOrigin = RelativePoint.Center;
        window.RenderTransform = new ScaleTransform(0.94, 0.94);

        var animation = new AvaloniaAnimation
        {
            Duration = MotionDurations.Modal,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.94),
                        new Setter(ScaleTransform.ScaleYProperty, 0.94)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.72),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1.025),
                        new Setter(ScaleTransform.ScaleYProperty, 1.025)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d)
                    }
                }
            }
        };
        await animation.RunAsync(window);
    }
}
