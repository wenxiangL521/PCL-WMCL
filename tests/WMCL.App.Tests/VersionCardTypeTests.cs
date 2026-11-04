using WMCL.App.ViewModels.Instances;

namespace WMCL.App.Tests;

public sealed class VersionCardTypeTests
{
    [Fact]
    public void HasSixOptions()
    {
        Assert.Equal(6, VersionCardType.Options.Length);
        Assert.Equal("自动", VersionCardType.Options[0]);
    }

    [Theory]
    [InlineData(-1, "自动")]
    [InlineData(0, "自动")]
    [InlineData(5, "愚人节版本")]
    [InlineData(99, "愚人节版本")]
    public void LabelsClampOutOfRange(int index, string expected)
    {
        Assert.Equal(expected, VersionCardType.Label(index));
    }

    [Theory]
    [InlineData("常规版本", 3)]
    [InlineData("不存在的类型", 0)]
    [InlineData(null, 0)]
    public void IndexOfFallsBackToAuto(string? label, int expected)
    {
        Assert.Equal(expected, VersionCardType.IndexOf(label));
    }

    [Fact]
    public void RoundTripsEveryOption()
    {
        for (var i = 0; i < VersionCardType.Options.Length; i++)
            Assert.Equal(i, VersionCardType.IndexOf(VersionCardType.Label(i)));
    }
}
