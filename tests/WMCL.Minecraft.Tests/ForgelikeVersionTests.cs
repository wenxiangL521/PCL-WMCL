using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public class ForgelikeVersionTests
{
    [Fact]
    public void ForgeDetailsKeepInstallerFilteringOrderingAndTimestampOffsets()
    {
        const string json = """
            [
              {"version":"1.20.1-47.3.0","modified":"2024-05-01T12:34:00+08:00","files":[{"category":"installer","format":"jar"}]},
              {"version":"47.4.28","modified":"2025-07-01T03:04:00Z","files":[{"category":"installer","format":"jar"}]},
              {"version":"47.4.28","modified":"bad","files":[{"category":"installer","format":"jar"}]},
              {"version":"47.2.0","modified":"2024-01-01T00:00:00Z","files":[{"category":"changelog","format":"txt"}]}
            ]
            """;
        var versions = ForgelikeInstaller.ParseForgeVersionDetails(json, "1.20.1");
        Assert.Equal(["47.4.28", "47.3.0"], versions.Select(version => version.Version));
        Assert.Equal(new DateTimeOffset(2025, 7, 1, 3, 4, 0, TimeSpan.Zero), versions[0].Published);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 12, 34, 0, TimeSpan.FromHours(8)), versions[1].Published);
        Assert.Equal(TimeSpan.FromHours(8), versions[1].Published!.Value.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"modified\":null")]
    [InlineData(",\"modified\":42")]
    [InlineData(",\"modified\":\"invalid\"")]
    public void UnknownForgeDatesDoNotDiscardInstallableVersions(string modified)
    {
        var json = "[{\"version\":\"47.3.0\",\"files\":[{\"category\":\"installer\",\"format\":\"jar\"}]" + modified + "}]";
        var version = Assert.Single(ForgelikeInstaller.ParseForgeVersionDetails(json, "1.20.1"));
        Assert.Equal("47.3.0", version.Version);
        Assert.Null(version.Published);
    }

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
