using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Animation;

/// <summary>
/// 滚动到阈值比例时自动执行「加载更多」。用于网络资源列表：已加载 20 条时，
/// 滚到约第 10 条就继续请求下一页，避免用户滚到底部才发现要再点一次。
/// </summary>
public static class InfiniteScroll
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(InfiniteScroll));

    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ICommand?>("Command", typeof(InfiniteScroll));

    public static readonly AttachedProperty<bool> CanLoadMoreProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("CanLoadMore", typeof(InfiniteScroll));

    public static readonly AttachedProperty<double> TriggerRatioProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("TriggerRatio", typeof(InfiniteScroll), 0.55);

    private sealed class State
    {
        public ICommand? Command;
        public bool CanLoadMore;
        public double Ratio = 0.55;
        public bool Requested;
        public double LastExtent;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = [];

    static InfiniteScroll()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((scroll, args) =>
        {
            if (args.NewValue is not true) return;
            if (States.TryGetValue(scroll, out _)) return;
            var state = new State();
            States.Add(scroll, state);
            scroll.ScrollChanged += (_, _) => Evaluate(scroll, state);
        });
        CommandProperty.Changed.AddClassHandler<ScrollViewer>((scroll, args) =>
        {
            if (States.TryGetValue(scroll, out var state)) state.Command = args.NewValue as ICommand;
        });
        CanLoadMoreProperty.Changed.AddClassHandler<ScrollViewer>((scroll, args) =>
        {
            if (!States.TryGetValue(scroll, out var state)) return;
            state.CanLoadMore = args.NewValue is true;
            if (!state.CanLoadMore) state.Requested = false;
            else Evaluate(scroll, state);
        });
        TriggerRatioProperty.Changed.AddClassHandler<ScrollViewer>((scroll, args) =>
        {
            if (States.TryGetValue(scroll, out var state) && args.NewValue is double ratio)
                state.Ratio = Math.Clamp(ratio, 0.1, 0.95);
        });
    }

    public static bool GetEnabled(ScrollViewer scroll) => scroll.GetValue(EnabledProperty);
    public static void SetEnabled(ScrollViewer scroll, bool value) => scroll.SetValue(EnabledProperty, value);
    public static ICommand? GetCommand(ScrollViewer scroll) => scroll.GetValue(CommandProperty);
    public static void SetCommand(ScrollViewer scroll, ICommand? value) => scroll.SetValue(CommandProperty, value);
    public static bool GetCanLoadMore(ScrollViewer scroll) => scroll.GetValue(CanLoadMoreProperty);
    public static void SetCanLoadMore(ScrollViewer scroll, bool value) => scroll.SetValue(CanLoadMoreProperty, value);
    public static double GetTriggerRatio(ScrollViewer scroll) => scroll.GetValue(TriggerRatioProperty);
    public static void SetTriggerRatio(ScrollViewer scroll, double value) => scroll.SetValue(TriggerRatioProperty, value);

    private static void Evaluate(ScrollViewer scroll, State state)
    {
        // 新一页加载完成后列表变长，重新武装，允许滚到下一页阈值时再次触发。
        if (state.Requested && scroll.Extent.Height > state.LastExtent + 1)
            state.Requested = false;
        if (state.Requested || !state.CanLoadMore || state.Command is null) return;
        if (scroll.Extent.Height <= scroll.Viewport.Height) return;
        var threshold = scroll.Extent.Height * state.Ratio;
        if (scroll.Offset.Y + scroll.Viewport.Height < threshold) return;
        if (!state.Command.CanExecute(null)) return;
        state.Requested = true;
        state.LastExtent = scroll.Extent.Height;
        state.Command.Execute(null);
    }
}
