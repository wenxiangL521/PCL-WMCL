using WMCL.App.Features.Mods;
using WMCL.Modding.Local;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class LocalModFilterTests
{
    private static LocalMod Mod(string name, string file) =>
        new("/tmp/" + file, file, name, true, 1);

    [Fact]
    public void EmptyFilterKeepsEverything()
    {
        var mods = new[] { Mod("Sodium", "sodium.jar"), Mod("Iris", "iris.jar") };
        Assert.Equal(2, LocalModFilter.Apply(mods, "  ").Count);
    }

    [Fact]
    public void MatchesNameAndFileNameCaseInsensitively()
    {
        var mods = new[] { Mod("Sodium", "sodium.jar"), Mod("Iris", "iris.jar") };
        Assert.Single(LocalModFilter.Apply(mods, "sod"));
        Assert.Single(LocalModFilter.Apply(mods, "IRIS.JAR"));
        Assert.Empty(LocalModFilter.Apply(mods, "forge"));
    }

    [Theory]
    [InlineData(0, 0, 0, "该实例 mods 目录为空")]
    [InlineData(5, 5, 3, "5 个 Mod，已启用 3 个")]
    [InlineData(5, 2, 1, "显示 2/5 个 Mod，已启用 1 个")]
    public void DescribeFormatsStatusLine(int total, int shown, int enabled, string expected)
    {
        Assert.Equal(expected, LocalModFilter.Describe(total, shown, enabled));
    }
}
