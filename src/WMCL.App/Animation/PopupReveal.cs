using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

/// <summary>
/// 下拉弹层的展开动画：与页面切换一致的短促淡入 + 轻微上移。
/// 全局挂在 ComboBox 上，不需要每个页面单独声明。
/// </summary>
public static class PopupReveal
{
    private const double Duration = 140;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ComboBox.IsDropDownOpenProperty.Changed.AddClassHandler<ComboBox>((combo, args) =>
        {
            if (args.NewValue is true) Reveal(combo);
        });
    }

    private static Border? FindBorder(ComboBox combo) =>
        combo.GetVisualDescendants().OfType<Popup>()
            .Select(popup => popup.Child)
            .OfType<Border>()
            .FirstOrDefault(item => item.Name == "PopupBorder")
        ?? combo.GetVisualDescendants().OfType<Border>().FirstOrDefault(item => item.Name == "PopupBorder");

    private static void Reveal(ComboBox combo)
    {
        // 弹层在打开后的下一次布局才实化，等一帧再取。
        Dispatcher.UIThread.Post(() => Animate(combo), DispatcherPriority.Loaded);
    }

    private static void Animate(ComboBox combo)
    {
        if (!combo.IsDropDownOpen) return;
        // 弹层内容挂在独立的 PopupRoot 上，必须从 Popup.Child 取，而不是 ComboBox 的视觉子树。
        var border = FindBorder(combo);
        if (border is null) return;
        var natural = border.Bounds.Height > 1 ? border.Bounds.Height : border.DesiredSize.Height;
        if (Motion.ReducedMotion || natural <= 1)
        {
            border.Opacity = 1;
            border.Height = double.NaN;
            return;
        }
        // 与下载页展开区一致：从 0 高度长到自然高度，而不是整块淡入。
        var transform = new TranslateTransform(0, -4);
        border.ClipToBounds = true;
        border.RenderTransform = transform;
        border.Opacity = 0;
        border.Height = 0;
        _ = RunAsync(border, transform, natural);
    }

    private static async Task RunAsync(Border border, TranslateTransform transform, double natural)
    {
        try
        {
            await PclMotionClock.RunAsync(Duration, progress =>
            {
                var eased = PclEasing.Out(progress, 3);
                border.Height = natural * eased;
                border.Opacity = Math.Clamp(progress * 2.5, 0, 1);
                transform.Y = -4 * (1 - eased);
            }, CancellationToken.None);
        }
        finally
        {
            border.Height = double.NaN;
            border.Opacity = 1;
            transform.Y = 0;
            border.ClipToBounds = false;
        }
    }
}
