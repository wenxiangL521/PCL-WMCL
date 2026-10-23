using System.ComponentModel;
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

public partial class VersionCatalogCard : UserControl
{
    private VersionCatalogGroup? _group;
    private readonly RotateTransform _arrow = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private double _targetHeight;
    private double _startAngle;
    private double _targetAngle;
    private CatalogExpansionAnimation? _expansion;

    public VersionCatalogCard()
    {
        InitializeComponent();
        Chevron.RenderTransform = _arrow;
        _timer.Tick += OnAnimationTick;
        AttachedToVisualTree += (_, _) => BindGroup();
        DetachedFromVisualTree += (_, _) => UnbindGroup();
        DataContextChanged += (_, _) => BindGroup();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void BindGroup()
    {
        UnbindGroup();
        _group = DataContext as VersionCatalogGroup;
        if (_group is null)
        {
            _targetHeight = 0;
            _targetAngle = 0;
            FinishAnimation();
            return;
        }
        _group.PropertyChanged += OnGroupChanged;
        SetExpanded(false);
    }

    private void UnbindGroup()
    {
        _timer.Stop();
        if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
        _group = null;
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VersionCatalogGroup.IsExpanded)) SetExpanded(true);
    }

    private void SetExpanded(bool animate)
    {
        if (_group is null) return;
        _timer.Stop();
        var expanded = _group.IsExpanded;
        _targetHeight = expanded ? _group.Entries.Count * 42 + 18 : 0;
        _expansion = new CatalogExpansionAnimation(ContentClip.Height, _targetHeight);
        _startAngle = _arrow.Angle;
        _targetAngle = expanded ? 180 : 0;
        if (expanded)
        {
            ContentClip.IsVisible = true;
            VersionItems.Height = _targetHeight - 18;
            VersionItems.ItemsSource = _group.Entries;
        }
        var restoreHeaderFocus = !expanded && ContentClip.IsKeyboardFocusWithin;
        ContentClip.IsHitTestVisible = expanded;
        ContentClip.IsEnabled = expanded;
        if (restoreHeaderFocus) HeaderButton.Focus();
        if (!animate || Motion.ReducedMotion || this.GetVisualRoot() is null)
        {
            FinishAnimation();
            return;
        }

        _elapsed.Restart();
        _timer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs args)
    {
        var milliseconds = _elapsed.Elapsed.TotalMilliseconds;
        if (_expansion is null) return;
        ContentClip.Height = _expansion.HeightAt(milliseconds);
        _arrow.Angle = _startAngle + (_targetAngle - _startAngle) * CatalogExpansionAnimation.EaseOut(milliseconds / 250);
        if (milliseconds >= Math.Max(_expansion.DurationMilliseconds, 250)) FinishAnimation();
    }

    private void FinishAnimation()
    {
        _timer.Stop();
        ContentClip.Height = _targetHeight;
        _arrow.Angle = _targetAngle;
        if (_targetHeight == 0)
        {
            ContentClip.IsVisible = false;
            VersionItems.ItemsSource = null;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_group is null || args.KeyModifiers != KeyModifiers.None) return;
        if (args.Key == Key.Left)
        {
            _group.IsExpanded = false;
            HeaderButton.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Right && HeaderButton.IsKeyboardFocusWithin)
        {
            _group.IsExpanded = true;
            args.Handled = true;
        }
        else if (args.Key is Key.Down or Key.Up)
        {
            var focused = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsFocused);
            if (focused is null) return;
            var current = focused == HeaderButton ? -1 : _group.Entries.ToList()
                .FindIndex(entry => ReferenceEquals(entry, focused.DataContext));
            var next = current + (args.Key == Key.Down ? 1 : -1);
            if (next == -1 && focused != HeaderButton)
            {
                HeaderButton.Focus(NavigationMethod.Directional);
                HeaderButton.BringIntoView();
                args.Handled = true;
            }
            else if (_group.IsExpanded && next >= 0 && next < _group.Entries.Count)
            {
                VersionItems.ScrollIntoView(next);
                Dispatcher.UIThread.Post(() =>
                {
                    var container = VersionItems.ContainerFromIndex(next);
                    var button = container?.GetVisualDescendants().OfType<Button>().FirstOrDefault();
                    button?.Focus(NavigationMethod.Directional);
                    button?.BringIntoView();
                }, DispatcherPriority.Loaded);
                args.Handled = true;
            }
        }
    }
}
