using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Controls;

public partial class InstallComponentRow : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private double _startOpacity;
    private double _startPadding = 4;
    private double _targetPadding = 4;
    private bool _show;
    private bool _attached;

    public InstallComponentRow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => { _attached = true; Refresh(false); };
        DetachedFromVisualTree += (_, _) => { _attached = false; _timer.Stop(); Actions.IsHitTestVisible = false; };
        DataContextChanged += (_, _) => Refresh(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsKeyboardFocusWithinProperty)
            Refresh(true);
    }

    private void Refresh(bool animate)
    {
        if (Actions is null || TextLayout is null) return;
        var count = (DataContext as InstallComponentEntry)?.Actions.Count ?? 0;
        _show = count > 0 && (IsPointerOver || IsKeyboardFocusWithin);
        _targetPadding = _show ? Math.Max(4, 5 + count * 25) : 4;
        _startOpacity = Actions.Opacity;
        _startPadding = TextLayout.ColumnDefinitions[3].Width.Value;
        Actions.IsHitTestVisible = _show && _attached;
        _timer.Stop();
        if (!animate || !_attached || Motion.ReducedMotion)
        {
            Apply(_show ? 1 : 0, _targetPadding);
            return;
        }
        _clock.Restart();
        _timer.Start();
    }

    private void Tick()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        var opacity = Math.Clamp(_show ? (elapsed - 36) / 84 : elapsed / 72, 0, 1);
        var padding = Math.Clamp(_show ? (elapsed - 84) / 36 : elapsed / 72, 0, 1);
        Apply(_startOpacity + ((_show ? 1 : 0) - _startOpacity) * opacity,
            _startPadding + (_targetPadding - _startPadding) * padding);
        if (elapsed >= (_show ? 120 : 72)) _timer.Stop();
    }

    private void Apply(double opacity, double padding)
    {
        Actions.Opacity = opacity;
        TextLayout.ColumnDefinitions[3].Width = new GridLength(padding);
    }
}
