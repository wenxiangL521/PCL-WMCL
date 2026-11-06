using Avalonia;
using Avalonia.Platform;
using Avalonia.Media.Imaging;

namespace WMCL.App.Services;

/// <summary>
/// 托盘图标像素转换。Avalonia 12 的 DBusTrayIconImpl 期望
/// <c>[宽, 高, ARGB, ...]</c> 形式的数组；原生 Wayland 后端没有平台注入的转换器，
/// 这里统一从内置资源生成，X11/Windows 不需要使用。
/// </summary>
internal static class TrayIconImage
{
    internal const string AssetUri = "avares://WMCL.App/Assets/wmcl-tray.png";

    public static uint[] Load()
    {
        using var stream = AssetLoader.Open(new Uri(AssetUri));
        return Convert(stream);
    }

    internal static uint[] Convert(Stream png)
    {
        using var bitmap = new Bitmap(png);
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0) return [];
        var bytes = new byte[width * height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(), bytes.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        var pixels = new uint[2 + width * height];
        pixels[0] = (uint)width;
        pixels[1] = (uint)height;
        for (var index = 0; index < width * height; index++)
        {
            var offset = index * 4;
            pixels[2 + index] = (uint)((bytes[offset + 3] << 24) | (bytes[offset + 2] << 16)
                | (bytes[offset + 1] << 8) | bytes[offset]);
        }
        return pixels;
    }
}
