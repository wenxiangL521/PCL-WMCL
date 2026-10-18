using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect：左侧 Minecraft 文件夹，右侧按加载器分组的已安装版本。</summary>
public partial class VersionSelectViewModel : ViewModelBase
{
    private readonly LaunchViewModel _launch;
    private readonly AppServices _services;
    private readonly Action _close;
    private readonly Action _download;

    public VersionSelectViewModel(
        LaunchViewModel launch,
        AppServices services,
        Action close,
        Action download)
    {
        _launch = launch;
        _services = services;
        _close = close;
        _download = download;
        SelectedVersion = launch.SelectedVersion is { IsInstalled: true } selected
            ? selected
            : Versions.FirstOrDefault();
        Versions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(CountText));
            RebuildGroups();
            RefreshSelection();
        };
        RebuildGroups();
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public ObservableCollection<VersionGroup> Groups { get; } = [];
    public bool HasVersions => Versions.Count > 0;
    public bool HasGroups => Groups.Count > 0;
    public string CountText => $"已安装 {Versions.Count} 个版本";
    public string GameDirectory => _services.Settings.GameDirectory;
    public string FolderName => Path.GetFileName(Path.TrimEndingDirectorySeparator(GameDirectory));
    public string VersionsDirectory => Path.Combine(GameDirectory, "versions");

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    public void RefreshSelection()
    {
        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
            SelectedVersion = _launch.SelectedVersion is { IsInstalled: true } selected
                ? selected
                : Versions.FirstOrDefault();
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var titles = new (string Category, string Title)[]
        {
            ("vanilla", "常规版本"),
            ("fabric", "Fabric 版本"),
            ("forge", "Forge 版本"),
            ("neoforge", "NeoForge 版本"),
            ("quilt", "Quilt 版本"),
            ("optifine", "OptiFine 版本"),
            ("", "其他版本")
        };
        foreach (var (category, title) in titles)
        {
            var items = Versions
                .Where(v => string.Equals(
                    string.IsNullOrWhiteSpace(v.Category) ? "" : v.Category,
                    category,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.ReleaseTime ?? "")
                .ToList();
            if (items.Count > 0)
                Groups.Add(new VersionGroup(title, items));
        }
        OnPropertyChanged(nameof(HasGroups));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedVersion is null) return;
        _launch.SelectedVersion = SelectedVersion;
        _close();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(VersionsDirectory);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? VersionsDirectory : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : VersionsDirectory,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    [RelayCommand]
    private void Download()
    {
        _close();
        _download();
    }
}
