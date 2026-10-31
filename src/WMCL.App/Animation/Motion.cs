using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

/// <summary>控件变为可见时，复用 PCL2 主内容页的错峰透明度/位移进入动画。</summary>
public static class Motion
{
    public static bool ReducedMotion => Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION") == "1";

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
        if (ReducedMotion)
        {
            PclPageAnimation.Reset([control]);
            return;
        }
        _ = RunAsync(control);
    }

    private static async Task RunAsync(Control control)
    {
        try
        {
            await PclPageAnimation.RunAsync([control], entering: true, CancellationToken.None,
                scrollViewer: control.FindAncestorOfType<ScrollViewer>());
        }
        catch (Exception)
        {
            PclPageAnimation.Reset([control]);
        }
    }
}
