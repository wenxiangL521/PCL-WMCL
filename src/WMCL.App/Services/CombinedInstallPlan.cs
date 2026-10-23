using System.IO;
using WMCL.Minecraft;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.Services;

internal enum CombinedInstallStep
{
    Vanilla,
    Loader,
    LiteLoader,
    Merge,
    OptiFineMod,
    Api,
    OptiFabric,
    Manifest,
    Apply
}

internal sealed record CombinedInstallPlan(
    string Minecraft,
    string InstanceId,
    string BaseLoader,
    string? LoaderVersion,
    OptiFineVersion? OptiFine,
    bool InstallApi,
    bool InstallOptiFabric,
    bool VanillaInstalled,
    OptiFabricFile? OptiFabric,
    ModrinthVersion? ApiVersion,
    LiteLoaderVersion? LiteLoader = null)
{
    public string? RenameTo { get; init; }
    public bool HasBaseLoader => BaseLoader != "无" && !(BaseLoader == "OptiFine" && LiteLoader is not null);
    public bool HasLoader => HasBaseLoader || LiteLoader is not null;
    public bool NeedsMerge => HasLoader || InstanceId != Minecraft;
    public bool OptiFineAsMod => OptiFine is not null && (BaseLoader != "OptiFine" || LiteLoader is not null);
    public string ApiProject => ApiComponentService.Project(BaseLoader);
    public string ApiName => ApiComponentService.Name(BaseLoader);
    public string ModLoader => ApiComponentService.Loader(BaseLoader);
    public IReadOnlyList<CombinedInstallStep> Steps =>
    [
        CombinedInstallStep.Vanilla,
        .. HasBaseLoader ? new[] { CombinedInstallStep.Loader } : [],
        .. LiteLoader is not null ? new[] { CombinedInstallStep.LiteLoader } : [],
        .. NeedsMerge ? new[] { CombinedInstallStep.Merge } : [],
        .. OptiFineAsMod ? new[] { CombinedInstallStep.OptiFineMod } : [],
        .. InstallApi ? new[] { CombinedInstallStep.Api } : [],
        .. InstallOptiFabric ? new[] { CombinedInstallStep.OptiFabric } : [],
        CombinedInstallStep.Manifest
    ];

    public string Title(CombinedInstallStep step) => step switch
    {
        CombinedInstallStep.Vanilla => "原版游戏",
        CombinedInstallStep.Loader when BaseLoader is "Fabric" or "Quilt" or "LegacyFabric" => BaseLoader + " Loader",
        CombinedInstallStep.Loader => BaseLoader,
        CombinedInstallStep.LiteLoader => "LiteLoader",
        CombinedInstallStep.Merge => "合并实例",
        CombinedInstallStep.OptiFineMod => "OptiFine Mod",
        CombinedInstallStep.Api => ApiName,
        CombinedInstallStep.OptiFabric => "OptiFabric",
        CombinedInstallStep.Manifest => "安装清单",
        CombinedInstallStep.Apply => "应用安装更改",
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };

    public static CombinedInstallPlan Create(
        string gameRoot, string minecraft, bool vanillaInstalled, string baseLoader,
        string? loaderVersion, OptiFineVersion? optifine, bool installApi,
        bool installOptiFabric, string requestedName, OptiFabricFile? optiFabric = null,
        ModrinthVersion? apiVersion = null, string? existingInstanceId = null, LiteLoaderVersion? liteLoader = null)
    {
        InstanceManager.ValidateName(minecraft);
        if (baseLoader is not ("无" or "Fabric" or "Quilt" or "LegacyFabric" or "Forge" or "NeoForge" or "OptiFine"))
            throw new InvalidOperationException("未知加载器：" + baseLoader);
        if (baseLoader is not ("无" or "OptiFine") && string.IsNullOrWhiteSpace(loaderVersion))
            throw new InvalidOperationException($"请先选择 {baseLoader} 版本");
        if (baseLoader == "OptiFine" && optifine is null)
            throw new InvalidOperationException("请先选择 OptiFine 版本");
        if (baseLoader == "无" && optifine is not null)
            throw new InvalidOperationException("请先选择 OptiFine 或兼容的加载器");
        if (optifine is not null && optifine.Minecraft != minecraft)
            throw new InvalidOperationException("OptiFine 与所选 Minecraft 版本不一致，请等待版本列表加载完成");
        if (liteLoader is not null)
        {
            if (liteLoader.Minecraft != minecraft)
                throw new InvalidOperationException("LiteLoader 与所选 Minecraft 版本不一致，请等待版本列表加载完成");
            LiteLoaderService.CreateProfile(liteLoader);
        }
        var withOptiFabric = baseLoader == "Fabric" && optifine is not null && installOptiFabric;
        if (withOptiFabric && optiFabric is null)
            throw new InvalidOperationException("请先选择 OptiFabric 版本，或取消附加 OptiFabric");
        if (withOptiFabric && !OptiFabricService.IsCompatible(optiFabric!, minecraft))
            throw new InvalidOperationException("所选 OptiFabric 不支持当前 Minecraft 版本");
        var families = new List<LoaderFamily>();
        if (baseLoader is not ("无" or "OptiFine"))
            families.Add(Enum.Parse<LoaderFamily>(baseLoader));
        if (optifine is not null) families.Add(LoaderFamily.OptiFine);
        if (liteLoader is not null) families.Add(LoaderFamily.LiteLoader);
        var error = LoaderCompatibility.Validate(families, minecraft, optifine?.ForgeVersion, loaderVersion);
        if (error is not null) throw new InvalidOperationException(error);
        var explicitName = requestedName.Trim();
        if (existingInstanceId is not null && requestedName != explicitName)
            throw new ArgumentException("实例名不能以空格开头或结尾", nameof(requestedName));
        if (existingInstanceId is not null && explicitName.Length > 0)
        {
            InstanceManager.ValidateName(explicitName);
            var samePath = string.Equals(existingInstanceId, explicitName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (!samePath && (Path.Exists(Path.Combine(gameRoot, "versions", explicitName)) ||
                             Path.Exists(Path.Combine(gameRoot, "libraries", "wmcl-packs", explicitName))))
                throw new IOException("实例或专属库目录已存在：" + explicitName);
        }
        var combination = optifine is not null && baseLoader != "OptiFine"
            ? baseLoader + "-OptiFine" : baseLoader;
        if (liteLoader is not null)
            combination = (baseLoader is "无" or "OptiFine" ? "LiteLoader" : baseLoader + "-LiteLoader") +
                (optifine is null ? "" : "-OptiFine");
        var instanceId = existingInstanceId ?? (explicitName.Length > 0 ? explicitName
            : baseLoader == "无" && liteLoader is null ? minecraft : minecraft + "-" + combination);
        InstanceManager.ValidateName(instanceId);
        if (existingInstanceId is not null && !File.Exists(Path.Combine(gameRoot, "versions", instanceId, instanceId + ".json")))
            throw new FileNotFoundException("目标实例已不存在，请重新打开安装页");
        if (existingInstanceId is null && explicitName.Length > 0 && Directory.Exists(Path.Combine(gameRoot, "versions", instanceId)))
            throw new IOException($"实例已存在：{instanceId}");
        if (existingInstanceId is null && explicitName.Length == 0 && (baseLoader != "无" || liteLoader is not null))
        {
            var defaultName = instanceId;
            var suffix = 2;
            while (Directory.Exists(Path.Combine(gameRoot, "versions", instanceId)))
                instanceId = $"{defaultName}-{suffix++}";
        }
        InstanceManager.ValidateName(instanceId);
        var fabricFamily = baseLoader is "Fabric" or "Quilt" or "LegacyFabric";
        if (fabricFamily && installApi)
        {
            if (apiVersion is null)
                throw new InvalidOperationException($"请先选择 {ApiComponentService.Name(baseLoader)} 版本，或清除该组件");
            if (!ApiComponentService.IsCompatible(apiVersion, minecraft, baseLoader))
                throw new InvalidOperationException("所选 API 与当前 Minecraft / 加载器不一致，请等待版本列表加载完成");
        }
        return new CombinedInstallPlan(minecraft, instanceId, baseLoader, loaderVersion, optifine,
            fabricFamily && installApi,
            withOptiFabric, vanillaInstalled, withOptiFabric ? optiFabric : null,
            fabricFamily && installApi ? ApiComponentService.Snapshot(apiVersion!) : null,
            liteLoader is null ? null : liteLoader with { Libraries = liteLoader.Libraries.Clone() })
        {
            RenameTo = existingInstanceId is not null && explicitName.Length > 0 && explicitName != existingInstanceId ? explicitName : null
        };
    }
}
