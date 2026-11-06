using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WMCL.App.Controls;

public sealed class SkinColorPickedEventArgs(Color color) : EventArgs
{
    public Color Color { get; } = color;
}

/// <summary>皮肤像素编辑器画布。按像素渲染，支持鼠标/触摸绘制。</summary>
public sealed class SkinPixelEditor : Control
{
    public static readonly StyledProperty<WMCL.App.Features.Accounts.SkinEditorViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<SkinPixelEditor, WMCL.App.Features.Accounts.SkinEditorViewModel?>(
            nameof(ViewModel));

    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<SkinPixelEditor, double>(
        nameof(Zoom), 1, coerce: (_, value) => double.IsFinite(value) ? Math.Clamp(value, 0.5, 24) : 1);
    public static readonly StyledProperty<double> PanXProperty = AvaloniaProperty.Register<SkinPixelEditor, double>(nameof(PanX));
    public static readonly StyledProperty<double> PanYProperty = AvaloniaProperty.Register<SkinPixelEditor, double>(nameof(PanY));
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public double PanX { get => GetValue(PanXProperty); set => SetValue(PanXProperty, value); }
    public double PanY { get => GetValue(PanYProperty); set => SetValue(PanYProperty, value); }

    public static readonly StyledProperty<bool> IsColorPickingProperty = AvaloniaProperty.Register<SkinPixelEditor, bool>(nameof(IsColorPicking));
    public bool IsColorPicking { get => GetValue(IsColorPickingProperty); set => SetValue(IsColorPickingProperty, value); }
    public event EventHandler<SkinColorPickedEventArgs>? ColorPicked;

    static SkinPixelEditor()
    {
        AffectsRender<SkinPixelEditor>(ViewModelProperty, ZoomProperty, PanXProperty, PanYProperty);
    }

