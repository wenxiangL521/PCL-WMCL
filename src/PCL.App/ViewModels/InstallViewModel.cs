namespace PCL.App.ViewModels;

/// <summary>“下载 → 原版游戏”安装页：Minecraft 与各加载器的单列卡片。</summary>
public sealed class InstallViewModel(DownloadViewModel download, ModsViewModel mods) : ViewModelBase
{
    public DownloadViewModel Download { get; } = download;
    public ModsViewModel Mods { get; } = mods;
}
