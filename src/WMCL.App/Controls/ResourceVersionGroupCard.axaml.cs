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

public partial class ResourceVersionGroupCard : UserControl
{
    private const double RowHeight = 42;
    private const double BottomMargin = 18;

    private readonly RotateTransform _arrow = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private ResourceVersionGroup? _group;
    private CatalogExpansionAnimation? _expansion;
    private double _targetHeight;
    private double _startAngle;
    private double _targetAngle;

    public ResourceVersionGroupCard()
    {
        InitializeComponent();
        Chevron.RenderTransform = _arrow;
        _timer.Tick += OnAnimationTick;
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void Bind()
    {
        Unbind();
        _group = DataContext as ResourceVersionGroup;
        if (_group is null)
        {
            _targetHeight = 0;
            _targetAngle = 0;
            Finish();
            return;
        }
        _group.PropertyChanged += OnGroupChanged;
        SetExpanded(false);
    }

    private void Unbind()
    {
        _timer.Stop();
        if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
        _group = null;
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ResourceVersionGroup.IsExpanded)) SetExpanded(true);
    }

    private void SetExpanded(bool animate)
    {
        if (_group is null) return;
        _timer.Stop();
        var expanded = _group.IsExpanded;
        _targetHeight = expanded ? _group.Entries.Count * RowHeight + BottomMargin : 0;
        _expansion = new CatalogExpansionAnimation(ContentClip.Height, _targetHeight);
        _startAngle = _arrow.Angle;
        _targetAngle = expanded ? 180 : 0;
        if (expanded)
        {
            ContentClip.IsVisible = true;
            VersionItems.Height = _group.Entries.Count * RowHeight;
            VersionItems.ItemsSource = _group.Entries;
        }
        if (!expanded && ContentClip.IsKeyboardFocusWithin) HeaderButton.Focus(NavigationMethod.Directional);
        ContentClip.IsHitTestVisible = expanded;
        ContentClip.IsEnabled = expanded;
        if (!animate || Motion.ReducedMotion || this.GetVisualRoot() is null)
        {
            Finish();
            return;
        }
        _elapsed.Restart();
        _timer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs args)
    {
        if (_expansion is null) return;
        var milliseconds = _elapsed.Elapsed.TotalMilliseconds;
        ContentClip.Height = _expansion.HeightAt(milliseconds);
        _arrow.Angle = _startAngle +
                       (_targetAngle - _startAngle) * CatalogExpansionAnimation.EaseOut(milliseconds / 250);
        if (milliseconds >= Math.Max(_expansion.DurationMilliseconds, 250)) Finish();
    }

    private void Finish()
    {
        _timer.Stop();
        ContentClip.Height = _targetHeight;
        _arrow.Angle = _targetAngle;
        if (_targetHeight <= 0)
        {
            ContentClip.IsVisible = false;
            VersionItems.ItemsSource = null;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_group is null || args.KeyModifiers != KeyModifiers.None) return;
        if (args.Key == Key.Left && _group.IsExpanded)
        {
            _group.IsExpanded = false;
            HeaderButton.Focus(NavigationMethod.Directional);
            args.Handled = true;
        }
        else if (args.Key == Key.Right && HeaderButton.IsFocused)
        {
            _group.IsExpanded = true;
            args.Handled = true;
        }
        else if (args.Key is Key.Down or Key.Up)
        {
            var focused = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("resourceVersionRow") && button.IsFocused);
            if (focused is null && !HeaderButton.IsFocused) return;
            if (!_group.IsExpanded) return;
            var current = focused?.DataContext is ResourceVersionEntry entry
                ? _group.Entries.ToList().IndexOf(entry)
                : -1;
            var next = current + (args.Key == Key.Down ? 1 : -1);
            if (next < 0)
            {
                HeaderButton.Focus(NavigationMethod.Directional);
                args.Handled = true;
            }
            else if (next < _group.Entries.Count)
            {
                var container = VersionItems.ContainerFromIndex(next);
                var button = container?.GetVisualDescendants().OfType<Button>().FirstOrDefault();
                if (button is not null)
                {
                    button.Focus(NavigationMethod.Directional);
                    button.BringIntoView();
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var pending = VersionItems.ContainerFromIndex(next);
                        pending?.GetVisualDescendants().OfType<Button>().FirstOrDefault()?
                            .Focus(NavigationMethod.Directional);
                    }, DispatcherPriority.Loaded);
                }
                args.Handled = true;
            }
        }
    }
}
