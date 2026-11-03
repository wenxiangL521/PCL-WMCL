using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WMCL.App.Controls;

/// <summary>
/// A complete textured Minecraft player, projected on the CPU without a GPU scene or network access.
/// Skin and Cape are borrowed: callers own their lifetime. Yaw=0 faces forward, 180 shows the cape/back.
/// Left drag rotates yaw/pitch, right or middle drag pans, the wheel zooms, and Home resets the view.
/// </summary>
public sealed class SkinModelPreview : Control
{
    public static readonly StyledProperty<Bitmap?> SkinProperty =
        AvaloniaProperty.Register<SkinModelPreview, Bitmap?>(nameof(Skin));
    public static readonly StyledProperty<Bitmap?> CapeProperty =
        AvaloniaProperty.Register<SkinModelPreview, Bitmap?>(nameof(Cape));
    public static readonly StyledProperty<string> VariantProperty =
        AvaloniaProperty.Register<SkinModelPreview, string>(nameof(Variant), "classic");
    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<SkinModelPreview, double>(nameof(Yaw), -25,
            coerce: (_, value) => double.IsFinite(value) ? ((value % 360) + 540) % 360 - 180 : -25);

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<SkinModelPreview, double>(nameof(Pitch), 10,
            coerce: (_, value) => double.IsFinite(value) ? ((value % 360) + 540) % 360 - 180 : 10);
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<SkinModelPreview, double>(nameof(Zoom), 1,
            coerce: (_, value) => double.IsFinite(value) ? Math.Clamp(value, 0.5, 3) : 1);
    public static readonly StyledProperty<double> PanXProperty =
        AvaloniaProperty.Register<SkinModelPreview, double>(nameof(PanX), 0,
            coerce: (_, value) => double.IsFinite(value) ? value : 0);
    public static readonly StyledProperty<double> PanYProperty =
        AvaloniaProperty.Register<SkinModelPreview, double>(nameof(PanY), 0,
            coerce: (_, value) => double.IsFinite(value) ? value : 0);

    private static readonly Lazy<Bitmap?> DefaultSkin = new(LoadDefaultSkin);
    private Bitmap? _preparedSkin;
    private Bitmap? _preparedCape;
    private SkinModelRasterizer.Texture? _skinPixels;
    private SkinModelRasterizer.Texture? _capePixels;
    private SkinModelRasterizer? _rasterizer;
    private WriteableBitmap? _frame;
    private (int Width, int Height, double Yaw, double Pitch, double Zoom, double PanX, double PanY, string Variant)? _renderKey;
    private bool _hideOpaqueHat;
    private bool _dragging;
    private bool _panning;
    private Point _dragStart;
    private double _dragYaw;
    private double _dragPitch;
    private Point _dragPan;
    private IPointer? _dragPointer;

    static SkinModelPreview() => AffectsRender<SkinModelPreview>(SkinProperty, CapeProperty, VariantProperty,
        YawProperty, PitchProperty, ZoomProperty, PanXProperty, PanYProperty);

