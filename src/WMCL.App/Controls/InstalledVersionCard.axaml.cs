using System.Collections.Specialized;
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
using WMCL.App.Views;

namespace WMCL.App.Controls;

public partial class InstalledVersionCard : UserControl
{
    private const double RowHeight = 42;
    private const double BottomMargin = 18;
    private const double ArrowDurationMilliseconds = 250;

    private readonly RotateTransform _arrow = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private VersionGroup? _group;
    private CatalogExpansionAnimation? _expansion;
    private double _targetHeight;
    private double _startAngle;
    private double _targetAngle;

    public InstalledVersionCard()
    {
        InitializeComponent();
        Chevron.RenderTransform = _arrow;
        _timer.Tick += OnAnimationTick;
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private VersionSelectViewModel? ViewModel =>
        this.FindAncestorOfType<VersionSelectView>()?.DataContext as VersionSelectViewModel;

    private void Bind()
    {
        Unbind();
        _group = DataContext as VersionGroup;
        if (_group is null)
        {
            _targetHeight = 0;
            _targetAngle = 0;
            Finish();
            return;
        }
        _group.PropertyChanged += OnGroupChanged;
        _group.Versions.CollectionChanged += OnVersionsChanged;
        SetExpanded(false);
    }

    private void Unbind()
    {
        _timer.Stop();
        if (_group is not null)
        {
            _group.PropertyChanged -= OnGroupChanged;
            _group.Versions.CollectionChanged -= OnVersionsChanged;
        }
        _group = null;
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VersionGroup.IsExpanded)) SetExpanded(true);
    }

    private void OnVersionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_group?.IsExpanded == true) SetExpanded(true);
    }

    private void SetExpanded(bool animate)
    {
        if (_group is null) return;
        _timer.Stop();
        var expanded = _group.IsExpanded;
        _targetHeight = expanded ? _group.Versions.Count * RowHeight + BottomMargin : 0;
        _expansion = new CatalogExpansionAnimation(ContentClip.Height, _targetHeight);
        _startAngle = _arrow.Angle;
        _targetAngle = expanded ? 180 : 0;

        if (expanded)
        {
            ContentClip.IsVisible = true;
            VersionList.Height = _group.Versions.Count * RowHeight;
            VersionList.ItemsSource = _group.Versions;
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
                       (_targetAngle - _startAngle) * CatalogExpansionAnimation.EaseOut(
                           milliseconds / ArrowDurationMilliseconds);
        if (milliseconds >= Math.Max(_expansion.DurationMilliseconds, ArrowDurationMilliseconds)) Finish();
    }

    private void Finish()
    {
        _timer.Stop();
        ContentClip.Height = _targetHeight;
        _arrow.Angle = _targetAngle;
        if (_targetHeight <= 0)
        {
            ContentClip.IsVisible = false;
            VersionList.ItemsSource = null;
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
            var focusedItem = this.GetVisualDescendants().OfType<ListBoxItem>()
                .FirstOrDefault(item => item.IsFocused);
            if (focusedItem is null && !HeaderButton.IsFocused) return;
            if (!_group.IsExpanded) return;
            var current = focusedItem?.DataContext is VersionItem version
                ? _group.Versions.IndexOf(version)
                : -1;
            var next = current + (args.Key == Key.Down ? 1 : -1);
            if (next < 0)
            {
                HeaderButton.Focus(NavigationMethod.Directional);
                args.Handled = true;
            }
            else if (next < _group.Versions.Count)
            {
                VersionList.ScrollIntoView(next);
                var container = VersionList.ContainerFromIndex(next);
                if (container is not null)
                {
                    container.Focus(NavigationMethod.Tab);
                    container.BringIntoView();
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var pending = VersionList.ContainerFromIndex(next);
                        pending?.Focus(NavigationMethod.Tab);
                        pending?.BringIntoView();
                    }, DispatcherPriority.Loaded);
                }
                args.Handled = true;
            }
        }
    }

    private void OnFavoriteClick(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is not Button { DataContext: VersionItem item }) return;
        ViewModel?.ToggleFavorite(item);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is Button { DataContext: VersionItem item }) ViewModel?.OpenVersionSettings(item);
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is Button { DataContext: VersionItem item }) ViewModel?.OpenVersionFolder(item);
    }

    private void OnMenuSettingsClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: VersionItem item }) ViewModel?.OpenVersionSettings(item);
    }

    private void OnMenuOpenClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: VersionItem item }) ViewModel?.OpenVersionFolder(item);
    }

    private void OnMenuHideClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: VersionItem item }) ViewModel?.ToggleHidden(item);
    }
}
