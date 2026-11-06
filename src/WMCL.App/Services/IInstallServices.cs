using WMCL.Core.Jobs;
using WMCL.Launch;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Services;

/// <summary>安装流程（含 OptiFine / OptiFabric）所需的最小依赖集合。</summary>
public interface IInstallServices : IEnvironmentServices
{
    OptiFineService Optifine { get; }

    OptiFabricService Optifabric { get; }

    LaunchPreparationService LaunchPreparation { get; }

    GameLauncher GameLauncher { get; }

    Task EnsureGameFilesAsync(string root, string versionId, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default);

    McbbsModpackUpdater CreateModpackUpdater(string root);
}
