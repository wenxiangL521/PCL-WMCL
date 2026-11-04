using WMCL.App.ViewModels.Instances;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class IsolationLabelTests
{
    [Theory]
    [InlineData(IsolationPolicy.Isolated, "隔离")]
    [InlineData(IsolationPolicy.Global, "不隔离")]
    [InlineData("", "跟随全局")]
    [InlineData(null, "跟随全局")]
    public void MapsModeToLabel(string? mode, string expected)
    {
        Assert.Equal(expected, IsolationLabel.ToLabel(mode));
    }

    [Theory]
    [InlineData("隔离", IsolationPolicy.Isolated)]
    [InlineData("不隔离", IsolationPolicy.Global)]
    [InlineData("跟随全局", "")]
    public void MapsLabelToMode(string label, string expected)
    {
        Assert.Equal(expected, IsolationLabel.ToMode(label));
    }

    [Fact]
    public void RoundTripsKnownModes()
    {
        foreach (var mode in new[] { IsolationPolicy.Isolated, IsolationPolicy.Global })
            Assert.Equal(mode, IsolationLabel.ToMode(IsolationLabel.ToLabel(mode)));
    }
}
