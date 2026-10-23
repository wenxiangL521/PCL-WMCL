using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

public class IsolationTests
{
    private const string Root = "/tmp/wmcl-isolation-test";

    [Fact]
    public void DetectsFabricAsModable()
    {
        var version = new VersionJson
        {
            MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
            InheritsFrom = "1.21.1"
        };

        Assert.True(MinecraftVersionUtil.IsModable(version));
        Assert.Equal("fabric", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("1.21.1", MinecraftVersionUtil.GetBaseGameVersion("fabric-x", version));
        Assert.Equal(
            Path.Combine(Root, "versions", "fabric-x"),
            MinecraftVersionUtil.GetGameDirectory(Root, "fabric-x", version));
    }

    [Fact]
    public void VanillaUsesRootDirectory()
    {
        var version = new VersionJson
        {
            MainClass = "net.minecraft.client.main.Main"
        };

        Assert.False(MinecraftVersionUtil.IsModable(version));
        Assert.Equal("vanilla", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("1.21.1", MinecraftVersionUtil.GetBaseGameVersion("1.21.1", version));
        Assert.Equal(Root, MinecraftVersionUtil.GetGameDirectory(Root, "1.21.1", version));
    }

    [Fact]
    public void DetectsOptiFineAsModable()
    {
        var version = new VersionJson
        {
            MainClass = "net.minecraft.launchwrapper.Launch",
            Libraries = [new Library { Name = "optifine:OptiFine:1.20.1_HD_U_I6" }]
        };

        Assert.True(MinecraftVersionUtil.IsModable(version));
        Assert.Equal("optifine", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(
            Path.Combine(Root, "versions", "1.20.1-OptiFine"),
            MinecraftVersionUtil.GetGameDirectory(Root, "1.20.1-OptiFine", version));
    }

    [Fact]
    public void DetectsForgeByLibraries()
    {
        var version = new VersionJson
        {
            MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher",
            Libraries =
            [
                new Library { Name = "net.minecraftforge:fmlloader:1.20.1-47.2.0" }
            ]
        };

        Assert.True(MinecraftVersionUtil.IsModable(version));
        Assert.Equal("forge", MinecraftVersionUtil.GetLoader(version));
    }

    [Theory]
    [InlineData("net.fabricmc:fabric-loader:0.16.10", "fabric")]
    [InlineData("net.minecraftforge:forge:1.20.1-47.2.18", "forge")]
    [InlineData("org.quiltmc:quilt-loader:0.28.0", "quilt")]
    [InlineData("net.neoforged:neoforge:21.1.0", "neoforge")]
    public void OptiFineDoesNotReplaceTheBaseLoader(string loaderLibrary, string loader)
    {
        var version = new VersionJson
        {
            Libraries =
            [
                new Library { Name = "optifine:OptiFine:1.20.1_HD_U_I6" },
                new Library { Name = loaderLibrary }
            ]
        };

        Assert.Equal(loader, MinecraftVersionUtil.GetLoader(version));
    }
}

public class IsolationPolicyTests
{
    [Fact]
    public void ResolvesInstanceOverrideAndGlobalFallback()
    {
        Assert.Equal(IsolationPolicy.Isolated,
            IsolationPolicy.Resolve("isolated", "global"));
        Assert.Equal(IsolationPolicy.Global,
            IsolationPolicy.Resolve("", "global"));
        Assert.Equal(IsolationPolicy.Auto,
            IsolationPolicy.Resolve("", "auto"));
    }

    [Fact]
    public void ExplicitModesOverrideModableDetection()
    {
        var vanilla = new WMCL.Minecraft.Models.VersionJson
        {
            Id = "1.20.1",
            MainClass = "net.minecraft.client.main.Main"
        };
        var modable = new WMCL.Minecraft.Models.VersionJson
        {
            Id = "1.20.1-forge",
            MainClass = "net.minecraftforge.bootstrap.ForgeBootstrap"
        };
        var root = Path.Combine(Path.GetTempPath(), "wmcl-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(Path.Combine(root, "versions", "1.20.1"),
                MinecraftVersionUtil.GetGameDirectory(root, "1.20.1", vanilla, IsolationPolicy.Isolated));
            Assert.Equal(root,
                MinecraftVersionUtil.GetGameDirectory(root, "1.20.1-forge", modable, IsolationPolicy.Global));
            Assert.Equal(root,
                MinecraftVersionUtil.GetGameDirectory(root, "1.20.1", vanilla, IsolationPolicy.Auto));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
