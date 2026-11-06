using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ClipboardServiceTests
{
    [Fact]
    public void EmptyTextIsRejectedWithoutTouchingClipboard()
    {
        Assert.False(ClipboardService.TrySetText(""));
        Assert.False(ClipboardService.TrySetText(null));
    }

    [AvaloniaFact]
    public async Task MissingMainWindowFailsGracefully()
    {
        // 无头测试里没有主窗口：应该返回 false 而不是抛异常
        Assert.False(ClipboardService.TrySetText("hello"));
        Assert.False(await ClipboardService.SetTextAsync("hello"));
    }
}
