using PCL.Modding.Modpack;

namespace PCL.Minecraft.Tests;

public class CurseForgeModpackTests
{
    [Fact]
    public void ParsesManifestAndPrimaryLoader()
    {
        var manifest = CurseForgeModpackInstaller.ParseManifest("""
        {
          "minecraft": {"version": "1.20.1", "modLoaders": [{"id": "forge-47.2.18", "primary": true}]},
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "Test Pack",
          "version": "1.2.3",
          "files": [
            {"projectID": 238222, "fileID": 5000000, "required": true},
            {"projectID": 111, "fileID": 222, "required": false}
          ],
          "overrides": "overrides"
        }
        """);

        Assert.Equal("Test Pack", manifest.Name);
        Assert.Equal("1.20.1", manifest.Minecraft);
        Assert.Equal("forge-47.2.18", manifest.LoaderId);
        Assert.Equal("overrides", manifest.Overrides);
        Assert.Equal(2, manifest.Files.Count);
        Assert.True(manifest.Files[0].Required);
        Assert.False(manifest.Files[1].Required);
    }
}
