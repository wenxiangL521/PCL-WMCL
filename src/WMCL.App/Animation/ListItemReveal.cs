using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

/// <summary>
/// 本地资源列表的进入动画：与下载页一致，可见行错峰淡入。
/// 只改透明度、不改布局位移，避免动画期间点击落在错误的行上；
/// 只处理已实化的前若干行，并合并同一次批量刷新。
/// </summary>
public static class ListItemReveal
{
    private const int MaxAnimatedRows = 14;
    private const double RowDuration = 180;
    private const double RowStagger = 18;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("Enabled", typeof(ListItemReveal));

    private static readonly ConditionalWeakTable<ListBox, State> States = [];

    static ListItemReveal()
    {
        EnabledProperty.Changed.AddClassHandler<ListBox>((list, args) =>
        {
            if (args.NewValue is not true) return;
            if (States.TryGetValue(list, out _)) return;
            var state = new State();
            States.Add(list, state);
            list.AttachedToVisualTree += (_, _) => Attach(list, state);
            list.DetachedFromVisualTree += (_, _) => Detach(state);
            if (list.IsAttachedToVisualTree()) Attach(list, state);
        });
    }

    public static bool GetEnabled(ListBox list) => list.GetValue(EnabledProperty);
    public static void SetEnabled(ListBox list, bool value) => list.SetValue(EnabledProperty, value);

    private static void Attach(ListBox list, State state)
    {
        Detach(state);
        if (list.ItemsSource is INotifyCollectionChanged changed)
        {
            state.Source = changed;
            state.Handler = (_, args) =>
            {
                // 全新搜索（Reset）重新播；无限滚动的追加完全静默，不再触发任何行动画。
                if (args.Action == NotifyCollectionChangedAction.Reset) state.Seen.Clear();
                else if (args.Action == NotifyCollectionChangedAction.Add && state.Seen.Count > 0) return;
                Schedule(list, state);
            };
            changed.CollectionChanged += state.Handler;
        }
        state.Attached = true;
        state.Seen.Clear();
        // 列表常常在“加载中”被隐藏、加载结束后才显示；可见性恢复时补播一次入场。
        state.List = list;
        state.VisibilityHandler = (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty && list.IsEffectivelyVisible)
                Schedule(list, state);
        };
        list.PropertyChanged += state.VisibilityHandler;
        Schedule(list, state);
    }

    private static void Detach(State state)
    {
        if (state.Handler is not null && state.Source is INotifyCollectionChanged changed)
            changed.CollectionChanged -= state.Handler;
        state.Handler = null;
        state.Source = null;
        state.Attached = false;
        state.Scheduled = false;
        if (state.List is { } list && state.VisibilityHandler is { } handler)
            list.PropertyChanged -= handler;
        state.List = null;
        state.VisibilityHandler = null;
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = null;
    }

    private static void Schedule(ListBox list, State state)
    {
        if (!state.Attached || state.Scheduled) return;
        state.Scheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.Scheduled = false;
            Animate(list, state);
        }, DispatcherPriority.Loaded);
    }

    private static void Animate(ListBox list, State state)
    {
        if (!state.Attached || !list.IsEffectivelyVisible) return;
        var rows = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(item => item.IsEffectivelyVisible && item.Bounds.Height > 0)
            .Where(item => item.DataContext is not { } data || state.Seen.Add(data))
            .Take(MaxAnimatedRows)
            .ToArray();
        if (rows.Length == 0) return;
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;
        _ = RunAsync(rows, cancellation);
    }

    private static async Task RunAsync(Control[] rows, CancellationTokenSource cancellation)
    {
        var clock = Stopwatch.StartNew();
        var total = RowDuration + (rows.Length - 1) * RowStagger;
        try
        {
            foreach (var row in rows) row.Opacity = Motion.ReducedMotion ? 1 : 0;
            if (Motion.ReducedMotion) return;
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var elapsed = clock.Elapsed.TotalMilliseconds;
                for (var index = 0; index < rows.Length; index++)
                {
                    var local = elapsed - index * RowStagger;
                    rows[index].Opacity = Math.Clamp(local / RowDuration, 0, 1);
                }
                if (elapsed >= total) return;
                await Task.Delay(10, cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var row in rows) row.Opacity = 1;
        }
    }

    private sealed class State
    {
        public bool Attached;
        public bool Scheduled;
        public NotifyCollectionChangedEventHandler? Handler;
        public INotifyCollectionChanged? Source;
        public CancellationTokenSource? Cancellation;
        public ListBox? List;
        public EventHandler<AvaloniaPropertyChangedEventArgs>? VisibilityHandler;
        public HashSet<object> Seen = new(ReferenceEqualityComparer.Instance);
    }
}
