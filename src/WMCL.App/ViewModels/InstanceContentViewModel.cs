using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Local;
using WMCL.Modding.Modrinth;

namespace WMCL.App.ViewModels;

public partial class InstanceContentViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private int _instanceLoadRequest;
    private string _gameVersion = "";
    private string? _targetGameRoot;
    private string TargetGameRoot => _targetGameRoot ?? _services.Settings.GameDirectory;

    public InstanceContentViewModel(AppServices services)
    {
        _services = services;
        Favorite = new ResourceFavoritePickerViewModel(services.ResourceFavorites);
        DownloadResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDownloadResults));
    }

    public ObservableCollection<string> Instances { get; } = [];
    public ObservableCollection<InstanceContentItem> ResourcePacks { get; } = [];
    public ObservableCollection<InstanceContentItem> ShaderPacks { get; } = [];
    public ObservableCollection<InstanceContentItem> Worlds { get; } = [];
    public ObservableCollection<WorldBackupItem> WorldBackups { get; } = [];

    [ObservableProperty]
    private string? _selectedInstance;

    [ObservableProperty]
    private InstanceContentItem? _selectedResourcePack;

    [ObservableProperty]
    private InstanceContentItem? _selectedShaderPack;

    [ObservableProperty]
    private InstanceContentItem? _selectedWorld;

    [ObservableProperty]
    private WorldBackupItem? _selectedWorldBackup;

    [ObservableProperty]
    private bool _hasResourcePack;

    [ObservableProperty]
    private string _toggleResourcePackText = "启用";

    [ObservableProperty]
    private bool _hasShaderPack;

    [ObservableProperty]
    private bool _hasWorld;

    [ObservableProperty]
    private string _gameDirectory = "";

    [ObservableProperty]
    private string _pluginTargetDirectory = "";

    [ObservableProperty]
    private string _status = "选择实例以查看资源包、光影和存档";

    [ObservableProperty]
    private bool _showResourcePacks = true;

    [ObservableProperty]
    private bool _showShaders;

    [ObservableProperty]
    private bool _showWorlds;

    [ObservableProperty]
    private bool _showDataPacks;

    public bool IsWorldMode => ShowWorlds;
    public bool IsPluginDownload => SelectedDownloadType == "插件";
    public bool RequiresDataPackWorld => ShowDataPacks;
    public string ContentTypeLabel => ShowWorlds ? "存档"
        : ShowDataPacks ? "数据包"
        : ShowShaders ? "光影" : "资源包";
    public string DownloadExample => SelectedDownloadType == "插件"
        ? "搜索服务端插件，例如 luckperms"
        : ShowWorlds
        ? "搜索世界，例如 skyblock"
        : ShowDataPacks
            ? "搜索数据包，例如 terralith"
            : ShowShaders ? "搜索光影，例如 bsl" : "搜索资源包，例如 faithful";
    public ObservableCollection<string> DataPackWorlds { get; } = [];

    [ObservableProperty]
    private string? _selectedDataPackWorld;

    partial void OnSelectedDataPackWorldChanged(string? value) =>
        UpdateDownloadCommands();

    public void SetMode(string key)
    {
        ShowResourcePacks = key is "content" or "content-resource";
        ShowShaders = key == "content-shader";
        ShowWorlds = key == "content-world";
        ShowDataPacks = key == "content-datapack";
        Platforms.Clear();
        if (!ShowWorlds) Platforms.Add("Modrinth");
        Platforms.Add("CurseForge");
        SelectedPlatform = ShowWorlds ? "CurseForge" : Platforms.Contains(SelectedPlatform) ? SelectedPlatform : "Modrinth";
        SelectedImportType = ContentTypeLabel;
        SelectedDownloadType = ShowWorlds ? "世界" : ContentTypeLabel;
        OnPropertyChanged(nameof(IsWorldMode));
        OnPropertyChanged(nameof(RequiresDataPackWorld));
        OnPropertyChanged(nameof(ContentTypeLabel));
        OnPropertyChanged(nameof(DownloadExample));
        DownloadStatus = ShowWorlds ? "搜索存档（CurseForge）"
            : $"搜索{ContentTypeLabel}（Modrinth / CurseForge）";
    }

    public ObservableCollection<string> DownloadTypes { get; } = ["资源包", "光影", "数据包", "世界", "插件"];
    public ObservableCollection<string> ImportTypes { get; } = ["资源包", "光影", "存档"];

    [ObservableProperty]
    private string _selectedImportType = "资源包";

    [ObservableProperty]
    private string _importPath = "";
    public ObservableCollection<ResourceItem> DownloadResults { get; } = [];
    public ObservableCollection<ResourceVersionItem> ResourceVersions { get; } = [];
    public ObservableCollection<ResourceVersionGroup> ResourceVersionGroups { get; } = [];
    public ObservableCollection<string> ResourceVersionFilters { get; } = ["全部"];
    public bool HasResourceVersionFilters => ResourceVersionFilters.Count > 2;
    public ObservableCollection<string> Platforms { get; } = ["Modrinth", "CurseForge"];

    [ObservableProperty]
    private string _selectedResourceVersionFilter = "全部";

    partial void OnSelectedResourceVersionFilterChanged(string value)
    {
        if (!_updatingResourceVersionFilters) ApplyResourceVersionFilter();
    }

    [ObservableProperty]
    private ResourceVersionItem? _selectedResourceVersion;

    private int _versionLoadToken;
    private readonly List<ResourceVersionItem> _allResourceVersions = [];
    private readonly List<ResourceVersionEntry> _resourceVersionEntries = [];
    private ResourceVersionFilterSet _resourceVersionFilterSet = ResourceVersionFilterSet.Create([]);
    private bool _updatingResourceVersionFilters;
    private int _resourceSearchToken;
    private int _targetRequest;
    private Task _resourceVersionLoad = Task.CompletedTask;

    public ResourceFavoritePickerViewModel Favorite { get; }

    [ObservableProperty]
    private int _favoriteOpenRequest;

    public async Task OpenFavoriteAsync(FavoriteResource resource)
    {
        if (IsDownloadBusy) throw new InvalidOperationException("请等待资源搜索或安装结束后再打开收藏。");
        if (!resource.IsValid() || resource.ResourceType is "mod" or "modpack")
            throw new InvalidOperationException("该收藏不是有效的资源项目。");
        SetMode(resource.ResourceType switch
        {
            "shader" => "content-shader",
            "datapack" => "content-datapack",
            "world" => "content-world",
            _ => "content-resource"
        });
        SelectedDownloadType = resource.ResourceType == "plugin" ? "插件" : SelectedDownloadType;
        SelectedPlatform = resource.Platform;
        _resourceSearchToken++;
        SelectedDownloadProject = null;
        DownloadResults.Clear();
        CanLoadMoreResources = false;
        var project = new ResourceItem(resource.Title, resource.Description, resource.Platform,
            resource.Platform == "Modrinth" ? resource.ProjectId : null,
            resource.Platform == "CurseForge" ? long.Parse(resource.ProjectId) : 0,
            resource.ResourceType, resource.Slug);
        DownloadResults.Add(project);
        SelectedDownloadProject = project;
        await _resourceVersionLoad;
        if (ReferenceEquals(SelectedDownloadProject, project)) FavoriteOpenRequest++;
    }

    [ObservableProperty]
    private string _selectedPlatform = "Modrinth";

    [ObservableProperty]
    private string _selectedDownloadType = "资源包";

    [ObservableProperty]
    private string _downloadSearchText = "";

    [ObservableProperty]
    private ResourceItem? _selectedDownloadProject;

    [ObservableProperty]
    private bool _isDownloadBusy;

    [ObservableProperty]
    private bool _canCancelDownload;

    [ObservableProperty]
    private bool _canSaveDownload;

    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private string _downloadStatus = "搜索 Modrinth 上的资源包/光影";

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = "";

    [ObservableProperty]
    private bool _canDownload;

    [ObservableProperty]
    private bool _canLoadMoreResources;

    private int _resourceSearchOffset;

    partial void OnSelectedDownloadProjectChanged(ResourceItem? value)
    {
        OnPropertyChanged(nameof(HasResourceDetail));
        OnPropertyChanged(nameof(ProjectPageText));
        Favorite.Resource = value is null ? null : new FavoriteResource(value.Platform,
            value.ModrinthId ?? value.CurseForgeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.ResourceType, value.Title, value.Description, value.Slug);
        UpdateDownloadCommands();
        _resourceVersionLoad = LoadResourceVersionsAsync(value);
    }

    partial void OnSelectedResourceVersionChanged(ResourceVersionItem? value)
    {
        foreach (var entry in _resourceVersionEntries)
            entry.IsSelected = ReferenceEquals(entry.Source, value);
        UpdateDownloadCommands();
    }

    private async Task LoadResourceVersionsAsync(ResourceItem? project)
    {
        var token = ++_versionLoadToken;
        _allResourceVersions.Clear();
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        if (project is null)
        {
            ResetResourceVersionFilters();
            return;
        }
        try
        {
            var game = _gameVersion;
            if (project.Platform == "CurseForge")
            {
                var files = await _services.CurseForge.GetFilesAsync(
                    project.CurseForgeId, "", null);
                if (token != _versionLoadToken) return;
                foreach (var file in files)
                {
                    var compatible = string.IsNullOrWhiteSpace(game) ||
                                     file.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase);
                    var versions = string.Join(" / ", file.GameVersions.Take(5));
                    _allResourceVersions.Add(new ResourceVersionItem(
                        file.DisplayName,
                        string.IsNullOrWhiteSpace(versions) ? file.FileName : $"{file.FileName} · {versions}",
                        null, file, file.GameVersions, compatible));
                }
            }
            else
            {
                var versions = await _services.Modrinth.GetVersionsAsync(project.ModrinthId!);
                if (token != _versionLoadToken) return;
                foreach (var version in versions
                             .Where(v => v.Files.Count > 0)
                             .OrderByDescending(v => v.DatePublished))
                {
                    var compatible = string.IsNullOrWhiteSpace(game) ||
                                     version.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase);
                    var gameVersions = string.Join(" / ", version.GameVersions.Take(5));
                    _allResourceVersions.Add(new ResourceVersionItem(
                        version.VersionNumber,
                        $"{version.VersionNumber} · {version.VersionType}" +
                        (string.IsNullOrWhiteSpace(gameVersions) ? "" : $" · {gameVersions}"),
                        version, null, version.GameVersions, compatible));
                }
            }
            RebuildResourceVersionFilters();
        }
        catch (Exception ex)
        {
            if (token == _versionLoadToken)
                DownloadStatus = "获取版本列表失败：" + ex.Message;
        }
    }

    private void ResetResourceVersionFilters()
    {
        _allResourceVersions.Clear();
        _resourceVersionFilterSet = ResourceVersionFilterSet.Create([]);
        _updatingResourceVersionFilters = true;
        ResourceVersionFilters.Clear();
        ResourceVersionFilters.Add("全部");
        SelectedResourceVersionFilter = "全部";
        _updatingResourceVersionFilters = false;
        ResourceVersions.Clear();
        ResourceVersionGroups.Clear();
        _resourceVersionEntries.Clear();
        SelectedResourceVersion = null;
        OnPropertyChanged(nameof(HasResourceVersionFilters));
    }

    private void RebuildResourceVersionFilters()
    {
        _resourceVersionFilterSet = ResourceVersionFilterSet.Create(
            _allResourceVersions.SelectMany(version => version.GameVersions ?? []));
        _updatingResourceVersionFilters = true;
        ResourceVersionFilters.Clear();
        foreach (var filter in _resourceVersionFilterSet.Labels) ResourceVersionFilters.Add(filter);
        var target = string.IsNullOrWhiteSpace(_gameVersion)
            ? "全部"
            : _resourceVersionFilterSet.GroupFor(_gameVersion);
        SelectedResourceVersionFilter = ResourceVersionFilters.Contains(target) ? target : "全部";
        _updatingResourceVersionFilters = false;
        OnPropertyChanged(nameof(HasResourceVersionFilters));
        ApplyResourceVersionFilter();
    }

    private void ApplyResourceVersionFilter()
    {
        var selected = SelectedResourceVersion;
        ResourceVersions.Clear();
        foreach (var version in _allResourceVersions.Where(version =>
                     _resourceVersionFilterSet.Matches(SelectedResourceVersionFilter,
                         version.GameVersions ?? [])))
            ResourceVersions.Add(version);
        SelectedResourceVersion = ResourceVersions.FirstOrDefault(version => ReferenceEquals(version, selected))
                                  ?? ResourceVersions.FirstOrDefault();
        RebuildResourceVersionGroups();
        UpdateDownloadCommands();
        OnPropertyChanged(nameof(SaveFileName));
        DownloadStatus = ResourceVersions.Count == 0
            ? "当前筛选没有版本"
            : $"共 {ResourceVersions.Count} 个版本" +
              (SelectedResourceVersionFilter == "全部" ? "" : $"（筛选：{SelectedResourceVersionFilter}）");
    }

    private void RebuildResourceVersionGroups()
    {
        ResourceVersionGroups.Clear();
        _resourceVersionEntries.Clear();
        var groups = new Dictionary<string, List<ResourceVersionEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in ResourceVersions)
        {
            var captured = version;
            var detail = version.Detail +
                         (version.IsCompatible ? "" : " · 不兼容当前安装目标");
            var gameVersions = version.GameVersions ?? [];
            if (gameVersions.Count == 0)
            {
                AddEntry("其他", captured, detail);
                continue;
            }
            foreach (var gameVersion in gameVersions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_resourceVersionFilterSet.Matches(SelectedResourceVersionFilter, [gameVersion])) continue;
                AddEntry(ResourceVersionFilterSet.CardName(gameVersion), captured, detail);
            }
        }

        var target = string.IsNullOrWhiteSpace(_gameVersion)
            ? null
            : ResourceVersionFilterSet.CardName(_gameVersion);
        foreach (var pair in groups
                     .OrderBy(pair => pair.Key == target ? 0 : pair.Key == "其他" ? 2 : 1)
                     .ThenByDescending(pair => ResourceVersionFilterSet.SortCode(pair.Key))
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entries = pair.Value
                .OrderByDescending(entry => VersionDate(entry.Source))
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ResourceVersionGroups.Add(new ResourceVersionGroup(pair.Key, entries,
                pair.Key == target || groups.Count == 1));
        }
        foreach (var entry in _resourceVersionEntries)
            entry.IsSelected = ReferenceEquals(entry.Source, SelectedResourceVersion);
        return;

        void AddEntry(string group, ResourceVersionItem version, string detail)
        {
            if (!groups.TryGetValue(group, out var entries))
                groups[group] = entries = [];
            if (entries.Any(entry => ReferenceEquals(entry.Source, version))) return;
            var entry = new ResourceVersionEntry(version, version.Label, detail,
                version.IsCompatible, new RelayCommand(() => SelectedResourceVersion = version));
            entries.Add(entry);
            _resourceVersionEntries.Add(entry);
        }
    }

    private static DateTimeOffset VersionDate(object source) => source switch
    {
        ResourceVersionItem { Modrinth: { } modrinth } => modrinth.DatePublished,
        ResourceVersionItem { CurseForge: { } curseForge } => curseForge.FileDate,
        _ => DateTimeOffset.MinValue
    };

    partial void OnSelectedDownloadTypeChanged(string value)
    {
        _hasBrowsedResources = false;
        _resourceSearchToken++;
        DownloadResults.Clear();
        SelectedDownloadProject = null;
        CanLoadMoreResources = false;
        OnPropertyChanged(nameof(IsPluginDownload));
        UpdateDownloadCommands();
    }

    partial void OnSelectedPlatformChanged(string value)
    {
        _hasBrowsedResources = false;
        _resourceSearchToken++;
        DownloadResults.Clear();
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedDownloadProject = null;
        CanLoadMoreResources = false;
        DownloadStatus = value == "CurseForge"
            ? "搜索 CurseForge（需要设置 API Key）"
            : "搜索 Modrinth 上的资源";
    }

    partial void OnIsDownloadBusyChanged(bool value) => UpdateDownloadCommands();

    public async Task LoadAsync()
    {
        var request = ++_instanceLoadRequest;
        var root = _services.Settings.GameDirectory;
        var installed = await Task.Run(new VersionRepository(root).ListInstalled);
        if (request != _instanceLoadRequest || _services.Settings.GameDirectory != root) return;
        var selected = SelectedInstance;
        Instances.Clear();
        foreach (var version in installed) Instances.Add(version.Id);

        SelectedInstance = Instances.FirstOrDefault(id => id == selected)
                             ?? Instances.FirstOrDefault(id =>
                                 id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
                             ?? Instances.FirstOrDefault();

        if (SelectedInstance is null)
            Status = "还没有已安装实例";
        else
            await UpdateAsync();
    }

    partial void OnSelectedInstanceChanged(string? value)
    {
        _resourceSearchToken++;
        _versionLoadToken++;
        _gameVersion = "";
        GameDirectory = "";
        PluginTargetDirectory = "";
        SelectedResourcePack = null;
        SelectedShaderPack = null;
        SelectedWorld = null;
        SelectedWorldBackup = null;
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        UpdateDownloadCommands();
        _ = UpdateAsync();
    }

    partial void OnSelectedResourcePackChanged(InstanceContentItem? value)
    {
        HasResourcePack = value is not null;
        ToggleResourcePackText = value?.IsEnabled == true ? "禁用" : "启用";
    }

    partial void OnSelectedShaderPackChanged(InstanceContentItem? value) =>
        HasShaderPack = value is not null;

    partial void OnSelectedWorldChanged(InstanceContentItem? value) =>
        HasWorld = value is not null;

    private async Task UpdateAsync()
    {
        var request = ++_targetRequest;
        var id = SelectedInstance;
        var root = _services.Settings.GameDirectory;
        if (string.IsNullOrWhiteSpace(id)) return;

        try
        {
            var version = await new VersionRepository(root).GetVersionAsync(id);
            if (request != _targetRequest || _services.Settings.GameDirectory != root) return;
            GameDirectory = IsolationPolicy.GetGameDirectory(
                root, id, version, _services.Settings.IsolationMode);
            _targetGameRoot = root;
            _gameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            var settings = InstanceSettingsStore.Load(
                Path.Combine(root, "versions", id));
            PluginTargetDirectory = string.IsNullOrWhiteSpace(settings.PluginDirectory)
                ? Path.Combine(GameDirectory, "plugins")
                : Path.IsPathRooted(settings.PluginDirectory)
                    ? settings.PluginDirectory
                    : Path.Combine(GameDirectory, settings.PluginDirectory);
            Refresh();
            _resourceVersionLoad = LoadResourceVersionsAsync(SelectedDownloadProject);
        }
        catch (Exception ex)
        {
            if (request == _targetRequest && _services.Settings.GameDirectory == root)
                Status = "读取实例失败：" + ex.Message;
        }
        finally
        {
            if (request == _targetRequest && _services.Settings.GameDirectory == root) UpdateDownloadCommands();
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Fill(ResourcePacks, InstanceContentKind.ResourcePack);
        Fill(ShaderPacks, InstanceContentKind.ShaderPack);
        Fill(Worlds, InstanceContentKind.World);

        SelectedResourcePack = ResourcePacks.FirstOrDefault();
        SelectedShaderPack = ShaderPacks.FirstOrDefault();
        SelectedWorld = Worlds.FirstOrDefault();

        DataPackWorlds.Clear();
        var saves = Path.Combine(GameDirectory, "saves");
        if (Directory.Exists(saves))
            foreach (var directory in Directory.EnumerateDirectories(saves)
                         .OrderByDescending(Directory.GetLastWriteTimeUtc))
                DataPackWorlds.Add(Path.GetFileName(directory));
        if (SelectedDataPackWorld is null || !DataPackWorlds.Contains(SelectedDataPackWorld))
            SelectedDataPackWorld = DataPackWorlds.FirstOrDefault();

        RefreshWorldBackups();
        Status = $"{ResourcePacks.Count} 个资源包 · {ShaderPacks.Count} 个光影 · {Worlds.Count} 个存档 · {WorldBackups.Count} 个备份";
    }

    private void RefreshWorldBackups()
    {
        var selected = SelectedWorldBackup?.Path;
        WorldBackups.Clear();
        var backupDirectory = Path.Combine(WmclPaths.DataDirectory, "backups", "worlds");
        if (Directory.Exists(backupDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(backupDirectory, "*.zip")
                         .OrderByDescending(File.GetLastWriteTime))
            {
                var file = new FileInfo(path);
                WorldBackups.Add(new WorldBackupItem(
                    file.Name, path, file.Length, file.LastWriteTime));
            }
        }
        SelectedWorldBackup = WorldBackups.FirstOrDefault(item => item.Path == selected)
                              ?? WorldBackups.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RestoreWorldBackupAsync()
    {
        if (SelectedWorldBackup is null) return;
        var root = TargetGameRoot;
        var directory = GameDirectory;
        var selected = SelectedWorldBackup;
        if (!await DialogService.ConfirmAsync(
                "恢复存档备份",
                $"确定从 {SelectedWorldBackup.Name} 恢复存档？" + Environment.NewLine +
                "会新建一个存档目录，不覆盖现有存档。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "恢复存档备份");
            var baseName = Path.GetFileNameWithoutExtension(selected.Name);
            var stamp = System.Text.RegularExpressions.Regex.Match(
                baseName, @"-\d{8}-\d{6}$");
            if (stamp.Success)
                baseName = baseName[..^stamp.Length];
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "RestoredWorld";

            var saves = Path.Combine(directory, "saves");
            var targetName = baseName;
            var suffix = 2;
            while (Directory.Exists(Path.Combine(saves, targetName)))
                targetName = $"{baseName}-restored{suffix++}";

            var item = InstanceContentService.Import(
                directory, InstanceContentKind.World,
                selected.Path, targetName);
            Refresh();
            Status = "已恢复存档：" + item.Name;
        }
        catch (Exception ex)
        {
            Status = "恢复存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWorldBackupFolder()
    {
        var path = Path.Combine(WmclPaths.DataDirectory, "backups", "worlds");
        Directory.CreateDirectory(path);
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 打开失败不阻塞页面
        }
    }

    [RelayCommand]
    private async Task DeleteResourcePackAsync()
    {
        if (SelectedResourcePack is null) return;
        var root = TargetGameRoot;
        var selected = SelectedResourcePack;
        if (!await DialogService.ConfirmAsync(
                "删除资源包",
                $"确定删除 {SelectedResourcePack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除资源包");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            Refresh();
            Status = $"已删除资源包 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除资源包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteShaderPackAsync()
    {
        if (SelectedShaderPack is null) return;
        var root = TargetGameRoot;
        var selected = SelectedShaderPack;
        if (!await DialogService.ConfirmAsync(
                "删除光影",
                $"确定删除 {SelectedShaderPack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除光影");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            Refresh();
            Status = $"已删除光影 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除光影失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteWorldAsync()
    {
        if (SelectedWorld is null) return;
        var root = TargetGameRoot;
        var selected = SelectedWorld;
        if (!await DialogService.ConfirmAsync(
                "删除存档",
                $"确定删除存档 {SelectedWorld.Name}？" + Environment.NewLine +
                "此操作不可恢复，建议先备份。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除存档");
            var name = selected.Name;
            InstanceContentService.Delete(selected);
            Refresh();
            Status = $"已删除存档 {name}";
        }
        catch (Exception ex)
        {
            Status = "删除存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void BackupWorld()
    {
        if (SelectedWorld is null) return;
        try
        {
            using var operation = _services.GameOperations.Acquire(TargetGameRoot, "备份存档");
            var destination = Path.Combine(
                WmclPaths.DataDirectory, "backups", "worlds");
            var file = InstanceContentService.BackupWorld(SelectedWorld, destination);
            Status = $"存档已备份：{file}";
        }
        catch (Exception ex)
        {
            Status = "备份存档失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ImportContent()
    {
        if (string.IsNullOrWhiteSpace(ImportPath) || string.IsNullOrWhiteSpace(GameDirectory)) return;
        var kind = SelectedImportType switch
        {
            "光影" => InstanceContentKind.ShaderPack,
            "存档" => InstanceContentKind.World,
            _ => InstanceContentKind.ResourcePack
        };

        try
        {
            using var operation = _services.GameOperations.Acquire(TargetGameRoot, "导入资源");
            var item = InstanceContentService.Import(GameDirectory, kind, ImportPath.Trim());
            Refresh();
            Status = $"已导入{SelectedImportType}：{item.Name}";
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SearchDownloadsAsync()
    {
        if (IsDownloadBusy) return;
        _hasBrowsedResources = true;
        _resourceSearchOffset = 0;
        DownloadResults.Clear();
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedDownloadProject = null;
        CanLoadMoreResources = false;
        await FetchDownloadsAsync(append: false);
    }

    [RelayCommand]
    private async Task LoadMoreDownloadsAsync()
    {
        if (!CanLoadMoreResources || IsDownloadBusy) return;
        await FetchDownloadsAsync(append: true);
    }

    private Task FetchDownloadsAsync(bool append) => _activeResourceSearch = FetchDownloadsCoreAsync(append);

    private async Task FetchDownloadsCoreAsync(bool append)
    {
        if (IsDownloadBusy) return;
        var game = SearchGameVersion.Trim();
        var searchToken = ++_resourceSearchToken;
        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        if (projectType == "world" && SelectedPlatform != "CurseForge")
        {
            DownloadStatus = "世界下载目前仅支持 CurseForge";
            return;
        }
        IsDownloadBusy = true;
        DownloadStatus = $"正在搜索 {SelectedPlatform} {SelectedDownloadType}…";
        try
        {
            if (SelectedPlatform == "CurseForge")
            {
                if (!_services.CurseForge.IsConfigured)
                    throw new InvalidOperationException("未配置 CurseForge API Key，请在设置中填写。");
                var classId = SelectedDownloadType switch
                {
                    "光影" => CurseForgeClient.ClassIdShaders,
                    "数据包" => CurseForgeClient.ClassIdDataPacks,
                    "世界" => CurseForgeClient.ClassIdWorlds,
                    "插件" => CurseForgeClient.ClassIdPlugins,
                    _ => CurseForgeClient.ClassIdResourcePacks
                };
                var result = await _services.CurseForge.SearchAsync(
                    DownloadSearchText, game, "", 20, default, classId, false,
                    _resourceSearchOffset);
                if (searchToken != _resourceSearchToken) return;
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Name, project.Summary, "CurseForge", null, project.Id, projectType, project.Slug, project.LogoUrl));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalCount;
                DownloadStatus = $"共 {result.TotalCount} 个 CurseForge 结果";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    DownloadSearchText, game, "",
                    20, default, projectType, _resourceSearchOffset);
                if (searchToken != _resourceSearchToken) return;
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Title, project.Description, "Modrinth", project.Id, 0, projectType, project.Slug, project.IconUrl));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalHits;
                DownloadStatus = $"共 {result.TotalHits} 个 Modrinth 结果";
            }
        }
        catch (Exception ex)
        {
            if (searchToken == _resourceSearchToken) DownloadStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsDownloadBusy = false;
            UpdateDownloadCommands();
        }
    }

    public string ProjectPageText => SelectedDownloadProject is null
        ? "打开项目页"
        : SelectedDownloadProject.Platform == "CurseForge" ? "转到 CurseForge" : "转到 Modrinth";

    [RelayCommand]
    private void CopyProjectName()
    {
        var name = SelectedDownloadProject?.Title;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(name);
            DownloadStatus = "已复制名称：" + name;
        }
    }

    [RelayCommand]
    private void OpenSelectedProject()
    {
        if (SelectedDownloadProject is null) return;
        var selected = SelectedDownloadProject;
        var type = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        var url = selected.Platform == "CurseForge"
            ? selected.CurseForgeId > 0
                ? $"https://www.curseforge.com/minecraft/{type switch
                {
                    "shader" => "shaders",
                    "datapack" => "data-packs",
                    "world" => "worlds",
                    "plugin" => "bukkit-plugins",
                    _ => "texture-packs"
                }}/{selected.CurseForgeId}"
                : null
            : string.IsNullOrWhiteSpace(selected.ModrinthId)
                ? null
                : $"https://modrinth.com/{type}/{selected.ModrinthId}";
        if (url is null)
        {
            DownloadStatus = "该项目没有可用的项目页链接";
            return;
        }
        try
        {
            WMCL.App.Services.ExternalLauncher.Open(url);
            DownloadStatus = "已在浏览器中打开项目页";
        }
        catch (Exception ex)
        {
            DownloadStatus = "打开项目页失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallDownloadAsync()
    {
        if (SelectedDownloadProject is null || IsDownloadBusy) return;
        var root = TargetGameRoot;

        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        if (projectType == "world")
        {
            await InstallWorldAsync();
            return;
        }
        var target = projectType switch
        {
            "datapack" => Path.Combine(
                GameDirectory, "saves", SelectedDataPackWorld ?? "", "datapacks"),
            "shader" => Path.Combine(GameDirectory, "shaderpacks"),
            "plugin" => string.IsNullOrWhiteSpace(PluginTargetDirectory)
                ? Path.Combine(GameDirectory, "plugins")
                : PluginTargetDirectory,
            _ => Path.Combine(GameDirectory, "resourcepacks")
        };
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装 {SelectedDownloadProject.Title}…";
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装资源");
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = update.Detail ?? update.Stage;
            });
            IReadOnlyList<string> files;
            if (SelectedDownloadProject.Platform == "CurseForge")
            {
                var result = SelectedResourceVersion?.CurseForge is { } selected
                    ? await _services.CurseForge.InstallContentVersionAsync(
                        SelectedDownloadProject.CurseForgeId, selected.Id, target, progress, ct)
                    : await _services.CurseForge.InstallContentAsync(
                        SelectedDownloadProject.CurseForgeId, _gameVersion, target,
                        SelectedDownloadType switch
                        {
                            "光影" => CurseForgeClient.ClassIdShaders,
                            "数据包" => CurseForgeClient.ClassIdDataPacks,
                            _ => CurseForgeClient.ClassIdResourcePacks
                        }, progress, ct);
                files = result.Files;
            }
            else
            {
                var result = SelectedResourceVersion?.Modrinth is { } selected
                    ? await _services.Modrinth.InstallContentVersionAsync(
                        selected, target, projectType, progress, ct)
                    : await _services.Modrinth.InstallContentAsync(
                        SelectedDownloadProject.ModrinthId!, _gameVersion, target, projectType, progress, ct);
                files = result.Files;
            }
            DownloadStatus = files.Count == 0
                ? "文件已存在，无需重复安装"
                : $"已安装：{string.Join("，", files)}";
            DownloadProgress = 100;
            Refresh();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DownloadStatus = "已取消安装";
        }
        catch (Exception ex)
        {
            DownloadStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private async Task InstallWorldAsync()
    {
        var project = SelectedDownloadProject!;
        var selectedVersion = SelectedResourceVersion;
        var gameVersion = _gameVersion;
        var gameDirectory = GameDirectory;
        var root = TargetGameRoot;
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装世界 {project.Title}…";
        string? temp = null;
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装世界");
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = update.Detail ?? update.Stage;
            });
            var file = selectedVersion?.CurseForge
                       ?? await _services.CurseForge.FindFileAsync(
                           project.CurseForgeId, gameVersion, null)
                       ?? throw new InvalidOperationException("没有找到适用于当前版本的世界文件。");
            temp = Path.Combine(Path.GetTempPath(), "wmcl-world-" + Guid.NewGuid().ToString("N") + ".zip");
            await CurseForgeClient.DownloadFileAsync(file, temp, progress, ct);
            var destination = WorldInstaller.Install(
                temp, Path.Combine(gameDirectory, "saves"), project.Title);
            DownloadStatus = "世界已安装：" + Path.GetFileName(destination);
            DownloadProgress = 100;
            Refresh();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DownloadStatus = "已取消安装世界";
        }
        catch (Exception ex)
        {
            DownloadStatus = "世界安装失败：" + ex.Message;
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private void UpdateDownloadCommands()
    {
        CanDownload = !IsDownloadBusy &&
                      !string.IsNullOrWhiteSpace(GameDirectory) &&
                      SelectedDownloadProject is not null &&
                      (!RequiresDataPackWorld || !string.IsNullOrWhiteSpace(SelectedDataPackWorld));
        CanCancelDownload = IsDownloadBusy && _downloadCts is not null;
        CanSaveDownload = !IsDownloadBusy && SelectedDownloadProject is not null &&
                          SelectedDownloadType != "整合包" &&
                          (SelectedResourceVersion ?? ResourceVersions.FirstOrDefault(version => version.IsCompatible)
                              ?? ResourceVersions.FirstOrDefault()) is not null;
    }

    public string SaveFileName
    {
        get
        {
            var version = SelectedResourceVersion ??
                          ResourceVersions.FirstOrDefault(item => item.IsCompatible) ??
                          ResourceVersions.FirstOrDefault();
            return version?.FileName ?? "resource.zip";
        }
    }

    public async Task SaveDownloadAsync(string destination, CancellationToken ct = default)
    {
        var project = SelectedDownloadProject;
        var version = SelectedResourceVersion ??
                      ResourceVersions.FirstOrDefault(item => item.IsCompatible) ??
                      ResourceVersions.FirstOrDefault();
        if (project is null || version is null || IsDownloadBusy) return;
        SelectedResourceVersion = version;
        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        var temp = Path.Combine(Path.GetTempPath(), "wmcl-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在保存 {version.FileName}…";
        _downloadCts?.Dispose();
        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = update.Detail ?? update.Stage;
            });
            string? file;
            if (version.CurseForge is { } curse)
            {
                var result = await _services.CurseForge.InstallContentVersionAsync(
                    project.CurseForgeId, curse.Id, temp, progress, token);
                file = result.Files.FirstOrDefault();
            }
            else if (version.Modrinth is { } modrinth)
            {
                var result = await _services.Modrinth.InstallContentVersionAsync(
                    modrinth, temp, projectType, progress, token);
                file = result.Files.FirstOrDefault();
            }
            else
            {
                throw new InvalidOperationException("该版本没有可下载文件。");
            }
            if (string.IsNullOrWhiteSpace(file))
                throw new InvalidOperationException("下载结果为空。");
            var source = Path.Combine(temp, file);
            if (!File.Exists(source))
            {
                source = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories).FirstOrDefault()
                         ?? throw new FileNotFoundException("下载文件不存在。", source);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
            File.Move(source, destination, true);
            DownloadProgress = 100;
            DownloadStatus = "已保存：" + destination;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            DownloadStatus = "已取消保存";
        }
        catch (Exception ex)
        {
            DownloadStatus = "保存失败：" + ex.Message;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
        DownloadStatus = "正在取消安装…";
    }

    [RelayCommand]
    private void OpenResourcePacks() => OpenFolder(InstanceContentKind.ResourcePack);

    [RelayCommand]
    private void OpenShaderPacks() => OpenFolder(InstanceContentKind.ShaderPack);

    [RelayCommand]
    private void OpenWorlds() => OpenFolder(InstanceContentKind.World);

    private void Fill(
        ObservableCollection<InstanceContentItem> target,
        InstanceContentKind kind)
    {
        target.Clear();
        foreach (var item in InstanceContentService.List(GameDirectory, kind))
            target.Add(item);
    }

    private void OpenFolder(InstanceContentKind kind)
    {
        var path = Path.Combine(GameDirectory, InstanceContentService.FolderName(kind));
        Directory.CreateDirectory(path);
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // ignore
        }
    }
}


public sealed record ResourceItem(
    string Title,
    string Description,
    string Platform,
    string? ModrinthId,
    long CurseForgeId,
    string ResourceType = "resourcepack",
    string Slug = "",
    string? IconUrl = null);


public sealed record ResourceVersionItem(
    string Label,
    string Detail,
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge,
    IReadOnlyList<string>? GameVersions = null,
    bool IsCompatible = true)
{
    public string FileName => CurseForge?.FileName ??
        Modrinth?.Files.FirstOrDefault(file => file.Primary)?.Filename ??
        Modrinth?.Files.FirstOrDefault()?.Filename ?? "resource.zip";
}


public sealed record WorldBackupItem(
    string Name, string Path, long Size, DateTime Modified)
{
    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024d / 1024d:0.0} MB"
        : $"{Size / 1024d:0.0} KB";

    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
}
