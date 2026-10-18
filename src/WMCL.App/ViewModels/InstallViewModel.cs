using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;

namespace WMCL.App.ViewModels;

/// <summary>
/// “下载 → 原版游戏”安装页。组合安装只暴露 PCL2 允许的组合；
/// 加载器安装后复制 OptiFine/OptiFabric/Fabric API 到同一实例，而不是生成多个实例。
/// </summary>
public partial class InstallViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstallViewModel(DownloadViewModel download, ModsViewModel mods, AppServices services)
    {
        Download = download;
        Mods = mods;
        _services = services;
        Download.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadViewModel.SelectedVersion))
                _ = PrepareSelectedVersionAsync();
        };
        _ = PrepareSelectedVersionAsync();
    }

    private async Task PrepareSelectedVersionAsync()
    {
        if (Download.SelectedVersion is not { IsInstalled: true } version) return;
        await Mods.PrepareGameVersionAsync(version.Id);
    }

    public DownloadViewModel Download { get; }
    public ModsViewModel Mods { get; }
    public event EventHandler? InstallCompleted;

    public ObservableCollection<InstallStage> Stages { get; } = [];

    public ObservableCollection<string> LoaderKinds { get; } =
    [
        "无", "Fabric", "Forge", "NeoForge", "OptiFine",
        "Fabric + OptiFine", "Forge + OptiFine"
    ];

    public ObservableCollection<string> BaseLoaderKinds { get; } =
        ["无", "Fabric", "Forge", "NeoForge", "OptiFine"];

    [ObservableProperty]
    private string _selectedLoaderKind = "无";

    [ObservableProperty]
    private string _selectedBaseLoader = "无";

    [ObservableProperty]
    private bool _installOptiFineWithBase;

    public bool CanAddOptiFine =>
        SelectedBaseLoader is "Fabric" or "Forge";

    public bool IsFabricBase => SelectedBaseLoader == "Fabric";
    public bool IsForgelikeBase => SelectedBaseLoader is "Forge" or "NeoForge";
    public bool IsOptiFineBase => SelectedBaseLoader == "OptiFine";

    partial void OnSelectedBaseLoaderChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddOptiFine));
        OnPropertyChanged(nameof(IsFabricBase));
        OnPropertyChanged(nameof(IsForgelikeBase));
        OnPropertyChanged(nameof(IsOptiFineBase));
        if (!CanAddOptiFine) InstallOptiFineWithBase = false;
        if (IsForgelikeBase)
            Mods.SelectedForgelikeKind = value;
        if (SelectedBaseLoader != "无" &&
            Download.SelectedVersion is { IsInstalled: true } version)
            _ = Mods.PrepareGameVersionAsync(version.Id);
        SyncLoaderKind();
    }

    partial void OnInstallOptiFineWithBaseChanged(bool value) => SyncLoaderKind();

    private void SyncLoaderKind()
    {
        SelectedLoaderKind = CanAddOptiFine && InstallOptiFineWithBase
            ? $"{SelectedBaseLoader} + OptiFine"
            : SelectedBaseLoader;
    }

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private bool _isMinecraftExpanded;

    [ObservableProperty]
    private bool _isCombinedBusy;

    [ObservableProperty]
    private double _combinedProgress;

    [ObservableProperty]
    private string _combinedProgressText = "";

    [ObservableProperty]
    private string _combinedStatus = "选择 Minecraft 版本与加载器后一次安装";

    [RelayCommand]
    private async Task InstallCombinedAsync()
    {
        if (IsCombinedBusy) return;
        var target = Download.SelectedVersion;
        if (target is null)
        {
            CombinedStatus = "请先选择 Minecraft 版本";
            return;
        }

        IsCombinedBusy = true;
        CombinedProgress = 0;
        CombinedProgressText = "";
        var ct = CancellationToken.None;
        var vanillaWasInstalled = target.IsInstalled;
        try
        {
            if (!target.IsInstalled)
            {
                CombinedStatus = $"正在安装原版 {target.Id}…";
                Download.SelectedVersion = target;
                await Download.InstallCommand.ExecuteAsync(null);
                if (!target.IsInstalled)
                {
                    CombinedStatus = "原版安装未完成：" + Download.Status;
                    return;
                }
            }

            var progress = new Progress<JobProgress>(update =>
            {
                CombinedProgressText = FormatProgress(update);
                var active = Stages.FirstOrDefault(stage => stage.IsActive);
                if (active is not null)
                {
                    active.Progress = (update.Percent ?? 0) * 100;
                    active.Detail = string.IsNullOrWhiteSpace(update.Detail)
                        ? update.Stage
                        : update.Detail;
                    UpdateCombinedProgress();
                }
            });

            CombinedStatus = $"正在准备 {target.Id} 的加载器…";
            await Mods.PrepareGameVersionAsync(target.Id);

            var combination = SelectedLoaderKind;
            var withOptiFine = combination.Contains("OptiFine", StringComparison.Ordinal);
            var baseKind = combination.StartsWith("Fabric", StringComparison.Ordinal) ? "Fabric"
                : combination.StartsWith("Forge", StringComparison.Ordinal) ? "Forge"
                : combination == "NeoForge" ? "NeoForge"
                : combination == "OptiFine" ? "OptiFine"
                : "无";

            BeginStages(combination, vanillaWasInstalled);
            CompleteStage("原版游戏", vanillaWasInstalled ? "已安装" : "安装完成");
            OptiFineVersion? optifine = null;
            if (withOptiFine)
            {
                optifine = Mods.OptiFineVersions.FirstOrDefault(version =>
                               version.Patch == Mods.SelectedOptiFineVersion?.Patch)
                           ?? Mods.OptiFineVersions.FirstOrDefault();
                if (optifine is null)
                {
                    CombinedStatus = "没有可用的 OptiFine 版本";
                    return;
                }
            }

            string? forgeVersion = null;
            if (baseKind == "Forge")
                forgeVersion = Mods.SelectedForgelikeVersion
                               ?? Mods.ForgelikeVersions.FirstOrDefault();

            if (withOptiFine && baseKind == "Forge")
            {
                forgeVersion = forgeVersion is not null &&
                               LoaderCompatibility.ForgeOptiFineMatch(
                                   optifine!.ForgeVersion ?? "", forgeVersion)
                    ? forgeVersion
                    : Mods.ForgelikeVersions.FirstOrDefault(version =>
                        LoaderCompatibility.ForgeOptiFineMatch(
                            optifine!.ForgeVersion ?? "", version));
                if (forgeVersion is null)
                {
                    CombinedStatus =
                        $"没有与 OptiFine {optifine!.Patch} 匹配的 Forge 版本（需要 {optifine.ForgeVersion ?? "未知"}）。";
                    return;
                }
            }

            var families = new List<LoaderFamily>();
            if (baseKind == "Fabric") families.Add(LoaderFamily.Fabric);
            if (baseKind == "Forge") families.Add(LoaderFamily.Forge);
            if (baseKind == "NeoForge") families.Add(LoaderFamily.NeoForge);
            if (withOptiFine) families.Add(LoaderFamily.OptiFine);

            var validation = LoaderCompatibility.Validate(
                families, target.Id, optifine?.ForgeVersion, forgeVersion);
            if (validation is not null)
            {
                CombinedStatus = validation;
                return;
            }

            string id;
            ActivateStage(LoaderStageTitle(baseKind));
            CombinedStatus = $"正在安装 {combination}…";
            if (baseKind == "Fabric")
            {
                var loader = Mods.SelectedLoader
                             ?? Mods.Loaders.FirstOrDefault();
                if (loader is null)
                {
                    CombinedStatus = "没有可用的 Fabric Loader";
                    return;
                }
                id = await _services.Fabric.InstallAsync(
                    target.Id, loader, progress, ct);
            }
            else if (baseKind == "Forge" || baseKind == "NeoForge")
            {
                if (forgeVersion is null)
                {
                    CombinedStatus = $"没有可用的 {baseKind} 版本";
                    return;
                }
                var java = await _services.JavaProvisioner.EnsureAsync(
                    JavaLocator.InferMajorVersion(target.Id), progress, ct);
                id = await _services.Forgelike.InstallAsync(
                    baseKind == "NeoForge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge,
                    target.Id, forgeVersion, java.Path, progress, ct: ct);
            }
            else if (baseKind == "OptiFine")
            {
                var java = await _services.JavaProvisioner.EnsureAsync(
                    JavaLocator.InferMajorVersion(target.Id), progress, ct);
                id = await _services.Optifine.InstallAsync(
                    target.Id, optifine!.Patch, java.Path, progress, null, ct);
            }
            else
            {
                id = target.Id;
            }

            CompleteStage(LoaderStageTitle(baseKind), "安装完成");
            var gameRoot = _services.Settings.GameDirectory;
            ActivateStage("合并实例");
            if (baseKind != "无")
            {
                var requested = InstanceName.Trim();
                var baseName = requested.Length > 0
                    ? requested
                    : DefaultInstanceName(target.Id, combination);
                var mergedId = UniqueInstanceName(gameRoot, baseName);
                await MergedInstanceBuilder.BuildAsync(gameRoot, id, mergedId, ct);
                if (!string.Equals(mergedId, target.Id, StringComparison.OrdinalIgnoreCase))
                    InstanceManager.Delete(gameRoot, id);
                id = mergedId;
            }
            else if (InstanceName.Trim().Length > 0)
            {
                var mergedId = UniqueInstanceName(gameRoot, InstanceName.Trim());
                await MergedInstanceBuilder.BuildAsync(gameRoot, target.Id, mergedId, ct);
                id = mergedId;
            }

            CompleteStage("合并实例", "实例已展平");
            ActivateStage("安装清单");
            var manifest = new InstallManifest
            {
                Components = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["minecraft"] = target.Id
                }
            };
            if (baseKind == "Fabric")
                manifest.Components["fabric"] = Mods.SelectedLoader ?? "";
            else if (baseKind == "Forge")
                manifest.Components["forge"] = forgeVersion ?? "";
            else if (baseKind == "NeoForge")
                manifest.Components["neoforge"] = forgeVersion ?? "";
            else if (baseKind == "OptiFine")
                manifest.Components["optifine"] = optifine?.Patch ?? "";

            manifest.ManagedFiles.AddRange(await InstallManifestStore.CollectManagedFilesAsync(
                gameRoot, id, "merged", ct));

            if (withOptiFine && baseKind is "Fabric" or "Forge")
            {
                var hashed = await AddOptiFineModsAsync(
                    target.Id, id, baseKind, optifine!, progress, ct);
                manifest.Components["optifine"] = optifine!.Patch;
                if (hashed is not null)
                    manifest.ManagedFiles.Add(hashed);
                if (baseKind == "Fabric")
                {
                    manifest.Components["optifabric"] = "Modrinth";
                    manifest.Components["fabric-api"] = "Modrinth";
                }
            }

            InstallManifestStore.Save(gameRoot, id, manifest);
            CompleteStage("安装清单", "已写入 WMCL/install.json");

            CombinedProgress = 100;
            CombinedProgressText = "";
            CombinedStatus = $"安装完成：{id}";
            InstallCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            var active = Stages.FirstOrDefault(stage => stage.IsActive);
            if (active is not null)
            {
                active.IsActive = false;
                active.IsFailed = true;
                active.Detail = ex.Message;
            }
            CombinedStatus = "组合安装失败：" + ex.Message;
        }
        finally
        {
            IsCombinedBusy = false;
            CombinedProgress = 0;
            CombinedProgressText = "";
        }
    }

    private void BeginStages(string combination, bool vanillaInstalled)
    {
        Stages.Clear();
        Stages.Add(new InstallStage("原版游戏")
        {
            Detail = vanillaInstalled ? "已安装" : "等待",
            IsDone = vanillaInstalled,
            Progress = vanillaInstalled ? 100 : 0
        });
        var loader = LoaderStageTitle(combination.StartsWith("Fabric", StringComparison.Ordinal) ? "Fabric"
            : combination.StartsWith("Forge", StringComparison.Ordinal) ? "Forge"
            : combination == "NeoForge" ? "NeoForge"
            : combination == "OptiFine" ? "OptiFine" : "无");
        if (loader != "无") Stages.Add(new InstallStage(loader));
        if (combination.Contains("OptiFine", StringComparison.Ordinal))
        {
            Stages.Add(new InstallStage("OptiFine Mod"));
            if (combination.StartsWith("Fabric", StringComparison.Ordinal))
            {
                Stages.Add(new InstallStage("OptiFabric"));
                Stages.Add(new InstallStage("Fabric API"));
            }
        }
        Stages.Add(new InstallStage("合并实例"));
        Stages.Add(new InstallStage("安装清单"));
    }

    private static string LoaderStageTitle(string kind) => kind switch
    {
        "Fabric" => "Fabric Loader",
        "Forge" => "Forge",
        "NeoForge" => "NeoForge",
        "OptiFine" => "OptiFine",
        _ => "无"
    };

    private void ActivateStage(string title)
    {
        var stage = Stages.FirstOrDefault(item => item.Title == title);
        if (stage is null) return;
        stage.IsActive = true;
        stage.Detail = "进行中";
    }

    private void CompleteStage(string title, string detail)
    {
        var stage = Stages.FirstOrDefault(item => item.Title == title);
        if (stage is null) return;
        stage.IsActive = false;
        stage.IsDone = true;
        stage.Progress = 100;
        stage.Detail = detail;
        UpdateCombinedProgress();
    }

    private void UpdateCombinedProgress()
    {
        var totalWeight = Stages.Sum(stage => stage.Weight);
        CombinedProgress = totalWeight <= 0
            ? 0
            : Stages.Sum(stage => Math.Clamp(stage.IsDone ? 100 : stage.Progress, 0, 100)
                                  * stage.Weight) / totalWeight;
    }

    private async Task<InstallManifestFile?> AddOptiFineModsAsync(
        string minecraft,
        string instanceId,
        string baseKind,
        OptiFineVersion optifine,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var version = await _services.Repository.GetVersionAsync(instanceId, ct);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            _services.Settings.GameDirectory, instanceId, version, _services.Settings.IsolationMode);
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);

        ActivateStage("OptiFine Mod");
        var (_, installerPath) = await _services.Optifine.EnsureInstallerAsync(
            minecraft, optifine.Patch, progress, ct);
        var fileName = Path.GetFileName(installerPath);
        var destination = Path.Combine(modsDirectory, fileName);
        File.Copy(installerPath, destination, overwrite: true);
        CombinedStatus = $"已加入 OptiFine：{fileName}";
        CompleteStage("OptiFine Mod", fileName);

        if (baseKind == "Fabric")
        {
            ActivateStage("OptiFabric");
            CombinedStatus = "正在安装 OptiFabric…";
            await _services.Modrinth.InstallAsync(
                "optifabric", minecraft, modsDirectory, "fabric", progress, ct);
            CompleteStage("OptiFabric", "已安装");
            ActivateStage("Fabric API");
            await _services.Modrinth.InstallAsync(
                "fabric-api", minecraft, modsDirectory, "fabric", progress, ct);
            CompleteStage("Fabric API", "已安装");
        }

        using var stream = File.OpenRead(destination);
        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream));
        return new InstallManifestFile("mods/" + fileName, sha1, "OptiFine");
    }

    private static string DefaultInstanceName(string minecraft, string combination) =>
        combination == "无"
            ? minecraft
            : $"{minecraft}-{combination.Replace(" + ", "-").Replace(" ", "")}";

    private static string UniqueInstanceName(string gameRoot, string requested)
    {
        var name = requested;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(gameRoot, "versions", name)))
            name = $"{requested}-{suffix++}";
        return name;
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0}%";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : progress.Detail;
        return $"{progress.Stage} {percent} {detail}".Trim();
    }
}
