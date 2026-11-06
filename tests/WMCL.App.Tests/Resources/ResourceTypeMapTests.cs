using WMCL.App.ViewModels.Content;
using WMCL.Modding.CurseForge;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class ResourceTypeMapTests
{
    [Theory]
    [InlineData("光影", CurseForgeClient.ClassIdShaders)]
    [InlineData("数据包", CurseForgeClient.ClassIdDataPacks)]
    [InlineData("世界", CurseForgeClient.ClassIdWorlds)]
    [InlineData("插件", CurseForgeClient.ClassIdPlugins)]
    [InlineData("整合包", CurseForgeClient.ClassIdModpacks)]
    [InlineData("资源包", CurseForgeClient.ClassIdResourcePacks)]
    [InlineData(null, CurseForgeClient.ClassIdResourcePacks)]
    public void MapsCurseForgeClassIds(string? type, int expected)
    {
        Assert.Equal(expected, ResourceTypeMap.CurseForgeClassId(type));
    }

    [Theory]
    [InlineData("光影", "shader")]
    [InlineData("数据包", "datapack")]
    [InlineData("整合包", "modpack")]
    [InlineData("Mod", "mod")]
    [InlineData("资源包", "resourcepack")]
    [InlineData("世界", "resourcepack")]
    public void MapsModrinthProjectTypes(string type, string expected)
    {
        Assert.Equal(expected, ResourceTypeMap.ModrinthProjectType(type));
    }
}
