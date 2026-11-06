using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Services;

/// <summary>资源搜索与批量安装所需的最小依赖集合。</summary>
public interface IResourceServices : IGameServices
{
    ModrinthClient Modrinth { get; }

    CurseForgeClient CurseForge { get; }

    ResourceFavoritesService ResourceFavorites { get; }
}
