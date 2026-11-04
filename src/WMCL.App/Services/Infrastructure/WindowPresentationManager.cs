using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WMCL.App.Services;

internal readonly record struct WindowPresentation(double Scale, int OpacityPercent, bool BackdropBlur, double MinimumOpacity = 0.4)
{
    public double SafeScale => double.IsFinite(Scale) ? Math.Clamp(Scale, 0.8, 1.5) : 1;
    /// <summary>
    /// PCL-ME mapping: the slider controls window translucency. The 40% floor assumes a dark
    /// blurred backdrop (wallpaper); light themes without a backdrop need a higher floor or
    /// white text over a bright desktop washes out.
    /// </summary>
    public double Opacity
    {
        get
        {
            var floor = double.IsFinite(MinimumOpacity) ? Math.Clamp(MinimumOpacity, 0.4, 1) : 0.4;
            return floor + (1 - floor) * Math.Clamp(OpacityPercent, 0, 100) / 100d;
        }
    }
}

/// <summary>
/// Scales layout independently of animations and applies whole-window translucency,
/// matching PCL-ME: the blurred desktop shows through cards and panels while a 40% floor
/// keeps text and controls readable.
/// </summary>
internal static class WindowPresentationManager
{
    private sealed class State(Border surface, LayoutTransformControl scaleHost)
    {
        public Border Surface { get; } = surface;
        public LayoutTransformControl ScaleHost { get; } = scaleHost;
        public WindowPresentation Options { get; set; }
    }
    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static bool _initialized;

    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Window.IsVisibleProperty.Changed.AddClassHandler<Window>((window, _) =>
        {
            if (window.IsVisible && window.Owner is Window owner && States.TryGetValue(owner, out var parent))
                Apply(window, parent.Options);
        });
    }

    public static void Apply(Window window, WindowPresentation options)
    {
        Initialize();
        if (!States.TryGetValue(window, out var state))
        {
            if (window.Content is not Control content) return;
            window.Content = null;
            // LayoutTransformControl writes its child's RenderTransform. Keep a neutral
            // wrapper so the existing RootFrame/confirmation entrance animations cannot
            // overwrite the application scale.
            var scaleHost = new LayoutTransformControl { Child = new Border { Child = content } };
            var surface = new Border { Child = scaleHost, Background = Brushes.Transparent };
            state = new State(surface, scaleHost);
            States.Add(window, state);
            window.Content = surface;
            window.Closed += (_, _) => States.Remove(window);
        }
        state.Options = options;
        state.Surface.Opacity = options.Opacity;
        state.ScaleHost.LayoutTransform = new ScaleTransform(options.SafeScale, options.SafeScale);
        window.Background = Brushes.Transparent;
        window.TransparencyLevelHint = options.BackdropBlur
            ? [WindowTransparencyLevel.Blur, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]
            : [WindowTransparencyLevel.Transparent];
        // Popups use Avalonia's InheritsTransform support in the shared application
        // style; never detach/reparent an already-open popup child to scale it.
        foreach (var child in window.OwnedWindows.OfType<Window>()) Apply(child, options);
    }

    internal static LayoutTransformControl? GetScaleHost(Window window) => States.TryGetValue(window, out var state) ? state.ScaleHost : null;
    internal static double GetOpacity(Window window) => States.TryGetValue(window, out var state) ? state.Surface.Opacity : 1;
    internal static double GetBackgroundOpacity(Window window) => States.TryGetValue(window, out var state) ? state.Options.Opacity : 1;
    internal static double SliderOpacityPercent(Window window) => States.TryGetValue(window, out var state) ? state.Options.OpacityPercent : 100;
}
