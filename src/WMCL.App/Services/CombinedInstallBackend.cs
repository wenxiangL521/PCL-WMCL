using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Services;

internal sealed class CombinedInstallBackend(
    string gameRoot, MirrorMode mirror, int concurrency,
    JavaProvisioner javaProvisioner, OptiFineService optifine, ModrinthClient modrinth,
    OptiFabricService optifabric, IProgress<string>? loaderLog = null) : ICombinedInstallBackend
{
    private readonly VersionInstaller _installer = new(new VersionRepository(gameRoot));
    private InstallOptions Options => new() { Mirror = mirror, MaxConcurrency = concurrency };

    public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) =>
        await _installer.InstallAsync(plan.Minecraft, Options, progress, ct);

    public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var repository = new VersionRepository(stagingRoot);
        if (plan.BaseLoader is "Fabric" or "Quilt" or "LegacyFabric")
        {
            var kind = Enum.Parse<FabricFamilyKind>(plan.BaseLoader);
            return await new FabricInstaller(repository, kind: kind).InstallAsync(
                plan.Minecraft, plan.LoaderVersion, progress, ct, completeInstallation: false);
        }
        var java = await javaProvisioner.EnsureAsync(
            JavaRequirement.ForInstall(plan.Minecraft, plan.BaseLoader,
                plan.LoaderVersion ?? plan.OptiFine?.Patch, plan.LiteLoader is not null),
            progress, ct);
        if (plan.BaseLoader == "OptiFine")
            return await new OptiFineService(repository).InstallAsync(
                plan.OptiFine!, java.Path, progress, loaderLog, ct: ct, completeInstallation: false);
        return await new ForgelikeInstaller(repository).InstallAsync(
            plan.BaseLoader == "NeoForge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge,
            plan.Minecraft, plan.LoaderVersion!, java.Path, progress, loaderLog, ct: ct, completeInstallation: false);
    }

    public async Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct) =>
        await _installer.InstallAsync(instanceId, Options, progress, ct);

    public async Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) =>
        (await optifine.EnsureInstallerAsync(plan.OptiFine!, progress, ct)).Path;

    public async Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct)
    {
        if (project == "optifabric")
        {
            var file = plan.OptiFabric ?? throw new InvalidOperationException("请先选择 OptiFabric 版本");
            await optifabric.InstallAsync(file, plan.Minecraft, modsDirectory, progress, ct);
            return [file.FileName];
        }
        if (project != plan.ApiProject) throw new InvalidOperationException("未知附加组件：" + project);
        var version = plan.ApiVersion ?? throw new InvalidOperationException("请先选择 API 版本");
        return await new ApiComponentService(modrinth).InstallAsync(
            version, plan.Minecraft, plan.BaseLoader, plan.LoaderVersion!, modsDirectory, progress, ct);
    }
}
