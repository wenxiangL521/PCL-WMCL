using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class MinecraftMotdTests
{
    [Fact]
    public void ParsesColorsAndFormatting()
    {
        var runs = MinecraftMotd.Parse("§aHello §lWorld§r!");
        Assert.Equal("Hello World!", MinecraftMotd.ToPlainText(runs));
        Assert.Equal("#55FF55", runs[0].Color);
        Assert.False(runs[0].Bold);
        Assert.Equal("World", runs[1].Text);
        Assert.True(runs[1].Bold);
        Assert.Null(runs[2].Color);
    }

    [Fact]
    public void EmptyTextReturnsEmpty()
    {
        Assert.Empty(MinecraftMotd.Parse(""));
    }
}
