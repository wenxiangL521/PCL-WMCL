using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WMCL.App.Animation;

/// <summary>ListBoxItem 的 PCL2 MyListItem 悬停/按压反馈；选中项不再执行按压缩放。</summary>
internal static class PclListItemMotion
{
    private static readonly ConditionalWeakTable<Control, CancellationTokenSource> Cancellations = [];
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ListBoxItem.IsPointerOverProperty.Changed.AddClassHandler<ListBoxItem>((item, _) => OnPointerChanged(item));
        InputElement.PointerPressedEvent.AddClassHandler<ListBoxItem>((item, _) => OnPressedChanged(item, true));
        InputElement.PointerReleasedEvent.AddClassHandler<ListBoxItem>((item, _) => OnPressedChanged(item, false));
        ListBoxItem.IsSelectedProperty.Changed.AddClassHandler<ListBoxItem>((item, _) => Reset(item));
    }

    private static void OnPointerChanged(ListBoxItem item)
    {
        if (SkipsScaleMotion(item))
        {
            Reset(item);
            return;
        }
        var sidebar = item.FindAncestorOfType<ListBox>()?.Classes.Contains("subTabs") == true;
        var presenter = Content(item);
        if (sidebar && presenter is not null && Cancellations.TryGetValue(presenter, out _))
        {
            if (!item.IsPointerOver)
            {
                Cancel(item);
                item.RenderTransform = new ScaleTransform(1, 1);
            }
            return;
        }
        if (item.IsPointerOver)
        {
            if (presenter is null) return;
            var start = sidebar
                ? 0.8
                : (presenter.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
            Animate(presenter, 192, progress => start + (1 - start) * PclEasing.Out(progress, 3));
        }
        else
        {
            Reset(item);
        }
    }

    private static void OnPressedChanged(ListBoxItem item, bool pressed)
    {
        if (SkipsScaleMotion(item))
        {
            Reset(item);
            return;
        }
        if (item.IsSelected)
        {
            Reset(item);
            return;
        }
        Animate(item, pressed ? 108 : 540,
            progress => pressed
                ? 1 - 0.02 * PclEasing.Out(progress, 3)
                : 1);
    }

    private static bool SkipsScaleMotion(ListBoxItem item) => item.FindAncestorOfType<ListBox>() is { } list &&
        (list.Classes.Contains("topTabs") || list.Classes.Contains("taskRows"));

    private static void Reset(ListBoxItem item)
    {
        Cancel(item);
        item.RenderTransform = new ScaleTransform(1, 1);
        var presenter = Content(item);
        if (presenter is null) return;
        Cancel(presenter);
        presenter.RenderTransform = new ScaleTransform(1, 1);
    }

    private static ContentPresenter? Content(ListBoxItem item) => item.GetVisualDescendants()
        .OfType<ContentPresenter>().FirstOrDefault(part => part.Name == "PART_ContentPresenter");

    private static void Animate(Control control, double duration, Func<double, double> scaleAt)
    {
        Cancel(control);
        if (Motion.ReducedMotion || control.GetVisualRoot() is null)
        {
            control.RenderTransform = new ScaleTransform(1, 1);
            return;
        }
        var cancellation = new CancellationTokenSource();
        Cancellations.Remove(control);
        Cancellations.Add(control, cancellation);
        _ = RunAsync(control, duration, scaleAt, cancellation);
    }

    private static async Task RunAsync(Control control, double duration, Func<double, double> scaleAt,
        CancellationTokenSource cancellation)
    {
        try
        {
            await PclMotionClock.RunAsync(duration, progress =>
                control.RenderTransform = new ScaleTransform(scaleAt(progress), scaleAt(progress)), cancellation.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (Cancellations.TryGetValue(control, out var current) && ReferenceEquals(current, cancellation))
                Cancellations.Remove(control);
            cancellation.Dispose();
        }
    }

    private static void Cancel(Control control)
    {
        if (!Cancellations.TryGetValue(control, out var cancellation)) return;
        cancellation.Cancel();
        cancellation.Dispose();
        Cancellations.Remove(control);
    }
}
