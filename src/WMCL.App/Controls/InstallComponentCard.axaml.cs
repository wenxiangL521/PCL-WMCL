using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Controls;

public partial class InstallComponentCard : UserControl
{
    private InstallComponentCardViewModel? _model;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private readonly RotateTransform _arrow = new();
    private readonly TranslateTransform _summaryTranslation = new();
    private CatalogExpansionAnimation? _animation;
    private double _startAngle;
    private double _targetAngle;
    private double _targetHeight;
    private double _startSummaryOpacity;
    private double _startSummaryY;
    private bool _expanded;

    public InstallComponentCard()
    {
        InitializeComponent();
        Chevron.RenderTransform = _arrow;
        SummaryPanel.RenderTransform = _summaryTranslation;
        ComparisonPanel.RenderTransform = _summaryTranslation;
        _timer.Tick += (_, _) => Animate();
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void Bind()
    {
        Unbind();
        _model = DataContext as InstallComponentCardViewModel;
        if (_model is null)
        {
            _targetHeight = _targetAngle = 0;
            Finish();
            return;
        }
        _model.PropertyChanged += OnChanged;
        _model.DisplayItems.CollectionChanged += OnEntriesChanged;
        SetExpanded(false);
    }

    private void Unbind()
    {
        _timer.Stop();
        if (_model is not null)
        {
            _model.PropertyChanged -= OnChanged;
            _model.DisplayItems.CollectionChanged -= OnEntriesChanged;
        }
        _model = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InstallComponentCardViewModel.IsExpanded)) SetExpanded(true);
        if (args.PropertyName == nameof(InstallComponentCardViewModel.IsSelected)) UpdateSummaryMargin();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_model?.IsExpanded == true) SetExpanded(true);
    }

    private void UpdateSummaryMargin() => SummaryPanel.Margin = new Thickness(132, 11, _model?.IsSelected == true ? 80 : 35, 0);

    private void SetExpanded(bool animate)
    {
        if (_model is null) return;
        _timer.Stop();
        var expanded = _model.IsExpanded;
        _expanded = expanded;
        UpdateSummaryMargin();
        _targetHeight = expanded ? _model.ContentHeight : 0;
        _animation = new(ContentClip.Height, _targetHeight);
        _startAngle = _arrow.Angle;
        _targetAngle = expanded ? 180 : 0;
        _startSummaryOpacity = SummaryPanel.Opacity;
        _startSummaryY = _summaryTranslation.Y;
        if (expanded)
        {
            ContentClip.IsVisible = true;
            VersionItems.Height = _targetHeight - 15;
            VersionItems.ItemsSource = _model.DisplayItems;
        }
        if (!expanded && ContentClip.IsKeyboardFocusWithin) HeaderButton.Focus(NavigationMethod.Directional);
        ContentClip.IsHitTestVisible = expanded;
        ContentClip.IsEnabled = expanded;
        if (!animate || Motion.ReducedMotion || this.GetVisualRoot() is null) Finish();
        else
        {
            _elapsed.Restart();
            _timer.Start();
        }
    }

    private void Animate()
    {
        if (_animation is null) return;
        var milliseconds = _elapsed.Elapsed.TotalMilliseconds;
        ContentClip.Height = _animation.HeightAt(milliseconds);
        _arrow.Angle = _startAngle + (_targetAngle - _startAngle) * CatalogExpansionAnimation.EaseOut(milliseconds / 250);
        SummaryPanel.Opacity = _startSummaryOpacity + ((_expanded ? 0 : 1) - _startSummaryOpacity) * Math.Clamp(milliseconds / 60, 0, 1);
        ComparisonPanel.Opacity = SummaryPanel.Opacity;
        var summaryProgress = Math.Clamp(milliseconds / (_expanded ? 60 : 150), 0, 1);
        if (!_expanded) summaryProgress = 1 - Math.Pow(1 - summaryProgress, 3);
        _summaryTranslation.Y = _startSummaryY + ((_expanded ? 6 : 0) - _startSummaryY) * summaryProgress;
        if (milliseconds >= Math.Max(_animation.DurationMilliseconds, 250)) Finish();
    }

    private void Finish()
    {
        _timer.Stop();
        ContentClip.Height = _targetHeight;
        _arrow.Angle = _targetAngle;
        SummaryPanel.Opacity = _expanded ? 0 : 1;
        ComparisonPanel.Opacity = SummaryPanel.Opacity;
        _summaryTranslation.Y = _expanded ? 6 : 0;
        if (_targetHeight == 0)
        {
            ContentClip.IsVisible = false;
            VersionItems.ItemsSource = null;
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs args) => HeaderButton.Focus(NavigationMethod.Directional);

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_model is null || args.KeyModifiers != KeyModifiers.None) return;
        if (args.Key == Key.Left && _model.IsExpanded)
        {
            _model.IsExpanded = false;
            HeaderButton.Focus(NavigationMethod.Directional);
            args.Handled = true;
        }
        else if (args.Key == Key.Right && HeaderButton.IsFocused && _model.CanExpand)
        {
            _model.IsExpanded = true;
            args.Handled = true;
        }
        else if (args.Key is Key.Down or Key.Up)
        {
            var focused = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsFocused);
            if (focused is null) return;
            var focusedEntry = focused?.FindAncestorOfType<InstallComponentRow>()?.DataContext ?? focused?.DataContext;
            var current = focused == HeaderButton ? -1 : _model.DisplayItems.ToList()
                .FindIndex(entry => ReferenceEquals(entry, focusedEntry));
            var next = current + (args.Key == Key.Down ? 1 : -1);
            while (next >= 0 && next < _model.DisplayItems.Count && _model.DisplayItems[next] is InstallComponentSeparator)
                next += args.Key == Key.Down ? 1 : -1;
            if (next == -1 && focused != HeaderButton)
            {
                HeaderButton.Focus(NavigationMethod.Directional);
                args.Handled = true;
            }
            else if (_model.IsExpanded && next >= 0 && next < _model.DisplayItems.Count)
            {
                VersionItems.ScrollIntoView(next);
                Dispatcher.UIThread.Post(() =>
                {
                    var button = VersionItems.ContainerFromIndex(next)?.GetVisualDescendants().OfType<Button>().FirstOrDefault();
                    button?.Focus(NavigationMethod.Directional);
                    button?.BringIntoView();
                }, DispatcherPriority.Loaded);
                args.Handled = true;
            }
        }
    }
}
