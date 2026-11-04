using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.ViewModels.Content;

/// <summary>实例资源类型到各平台分类的映射（从 InstanceContentViewModel 抽出）。</summary>
public static class ResourceTypeMap
{
    /// <summary>CurseForge 的 classId；未知类型按资源包处理。</summary>
    public static int CurseForgeClassId(string? type) => type switch
    {
        "光影" => CurseForgeClient.ClassIdShaders,
        "数据包" => CurseForgeClient.ClassIdDataPacks,
        "世界" => CurseForgeClient.ClassIdWorlds,
        "插件" => CurseForgeClient.ClassIdPlugins,
        "整合包" => CurseForgeClient.ClassIdModpacks,
        _ => CurseForgeClient.ClassIdResourcePacks
    };

    /// <summary>Modrinth 的项目类型；世界与插件在 Modrinth 侧没有独立分类。</summary>
    public static string ModrinthProjectType(string? type) => type switch
    {
        "光影" => "shader",
        "数据包" => "datapack",
        "整合包" => "modpack",
        "Mod" => "mod",
        _ => "resourcepack"
    };
}
