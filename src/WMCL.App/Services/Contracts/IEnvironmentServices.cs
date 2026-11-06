using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.Services;

/// <summary>游戏目录、Java 与功能开关相关的最小依赖集合。</summary>
public interface IEnvironmentServices : IResourceServices
{
    /// <summary>切换游戏目录后触发，携带新目录路径。</summary>
    event EventHandler<string>? GameDirectoryChanged;

    JavaLocator JavaLocator { get; }

    JavaProvisioner JavaProvisioner { get; }

    FeatureVisibility Features { get; }

    void SetGameDirectory(string directory);

    List<string> GetFavoriteVersions(string folder);

    List<string> GetHiddenVersions(string folder);

    void RenameGameFolder(string path, string? name);

    void RemoveGameFolder(string path);
}
