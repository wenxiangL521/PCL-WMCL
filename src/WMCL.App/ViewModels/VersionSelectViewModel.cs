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
    private bool _isImporting;

    [ObservableProperty]
    private string _importStatus = "";

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private GameFolderItem? _selectedFolder;

    [ObservableProperty]
    private bool _showHidden;

    public string HiddenToggleText => ShowHidden ? "隐藏隐藏版本" : "显示隐藏版本";

    partial void OnShowHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(HiddenToggleText));
        RebuildGroups();
    }

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
                var defaultName = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
                var name = _services.Settings.GameFolderNames.TryGetValue(full, out var custom) &&
                           !string.IsNullOrWhiteSpace(custom)
                    ? custom
                    : defaultName is { Length: > 0 } ? defaultName : full;
                Folders.Add(new GameFolderItem(name, full));
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

    public async Task RefreshVersionsAsync() => await _launch.LoadAsync();

    public void OpenGameFolder(GameFolderItem item)
    {
        try
        {
            Directory.CreateDirectory(item.Path);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? item.Path : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : item.Path,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    public void RenameFolder(GameFolderItem item, string name)
    {
        _services.RenameGameFolder(item.Path, name);
        ReloadFolders();
    }

    public void RemoveFolder(GameFolderItem item)
    {
        if (Folders.Count <= 1) return;
        _services.RemoveGameFolder(item.Path);
        ReloadFolders();
    }

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = WmclPaths.Expand(path);
        if (!Directory.Exists(Path.Combine(full, "versions")))
        {
            var nested = Path.Combine(full, ".minecraft");
            if (Directory.Exists(Path.Combine(nested, "versions")))
                full = nested;
        }
        _services.SetGameDirectory(full);
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
        var hidden = Versions.Where(v => v.IsHidden).ToList();
        if (ShowHidden && hidden.Count > 0)
            Groups.Add(new VersionGroup("隐藏的版本", hidden));

        var favorites = Versions.Where(v => v.IsFavorite && !v.IsHidden)
            .OrderByDescending(v => v.ReleaseTime ?? "")
            .ToList();
        if (favorites.Count > 0)
            Groups.Add(new VersionGroup("收藏夹", favorites));

        var fools = Versions
            .Where(v => !v.IsFavorite && !v.IsHidden && IsAprilFools(v.ReleaseTime))
            .OrderByDescending(v => v.ReleaseTime ?? "")
            .ToList();
        if (fools.Count > 0)
            Groups.Add(new VersionGroup("愚人节版本", fools));

        var titles = new (string Category, string Title)[]
        {
            ("error", "错误的版本"),
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
                .Where(v => !v.IsFavorite && !v.IsHidden && !IsAprilFools(v.ReleaseTime) && string.Equals(
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

    private static bool IsAprilFools(string? releaseTime) =>
        DateTimeOffset.TryParse(releaseTime, out var time) &&
        time.Month == 4 && time.Day == 1;

    public event EventHandler? InstanceInstalled;

    public async Task ImportLocalAsync(string path)
    {
        if (IsImporting || string.IsNullOrWhiteSpace(path)) return;
        IsImporting = true;
        ImportStatus = "正在导入本地整合包…";
        try
        {
            var progress = new Progress<WMCL.Core.Jobs.JobProgress>(update =>
                ImportStatus = update.Percent is null
                    ? update.Stage
                    : $"{update.Stage} {update.Percent * 100:0}%");
            var id = await _services.LocalModpack.InstallFromFileAsync(
                path, null, progress);
            ImportStatus = "已导入实例：" + id;
            InstanceInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ImportStatus = "导入失败：" + ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }

    public void ToggleHidden(VersionItem item)
    {
        item.IsHidden = !item.IsHidden;
        var hidden = _services.GetHiddenVersions(_services.Settings.GameDirectory);
        hidden.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (item.IsHidden) hidden.Add(item.Id);
        _services.SaveSettings();
        RebuildGroups();
    }

    [RelayCommand]
    private void ToggleHiddenVersions() => ShowHidden = !ShowHidden;

    public void ToggleFavorite(VersionItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        var favorites = _services.GetFavoriteVersions(_services.Settings.GameDirectory);
        favorites.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (item.IsFavorite) favorites.Add(item.Id);
        _services.SaveSettings();
        RebuildGroups();
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
