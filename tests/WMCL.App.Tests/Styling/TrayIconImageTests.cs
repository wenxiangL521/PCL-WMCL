using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class TrayIconImageTests
{
    [AvaloniaFact]
    public void PixelsStartWithSizeHeaderAndMatchTheAsset()
    {
        var pixels = TrayIconImage.Load();
        Assert.True(pixels.Length > 2);
        var width = (int)pixels[0];
        var height = (int)pixels[1];
        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Equal(2 + width * height, pixels.Length);
        // 图标不透明像素应存在（不是全透明）
        Assert.Contains(pixels.Skip(2), pixel => (pixel >> 24) != 0);
    }
}
