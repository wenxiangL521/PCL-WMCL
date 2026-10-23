using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WMCL.App.Controls;

public static class ResourceSelectionNavigation
{
    public static readonly AttachedProperty<int> RequestProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Request", typeof(ResourceSelectionNavigation));

    private static readonly ConditionalWeakTable<Control, NavigationState> Hooks = [];

    static ResourceSelectionNavigation()
    {
        RequestProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (!Hooks.TryGetValue(control, out var state))
            {
                state = new NavigationState();
                Hooks.Add(control, state);
                control.LayoutUpdated += OnLayoutUpdated;
                control.AttachedToVisualTree += (_, _) =>
                {
                    state.LastRequest = 0;
                    control.InvalidateArrange();
                };
            }
            state.LastRequest = 0;
            control.InvalidateArrange();
        });
    }

    public static int GetRequest(Control control) => control.GetValue(RequestProperty);
    public static void SetRequest(Control control, int value) => control.SetValue(RequestProperty, value);

    private static void OnLayoutUpdated(object? sender, EventArgs args)
    {
        if (sender is not Control control || !Hooks.TryGetValue(control, out var state)) return;
        var request = GetRequest(control);
        if (request <= 0 || request == state.LastRequest || !control.IsEffectivelyVisible || control.Bounds.Height <= 0) return;
        state.LastRequest = request;
        Dispatcher.UIThread.Post(() =>
        {
            if (GetRequest(control) == request && control.IsEffectivelyVisible)
                control.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private sealed class NavigationState
    {
        public int LastRequest { get; set; }
    }
}
