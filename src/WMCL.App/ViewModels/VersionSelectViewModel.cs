using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect：左侧 Minecraft 文件夹列表，右侧按加载器分组的已安装版本。</summary>
public partial class VersionSelectViewModel : ViewModelBase
{
    private readonly LaunchViewModel _launch;
    private readonly AppServices _services;
    private readonly Action _close;
    private readonly Action _download;
    private bool _suppressFolderChange;

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
        ReloadFolders();
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public ObservableCollection<VersionGroup> Groups { get; } = [];
    public ObservableCollection<GameFolderItem> Folders { get; } = [];
    public bool HasVersions => Versions.Count > 0;
    public bool HasGroups => Groups.Count > 0;
    public bool CanRemoveFolder => Folders.Count > 1;
    public string CountText => $"已安装 {Versions.Count} 个版本";
    public string GameDirectory => _services.Settings.GameDirectory;
    public string VersionsDirectory => Path.Combine(GameDirectory, "versions");

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private GameFolderItem? _selectedFolder;

    partial void OnSelectedFolderChanged(GameFolderItem? value)
    {
        if (_suppressFolderChange || value is null) return;
        if (string.Equals(value.Path, _services.Settings.GameDirectory, StringComparison.OrdinalIgnoreCase))
            return;
        _services.SetGameDirectory(value.Path);
    }

    public void ReloadFolders()
    {
        _suppressFolderChange = true;
        try
        {
            Folders.Clear();
            foreach (var path in _services.Settings.GameFolders)
            {
                var full = WmclPaths.Expand(path);
                Folders.Add(new GameFolderItem(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) is { Length: > 0 } name
                        ? name
                        : full,
                    full));
            }
            SelectedFolder = Folders.FirstOrDefault(folder =>
                                 string.Equals(folder.Path, _services.Settings.GameDirectory,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? Folders.FirstOrDefault();
            OnPropertyChanged(nameof(CanRemoveFolder));
            OnPropertyChanged(nameof(GameDirectory));
            OnPropertyChanged(nameof(VersionsDirectory));
        }
        finally
        {
            _suppressFolderChange = false;
        }
    }

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _services.SetGameDirectory(path);
        ReloadFolders();
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (!CanRemoveFolder || SelectedFolder is null) return;
        _services.RemoveGameFolder(SelectedFolder.Path);
        ReloadFolders();
    }

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
