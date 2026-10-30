using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MainWindow : Window
{
    private const double ResizeMargin = 6;
    private MainWindowViewModel? _viewModel;
    private int _pageAnimationVersion;
    private bool _hiddenForGame;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            Icon = new WindowIcon(
                Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://WMCL.App/Assets/wmcl-icon.png")));
        }
        catch
        {
            // 图标缺失不影响启动
        }
        AddHandler(KeyDownEvent, OnPasteFallback, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalShortcut, RoutingStrategies.Bubble);
        PageHost.PageTransition = new CrossFade(MotionDurations.PageTransition);
        PageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                ResetPageScroll();
        };
        Opened += (_, _) =>
        {
            RootFrame.Opacity = 1;
            RootFrame.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");
        };
        Avalonia.Threading.DispatcherTimer.RunOnce(
            () => RootFrame.Opacity = 1,
            TimeSpan.FromMilliseconds(700));

    }

    private void OnGlobalShortcut(object? sender, KeyEventArgs e)
    {
        // Bubble 策略下，如果子控件已处理此事件（如 ListBox 的 Tab 导航），不再拦截。
        if (e.Handled) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var shortcuts = GetCachedShortcuts(vm);

        // Ctrl+Tab / Ctrl+Shift+Tab：循环切换顶栏。
        // 修饰键按“必需齐全”匹配，因此 Ctrl+Shift+Tab 也命中，Shift 用于反向。
        if (ShortcutParser.Matches(e, shortcuts["Tab"]))
        {
            e.Handled = true;
            vm.CycleTopTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }

        // Ctrl+1~5：切换顶栏（启动/下载/联机/设置/更多）
        var target = ShortcutParser.Matches(e, shortcuts["Tab1"]) ? "launch"
            : ShortcutParser.Matches(e, shortcuts["Tab2"]) ? "download"
            : ShortcutParser.Matches(e, shortcuts["Tab3"]) ? "link"
            : ShortcutParser.Matches(e, shortcuts["Tab4"]) ? "settings"
            : ShortcutParser.Matches(e, shortcuts["Tab5"]) ? "more"
            : null;
        if (target is not null)
        {
            e.Handled = true;
            vm.NavigateTo(target);
            return;
        }

        // Ctrl+F：全局搜索（跳转到下载页）。TextBox 内不劫持，避免影响输入框内的查找。
        if (ShortcutParser.Matches(e, shortcuts["Search"]))
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
            e.Handled = true;
            vm.NavigateTo("download");
        }
    }

    /// <summary>快捷键缓存：仅当 CustomShortcuts JSON 变化时重新解析，
    /// 避免每次 KeyDown 都做一次反序列化。</summary>
    private Dictionary<string, string> GetCachedShortcuts(MainWindowViewModel vm)
    {
        var json = vm.Services.Settings.CustomShortcuts;
        if (_shortcutJson is null || !string.Equals(_shortcutJson, json, StringComparison.Ordinal))
        {
            _shortcutTable = ShortcutParser.ParseTable(json);
            _shortcutJson = json;
        }
        return _shortcutTable!;
    }

    private string? _shortcutJson;
    private Dictionary<string, string>? _shortcutTable;

    private async void OnPasteFallback(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key != Key.V) return;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox textBox) return;

        // 正常的 X11 剪贴板有文本时交给 TextBox 自己处理。
        string? text = null;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        }
        catch
        {
            // X11 选择区不可用时走 Wayland 兜底。
        }
        if (!string.IsNullOrEmpty(text)) return;

        // Wayland 合成器没有桥接文本时，直接读取 Wayland 剪贴板。
        try
        {
            var waylandText = await Services.WaylandClipboard.ReadTextAsync();
            if (string.IsNullOrEmpty(waylandText)) return;
            // 在光标处插入，不替换选中文本
            var selStart = textBox.SelectionStart;
            var selLength = textBox.SelectionEnd - selStart;
            var oldText = textBox.Text ?? "";
            var newText = oldText.Remove(selStart, selLength).Insert(selStart, waylandText);
            textBox.Text = newText;
            textBox.CaretIndex = selStart + waylandText.Length;
            e.Handled = true;
        }
        catch
        {
            // Wayland 读取失败时静默忽略
        }
    }

    private void ResetPageScroll()
    {
        // PCL2 切页后从顶部开始，不保留上一页/上一次的内部滚动位置。
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var scroll in PageHost.GetVisualDescendants().OfType<ScrollViewer>())
                scroll.Offset = default;
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Launch.PropertyChanged -= OnLaunchPropertyChanged;
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Launch.PropertyChanged += OnLaunchPropertyChanged;
            ApplyBackgroundTransparency(_viewModel);
        }
    }

    private void ApplyBackgroundTransparency(MainWindowViewModel viewModel)
    {
        if (!viewModel.HasBackgroundImage)
        {
            RootFrame.Background = null;
            if (Application.Current is { } app2)
            {
                app2.Resources.Remove("WmclCard");
                app2.Resources.Remove("WmclSidebar");
                app2.Resources.Remove("WmclPage");
            }
            return;
        }
        RootFrame.Background = Brushes.Transparent;
        if (Application.Current is not { } app) return;
        var dark = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        app.Resources["WmclCard"] = new SolidColorBrush(
            dark ? Color.FromArgb(214, 32, 34, 38) : Color.FromArgb(214, 255, 255, 255));
        app.Resources["WmclSidebar"] = new SolidColorBrush(
            dark ? Color.FromArgb(180, 27, 29, 33) : Color.FromArgb(180, 255, 255, 255));
        app.Resources["WmclPage"] = new SolidColorBrush(Colors.Transparent);
    }

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null ||
            e.PropertyName != nameof(LaunchViewModel.IsGameRunning)) return;
        if (_viewModel.Launch.IsGameRunning)
        {
            if (_viewModel.Services.Settings.HideLauncherOnGame && !_hiddenForGame)
            {
                _hiddenForGame = true;
                Hide();
            }
        }
        else if (_hiddenForGame)
        {
            _hiddenForGame = false;
            Show();
            Activate();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            _ = AnimateCardsAsync();
    }

    private async Task AnimateCardsAsync()
    {
        var version = ++_pageAnimationVersion;
        PageHost.Opacity = 0.82;
        PageHost.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0px,8px)");
        await Task.Delay(20);
        if (version != _pageAnimationVersion) return;
        PageHost.Opacity = 1;
        PageHost.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0px,0px)");
        await Task.Delay(25);
        if (version != _pageAnimationVersion) return;

        var cards = PageHost
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("card"))
            .Take(8)
            .ToList();

        for (var i = 0; i < cards.Count; i++)
            _ = AnimateCardAsync(cards[i], version, i * 32);
    }

    private async Task AnimateCardAsync(Border card, int version, int delay)
    {
        await Task.Delay(delay);
        if (version != _pageAnimationVersion) return;

        var transform = new TranslateTransform(0, 10);
        card.RenderTransform = transform;
        card.Opacity = 0;

        const int steps = 8;
        for (var i = 1; i <= steps; i++)
        {
            if (version != _pageAnimationVersion) return;
            var progress = i / (double)steps;
            var eased = 1 - Math.Pow(1 - progress, 3);
            card.Opacity = eased;
            transform.Y = 10 * (1 - eased);
            await Task.Delay(16);
        }

        card.Opacity = 1;
        transform.Y = 0;
    }

    private void OnTaskBallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var window = new TaskManagerWindow { DataContext = viewModel.Tasks };
        window.ShowDialog(this);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var edge = GetEdge(e.GetPosition(this));
        if (edge is null) return;
        BeginResizeDrag(edge.Value, e);
        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var edge = GetEdge(e.GetPosition(this));
        Cursor = edge switch
        {
            WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.West or WindowEdge.East => new Cursor(StandardCursorType.SizeWestEast),
            WindowEdge.NorthWest or WindowEdge.SouthEast => new Cursor(StandardCursorType.TopLeftCorner),
            WindowEdge.NorthEast or WindowEdge.SouthWest => new Cursor(StandardCursorType.TopRightCorner),
            _ => Cursor.Default
        };
    }

    private WindowEdge? GetEdge(Point point)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var left = point.X <= ResizeMargin;
        var right = point.X >= width - ResizeMargin;
        var top = point.Y <= ResizeMargin;
        var bottom = point.Y >= height - ResizeMargin;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null
        };
    }
}
