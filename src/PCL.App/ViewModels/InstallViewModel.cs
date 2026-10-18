using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Launch.Java;
using PCL.Minecraft;
using PCL.Modding.Forgelike;
using PCL.Modding.Loaders;
using PCL.Modding.OptiFine;

namespace PCL.App.ViewModels;

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
    }

    public DownloadViewModel Download { get; }
    public ModsViewModel Mods { get; }
    public event EventHandler? InstallCompleted;

    public ObservableCollection<string> LoaderKinds { get; } =
    [
        "无", "Fabric", "Forge", "NeoForge", "OptiFine",
        "Fabric + OptiFine", "Forge + OptiFine"
    ];

    [ObservableProperty]
    private string _selectedLoaderKind = "无";

    [ObservableProperty]
    private string _instanceName = "";

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
                CombinedProgress = (update.Percent ?? CombinedProgress / 100) * 100;
                CombinedProgressText = FormatProgress(update);
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
                forgeVersion = Mods.ForgelikeVersions.FirstOrDefault();

            if (withOptiFine && baseKind == "Forge")
            {
                forgeVersion = Mods.ForgelikeVersions.FirstOrDefault(version =>
                    LoaderCompatibility.ForgeOptiFineMatch(optifine!.ForgeVersion ?? "", version));
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
            CombinedStatus = $"正在安装 {combination}…";
            if (baseKind == "Fabric")
            {
                var loader = Mods.Loaders.FirstOrDefault();
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

            var instanceName = InstanceName.Trim();
            if (instanceName.Length > 0 &&
                !string.Equals(instanceName, id, StringComparison.OrdinalIgnoreCase))
            {
                InstanceManager.Rename(_services.Settings.GameDirectory, id, instanceName);
                id = instanceName;
            }

            if (withOptiFine && baseKind is "Fabric" or "Forge")
                await AddOptiFineModsAsync(target.Id, id, baseKind, optifine!, progress, ct);

            CombinedProgress = 100;
            CombinedProgressText = "";
            CombinedStatus = $"安装完成：{id}";
            InstallCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            CombinedStatus = "组合安装失败：" + ex.Message;
        }
        finally
        {
            IsCombinedBusy = false;
            CombinedProgress = 0;
            CombinedProgressText = "";
        }
    }

    private async Task AddOptiFineModsAsync(
        string minecraft,
        string instanceId,
        string baseKind,
        OptiFineVersion optifine,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var version = await _services.Repository.GetVersionAsync(instanceId, ct);
        var gameDirectory = MinecraftVersionUtil.GetGameDirectory(
            _services.Settings.GameDirectory, instanceId, version);
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);

        var (_, installerPath) = await _services.Optifine.EnsureInstallerAsync(
            minecraft, optifine.Patch, progress, ct);
        var destination = Path.Combine(modsDirectory, Path.GetFileName(installerPath));
        File.Copy(installerPath, destination, overwrite: true);
        CombinedStatus = $"已加入 OptiFine：{Path.GetFileName(installerPath)}";

        if (baseKind == "Fabric")
        {
            CombinedStatus = "正在安装 OptiFabric / Fabric API…";
            await _services.Modrinth.InstallAsync(
                "optifabric", minecraft, modsDirectory, "fabric", progress, ct);
            await _services.Modrinth.InstallAsync(
                "fabric-api", minecraft, modsDirectory, "fabric", progress, ct);
        }
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0}%";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : progress.Detail;
        return $"{progress.Stage} {percent} {detail}".Trim();
    }
}
