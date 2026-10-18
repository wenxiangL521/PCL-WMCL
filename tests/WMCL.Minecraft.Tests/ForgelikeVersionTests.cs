using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public class ForgelikeVersionTests
{
    [Fact]
    public void ParsesForgeVersionsWithInstaller()
    {
        const string json = """
            [
              {"version":"47.3.0","files":[{"category":"installer","format":"jar"}]},
              {"version":"47.4.28","files":[{"category":"installer","format":"jar"}]},
              {"version":"47.2.0","files":[{"category":"changelog","format":"txt"}]}
            ]
            """;

        var versions = ForgelikeInstaller.ParseForgeVersions(json, "1.20.1");

        Assert.Equal(["47.4.28", "47.3.0"], versions);
    }

    [Fact]
    public void ParsesNeoForgeVersionsForMinecraftVersion()
    {
        const string json = """
            {
              "name":"neoforge",
              "files":[
                {"name":"21.1.80"},
                {"name":"21.1.79"},
                {"name":"20.1.1"},
                {"name":"21.1.80-beta"}
              ]
            }
            """;

        var versions = ForgelikeInstaller.ParseNeoForgeVersions(json, "1.21.1");

        Assert.Equal(["21.1.80", "21.1.80-beta", "21.1.79"], versions);
    }
}
