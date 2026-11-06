using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class MainWindow : Window
{
    private const double ResizeMargin = 6;
    private MainWindowViewModel? _viewModel;

    private TrayIcon? _trayIcon;

    /// <summary>托盘「退出」时置位，绕过「关闭即隐藏」策略。</summary>
    public bool ForceClose { get; private set; }

    /// <summary>按设置把窗口隐藏到托盘；不可用时返回 false 交由系统关闭。</summary>
    public bool TryHideToTray()
    {
        if (_trayIcon is null) return false;
        Hide();
        return true;
    }

    /// <summary>托盘图标：显示启动器 / 启动选中的实例 / 退出。</summary>
    private void SetupTrayIcon()
    {
        if (_trayIcon is not null || _viewModel is null) return;
        try
        {
            var menu = new NativeMenu();

            var show = new NativeMenuItem("显示启动器");
            show.Click += (_, _) => ShowLauncherFromTray();
            menu.Add(show);

            var launch = new NativeMenuItem("启动选中的实例");
            launch.Click += async (_, _) =>
            {
                ShowLauncherFromTray();
                try { await _viewModel.Launch.LaunchCommand.ExecuteAsync(null); }
                catch { /* 启动失败已有界面提示 */ }
            };
            menu.Add(launch);
            menu.Add(new NativeMenuItemSeparator());

            var exit = new NativeMenuItem("退出");
            exit.Click += (_, _) =>
            {
                ForceClose = true;
                Close();
            };
            menu.Add(exit);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "WMCL",
                Menu = menu,
                IsVisible = true
            };
            try
            {
                _trayIcon.Icon = new WindowIcon(
                    Avalonia.Platform.AssetLoader.Open(new Uri("avares://WMCL.App/Assets/wmcl-icon.png")));
            }
            catch
            {
                // 图标缺失不影响托盘菜单
            }
            _trayIcon.Clicked += (_, _) => ShowLauncherFromTray();
        }
        catch
        {
            // 某些桌面环境不支持托盘，静默跳过
            _trayIcon = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }

    /// <summary>窗口位置可能来自已断开或分辨率变化的显示器，越界时拉回主屏中央。</summary>
    private void EnsureVisibleOnScreen()
    {
        try
        {
            var screens = Screens.All;
            if (screens.Count == 0) return;
            var bounds = new PixelRect(Position, new PixelSize(
                Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height)));
            foreach (var screen in screens)
                if (screen.WorkingArea.Intersects(bounds)) return;
            var work = screens[0].WorkingArea;
            Position = new PixelPoint(
                work.X + Math.Max(0, (work.Width - bounds.Width) / 2),
                work.Y + Math.Max(0, (work.Height - bounds.Height) / 2));
        }
        catch
        {
            // 无窗口后端时忽略
        }
    }

    private void ShowLauncherFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }


    private readonly Dictionary<object, Control> _pageViews = new();

    /// <summary>页面切换时优先复用已实例化的视图，避免首次进入时解析模板卡住动画。</summary>
    private Control? ResolvePageView(object? page)
    {
        if (page is null) return null;
        if (_pageViews.TryGetValue(page, out var cached)) return cached;
        Control? view = page switch
        {
            LaunchViewModel => new LaunchView(),
            DownloadViewModel => new DownloadView(),
            InstanceViewModel => new InstanceView(),
            InstanceContentViewModel => new InstanceContentView(),
            ModsViewModel => new ModsView(),
            ModpackViewModel => new ModpackView(),
            SettingsViewModel => new SettingsView(),
            _ => null
        };
        if (view is null) return null;   // 其余页面仍走 DataTemplate
        view.DataContext = page;
        _pageViews[page] = view;
        return view;
    }

    /// <summary>启动后后台预建常用页面，第一次切换不再付出模板实例化成本。</summary>
    private void PrewarmPageViews()
    {
        if (_viewModel is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var page in new object[]
                     {
                         _viewModel.Launch, _viewModel.Download, _viewModel.Mods,
                         _viewModel.Content, _viewModel.Modpack, _viewModel.Settings
                     })
            {
                try { ResolvePageView(page); }
                catch { /* 预热失败不影响使用 */ }
            }
        }, DispatcherPriority.ContextIdle);
    }

    private int _pageAnimationVersion;
    private bool _hiddenForGame;
    private bool _draggingUiScale;
    private double _appliedScale = 1;

    private void OnFeatureRevealKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.F12) return;
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.Services.Features.RevealTemporarily(true);
    }

    private void OnFeatureRevealKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.F12) return;
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.Services.Features.RevealTemporarily(false);
    }

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
        AddHandler(PointerPressedEvent, OnScalePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => FinishScaleGesture(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => FinishScaleGesture();
        PageHost.PageTransition = new CrossFade(MotionDurations.PageTransition);
        // The reveal below drives opacity itself; a DoubleTransition on PageHost would
        // lag each frame and make the fade look like it never starts.

        PageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                ResetPageScroll();
        };
        Opened += (_, _) => EnsureVisibleOnScreen();
        Opened += (_, _) =>
        {
            RootFrame.Opacity = 1;
            RootFrame.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");
        };
        SizeChanged += (_, _) => UpdateCompactHeader();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == TopLevel.ActualTransparencyLevelProperty) UpdateWindowBackendStatus();
        };
        Closed += (_, _) =>
        {
            if (_viewModel is null) return;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Launch.PropertyChanged -= OnLaunchPropertyChanged;
            _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
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
            var waylandText = await WMCL.App.Services.ClipboardService.ReadTextAsync();
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
            _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        }
        _viewModel = DataContext as MainWindowViewModel;
        PrewarmPageViews();
        SetupTrayIcon();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Launch.PropertyChanged += OnLaunchPropertyChanged;
            _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
            ApplyPresentation();
            ApplyBackgroundTransparency(_viewModel);
            _ = PclExtraButtonMotion.SetVisibleAsync(TaskButtonBorder, _viewModel.IsTaskActive);
        }
    }

    private void OnScalePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var slider = e.Source is Slider direct ? direct : (e.Source as Visual)?.FindAncestorOfType<Slider>();
        if (slider?.Name == "UiScaleSlider" && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            _draggingUiScale = true;
    }

    private void FinishScaleGesture()
    {
        if (!_draggingUiScale) return;
        _draggingUiScale = false;
        ApplyPresentation();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.FontScale) && _draggingUiScale) return;
        if (e.PropertyName is nameof(SettingsViewModel.FontScale) or nameof(SettingsViewModel.ApplicationOpacity) or nameof(SettingsViewModel.WindowBackdropBlur) or
            nameof(SettingsViewModel.Theme) or nameof(SettingsViewModel.AccentHue) or nameof(SettingsViewModel.AccentSaturation) or nameof(SettingsViewModel.AccentLightAdjust))
            ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (_viewModel is null) return;
        var scale = _draggingUiScale ? _appliedScale : _viewModel.Settings.FontScale;
        // PCL-ME relies on a dark blurred backdrop; on a light theme without a background
        // image the same numbers wash text out, so keep a higher readability floor there.
        var darkTheme = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var minimumOpacity = darkTheme || _viewModel.HasBackgroundImage ? 0.4 : 0.75;
        // Translucency only makes sense when the compositor really blurs what is behind.
        // Without blur the desktop (or an overlay's backdrop) shows through the content,
        // which looked like leftover UI. Fall back to opaque until blur is active.
        WindowPresentationManager.Apply(this, new WindowPresentation(scale,
            (int)Math.Clamp(_viewModel.Settings.ApplicationOpacity, 0, 100), _viewModel.Settings.WindowBackdropBlur,
            minimumOpacity));
        _appliedScale = double.IsFinite(scale) ? Math.Clamp(scale, 0.8, 1.5) : 1;
        ApplyBackgroundTransparency(_viewModel);
        UpdateCompactHeader();
        UpdateWindowBackendStatus();
    }

    private void UpdateWindowBackendStatus()
    {
        if (_viewModel is null) return;
        var backend = PlatformImpl?.GetType().Name ?? "未知后端";
        if (backend.Contains("X11", StringComparison.OrdinalIgnoreCase))
            backend = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ? "XWayland" : "X11";
        _viewModel.Settings.WindowEffectsBackendStatus = _viewModel.Settings.IsNiriSession
            ? $"当前后端：{backend}；透明级别：{ActualTransparencyLevel}。Niri 模糊以窗口规则为准。"
            : $"当前后端：{backend}；系统实际透明级别：{ActualTransparencyLevel}。";
    }

    private void UpdateCompactHeader()
    {
        var scale = _appliedScale;
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.8, 1.5) : 1;
        Classes.Set("compactHeader", (ClientSize.Width > 0 ? ClientSize.Width : Width) / scale < 700);
    }

    private void ApplyBackgroundTransparency(MainWindowViewModel viewModel)
    {
        RootFrame.Background = !viewModel.HasBackgroundImage && viewModel.BackgroundColorOverlay
            ? RootFrame.FindResource(ActualThemeVariant, "WmclPage") as IBrush
            : Brushes.Transparent;
    }

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null ||
            e.PropertyName != nameof(LaunchViewModel.IsGameRunning)) return;
        if (_viewModel.Launch.IsGameRunning)
        {
            switch (_viewModel.Services.Settings.LauncherAfterLaunch)
            {
                case "close":
                    Close();
                    return;
                case "hide" when !_hiddenForGame:
                    _hiddenForGame = true;
                    Hide();
                    return;
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
        {
            if (sender is MainWindowViewModel vm)
            {
                var view = ResolvePageView(vm.CurrentPage);
                if (view is not null) PageHost.Content = view;
            }
            _ = AnimateCardsAsync();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsTaskActive) && _viewModel is not null)
            _ = PclExtraButtonMotion.SetVisibleAsync(TaskButtonBorder, _viewModel.IsTaskActive);
    }

    private async Task AnimateCardsAsync()
    {
        var version = ++_pageAnimationVersion;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            static () => { }, Avalonia.Threading.DispatcherPriority.Loaded);
        if (version != _pageAnimationVersion) return;

        var cards = PageHost
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("card"))
            .Take(8)
            .ToList();
        try
        {
            await PclPageAnimation.RunAsync(cards, entering: true, CancellationToken.None,
                scrollViewer: cards.FirstOrDefault()?.FindAncestorOfType<ScrollViewer>());
        }
        catch (Exception)
        {
            PclPageAnimation.Reset(cards);
        }
    }

    private void OnTaskBallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        viewModel.OpenTaskManager();
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

    /// <summary>Wayland 的 xdg-shell 没有最小化请求，需要客户端自己隐藏窗口。</summary>
    private static bool RunsOnWayland =>
        OperatingSystem.IsLinux() &&
        (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (RunsOnWayland)
        {
            // Wayland（niri/sway/GNOME）：没有最小化协议，隐藏到托盘；
            // 没有托盘时退回普通最小化，尽量给合成器一次机会。
            if (_trayIcon is not null)
            {
                Hide();
                return;
            }
            WindowState = WindowState.Minimized;
            return;
        }
        // Windows / X11 / macOS：保留系统原生最小化行为
        WindowState = WindowState.Minimized;
    }

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
