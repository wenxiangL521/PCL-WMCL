using WMCL.App.ViewModels.Mods;

namespace WMCL.App.Tests;

public sealed class ModSearchQueryTests
{
    [Fact]
    public void NormalizesWhitespaceAndAllLoader()
    {
        var query = ModSearchQuery.Create("  jei ", " 1.20.1 ", "全部", "Modrinth");
        Assert.Equal("jei", query.Text);
        Assert.Equal("1.20.1", query.GameVersion);
        Assert.Equal("", query.Loader);
        Assert.False(query.IsCurseForge);
    }

    [Fact]
    public void DetectsCurseForgeSource()
    {
        Assert.True(ModSearchQuery.Create(null, null, "fabric", "CurseForge").IsCurseForge);
        Assert.Equal("Modrinth", ModSearchQuery.Create(null, null, null, null).Source);
    }

    [Theory]
    [InlineData(0, false, "没有找到匹配的资源（Modrinth）")]
    [InlineData(20, true, "Modrinth：已显示 20 个，可继续加载")]
    [InlineData(20, false, "Modrinth：共 20 个")]
    public void DescribesResultState(int count, bool hasMore, string expected)
    {
        var query = ModSearchQuery.Create(null, null, null, "Modrinth");
        Assert.Equal(expected, query.DescribeResult(count, hasMore));
    }
}
