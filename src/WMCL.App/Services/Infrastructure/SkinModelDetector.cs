using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WMCL.App.Services;

/// <summary>
/// Detects the modern three-pixel arm layout from its unused transparent atlas strips.
/// This is a heuristic, not a replacement for an explicit user-selected model.
/// </summary>
internal static class SkinModelDetector
{
    internal static string Detect(Bitmap skin)
    {
        var size = skin.PixelSize;
        if (size.Width < 64 || size.Width > 4096 || size.Width % 64 != 0 || size.Height != size.Width)
            return "classic";
        var scale = size.Width / 64;
        using var pixels = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        skin.CopyPixels(buffer, AlphaFormat.Unpremul);
        // A slim skin has unused space after the three-pixel arm top/back rectangles
        // in both modern arms. Classic arm textures occupy these exact texels.
        (int X, int Y, int Width, int Height)[] unused = [(50, 16, 2, 4), (54, 20, 2, 12), (42, 48, 2, 4), (46, 52, 2, 12)];
        foreach (var region in unused)
            for (var y = region.Y * scale; y < (region.Y + region.Height) * scale; y++)
                for (var x = region.X * scale; x < (region.X + region.Width) * scale; x++)
                    if (Marshal.ReadByte(buffer.Address, y * buffer.RowBytes + x * 4 + 3) != 0)
                        return "classic";
        return "slim";
    }

    internal static string Resolve(Bitmap skin, string selection) => selection switch
    {
        "classic" => "classic",
        "slim" => "slim",
        "auto" => Detect(skin),
        _ => throw new InvalidDataException("皮肤模型必须为自动识别、classic 或 slim。")
    };
}
