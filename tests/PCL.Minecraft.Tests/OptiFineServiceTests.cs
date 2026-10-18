using PCL.Modding.OptiFine;

namespace PCL.Minecraft.Tests;

public class OptiFineServiceTests
{
    [Fact]
    public void ParsesAndFiltersOptiFineVersions()
    {
        const string json = """
            [
              {"mcversion":"1.20.1","type":"HD_U","patch":"I6","filename":"OptiFine_1.20.1_HD_U_I6.jar","forge":"Forge 47.2.18"},
              {"mcversion":"1.20.1","type":"HD_U_I6","patch":"pre1","filename":"preview_OptiFine_1.20.1_HD_U_I6_pre1.jar","forge":"Forge 47.1.0"},
              {"mcversion":"1.21.1","type":"HD_U","patch":"J1","filename":"OptiFine_1.21.1_HD_U_J1.jar","forge":"Forge N/A"}
            ]
            """;

        var versions = OptiFineService.ParseVersions(json, "1.20.1");

        Assert.Equal(2, versions.Count);
        Assert.Equal("I6", versions[0].Patch);
        Assert.False(versions[0].IsPreview);
        Assert.Equal("47.2.18", versions[0].ForgeVersion);
        Assert.Equal("pre1", versions[1].Patch);
        Assert.True(versions[1].IsPreview);
    }
}
