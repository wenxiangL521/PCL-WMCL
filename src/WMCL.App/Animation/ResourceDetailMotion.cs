using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

/// <summary>
/// PCL2 PageDownloadCompDetail 属于 MyPageRight，进入详情时由通用页面动画
/// 对卡片做错峰淡入/上移。此处复用同一 PclPageAnimation 公式，而不是单卡 AnimateOnVisible。
/// </summary>
public static class ResourceDetailMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ResourceDetailMotion));

    private static readonly ConditionalWeakTable<Control, State> States = [];

    static ResourceDetailMotion()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true)
        {
            Stop(control, reset: true);
            return;
        }

        var state = States.GetOrCreateValue(control);
        if (state.Subscribed) return;
        state.Subscribed = true;
        control.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty)
            {
                if (control.IsVisible) Run(control);
                else Stop(control, reset: true);
            }
        };
        control.AttachedToVisualTree += (_, _) =>
        {
            if (control.IsVisible) Run(control);
        };
        control.DetachedFromVisualTree += (_, _) => Stop(control);
        if (control.IsVisible) Run(control);
    }

    private static async void Run(Control control)
    {
        var state = States.GetOrCreateValue(control);
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = new CancellationTokenSource();
        var token = state.Cancellation.Token;
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (token.IsCancellationRequested || !control.IsVisible || control.GetVisualRoot() is null) return;
        var cards = Cards(control);
        if (cards.Count == 0) return;
        try
        {
            await PclPageAnimation.RunAsync(cards, entering: true, token,
                scrollViewer: control.FindAncestorOfType<ScrollViewer>());
        }
        catch (Exception)
        {
            PclPageAnimation.Reset(cards);
        }
    }

    public static async Task ExitAsync(Control control)
    {
        var state = States.GetOrCreateValue(control);
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = new CancellationTokenSource();
        var token = state.Cancellation.Token;
        var cards = Cards(control);
        if (cards.Count == 0) return;
        try
        {
            await PclPageAnimation.RunAsync(cards, entering: false, token,
                scrollViewer: control.FindAncestorOfType<ScrollViewer>());
        }
        catch (Exception)
        {
            PclPageAnimation.Reset(cards);
        }
    }

    private static void Stop(Control control, bool reset = false)
    {
        if (!States.TryGetValue(control, out var state)) return;
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = null;
        if (reset) PclPageAnimation.Reset(Cards(control));
    }

    private static List<Control> Cards(Control control) =>
        control is Panel panel ? panel.Children.OfType<Control>().ToList() : [];

    private sealed class State
    {
        public bool Subscribed;
        public CancellationTokenSource? Cancellation;
    }
}
