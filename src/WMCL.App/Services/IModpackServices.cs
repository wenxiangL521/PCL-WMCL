using WMCL.Modding.Modpack;

namespace WMCL.App.Services;

/// <summary>整合包安装与导入所需的最小依赖集合。</summary>
public interface IModpackServices : IModsWorkspaceServices
{
    ModpackInstaller Modpack { get; }

    CurseForgeModpackInstaller CurseForgeModpack { get; }

    ModpackImportWorkflow CreateModpackImports(string root);
}
