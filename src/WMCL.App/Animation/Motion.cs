using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaAnimation = Avalonia.Animation.Animation;

namespace WMCL.App.Animation;

/// <summary>
/// 参考 PCL-ME 的 Motion.AnimateOnVisible：
/// 控件从隐藏变为可见时，做一次淡入 + 轻微上移。
/// </summary>
public static class Motion
{
    public static readonly AttachedProperty<bool> AnimateOnVisibleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "AnimateOnVisible", typeof(Motion));

    private static readonly ConditionalWeakTable<Control, object> Hooks = [];

    static Motion()
    {
        AnimateOnVisibleProperty.Changed.AddClassHandler<Control>(OnAnimateOnVisibleChanged);
    }

    public static bool GetAnimateOnVisible(Control control) =>
        control.GetValue(AnimateOnVisibleProperty);

    public static void SetAnimateOnVisible(Control control, bool value) =>
        control.SetValue(AnimateOnVisibleProperty, value);

    private static void OnAnimateOnVisibleChanged(
        Control control,
        AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || Hooks.TryGetValue(control, out _)) return;

        Hooks.Add(control, new object());
        control.PropertyChanged += OnControlPropertyChanged;
        if (control.IsVisible) Animate(control);
    }

    private static void OnControlPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty &&
            args.NewValue is true &&
            sender is Control control)
            Animate(control);
    }

    private static void Animate(Control control)
    {
        control.Opacity = 0;
        control.RenderTransform = new TranslateTransform(0, 6);
        var animation = new AvaloniaAnimation
        {
            Duration = MotionDurations.Entrance,
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
                        new Setter(TranslateTransform.YProperty, 6d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                }
            }
        };
        _ = animation.RunAsync(control);
    }
}