    public WMCL.App.Features.Accounts.SkinEditorViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ViewModel is { HasSkin: true, Width: > 0 } model ? model.Width : 64;
        var height = ViewModel is { HasSkin: true, Height: > 0 } skin ? skin.Height : 64;
        var scale = 6d;
        if (double.IsFinite(availableSize.Width)) scale = Math.Min(scale, Math.Max(0, availableSize.Width) / width);
        if (double.IsFinite(availableSize.Height)) scale = Math.Min(scale, Math.Max(0, availableSize.Height) / height);
        return new Size(width * scale, height * scale);
    }

    private bool _isPainting;
    private bool _isPanning;
    private Point _panStart;
    private Point _panOffset;
    private IPointer? _paintingPointer;
    private WMCL.App.Features.Accounts.SkinEditorViewModel? _observedViewModel;
    private PropertyChangedEventHandler? _pixelsChangedHandler;

    // 渲染资源缓存：Render 每次重绘都对每个非空像素取画刷，
    // 逐像素 new SolidColorBrush 在长笔画下每帧分配数千个对象。
    private static readonly IBrush CanvasBackground = new SolidColorBrush(Color.FromRgb(224, 228, 234)).ToImmutable();
    private static readonly IBrush AlternateBackground = new SolidColorBrush(Color.FromRgb(247, 249, 252)).ToImmutable();
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(70, 40, 50, 65)).ToImmutable(), 0.5);
    private readonly Dictionary<Color, IBrush> _brushCache = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToPixels();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnsubscribeFromPixels();
        _isPainting = false;
        _isPanning = false;
        _paintingPointer?.Capture(null);
        _paintingPointer = null;
        _brushCache.Clear();
    }

    /// <summary>ViewModel 属性变化时重新订阅（绑定可能在附加后才被替换）。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            UnsubscribeFromPixels();
            if (this.IsAttachedToVisualTree()) SubscribeToPixels();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void SubscribeToPixels()
    {
        if (ViewModel is null || _pixelsChangedHandler is not null) return;
        _observedViewModel = ViewModel;
        _pixelsChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(WMCL.App.Features.Accounts.SkinEditorViewModel.PixelsVersion))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        };
        _observedViewModel.PropertyChanged += _pixelsChangedHandler;
    }

    private void UnsubscribeFromPixels()
    {
        if (_observedViewModel is not null && _pixelsChangedHandler is not null)
            _observedViewModel.PropertyChanged -= _pixelsChangedHandler;
        _observedViewModel = null;
        _pixelsChangedHandler = null;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var vm = ViewModel;
        if (vm is null || !vm.HasSkin || vm.Width <= 0 || vm.Height <= 0) return;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var (scale, offsetX, offsetY) = CanvasTransform();
        var drawWidth = vm.Width * scale;
        var drawHeight = vm.Height * scale;
        var pixelSize = Math.Max(1, (int)Math.Floor(scale));

        // 背景
        context.FillRectangle(CanvasBackground, new Rect(offsetX, offsetY, drawWidth, drawHeight));
        var tile = scale * 4;
        for (var row = 0; row < vm.Height; row += 4)
            for (var column = 0; column < vm.Width; column += 4)
                if (((row + column) / 4) % 2 == 0)
                    context.FillRectangle(AlternateBackground, new Rect(offsetX + column * scale,
                        offsetY + row * scale, Math.Min(tile, drawWidth - column * scale),
                        Math.Min(tile, drawHeight - row * scale)));

        for (var y = 0; y < vm.Height; y++)
        {
            for (var x = 0; x < vm.Width; x++)
            {
                var color = vm.Pixels[y * vm.Width + x];
                if (color is null) continue;
                var rect = new Rect(offsetX + x * scale, offsetY + y * scale,
                    scale, scale);
                context.FillRectangle(GetBrush(color.Value), rect);
            }
        }

        // 网格线（缩放足够大时显示）
        if (pixelSize >= 4)
        {
            for (var x = 0; x <= vm.Width; x++)
                context.DrawLine(GridPen,
                    new Point(offsetX + x * scale, offsetY),
                    new Point(offsetX + x * scale, offsetY + drawHeight));
            for (var y = 0; y <= vm.Height; y++)
                context.DrawLine(GridPen,
                    new Point(offsetX, offsetY + y * scale),
                    new Point(offsetX + drawWidth, offsetY + y * scale));
        }
    }

    /// <summary>取（或缓存）指定颜色的冻结画刷。</summary>
    private IBrush GetBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            if (_brushCache.Count >= 4096) _brushCache.Clear();
            brush = new SolidColorBrush(color).ToImmutable();
            _brushCache[color] = brush;
        }
        return brush;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panOffset = new Point(PanX, PanY);
            _paintingPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!buttons.IsLeftButtonPressed || !TryGetPixel(e.GetPosition(this), out var pixelX, out var pixelY)) return;
        if (IsColorPicking || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var color = ViewModel!.Pixels[pixelY * ViewModel.Width + pixelX] ?? Colors.Transparent;
            ViewModel.SelectedColor = color;
            ViewModel.IsEraser = false;
            IsColorPicking = false;
            ColorPicked?.Invoke(this, new SkinColorPickedEventArgs(color));
            e.Handled = true;
            return;
        }
        _isPainting = true;
        _paintingPointer = e.Pointer;
        ViewModel?.BeginStroke();
        PaintAt(e);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPanning)
        {
            var delta = e.GetPosition(this) - _panStart;
            PanX = _panOffset.X + delta.X;
            PanY = _panOffset.Y + delta.Y;
            e.Handled = true;
        }
        else if (_isPainting)
            PaintAt(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPainting = false;
        _isPanning = false;
        e.Pointer.Capture(null);
        _paintingPointer = null;
    }

    /// <summary>指针捕获意外丢失（窗口失活、弹窗抢占）时结束笔画，
    /// 否则 _isPainting 滞留 true，之后鼠标掠过会无按键连续绘制。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPainting = false;
        _isPanning = false;
        _paintingPointer = null;
    }

    public void ResetView()
    {
        Zoom = 1;
        PanX = 0;
        PanY = 0;
    }

    public void ZoomAt(Point position, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0 || ViewModel is not { HasSkin: true }) return;
        var before = CanvasTransform();
        if (before.Scale <= 0) return;
        var pixelX = (position.X - before.X) / before.Scale;
        var pixelY = (position.Y - before.Y) / before.Scale;
        Zoom *= factor;
        var after = CanvasTransform();
        PanX += position.X - (after.X + pixelX * after.Scale);
        PanY += position.Y - (after.Y + pixelY * after.Scale);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.25, e.Delta.Y));
        e.Handled = true;
    }

    private (double Scale, double X, double Y) CanvasTransform()
    {
        if (ViewModel is not { Width: > 0, Height: > 0 } model) return default;
        var scale = Math.Min(Bounds.Width / model.Width, Bounds.Height / model.Height) * Zoom;
        return (scale, (Bounds.Width - model.Width * scale) / 2 + PanX,
            (Bounds.Height - model.Height * scale) / 2 + PanY);
    }

    private void PaintAt(PointerEventArgs e)
    {
        if (!TryGetPixel(e.GetPosition(this), out var x, out var y)) return;
        ViewModel!.PaintPixel(x, y);
    }

    private bool TryGetPixel(Point position, out int x, out int y)
    {
        x = y = -1;
        var vm = ViewModel;
        if (vm is null || !vm.HasSkin || vm.Width <= 0 || vm.Height <= 0 ||
            Bounds.Width <= 0 || Bounds.Height <= 0) return false;
        var (scale, offsetX, offsetY) = CanvasTransform();
        if (scale <= 0) return false;
        x = (int)Math.Floor((position.X - offsetX) / scale);
        y = (int)Math.Floor((position.Y - offsetY) / scale);
        return x >= 0 && y >= 0 && x < vm.Width && y < vm.Height;
    }
}
