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
}
