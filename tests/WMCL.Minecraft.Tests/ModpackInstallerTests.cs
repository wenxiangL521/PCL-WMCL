using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public class ModpackInstallerTests
{
    [Fact]
    public void ParsesFabricModpackIndex()
    {
        const string json = """
            {
              "formatVersion": 1,
              "game": "minecraft",
              "versionId": "1.2.3",
              "name": "Test Pack",
              "files": [
                {"path":"mods/a.jar","hashes":{"sha1":"0123456789abcdef0123456789abcdef01234567"},"downloads":["https://cdn.example/a.jar"],"env":{"client":"required"}},
                {"path":"mods/b.jar","downloads":[],"env":{"client":"unsupported"}}
              ],
              "dependencies": {"minecraft":"1.21.1","fabric-loader":"0.19.5"}
            }
            """;

        var index = ModpackInstaller.ParseIndex(json);

        Assert.Equal("Test Pack", index.Name);
        Assert.Equal("1.2.3", index.VersionId);
        Assert.Equal("1.21.1", index.Minecraft);
        Assert.Equal("0.19.5", index.FabricLoader);
        Assert.Null(index.Forge);
        Assert.Equal(2, index.Files.Count);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", index.Files[0].Sha1);
        Assert.Equal("unsupported", index.Files[1].EnvironmentClient);
    }

    [Fact]
    public void RejectsMissingMinecraftDependency()
    {
        Assert.Throws<InvalidDataException>(
            () => ModpackInstaller.ParseIndex("""{"files":[]}"""));
    }
}
