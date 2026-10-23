using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.ViewModels;
using AvaloniaAnimation = Avalonia.Animation.Animation;

namespace WMCL.App.Views;

public partial class InstallView : UserControl
{
    private InstallViewModel? _viewModel;
    private CancellationTokenSource? _transition;
    private Control? _activeStage;
    private readonly Stopwatch _loaderWaitClock = new();
    private readonly Stopwatch _loaderShownClock = new();

    public InstallView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += (_, _) =>
        {
            _viewModel?.CancelInstanceInitialization();
            Detach();
        };
        DataContextChanged += (_, _) => BindViewModel();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    private void BindViewModel()
    {
        Detach();
        _viewModel = DataContext as InstallViewModel;
        if (_viewModel is null) return;
        _viewModel.PropertyChanged += OnStageChanged;
        _viewModel.VersionCatalog.PropertyChanged += OnCatalogChanged;
    }

    private void Detach()
    {
        _transition?.Cancel();
        _loaderWaitClock.Reset();
        _loaderShownClock.Reset();
        InstanceInitializationPanel.IsVisible = false;
        PclPageAnimation.Reset(PageCards(true).Append(InstanceInitializationPanel));
        PclPageAnimation.ResetScrollBar(PageScroll);
        PclPageAnimation.ResetScrollBar(CatalogScroll);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnStageChanged;
            _viewModel.VersionCatalog.PropertyChanged -= OnCatalogChanged;
        }
        _viewModel = null;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs args)
    {
        BindViewModel();
        if (_viewModel is null) return;
        _activeStage = null;
        SwitchStage();
        if (_viewModel.IsChoosingVersion)
            await _viewModel.OpenVersionCatalogCommand.ExecuteAsync(null);
    }

    private void OnStageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstallViewModel.IsChoosingVersion) or nameof(InstallViewModel.IsInstanceInitializationVisible)) SwitchStage();
    }

    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VersionCatalogViewModel.SearchText))
            CatalogScroll.Offset = default;
    }

    private async void SwitchStage()
    {
        if (_viewModel is null) return;
        _transition?.Cancel();
        var cancellation = new CancellationTokenSource();
        _transition = cancellation;
        var incoming = _viewModel.IsChoosingVersion ? CatalogScroll : PageScroll;
        var outgoing = _activeStage;
        _activeStage = incoming;
        ResetVisual(PageScroll);
        ResetVisual(CatalogScroll);
        PageScroll.IsHitTestVisible = false;
        CatalogScroll.IsHitTestVisible = false;
        PageScroll.IsEnabled = false;
        CatalogScroll.IsEnabled = false;
        Focus();
        try
        {
            if (_viewModel.IsInstanceInitializationVisible)
            {
                _activeStage = null;
                if (outgoing == PageScroll && PageScroll.IsVisible)
                    await PclPageAnimation.RunAsync(PageCards(), false, cancellation.Token, scrollViewer: PageScroll);
                cancellation.Token.ThrowIfCancellationRequested();
                PageScroll.IsVisible = false;
                CatalogScroll.IsVisible = false;
                InstallActionPanel.IsVisible = false;
                if (!InstanceInitializationPanel.IsVisible)
                {
                    if (!_loaderWaitClock.IsRunning) _loaderWaitClock.Start();
                    var wait = _viewModel.HasInstanceInitializationError || Motion.ReducedMotion
                        ? 0 : Math.Max(0, 400 - _loaderWaitClock.ElapsedMilliseconds);
                    await Task.Delay((int)wait, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    InstanceInitializationPanel.IsVisible = true;
                    _loaderShownClock.Restart();
                }
                await PclPageAnimation.RunAsync([InstanceInitializationPanel], true, cancellation.Token, _loaderShownClock.Elapsed.TotalMilliseconds);
                return;
            }
            var leavingLoader = InstanceInitializationPanel.IsVisible;
            if (leavingLoader)
            {
                if (_viewModel.IsExistingInstance && !_viewModel.IsChoosingVersion && !Motion.ReducedMotion)
                {
                    await PclPageAnimation.RunAsync([InstanceInitializationPanel], true, cancellation.Token, _loaderShownClock.Elapsed.TotalMilliseconds);
                    await Task.Delay((int)Math.Max(0, 750 - _loaderShownClock.ElapsedMilliseconds), cancellation.Token);
                }
                await PclPageAnimation.RunAsync([InstanceInitializationPanel], false, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            InstanceInitializationPanel.IsVisible = false;
            PclPageAnimation.Reset(PageCards(true).Append(InstanceInitializationPanel));
            if (!leavingLoader || incoming != PageScroll)
            {
                PclPageAnimation.ResetScrollBar(PageScroll);
                PclPageAnimation.ResetScrollBar(CatalogScroll);
            }
            if (leavingLoader) StartDownloadButton.SetCurrentValue(Controls.DownloadActionButton.ShowProperty, false);
            InstallActionPanel.IsVisible = true;
            _loaderWaitClock.Reset();
            _loaderShownClock.Reset();
            var animate = outgoing is not null && outgoing != incoming && !Motion.ReducedMotion;
            if (animate)
            {
                await Task.Delay(10, cancellation.Token);
                await AnimateStageAsync(outgoing!, false, incoming == PageScroll ? -50 : 50, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            PageScroll.IsVisible = incoming == PageScroll;
            CatalogScroll.IsVisible = incoming == CatalogScroll;
            incoming.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector));
            if (leavingLoader && incoming == PageScroll)
            {
                PageScroll.IsHitTestVisible = true;
                PageScroll.IsEnabled = true;
                StartDownloadButton.SetCurrentValue(Controls.DownloadActionButton.ShowProperty, _viewModel.ShowInstallAction);
                await PclPageAnimation.RunAsync(PageCards(), true, cancellation.Token, scrollViewer: PageScroll);
            }
            else if (animate)
                await AnimateStageAsync(incoming, true, incoming == PageScroll ? 40 : -50, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Dispatcher.UIThread.Post(() =>
            {
                if (cancellation.IsCancellationRequested || this.GetVisualRoot() is null) return;
                if (incoming == CatalogScroll)
                    FocusCatalog();
                else if (_viewModel?.IsExistingInstance == true) InstallNameBox.Focus(NavigationMethod.Directional);
                else BackToCatalogButton.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_transition, cancellation))
            {
                _transition = null;
                ResetVisual(PageScroll);
                ResetVisual(CatalogScroll);
                PageScroll.IsHitTestVisible = true;
                CatalogScroll.IsHitTestVisible = true;
                PageScroll.IsEnabled = true;
                CatalogScroll.IsEnabled = true;
            }
            cancellation.Dispose();
        }
    }

    private IReadOnlyList<Control> PageCards(bool includeHidden = false) => PageScroll.Content is Panel panel
        ? panel.Children.Where(control => includeHidden || control.IsVisible).ToArray() : [];

    private static void ResetVisual(Control control)
    {
        control.Opacity = 1;
        control.RenderTransform = new TranslateTransform();
    }

    private static Task AnimateStageAsync(Control control, bool entering, double offset, CancellationToken cancellation)
    {
        var opacity = new AvaloniaAnimation
        {
            Duration = TimeSpan.FromMilliseconds(70),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, entering ? 0d : 1d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, entering ? 1d : 0d)
                    }
                }
            }
        };
        var translation = new AvaloniaAnimation
        {
            Duration = TimeSpan.FromMilliseconds(entering ? 160 : 90),
            Easing = entering ? new QuinticEaseOut() : new LinearEasing(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.XProperty, entering ? offset : 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.XProperty, entering ? 0d : offset) }
                }
            }
        };
        return Task.WhenAll(opacity.RunAsync(control, cancellation), translation.RunAsync(control, cancellation));
    }

    private void OnBackToCatalogClick(object? sender, RoutedEventArgs args) =>
        _viewModel?.OpenVersionCatalogCommand.Execute(null);

    private void FocusCatalog()
    {
        var model = _viewModel;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(model, _viewModel) ||
                model is not { IsChoosingVersion: true } || model.VersionCatalog.IsSearchVisible ||
                this.GetVisualRoot() is null) return;
            ((Control?)CatalogGroups.GetVisualDescendants().OfType<Button>().FirstOrDefault() ?? this)
                .Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_viewModel is null) return;
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control && _viewModel.IsChoosingVersion)
        {
            _viewModel.VersionCatalog.IsSearchVisible = true;
            CatalogSearch.Focus();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            if (_viewModel.IsChoosingVersion && _viewModel.VersionCatalog.IsSearchVisible)
            {
                _viewModel.VersionCatalog.ClearSearchCommand.Execute(null);
                _viewModel.VersionCatalog.IsSearchVisible = false;
                FocusCatalog();
            }
            else if (_viewModel.IsChoosingVersion) _viewModel.VersionCatalog.CancelCommand.Execute(null);
            else _viewModel.OpenVersionCatalogCommand.Execute(null);
            args.Handled = true;
        }
    }

}
