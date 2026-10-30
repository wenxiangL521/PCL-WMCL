using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WMCL.App.Controls;

/// <summary>皮肤像素编辑器画布。按像素渲染，支持鼠标/触摸绘制。</summary>
public sealed class SkinPixelEditor : Control
{
    public static readonly StyledProperty<ViewModels.SkinEditorViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<SkinPixelEditor, ViewModels.SkinEditorViewModel?>(
            nameof(ViewModel));

    static SkinPixelEditor()
    {
        AffectsRender<SkinPixelEditor>(ViewModelProperty);
    }

    public ViewModels.SkinEditorViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private bool _isPainting;
    private PropertyChangedEventHandler? _pixelsChangedHandler;

    // 渲染资源缓存：Render 每次重绘都对每个非空像素取画刷，
    // 逐像素 new SolidColorBrush 在长笔画下每帧分配数千个对象。
    private static readonly IBrush CanvasBackground = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly Pen GridPen = new(Brushes.White, 0.5);
    private readonly Dictionary<Color, IBrush> _brushCache = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToPixels();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel is not null && _pixelsChangedHandler is not null)
            ViewModel.PropertyChanged -= _pixelsChangedHandler;
        _pixelsChangedHandler = null;
    }

    /// <summary>ViewModel 属性变化时重新订阅（绑定可能在附加后才被替换）。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            // 从旧 ViewModel 退订（替换后 ViewModel 已是新实例）。
            if (change.OldValue is ViewModels.SkinEditorViewModel old && _pixelsChangedHandler is not null)
                old.PropertyChanged -= _pixelsChangedHandler;
            _pixelsChangedHandler = null;
            SubscribeToPixels();
            InvalidateVisual();
        }
    }

    private void SubscribeToPixels()
    {
        if (ViewModel is null || _pixelsChangedHandler is not null) return;
        _pixelsChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModels.SkinEditorViewModel.PixelsVersion))
                InvalidateVisual();
        };
        ViewModel.PropertyChanged += _pixelsChangedHandler;
    }

    public override void Render(DrawingContext context)
    {
        var vm = ViewModel;
        if (vm is null || !vm.HasSkin || vm.Width <= 0 || vm.Height <= 0) return;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var scale = Math.Min(Bounds.Width / vm.Width, Bounds.Height / vm.Height);
        var drawWidth = vm.Width * scale;
        var drawHeight = vm.Height * scale;
        var offsetX = (Bounds.Width - drawWidth) / 2;
        var offsetY = (Bounds.Height - drawHeight) / 2;
        var pixelSize = Math.Max(1, (int)Math.Floor(scale));

        // 背景
        context.FillRectangle(CanvasBackground, new Rect(offsetX, offsetY, drawWidth, drawHeight));

        for (var y = 0; y < vm.Height; y++)
        {
            for (var x = 0; x < vm.Width; x++)
            {
                var color = vm.Pixels[y * vm.Width + x];
                if (color is null) continue;
                var rect = new Rect(offsetX + x * scale, offsetY + y * scale,
                    Math.Ceiling(scale), Math.Ceiling(scale));
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
            brush = new SolidColorBrush(color).ToImmutable();
            _brushCache[color] = brush;
        }
        return brush;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isPainting = true;
        ViewModel?.BeginStroke();
        PaintAt(e);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPainting)
            PaintAt(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPainting = false;
    }

    /// <summary>指针捕获意外丢失（窗口失活、弹窗抢占）时结束笔画，
    /// 否则 _isPainting 滞留 true，之后鼠标掠过会无按键连续绘制。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPainting = false;
    }

    private void PaintAt(PointerEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || !vm.HasSkin) return;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var position = e.GetPosition(this);
        var scale = Math.Min(Bounds.Width / vm.Width, Bounds.Height / vm.Height);
        if (scale <= 0) return;
        var offsetX = (Bounds.Width - vm.Width * scale) / 2;
        var offsetY = (Bounds.Height - vm.Height * scale) / 2;
        var x = (int)((position.X - offsetX) / scale);
        var y = (int)((position.Y - offsetY) / scale);
        vm.PaintPixel(x, y);
        InvalidateVisual();
    }
}
