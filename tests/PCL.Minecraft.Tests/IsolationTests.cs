using PCL.Minecraft;
using PCL.Minecraft.Models;

namespace PCL.Minecraft.Tests;

public class IsolationTests
{
    private const string Root = "/tmp/pcl-isolation-test";

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
}
