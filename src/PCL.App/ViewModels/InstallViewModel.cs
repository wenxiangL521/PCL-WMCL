using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

/// <summary>“下载 → 原版游戏”安装页：Minecraft 与各加载器的组合安装。</summary>
public partial class InstallViewModel(DownloadViewModel download, ModsViewModel mods) : ViewModelBase
{
    public DownloadViewModel Download { get; } = download;
    public ModsViewModel Mods { get; } = mods;

    public ObservableCollection<string> LoaderKinds { get; } = ["无", "Fabric", "Forge", "NeoForge", "OptiFine"];

    [ObservableProperty]
    private string _selectedLoaderKind = "无";

    [ObservableProperty]
    private bool _isCombinedBusy;

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

            await Mods.PrepareGameVersionAsync(target.Id);

            switch (SelectedLoaderKind)
            {
                case "Fabric":
                    Mods.SelectedLoader = Mods.Loaders.FirstOrDefault();
                    if (Mods.SelectedLoader is null)
                    {
                        CombinedStatus = "没有可用的 Fabric Loader";
                        return;
                    }
                    CombinedStatus = $"正在安装 Fabric {Mods.SelectedLoader}…";
                    await Mods.InstallFabricCommand.ExecuteAsync(null);
                    CombinedStatus = Mods.Status;
                    break;
                case "Forge":
                case "NeoForge":
                    Mods.SelectedForgelikeKind = SelectedLoaderKind;
                    await Mods.PrepareGameVersionAsync(target.Id);
                    Mods.SelectedForgelikeKind = SelectedLoaderKind;
                    Mods.SelectedForgelikeVersion = Mods.ForgelikeVersions.FirstOrDefault();
                    if (Mods.SelectedForgelikeVersion is null)
                    {
                        CombinedStatus = $"没有可用的 {SelectedLoaderKind} 版本";
                        return;
                    }
                    CombinedStatus = $"正在安装 {SelectedLoaderKind} {Mods.SelectedForgelikeVersion}…";
                    await Mods.InstallForgelikeCommand.ExecuteAsync(null);
                    CombinedStatus = Mods.ForgelikeStatus;
                    break;
                case "OptiFine":
                    Mods.SelectedOptiFineVersion = Mods.OptiFineVersions.FirstOrDefault();
                    if (Mods.SelectedOptiFineVersion is null)
                    {
                        CombinedStatus = "没有可用的 OptiFine 版本";
                        return;
                    }
                    CombinedStatus = $"正在安装 OptiFine {Mods.SelectedOptiFineVersion.Patch}…";
                    await Mods.InstallOptiFineCommand.ExecuteAsync(null);
                    CombinedStatus = Mods.OptiFineStatus;
                    break;
                default:
                    CombinedStatus = $"Minecraft {target.Id} 安装完成";
                    break;
            }
        }
        catch (Exception ex)
        {
            CombinedStatus = "组合安装失败：" + ex.Message;
        }
        finally
        {
            IsCombinedBusy = false;
        }
    }
}
