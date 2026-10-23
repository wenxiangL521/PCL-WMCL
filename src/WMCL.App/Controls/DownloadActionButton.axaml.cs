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

namespace WMCL.App.Controls;

/// <summary>PCL2 MyExtraTextButton: independent reveal and additive press animations.</summary>
public partial class DownloadActionButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<bool> ShowProperty =
        AvaloniaProperty.Register<DownloadActionButton, bool>(nameof(Show));

    public static readonly StyledProperty<bool> IsModificationProperty =
        AvaloniaProperty.Register<DownloadActionButton, bool>(nameof(IsModification));

    private static readonly Geometry ModificationIcon = Geometry.Parse("F1 M652.624 145.904c62.256-62.272 163.2-62.272 225.472 0 62.272 62.256 62.272 163.2 0 225.472L477.808 771.664a28.8 28.8 0 0 1-10.816 6.8L242.4 857.376c-47.04 16.544-92.32-28.736-75.776-75.776l78.896-224.592c1.44-4.08 3.76-7.776 6.816-10.816L652.624 145.904z M828.604 195.396a89.44 89.44 0 0 0-126.488 0L308.48 589.032 241.68 781.12 433.768 714.32 828.604 321.884a89.44 89.44 0 0 0 0-126.488z M606.256 194.816a35 35 0 0 1 49.504 0l173.728 173.728a35 35 0 0 1-49.504 49.504L606.256 244.32a35 35 0 0 1 0-49.504z M261.984 539.104a35 35 0 0 1 49.504 0l173.728 173.728a35 35 0 0 1-49.504 49.504L261.984 588.608a35 35 0 0 1 0-49.504z");
    private Avalonia.Controls.Shapes.Path? _actionIcon;
    private Geometry? _downloadIcon;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _showClock = new();
    private readonly Stopwatch _pressClock = new();
    private readonly ScaleTransform _showScale = new();
    private readonly ScaleTransform _pressScale = new();
    private Grid? _showPanel;
    private Border? _colorBorder;
    private double _showStart;
    private double _opacityStart;
    private double _pressStart = 1;
    private bool _showRunning;
    private bool _pressRunning;
    private bool _cancelledPress;
    private PressAnimation _pressAnimation;

    private enum PressAnimation { Down, Release, Leave }

    public bool Show { get => GetValue(ShowProperty); set => SetValue(ShowProperty, value); }
    public bool IsModification { get => GetValue(IsModificationProperty); set => SetValue(IsModificationProperty, value); }

    public DownloadActionButton()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => SetShow(false);
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            _showRunning = _pressRunning = false;
            SetPressScale(1);
        };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _showPanel = e.NameScope.Find<Grid>("ShowPanel");
        if (_showPanel is not null) _showPanel.RenderTransform = _showScale;
        var pressPanel = e.NameScope.Find<Grid>("PressPanel");
        if (pressPanel is not null) pressPanel.RenderTransform = _pressScale;
        _colorBorder = e.NameScope.Find<Border>("ColorBorder");
        _actionIcon = e.NameScope.Find<Avalonia.Controls.Shapes.Path>("DownloadIcon");
        _downloadIcon = _actionIcon?.Data;
        UpdateActionIcon();
        SetPressScale(1);
        SetShow(false);
        UpdateColorTransition();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowProperty) SetShow(true);
        if (change.Property == IsModificationProperty) UpdateActionIcon();
        if (change.Property == IsPressedProperty)
            StartPress(IsPressed ? PressAnimation.Down : PressAnimation.Release);
        if (change.Property == IsPointerOverProperty)
        {
            UpdateColorTransition();
            if (!IsPointerOver) StartPress(PressAnimation.Leave);
        }
        if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled)
        {
            _cancelledPress = true;
            StartPress(PressAnimation.Release);
        }
        if (change.Property == IsEffectivelyEnabledProperty) UpdateColorTransition();
    }

    private void UpdateActionIcon()
    {
        if (_actionIcon is not null) _actionIcon.Data = IsModification ? ModificationIcon : _downloadIcon;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _cancelledPress = false;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (IsPressed) _cancelledPress = true;
        base.OnPointerExited(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) _cancelledPress = false;
        base.OnKeyDown(e);
    }

    protected override void OnClick()
    {
        if (Show && IsEffectivelyEnabled && !_cancelledPress) base.OnClick();
    }

    private void UpdateColorTransition()
    {
        if (_colorBorder is null) return;
        _colorBorder.Transitions = Motion.ReducedMotion ? null : new Transitions
        {
            new BrushTransition
            {
                Property = Border.BackgroundProperty,
                Duration = TimeSpan.FromMilliseconds(IsPointerOver || !IsEffectivelyEnabled ? 120 : 150)
            }
        };
    }

    private void SetShow(bool animate)
    {
        IsHitTestVisible = Show;
        Focusable = Show;
        if (!Show)
        {
            _cancelledPress = true;
            _pressRunning = false;
            SetPressScale(1);
        }
        if (_showPanel is null) return;
        _showStart = _showScale.ScaleX;
        _opacityStart = Show ? 0 : _showPanel.Opacity;
        _showPanel.Opacity = _opacityStart;
        _showRunning = animate && !Motion.ReducedMotion && this.GetVisualRoot() is not null;
        if (!_showRunning)
        {
            SetShowFrame(Show ? 1 : 0, Show ? 1 : 0);
            return;
        }
        _showClock.Restart();
        _timer.Start();
    }

    private void StartPress(PressAnimation animation)
    {
        if (_showPanel is null) return;
        _pressAnimation = animation;
        _pressStart = _pressScale.ScaleX;
        _pressRunning = Show && IsEffectivelyEnabled && !Motion.ReducedMotion && this.GetVisualRoot() is not null;
        if (!_pressRunning)
        {
            SetPressScale(1);
            return;
        }
        _pressClock.Restart();
        _timer.Start();
    }

    private void Tick()
    {
        if (_showRunning)
        {
            var ms = _showClock.Elapsed.TotalMilliseconds;
            var scale = Show
                ? _showStart + (0.15 - _showStart) * EaseBack((ms - 50) / 400) + 0.85 * EaseOut((ms - 50) / 160, 3)
                : _showStart * (1 - Math.Pow(Progress(ms / 100), 2));
            var opacity = Show ? Progress((ms - 50) / 80) : _opacityStart * (1 - Progress((ms - 50) / 50));
            SetShowFrame(scale, opacity);
            if (ms >= (Show ? 450 : 100))
            {
                SetShowFrame(Show ? 1 : 0, Show ? 1 : 0);
                _showRunning = false;
            }
        }
        if (_pressRunning)
        {
            var ms = _pressClock.Elapsed.TotalMilliseconds;
            var duration = _pressAnimation switch { PressAnimation.Down => 800, PressAnimation.Release => 300, _ => 500 };
            var scale = _pressAnimation switch
            {
                PressAnimation.Down => _pressStart + (0.85 - _pressStart) * EaseOut(ms / 800, 4) - 0.05 * EaseOut(ms / 60, 3),
                PressAnimation.Release => _pressStart + (1 - _pressStart) * EaseBack(ms / 300),
                _ => _pressStart + (1 - _pressStart) * EaseOut(ms / 500, 3)
            };
            SetPressScale(scale);
            if (ms >= duration) _pressRunning = false;
        }
        if (!_showRunning && !_pressRunning) _timer.Stop();
    }

    private void SetShowFrame(double scale, double opacity)
    {
        _showScale.ScaleX = _showScale.ScaleY = scale;
        if (_showPanel is not null) _showPanel.Opacity = opacity;
    }

    private void SetPressScale(double scale) => _pressScale.ScaleX = _pressScale.ScaleY = scale;
    private static double Progress(double value) => Math.Clamp(value, 0, 1);
    private static double EaseOut(double value, int power) => 1 - Math.Pow(1 - Progress(value), power);
    private static double EaseBack(double value)
    {
        var t = Progress(value);
        return 1 - Math.Pow(1 - t, 1.5) * Math.Cos(1.5 * Math.PI * t);
    }
}
