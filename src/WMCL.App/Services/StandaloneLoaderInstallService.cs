using WMCL.Core.Jobs;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Services;

internal sealed record StandaloneLoaderInstallRequest(string GameRoot, string Minecraft, string Kind, string Version,
    OptiFineVersion? OptiFine = null)
{
    public string InstanceId => Kind switch
    {
        "Fabric" => $"fabric-loader-{Version}-{Minecraft}",
        "Quilt" => $"quilt-loader-{Version}-{Minecraft}",
        "LegacyFabric" => $"legacy-fabric-{Version}-{Minecraft}",
        "Forge" => $"{Minecraft}-forge-{Version}",
        "NeoForge" => $"neoforge-{Version}",
        "OptiFine" => $"{Minecraft}-OptiFine_{OptiFine?.Type ?? "HD_U"}_{Version}",
        _ => throw new InvalidOperationException("不支持的加载器：" + Kind)
    };
}

internal sealed class StandaloneLoaderInstallService(AppServices services, Func<string, ICombinedInstallBackend>? backendFactory = null)
{
    public async Task<string> InstallAsync(StandaloneLoaderInstallRequest request, IProgress<JobProgress> progress,
        IProgress<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Version)) throw new InvalidDataException("未选择加载器版本");
        var optifine = request.Kind == "OptiFine"
            ? request.OptiFine ?? throw new InvalidDataException("缺少已选 OptiFine 的完整版本信息，请重新获取版本目录。") : null;
        if (optifine is not null && optifine.Patch != request.Version)
            throw new InvalidDataException("OptiFine 文件与固定版本不一致");
        var plan = CombinedInstallPlan.Create(request.GameRoot, request.Minecraft, false, request.Kind,
            request.Version, optifine, false, false, request.InstanceId);
        var mirror = services.Mirror;
        var concurrency = Math.Clamp(services.Settings.DownloadThreads, 1, 64);
        ICombinedInstallBackend Backend(string root) => backendFactory?.Invoke(root) ?? new CombinedInstallBackend(
            root, mirror, concurrency, services.JavaProvisioner, services.Optifine, services.Modrinth, services.Optifabric, log);
        var steps = plan.Steps.ToList();
        var result = await new NewInstanceInstallService(request.GameRoot, services.Settings.IsolationMode, Backend)
            .InstallAsync(plan, (step, _, update) =>
            {
                var percent = Math.Clamp(update.Percent ?? 0, 0, 1);
                var stage = update.Stage switch
                {
                    "应用安装结果" => "应用加载器安装结果",
                    "发布实例" => "发布加载器实例",
                    _ => update.Stage
                };
                progress.Report(update with
                {
                    Stage = stage,
                    Percent = step == CombinedInstallStep.Apply ? 0.8 + 0.2 * percent
                        : 0.8 * (steps.IndexOf(step) + percent) / steps.Count
                });
            }, ct);
        return result.InstanceId;
    }
}
