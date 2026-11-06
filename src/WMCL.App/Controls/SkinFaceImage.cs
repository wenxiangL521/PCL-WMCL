using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace WMCL.App.Controls;

public sealed class SkinFaceImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<SkinFaceImage, Bitmap?>(nameof(Source));

    private static readonly Bitmap? DefaultSkin = LoadDefaultSkin();
    private Bitmap? _preparedSkin;
    private bool _drawOverlay;

    static SkinFaceImage() => AffectsRender<SkinFaceImage>(SourceProperty);

    public SkinFaceImage() =>
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var skin = Source ?? DefaultSkin;
        if (skin is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (skin.PixelSize.Width < 64 || skin.PixelSize.Height < 32) return;
        if (!ReferenceEquals(_preparedSkin, skin))
        {
            _drawOverlay = HasHeadOverlay(skin);
            _preparedSkin = skin;
        }

        var scale = skin.PixelSize.Width / 64d;
        var face = new Rect(8 * scale, 8 * scale, 8 * scale, 8 * scale);
        var hair = new Rect(40 * scale, 8 * scale, 8 * scale, 8 * scale);
        var faceWidth = Bounds.Width * 48 / 56;
        var faceHeight = Bounds.Height * 48 / 56;
        var faceDestination = new Rect((Bounds.Width - faceWidth) / 2,
            (Bounds.Height - faceHeight) / 2, faceWidth, faceHeight);
        context.DrawImage(skin, face, faceDestination);
        if (_drawOverlay)
            context.DrawImage(skin, hair, new Rect(0, 0, Bounds.Width, Bounds.Height));
    }

    private static bool HasHeadOverlay(Bitmap skin)
    {
        using var pixels = new WriteableBitmap(skin.PixelSize, skin.Dpi,
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        skin.CopyPixels(buffer, AlphaFormat.Unpremul);
        var width = skin.PixelSize.Width;
        var height = skin.PixelSize.Height;
        var scale = width / 64;
        var backgroundTop = ReadPixel(buffer, 1, 1);
        var backgroundBottom = ReadPixel(buffer, width - 1, height - 1);
        var backgroundMiddle = ReadPixel(buffer, width - 2, height / 2 - 2);
        var overlay = ReadPixel(buffer, scale * 41, scale * 9);
        return (backgroundTop >> 24) == 0 || (backgroundBottom >> 24) == 0 ||
               (backgroundMiddle >> 24) == 0 ||
               (backgroundTop != overlay && backgroundBottom != overlay && backgroundMiddle != overlay);
    }

    private static uint ReadPixel(ILockedFramebuffer buffer, int column, int row) =>
        unchecked((uint)Marshal.ReadInt32(buffer.Address, row * buffer.RowBytes + column * 4));

    private static Bitmap? LoadDefaultSkin()
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
