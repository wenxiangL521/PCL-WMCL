using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WMCL.App.Animation;

/// <summary>PCL2 MyMsg：透明度 + 纵向位移 + 小角度旋转，不使用缩放。</summary>
public static class ModalMotion
{
    public static async Task AnimateInAsync(Window window)
    {
        if (Motion.ReducedMotion)
        {
            window.Opacity = 1;
            window.RenderTransform = null;
            return;
        }

        window.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        var translate = new TranslateTransform(0, 40);
        var rotate = new RotateTransform(-4);
        window.RenderTransform = new TransformGroup
        {
            Children = { translate, rotate }
        };
        window.Opacity = 0;

        await PclMotionClock.RunAsync(360, progress =>
        {
            var milliseconds = progress * 360;
            window.Opacity = PclEasing.Clamp((milliseconds - 60) / 120);
            translate.Y = 40 * (1 - PclEasing.OutBack((milliseconds - 60) / 300, 2));
            rotate.Angle = -4 * (1 - PclEasing.Out((milliseconds - 60) / 300, 2));
        });
    }

    public static async Task AnimateOutAsync(Window window)
    {
        if (Motion.ReducedMotion)
        {
            window.Opacity = 0;
            return;
        }

        if (window.RenderTransform is not TransformGroup { Children.Count: 2 } group
            || group.Children[0] is not TranslateTransform translate
            || group.Children[1] is not RotateTransform rotate)
        {
            translate = new TranslateTransform();
            rotate = new RotateTransform();
            window.RenderTransform = new TransformGroup { Children = { translate, rotate } };
        }

        var opacity = window.Opacity;
        var startY = translate.Y;
        var startAngle = rotate.Angle;
        await PclMotionClock.RunAsync(150, progress =>
        {
            var milliseconds = progress * 150;
            window.Opacity = opacity * (1 - PclEasing.Clamp((milliseconds - 20) / 80));
            translate.Y = startY + (20 - startY) * PclEasing.Out(milliseconds / 150, 3);
            rotate.Angle = startAngle + (6 - startAngle) * PclEasing.In(milliseconds / 150, 2);
        });
    }
}