    public SkinModelPreview()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public Bitmap? Skin { get => GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
    public Bitmap? Cape { get => GetValue(CapeProperty); set => SetValue(CapeProperty, value); }
    public string Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public double Yaw { get => GetValue(YawProperty); set => SetValue(YawProperty, value); }
    public double Pitch { get => GetValue(PitchProperty); set => SetValue(PitchProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public double PanX { get => GetValue(PanXProperty); set => SetValue(PanXProperty, value); }
    public double PanY { get => GetValue(PanYProperty); set => SetValue(PanYProperty, value); }

    public void ResetView()
    {
        _dragging = false;
        _dragPointer?.Capture(null);
        _dragPointer = null;
        SetCurrentValue(YawProperty, -25);
        SetCurrentValue(PitchProperty, 10);
        SetCurrentValue(ZoomProperty, 1);
        SetCurrentValue(PanXProperty, 0);
        SetCurrentValue(PanYProperty, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SkinProperty) { _preparedSkin = null; _skinPixels = null; _renderKey = null; }
        if (change.Property == CapeProperty) { _preparedCape = null; _capePixels = null; _renderKey = null; }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Transparent hit surface keeps dragging available even in the spaces between limbs.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var skin = Skin ?? DefaultSkin.Value;
        if (skin is null) return;
        try
        {
            if (!ValidSkin(skin.PixelSize)) return;
            var legacy = skin.PixelSize.Height * 2 == skin.PixelSize.Width;
            if (!ReferenceEquals(_preparedSkin, skin))
            {
                _skinPixels = SkinModelRasterizer.Texture.Read(skin);
                _hideOpaqueHat = legacy && HasBlankOpaqueHat(skin);
                _preparedSkin = skin;
                _renderKey = null;
            }
            var cape = Cape;
            var capeValid = cape is not null && cape.PixelSize.Width is >= 64 and <= 4096 &&
                cape.PixelSize.Width % 64 == 0 && cape.PixelSize.Height * 2 == cape.PixelSize.Width;
            if (!ReferenceEquals(_preparedCape, cape))
            {
                _capePixels = capeValid ? SkinModelRasterizer.Texture.Read(cape!) : null;
                _preparedCape = cape;
                _renderKey = null;
            }
            // Honour device scaling, but bound CPU/memory cost even for a maximised editor.
            var deviceScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var resolutionScale = Math.Min(deviceScale, 768 / Math.Max(Bounds.Width, Bounds.Height));
            var width = Math.Clamp((int)Math.Ceiling(Bounds.Width * resolutionScale), 1, 768);
            var height = Math.Clamp((int)Math.Ceiling(Bounds.Height * resolutionScale), 1, 768);
            var key = (width, height, Yaw, Pitch, Zoom, PanX * resolutionScale, PanY * resolutionScale, Variant);
            if (_renderKey != key)
            {
                var faces = SkinModelGeometry.Build(string.Equals(Variant, "slim", StringComparison.OrdinalIgnoreCase), legacy, capeValid);
                if (_hideOpaqueHat) faces = faces.Where(face => face.Part != "Hat").ToArray();
                var pixels = (_rasterizer ??= new()).Render(_skinPixels!, _capePixels, faces,
                    width, height, Yaw, Pitch, Zoom, key.Item6, key.Item7);
                if (_frame is null || _frame.PixelSize.Width != width || _frame.PixelSize.Height != height)
                {
                    _frame?.Dispose();
                    _frame = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                }
                using (var buffer = _frame.Lock())
                {
                    var row = new int[width];
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++) row[x] = unchecked((int)pixels[y * width + x]);
                        Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, width);
                    }
                }
                _renderKey = key;
            }
            if (_frame is not null)
                context.DrawImage(_frame, new Rect(0, 0, width, height), new Rect(Bounds.Size));
        }
        catch (ObjectDisposedException)
        {
            // A caller replacing/discarding its borrowed bitmap must never crash the UI thread.
            _preparedSkin = null;
            _preparedCape = null;
            _renderKey = null;
        }
    }

    internal static bool ValidSkin(PixelSize size) => size.Width is >= 64 and <= 4096 && size.Width % 64 == 0 &&
        (size.Height == size.Width || size.Height * 2 == size.Width);

    private static bool HasBlankOpaqueHat(Bitmap skin)
    {
        // Classic 64x32 PNGs often used one opaque background colour in the unused hat area.
        using var pixels = new WriteableBitmap(skin.PixelSize, skin.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        skin.CopyPixels(buffer, AlphaFormat.Unpremul);
        var background = Marshal.ReadInt32(buffer.Address);
        if ((unchecked((uint)background) >> 24) != 255) return false;
        var scale = skin.PixelSize.Width / 64;
        for (var y = 0; y < 16 * scale; y++)
            for (var x = 32 * scale; x < 64 * scale; x++)
                if (Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4) != background) return false;
        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;
        var buttons = e.GetCurrentPoint(this).Properties;
        if (!buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed && !buttons.IsMiddleButtonPressed) return;
        Focus();
        _dragging = true;
        _panning = buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed;
        _dragStart = e.GetPosition(this);
        _dragYaw = Yaw;
        _dragPitch = Pitch;
        _dragPan = new Point(PanX, PanY);
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (_panning)
        {
            SetCurrentValue(PanXProperty, _dragPan.X + delta.X);
            SetCurrentValue(PanYProperty, _dragPan.Y + delta.Y);
        }
        else
        {
            SetCurrentValue(YawProperty, _dragYaw + delta.X * 0.75);
            SetCurrentValue(PitchProperty, _dragPitch - delta.Y * 0.5);
        }
        _dragStart = e.GetPosition(this);
        _dragYaw = Yaw;
        _dragPitch = Pitch;
        _dragPan = new Point(PanX, PanY);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled || e.Delta.Y == 0) return;
        SetCurrentValue(ZoomProperty, Zoom * Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        _dragPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        _dragPointer = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _dragging = false;
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _frame?.Dispose();
        _frame = null;
        _preparedSkin = null;
        _preparedCape = null;
        _skinPixels = null;
        _capePixels = null;
        _rasterizer = null;
        _renderKey = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Left: SetCurrentValue(YawProperty, Yaw - 15); break;
            case Key.Right: SetCurrentValue(YawProperty, Yaw + 15); break;
            case Key.Up: SetCurrentValue(PitchProperty, Pitch + 15); break;
            case Key.Down: SetCurrentValue(PitchProperty, Pitch - 15); break;
            case Key.Home: ResetView(); break;
            default: return;
        }
        e.Handled = true;
    }

    private static Bitmap? LoadDefaultSkin()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
            return new Bitmap(stream);
        }
        catch (IOException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
