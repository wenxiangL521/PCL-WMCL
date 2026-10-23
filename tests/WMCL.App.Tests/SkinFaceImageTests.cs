using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

public sealed class SkinFaceImageTests
{
    [AvaloniaTheory]
    [InlineData(64, 64, 96)]
    [InlineData(64, 32, 96)]
    [InlineData(128, 64, 192)]
    public void OpaqueLegacyBackgroundDoesNotCoverFace(int width, int height, int dpi)
    {
        using var skin = CreateSkin(width, height, dpi, 0xffffffff, 0xffffffff);
        using var rendered = Render(skin);
        Assert.Equal(0xffff0000u, Pixel(rendered, 28, 28));
        Assert.Equal(0u, Pixel(rendered, 0, 0) >> 24);
        Assert.Equal(0xffff0000u, Pixel(rendered, 4, 4));
        Assert.Equal(0u, Pixel(rendered, 3, 3) >> 24);
    }

    [AvaloniaTheory]
    [InlineData(0x00000000u)]
    [InlineData(0xffffffffu)]
    public void RealOverlayUsesLargerHeadLayer(uint background)
    {
        using var skin = CreateSkin(64, 64, 96, background, 0xff00ff00);
        using var rendered = Render(skin);
        Assert.Equal(0xff00ff00u, Pixel(rendered, 0, 0));
        Assert.Equal(0xff00ff00u, Pixel(rendered, 28, 28));
        Assert.Equal(0xff00ff00u, Pixel(rendered, 55, 55));
    }

    [AvaloniaFact]
    public void FacePixelsStaySharpAtTheirOriginalSixPixelScale()
    {
        using var skin = CreateSkin(64, 64, 96, 0, 0);
        using (var buffer = skin.Lock())
            Marshal.WriteInt32(buffer.Address, 8 * buffer.RowBytes + 8 * 4, unchecked((int)0xff0000ffu));
        using var rendered = Render(skin);
        Assert.Equal(0xff0000ffu, Pixel(rendered, 4, 4));
        Assert.Equal(0xff0000ffu, Pixel(rendered, 9, 9));
        Assert.Equal(0xffff0000u, Pixel(rendered, 10, 9));
        Assert.Equal(0xffff0000u, Pixel(rendered, 9, 10));
    }

    [AvaloniaFact]
    public void ReplacingSkinReevaluatesOverlayAndNullRestoresSteveFace()
    {
        using var legacy = CreateSkin(64, 32, 96, 0xffffffff, 0xffffffff);
        using var modern = CreateSkin(64, 64, 96, 0, 0xff00ff00);
        var control = new SkinFaceImage { Source = legacy };
        using var initial = RenderControl(control);
        Assert.Equal(0xffff0000u, Pixel(initial, 28, 28));
        control.Source = modern;
        using var changed = RenderControl(control);
        Assert.Equal(0xff00ff00u, Pixel(changed, 28, 28));
        control.Source = null;
        using var fallback = RenderControl(control);
        Assert.NotEqual(0u, Pixel(fallback, 28, 28) >> 24);
        Assert.NotEqual(0xff00ff00u, Pixel(fallback, 28, 28));
    }

    private static WriteableBitmap CreateSkin(int width, int height, int dpi, uint background, uint overlay)
    {
        var skin = new WriteableBitmap(new PixelSize(width, height), new Vector(dpi, dpi),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var pixels = Enumerable.Repeat(unchecked((int)background), width * height).ToArray();
        var scale = width / 64;
        for (var row = 8 * scale; row < 16 * scale; row++)
        {
            for (var column = 8 * scale; column < 16 * scale; column++)
                pixels[row * width + column] = unchecked((int)0xffff0000u);
            for (var column = 40 * scale; column < 48 * scale; column++)
                pixels[row * width + column] = unchecked((int)overlay);
        }
        using var buffer = skin.Lock();
        for (var row = 0; row < height; row++)
            Marshal.Copy(pixels, row * width, buffer.Address + row * buffer.RowBytes, width);
        return skin;
    }

    private static RenderTargetBitmap Render(Bitmap skin) =>
        RenderControl(new SkinFaceImage { Source = skin });

    private static RenderTargetBitmap RenderControl(SkinFaceImage control)
    {
        control.Measure(new Size(56, 56));
        control.Arrange(new Rect(0, 0, 56, 56));
        var rendered = new RenderTargetBitmap(new PixelSize(56, 56), new Vector(96, 96));
        rendered.Render(control);
        return rendered;
    }

    private static uint Pixel(Bitmap bitmap, int column, int row)
    {
        using var pixels = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi,
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        bitmap.CopyPixels(buffer, AlphaFormat.Unpremul);
        return unchecked((uint)Marshal.ReadInt32(buffer.Address, row * buffer.RowBytes + column * 4));
    }
}
