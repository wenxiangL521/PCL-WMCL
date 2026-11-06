using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;

namespace WMCL.App.Views;

public class ResourceBrowserView : UserControl
{
    public static readonly StyledProperty<bool> IsTargetLockedProperty =
        AvaloniaProperty.Register<ResourceBrowserView, bool>(nameof(IsTargetLocked));

    private Vector? _resultsOffset;
    private bool _returningToResults;

    public bool IsTargetLocked
    {
        get => GetValue(IsTargetLockedProperty);
        set => SetValue(IsTargetLockedProperty, value);
    }

    protected void RememberResultsPosition() =>
        _resultsOffset = this.FindAncestorOfType<ScrollViewer>()?.Offset;

    protected async Task ReturnToResultsAsync(StackPanel detailPanel, Action returnToResults)
    {
        if (_returningToResults) return;
        _returningToResults = true;
        try
        {
            var scroll = this.FindAncestorOfType<ScrollViewer>();
            await ResourceDetailMotion.ExitAsync(detailPanel);
            returnToResults();
            if (scroll is not null && _resultsOffset is { } offset)
                Dispatcher.UIThread.Post(() =>
                {
                    scroll.UpdateLayout();
                    scroll.Offset = offset;
                }, DispatcherPriority.Loaded);
        }
        finally { _returningToResults = false; }
    }
}
