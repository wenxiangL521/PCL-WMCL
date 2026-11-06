using WMCL.Download;
using WMCL.Minecraft;

namespace WMCL.App.Services;

/// <summary>游戏目录与版本相关的最小依赖集合（版本仓库、安装器、游戏操作协调器）。</summary>
public interface IGameServices : IUserSettingsStore
{
    VersionRepository Repository { get; }

    VersionInstaller Installer { get; }

    GameOperationCoordinator GameOperations { get; }

    MirrorMode Mirror { get; }
}
