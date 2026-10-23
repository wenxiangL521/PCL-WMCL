using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;

namespace WMCL.App.Controls;

public partial class PclLoadingIndicator : UserControl
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<PclLoadingIndicator, bool>(nameof(IsRunning));
    public static readonly StyledProperty<string> ErrorProperty =
        AvaloniaProperty.Register<PclLoadingIndicator, string>(nameof(Error), "");
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PclLoadingIndicator, ICommand?>(nameof(Command));
    public static readonly StyledProperty<double?> ProgressProperty =
        AvaloniaProperty.Register<PclLoadingIndicator, double?>(nameof(Progress));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _errorClock = new();
    private readonly RotateTransform _rotation = new(55);
    private readonly ScaleTransform _errorScale = new(0.6, 0.6);
    private readonly SolidColorBrush _brush = new();
    private bool _attached;
    private bool _failed;
    private bool _loopRunning;
    private double _loopStopAt = double.PositiveInfinity;
    private bool _errorAnimating;
    private double _errorDelay;
    private double _opacityStart;
    private double _scaleStart = 0.6;
    private Color _colorStart;
    private Color _colorTarget;

    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public string Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public double? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public PclLoadingIndicator()
    {
        InitializeComponent();
        Pickaxe.RenderTransform = _rotation;
        ErrorIcon.RenderTransform = _errorScale;
        Pickaxe.Stroke = LoadingText.Foreground = _brush;
        LeftFragment.Fill = RightFragment.Fill = ErrorIcon.Fill = Ground.Fill = _brush;
        _timer.Tick += (_, _) => Animate();
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            Refresh(false);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _timer.Stop();
            _loopRunning = _errorAnimating = false;
        };
        ActualThemeVariantChanged += (_, _) => Refresh(false);
        ResourcesChanged += (_, _) => Refresh(false);
        Refresh(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRunningProperty || change.Property == ErrorProperty || change.Property == CommandProperty)
            if (RetryButton is not null) Refresh(true);
        if (change.Property == ProgressProperty && LoadingText is not null) UpdateText();
    }

    private void Refresh(bool animate)
    {
        var failed = !IsRunning && !string.IsNullOrEmpty(Error);
        UpdateText();
        RetryButton.Command = Command;
        RetryButton.IsEnabled = failed;
        RetryButton.Focusable = failed;
        if (!animate || Motion.ReducedMotion || !_attached)
        {
            _failed = failed;
            _brush.Color = ResourceColor(failed);
            ErrorIcon.Opacity = failed ? 1 : 0;
            _errorScale.ScaleX = _errorScale.ScaleY = failed ? 1 : 0.6;
            _errorAnimating = false;
            _rotation.Angle = 55;
            LeftFragment.Opacity = RightFragment.Opacity = 0;
            _loopRunning = false;
        }
        else if (_failed != failed)
        {
            if (_errorAnimating) AnimateError();
            _colorStart = _brush.Color;
            _colorTarget = ResourceColor(failed);
            _opacityStart = ErrorIcon.Opacity;
            _scaleStart = _errorScale.ScaleX;
            _errorDelay = failed ? 300 + (_loopRunning && _clock.Elapsed.TotalMilliseconds % 1500 < 600 ? 400 : 0) : 0;
            _failed = failed;
            _errorAnimating = true;
            _errorClock.Restart();
        }
        if (IsRunning && !_loopRunning && _attached && !Motion.ReducedMotion)
        {
            _loopRunning = true;
            _clock.Restart();
        }
        if (IsRunning) _loopStopAt = double.PositiveInfinity;
        else if (_loopRunning && double.IsPositiveInfinity(_loopStopAt))
            _loopStopAt = Math.Ceiling(_clock.Elapsed.TotalMilliseconds / 1500) * 1500;
        UpdateTimer();
    }

    private Color ResourceColor(bool failed) =>
        (this.FindResource(ActualThemeVariant, failed ? "WmclDanger" : "WmclBlue3") as ISolidColorBrush)?.Color ?? Colors.Transparent;

    private void UpdateText() => LoadingText.Text = !IsRunning && !string.IsNullOrEmpty(Error) ? Error
        : Progress is { } progress && double.IsFinite(progress)
            ? $"正在加载 - {Math.Floor(Math.Clamp(progress, 0, 1) * 100)}%" : "正在加载";

    private void UpdateTimer()
    {
        if (_attached && (_loopRunning || _errorAnimating)) _timer.Start();
        else _timer.Stop();
    }

    private void Animate()
    {
        if (Motion.ReducedMotion)
        {
            Refresh(false);
            return;
        }
        if (_loopRunning) AnimateLoop();
        if (_errorAnimating) AnimateError();
        UpdateTimer();
    }

    private void AnimateError()
    {
        var elapsed = _errorClock.Elapsed.TotalMilliseconds;
        var colorProgress = Math.Clamp(elapsed / 300, 0, 1);
        _brush.Color = Color.FromArgb(
            Channel(_colorStart.A, _colorTarget.A, colorProgress),
            Channel(_colorStart.R, _colorTarget.R, colorProgress),
            Channel(_colorStart.G, _colorTarget.G, colorProgress),
            Channel(_colorStart.B, _colorTarget.B, colorProgress));
        var opacityProgress = Math.Clamp((elapsed - _errorDelay) / 100, 0, 1);
        ErrorIcon.Opacity = _opacityStart + ((_failed ? 1 : 0) - _opacityStart) * opacityProgress;
        var scaleProgress = Math.Clamp((elapsed - _errorDelay) / (_failed ? 400 : 200), 0, 1);
        var easedScale = _failed ? 1 - Math.Pow(1 - scaleProgress, 1.5) * Math.Cos(1.5 * Math.PI * scaleProgress) : scaleProgress;
        _errorScale.ScaleX = _errorScale.ScaleY = _scaleStart + ((_failed ? 1 : 0.5) - _scaleStart) * easedScale;
        if (elapsed >= Math.Max(300, _errorDelay + (_failed ? 400 : 200))) _errorAnimating = false;
    }

    private static byte Channel(byte start, byte end, double progress) => (byte)Math.Round(start + (end - start) * progress);

    private void AnimateLoop()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        var cycle = elapsed >= _loopStopAt ? 1500 : elapsed % 1500;
        var lift = Math.Clamp((cycle - 250) / 350, 0, 1);
        var swing = Math.Clamp((cycle - 600) / 900, 0, 1);
        var elasticRemaining = 1 - swing;
        _rotation.Angle = 55 - 75 * Math.Pow(lift, 2) * Math.Cos(1.5 * Math.PI * (1 - lift))
            + 50 * (1 - Math.Pow(1 - swing, 3))
            + 25 * (1 - Math.Pow(elasticRemaining, 1.25) * Math.Cos(2.5 * Math.PI * Math.Pow(swing, 1.5)));
        var fragments = cycle - 600;
        var offset = 1 - Math.Pow(1 - Math.Clamp(fragments / 180, 0, 1), 3);
        LeftFragment.Opacity = RightFragment.Opacity = fragments < 0 ? 0 : 1 - Math.Clamp((fragments - 50) / 100, 0, 1);
        LeftFragment.Margin = new Thickness(7 - 5 * offset, 41 - 6 * offset, 0, 0);
        RightFragment.Margin = new Thickness(14 + 5 * offset, 41 - 6 * offset, 0, 0);
        if (elapsed >= _loopStopAt) _loopRunning = false;
    }
}
