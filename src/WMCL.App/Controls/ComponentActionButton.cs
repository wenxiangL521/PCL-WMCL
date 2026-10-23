using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace WMCL.App.Controls;

public sealed class ComponentActionButton : Button
{
    protected override Type StyleKeyOverride => typeof(ComponentActionButton);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly ScaleTransform _scale = new();
    private double _start = 1;
    private bool _cancelled;
    private int _animation;
    private IconPath? _icon;

    public ComponentActionButton()
    {
        _timer.Tick += (_, _) => Tick();
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); SetScale(1); _cancelled = true; };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs args)
    {
        base.OnApplyTemplate(args);
        if (args.NameScope.Find<Border>("PressPanel") is { } panel) panel.RenderTransform = _scale;
        _icon = args.NameScope.Find<IconPath>("Icon");
        UpdateColorTransition();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPressedProperty) Animate(IsPressed ? 0 : 1);
        if (change.Property == IsPointerOverProperty || change.Property == IsEffectivelyEnabledProperty)
            UpdateColorTransition();
        if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled)
        {
            _cancelled = true;
            _timer.Stop();
            SetScale(1);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _cancelled = false;
        base.OnPointerPressed(args);
    }

    protected override void OnPointerExited(PointerEventArgs args)
    {
        if (IsPressed) _cancelled = true;
        base.OnPointerExited(args);
        Animate(2);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (args.Key is Key.Space or Key.Enter) _cancelled = false;
        base.OnKeyDown(args);
    }

    protected override void OnClick()
    {
        if (!_cancelled && IsEffectivelyEnabled && this.GetVisualRoot() is not null) base.OnClick();
    }

    private void Animate(int animation)
    {
        _animation = animation;
        _start = _scale.ScaleX;
        if (Motion.ReducedMotion || this.GetVisualRoot() is null)
        {
            _timer.Stop();
            SetScale(1);
            return;
        }
        _clock.Restart();
        _timer.Start();
    }

    private void Tick()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        var progress = Math.Clamp(elapsed / (_animation == 0 ? 400 : 250), 0, 1);
        var eased = 1 - Math.Pow(1 - progress, _animation == 2 ? 3 : 5);
        var scale = _animation switch
        {
            0 => _start + (0.8 - _start) * eased,
            1 => _start + (1.05 - _start) * (1 - Math.Pow(1 - progress, 1.5) * Math.Cos(1.5 * Math.PI * progress)) - 0.05 * eased,
            _ => _start + (1 - _start) * eased
        };
        SetScale(scale);
        if (progress == 1) _timer.Stop();
    }

    private void SetScale(double value) => _scale.ScaleX = _scale.ScaleY = value;

    private void UpdateColorTransition()
    {
        if (_icon is null) return;
        _icon.Transitions = Motion.ReducedMotion ? null : new Transitions
        {
            new BrushTransition
            {
                Property = IconPath.FillProperty,
                Duration = TimeSpan.FromMilliseconds(IsPointerOver || !IsEffectivelyEnabled ? 120 : 150)
            }
        };
    }
}
