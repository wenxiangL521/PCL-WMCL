using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Local;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Features.Instances;

public partial class InstanceContentViewModel : ViewModelBase, IDisposable, ITaskRetrySource
{
    private readonly IResourceServices _services;
    private int _instanceLoadRequest;
    private string _gameVersion = "";
    private string? _targetGameRoot;
    private string TargetGameRoot => _targetGameRoot ?? _services.Settings.GameDirectory;

    /// <summary>最近一次失败任务的堆栈载荷（先于状态赋值，供任务中心在 Refresh 时消费）。</summary>
    public TaskFailureInfo? LastTaskFailure { get; private set; }

    private TaskRetryPayload? _lastRetryPayload;

    public TaskRetryPayload? CaptureRetryPayload() => _lastRetryPayload;

    public void ClearTaskFailure()
    {
        LastTaskFailure = null;
        _lastRetryPayload = null;
    }

    public async Task<bool> RetryAsync(TaskRetryPayload payload)
    {
        if (payload.Kind != TaskRetryKind.ResourceInstall) return false;
        if (IsDownloadBusy)
        {
            DownloadStatus = "重试失败：已有任务进行中";
            return false;
        }
        if (!string.Equals(TargetGameRoot, payload.GameRoot, StringComparison.Ordinal))
        {
            DownloadStatus = "重试失败：实例目录已变化，请重新选择后安装";
            return false;
        }
        if (SelectedDownloadProject is null)
        {
            DownloadStatus = "重试失败：资源选择已变化，请重新选择后安装";
            return false;
        }
        await InstallDownloadAsync();
        return true;
    }

    private readonly Func<string, string, Task<bool>> _confirmContentDelete;

    public InstanceContentViewModel(IResourceServices services, Func<string, string, Task<bool>>? confirmContentDelete = null)
    {
        _services = services;
        _confirmContentDelete = confirmContentDelete ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        Favorite = new ResourceFavoritePickerViewModel(services.ResourceFavorites);
        DownloadResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDownloadResults));
            OnPropertyChanged(nameof(ShowDownloadResults));
            OnPropertyChanged(nameof(ShowSearchEmpty));
        };
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

    /// <summary>请求跳转到对应下载页面（由主窗口处理导航）。</summary>
    public event EventHandler<string>? DownloadRequested;

    /// <summary>顶部「下载 XXX」按钮的文案。</summary>
    public string DownloadButtonLabel => "下载" + ContentTypeLabel;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenDownload()
    {
        var target = ShowShaders ? "content-shader"
            : ShowWorlds ? "content-world"
            : ShowDataPacks ? "content-datapack"
            : "content-resource";
        DownloadRequested?.Invoke(this, target);
    }
    public bool IsPluginDownload => WMCL.App.ViewModels.Content.ContentTypeState.From(SelectedDownloadType).IsPlugin;
    public bool RequiresDataPackWorld => ShowDataPacks;
    public string ContentTypeLabel =>
        WMCL.App.ViewModels.Content.ContentTypeLabel.Of(ShowWorlds, ShowDataPacks, ShowShaders);
    public string DownloadExample => WMCL.App.ViewModels.Content.ContentTypeState.From(SelectedDownloadType).SearchExample;
    public ObservableCollection<string> DataPackWorlds { get; } = [];

    [ObservableProperty]
    private string? _selectedDataPackWorld;

    partial void OnSelectedDataPackWorldChanged(string? value) =>
        UpdateDownloadCommands();

    public void SetMode(string key)
    {
        _ = EnsureSearchGameVersionsAsync();
        ShowResourcePacks = key is "content" or "content-resource";
        var state = WMCL.App.ViewModels.Content.ContentTypeState.From(key);
        ShowShaders = state.Shaders;
        ShowWorlds = state.Worlds;
        ShowDataPacks = state.DataPacks;
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

    public bool IsSearchError => DownloadStatus.StartsWith("搜索失败", StringComparison.Ordinal)
        || DownloadStatus.StartsWith("未配置", StringComparison.Ordinal);

    public bool ShowSearchEmpty => !IsDownloadBusy && !HasDownloadResults && !IsSearchError;

    partial void OnDownloadStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearchError));
        OnPropertyChanged(nameof(ShowSearchEmpty));
    }

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
        OnPropertyChanged(nameof(IsPageLoading));
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
        SelectedResourceVersion = WMCL.App.ViewModels.Content.ContentVersionPicker.Pick(ResourceVersions, selected);
        RebuildResourceVersionGroups();
        UpdateDownloadCommands();
        OnPropertyChanged(nameof(SaveFileName));
        DownloadStatus = WMCL.App.ViewModels.Content.ContentVersionPicker.Describe(ResourceVersions.Count, SelectedResourceVersionFilter);
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

    /// <summary>程序内改来源时不要触发搜索。</summary>
    internal bool SuppressSourceAutoSearch { get; set; }

    partial void OnSelectedPlatformChanged(string value)
    {
        var hadResults = _hasBrowsedResources;
        _hasBrowsedResources = false;
        _resourceSearchToken++;
        DownloadResults.Clear();
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedDownloadProject = null;
        CanLoadMoreResources = false;
        DownloadStatus = value == "CurseForge"
            ? "搜索 CurseForge（无 Key 时自动走镜像）"
            : "搜索 Modrinth 上的资源";
        if (!SuppressSourceAutoSearch && hadResults && IsDownloadPage && !IsDownloadBusy)
            _ = SearchDownloadsCommand.ExecuteAsync(null);
    }

    /// <summary>本地资源（资源包/光影/存档）加载状态。</summary>
    [ObservableProperty]
    private bool _isLoadingContent;

    /// <summary>页面级加载态：下载页搜索中，或管理页正在读取本地资源。</summary>
    public bool IsPageLoading =>
        (IsDownloadPage && IsDownloadBusy && !HasResourceDetail) || (!IsDownloadPage && IsLoadingContent);

    partial void OnIsLoadingContentChanged(bool value) => OnPropertyChanged(nameof(IsPageLoading));

    /// <summary>结果列表在搜索结束、遮罩消失后才显示，让入场动画可见。</summary>
    public bool ShowDownloadResults => HasDownloadResults && !IsDownloadBusy;

    partial void OnIsDownloadBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSearchEmpty));
        OnPropertyChanged(nameof(IsPageLoading));
        OnPropertyChanged(nameof(ShowDownloadResults));
        UpdateDownloadCommands();
    }

    partial void OnIsDownloadPageChanged(bool value) => OnPropertyChanged(nameof(IsPageLoading));

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

    [ObservableProperty]
    private bool _isEmbedded;

    private Task _targetPreparationTask = Task.CompletedTask;

    internal async Task OpenManagementAsync(string id, string key)
    {
        IsEmbedded = true;
        IsDownloadPage = false;
        SetMode(key);
        if (SelectedInstance != id) SelectedInstance = id;
        else _targetPreparationTask = UpdateAsync();
        await _targetPreparationTask;
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
        _targetPreparationTask = UpdateAsync();
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

    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
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
