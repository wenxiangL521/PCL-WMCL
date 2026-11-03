using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WMCL.UI.Controls;

public sealed class ExpanderReveal : Decorator
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<ExpanderReveal, bool>(nameof(IsExpanded));

    public static readonly DirectProperty<ExpanderReveal, bool> IsContentVisibleProperty =
        AvaloniaProperty.RegisterDirect<ExpanderReveal, bool>(
            nameof(IsContentVisible), control => control.IsContentVisible);

    private const double DurationMilliseconds = 180;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(10) };
    private readonly Stopwatch _clock = new();
    private bool _attached;
    private bool _isContentVisible;
    private bool _pendingExpansion;
    private bool _animating;
    private double _naturalHeight;
    private double _displayHeight;
    private double _startHeight;
    private double _targetHeight;

    public ExpanderReveal()
    {
        ClipToBounds = true;
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public bool IsContentVisible
    {
        get => _isContentVisible;
        private set => SetAndRaise(IsContentVisibleProperty, ref _isContentVisible, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        _attached = true;
        _timer.Tick += OnTick;
        IsContentVisible = IsExpanded;
        _displayHeight = IsExpanded ? _naturalHeight : 0;
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        StopAnimation();
        _timer.Tick -= OnTick;
        _pendingExpansion = false;
        _displayHeight = IsExpanded ? _naturalHeight : 0;
        IsContentVisible = IsExpanded;
        InvalidateMeasure();
        base.OnDetachedFromVisualTree(args);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsExpandedProperty) return;

        if (!_attached)
        {
            IsContentVisible = IsExpanded;
            _displayHeight = IsExpanded ? _naturalHeight : 0;
        }
        else if (IsExpanded)
        {
            IsContentVisible = true;
            StopAnimation();
            _pendingExpansion = true;
        }
        else
        {
            _pendingExpansion = false;
            AnimateTo(0);
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null || !IsContentVisible) return default;

        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var naturalHeight = Child.DesiredSize.Height;
        if (!double.IsFinite(naturalHeight)) naturalHeight = 0;
        var changed = Math.Abs(naturalHeight - _naturalHeight) > 0.01;
        _naturalHeight = naturalHeight;
        if (_pendingExpansion)
        {
            _pendingExpansion = false;
            AnimateTo(_naturalHeight);
        }
        else if (_animating && IsExpanded && changed)
        {
            AnimateTo(_naturalHeight);
        }
        else if (!_animating)
        {
            _displayHeight = IsExpanded ? _naturalHeight : 0;
        }
        return new Size(Child.DesiredSize.Width, _displayHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, _naturalHeight));
        return finalSize;
    }

    private void AnimateTo(double targetHeight)
    {
        StopAnimation();
        _targetHeight = targetHeight;
        if (!_attached || Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION") == "1" ||
            Math.Abs(_displayHeight - targetHeight) < 0.01)
        {
            _displayHeight = targetHeight;
            IsContentVisible = IsExpanded;
            return;
        }

        _startHeight = _displayHeight;
        _animating = true;
        _clock.Restart();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!_attached || !_animating) return;
        var progress = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION") == "1"
            ? 1
            : Math.Clamp(_clock.Elapsed.TotalMilliseconds / DurationMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _displayHeight = _startHeight + (_targetHeight - _startHeight) * eased;
        if (progress >= 1)
        {
            StopAnimation();
            IsContentVisible = IsExpanded;
        }
        InvalidateMeasure();
    }

    private void StopAnimation()
    {
        _timer.Stop();
        _clock.Reset();
        _animating = false;
    }
}
