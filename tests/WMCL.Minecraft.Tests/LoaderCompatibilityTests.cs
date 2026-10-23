using WMCL.Modding.Loaders;

namespace WMCL.Minecraft.Tests;

public class LoaderCompatibilityTests
{
    [Fact]
    public void RejectsNeoForgeWithAnyOtherLoader()
    {
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.NeoForge, LoaderFamily.OptiFine], "1.21.1"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.NeoForge, LoaderFamily.Forge], "1.20.1"));
    }

    [Fact]
    public void RejectsForgeAndFabric()
    {
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.Fabric], "1.20.1"));
    }

    [Fact]
    public void AllowsForgeOptiFineOnlyWithMatchingForge()
    {
        Assert.Null(LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.OptiFine], "1.20.1", "47.2.18", "47.2.18"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.OptiFine], "1.20.1", "47.2.18", "47.3.0"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData("47.2.18", true)]
    [InlineData("47.3.0", false)]
    public void ForgeCompatibilityDistinguishesExplicitIncompatibilityFromAnUnrestrictedVersion(string? required, bool allowed)
    {
        var error = LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.OptiFine], "1.20.1", required, "47.2.18");
        Assert.Equal(allowed, error is null);
    }

    [Theory]
    [InlineData("2318", "11.15.1.2318", true)]
    [InlineData("2318", "11.15.1.2319", false)]
    [InlineData("18", "47.2.18", false)]
    [InlineData("47.2.18", "47.2.18", true)]
    [InlineData("47.2.18", "47.2.19", false)]
    [InlineData("invalid", "11.15.1.2318", false)]
    public void ShortForgeRequirementsMatchTheOriginalRevisionField(string required, string selected, bool allowed) =>
        Assert.Equal(allowed, LoaderCompatibility.ForgeOptiFineMatch(required, selected));

    [Fact]
    public void AllowsFabricOptiFineOnlyUntil1_20_4()
    {
        Assert.Null(LoaderCompatibility.Validate(
            [LoaderFamily.Fabric, LoaderFamily.OptiFine], "1.20.4"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Fabric, LoaderFamily.OptiFine], "1.20.5"));
    }

    [Fact]
    public void ValidSingleLoadersPass()
    {
        Assert.Null(LoaderCompatibility.Validate([LoaderFamily.Fabric], "1.21.1"));
        Assert.Null(LoaderCompatibility.Validate([LoaderFamily.Forge], "1.20.1"));
        Assert.Null(LoaderCompatibility.Validate([LoaderFamily.OptiFine], "1.21.1"));
    }

    [Fact]
    public void RejectsQuiltWithAnyOtherLoader()
    {
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Quilt, LoaderFamily.Forge], "1.20.1"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Quilt, LoaderFamily.Fabric], "1.20.1"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Quilt, LoaderFamily.OptiFine], "1.20.1"));
        Assert.Null(LoaderCompatibility.Validate(
            [LoaderFamily.Quilt], "1.20.1"));
    }

    [Fact]
    public void RejectsLegacyFabricAbove1_13_2()
    {
        Assert.Null(LoaderCompatibility.Validate(
            [LoaderFamily.LegacyFabric], "1.12.2"));
        Assert.Null(LoaderCompatibility.Validate(
            [LoaderFamily.LegacyFabric], "1.13.2"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.LegacyFabric], "1.14.4"));
    }

    [Fact]
    public void RejectsLegacyFabricWithAnyOtherLoader()
    {
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.LegacyFabric, LoaderFamily.Forge], "1.12.2"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.LegacyFabric, LoaderFamily.Fabric], "1.12.2"));
    }

    [Fact]
    public void RejectsForgeWithQuiltAndLegacyFabric()
    {
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.Quilt], "1.20.1"));
        Assert.NotNull(LoaderCompatibility.Validate(
            [LoaderFamily.Forge, LoaderFamily.LegacyFabric], "1.12.2"));
    }
}
