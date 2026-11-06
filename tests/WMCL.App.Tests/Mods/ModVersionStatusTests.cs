using WMCL.App.Features.Mods;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class ModVersionStatusTests
{
    [Theory]
    [InlineData(0, "全部", "当前筛选没有版本")]
    [InlineData(3, "全部", "共 3 个版本")]
    [InlineData(3, "", "共 3 个版本")]
    [InlineData(3, "1.20.1", "共 3 个版本（筛选：1.20.1）")]
    public void DescribesVersionCount(int count, string filter, string expected)
    {
        Assert.Equal(expected, ModVersionStatus.Describe(count, filter));
    }
}
