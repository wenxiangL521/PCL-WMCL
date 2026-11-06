using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Modrinth;
using WMCL.Modding.Resources;

namespace WMCL.App.Services;

/// <summary>Mod 管理与资源搜索页需要的最小依赖集合。</summary>
public interface IModsWorkspaceServices : IInstallServices
{
    ModCatalogService ModCatalog { get; }

    FabricInstaller Fabric { get; }

    FabricInstaller Quilt { get; }

    FabricInstaller LegacyFabric { get; }

    ForgelikeInstaller Forgelike { get; }

    LiteLoaderService LiteLoader { get; }

    ModUpdateService ModUpdates { get; }

    ResourceInstallPlanner ResourcePlanner { get; }

    ResourceBatchInstallService ResourceBatch { get; }
}
