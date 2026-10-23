using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Local;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private int _instanceLoadRequest;
    private readonly FabricInstaller _fabric;
    private readonly ForgelikeInstaller _forgelike;
    private readonly OptiFineService _optifine;
    private readonly List<FabricLoaderInfo> _loaderInfos = [];
    private readonly Func<string, string, Task<IReadOnlyList<FabricLoaderInfo>>> _getLoaders;
    private readonly Func<ForgelikeKind, string, Task<IReadOnlyList<string>>> _getForgelikeVersions;
    private readonly Func<string, Task<IReadOnlyList<ForgeVersionInfo>>> _getForgeVersionDetails;
    private readonly Func<string, Task<IReadOnlyList<OptiFineVersion>>> _getOptiFineVersions;
    private readonly Func<string, Task<IReadOnlyList<LiteLoaderVersion>>> _getLiteLoaderVersions;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<FabricLoaderInfo>>> _getLoadersCt;
    private readonly Func<ForgelikeKind, string, CancellationToken, Task<IReadOnlyList<string>>> _getForgelikeVersionsCt;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ForgeVersionInfo>>> _getForgeVersionDetailsCt;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<OptiFineVersion>>> _getOptiFineVersionsCt;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<LiteLoaderVersion>>> _getLiteLoaderVersionsCt;
    private Task? _loaderLoadTask;
    private Task? _forgelikeLoadTask;
    private Task? _optiFineLoadTask;
    private string? _loaderLoadKey;
    private string? _forgelikeLoadKey;
    private string? _optiFineLoadKey;
    private CancellationTokenSource? _cts;

    public ModsViewModel(AppServices services)
        : this(services, null, null, null)
    {
    }

    internal ModsViewModel(AppServices services,
        Func<string, string, Task<IReadOnlyList<FabricLoaderInfo>>>? getLoaders,
        Func<ForgelikeKind, string, Task<IReadOnlyList<string>>>? getForgelikeVersions,
        Func<string, Task<IReadOnlyList<OptiFineVersion>>>? getOptiFineVersions,
        Func<Task<IReadOnlyList<string>>>? getOfficialVersions = null,
        Func<StandaloneLoaderInstallRequest, IProgress<JobProgress>, IProgress<string>?, CancellationToken, Task<string>>? installComponent = null,
        Func<string, Task<IReadOnlyList<ForgeVersionInfo>>>? getForgeVersionDetails = null,
        Func<string, Task<IReadOnlyList<LiteLoaderVersion>>>? getLiteLoaderVersions = null,
        Func<string, string, Task<bool>>? confirmModUpdate = null)
    {
        _services = services;
        _confirmModUpdate = confirmModUpdate ?? ((title, message) =>
            DialogService.ConfirmAsync(title, message, "我已了解风险，继续更新"));
        services.GameDirectoryChanged += (_, _) => InvalidateModUpdates();
        Favorite = new ResourceFavoritePickerViewModel(services.ResourceFavorites);
        _fabric = services.Fabric;
        _forgelike = services.Forgelike;
        _optifine = services.Optifine;
        _getLoaders = getLoaders ?? ((kind, game) => FabricInstallerFor(kind).GetLoadersAsync(game));
        _getForgelikeVersions = getForgelikeVersions ?? ((kind, game) => _forgelike.GetVersionsAsync(kind, game));
        _getForgeVersionDetails = getForgeVersionDetails ?? (getForgelikeVersions is null
            ? game => _forgelike.GetForgeVersionDetailsAsync(game)
            : async game => (await getForgelikeVersions(ForgelikeKind.Forge, game))
                .Select(version => new ForgeVersionInfo(version, null)).ToArray());
        _getOptiFineVersions = getOptiFineVersions ?? (game => _optifine.GetVersionsAsync(game));
        _getLiteLoaderVersions = getLiteLoaderVersions ?? (getLoaders is not null || getForgelikeVersions is not null || getOptiFineVersions is not null
            ? _ => Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([])
            : game => services.LiteLoader.GetVersionsAsync(game));
        _getOfficialVersions = getOfficialVersions ?? (async () =>
            (await new VersionRepository(_services.Settings.GameDirectory).GetManifestAsync()).Versions.Select(version => version.Id).ToArray());
        _getLoadersCt = (kind, game, ct) => getLoaders is null
            ? FabricInstallerFor(kind).GetLoadersAsync(game, ct)
            : getLoaders(kind, game);
        _getForgelikeVersionsCt = (kind, game, ct) => getForgelikeVersions is null
            ? _forgelike.GetVersionsAsync(kind, game, ct)
            : getForgelikeVersions(kind, game);
        _getForgeVersionDetailsCt = (game, ct) => getForgeVersionDetails is not null
            ? getForgeVersionDetails(game)
            : getForgelikeVersions is not null
                ? ForgeDetailsFromInjectedAsync(getForgelikeVersions, game)
                : _forgelike.GetForgeVersionDetailsAsync(game, ct);
        _getOptiFineVersionsCt = (game, ct) => getOptiFineVersions is null
            ? _optifine.GetVersionsAsync(game, ct)
            : getOptiFineVersions(game);
        _getLiteLoaderVersionsCt = (game, ct) => getLiteLoaderVersions is not null
            ? getLiteLoaderVersions(game)
            : getLoaders is null && getForgelikeVersions is null && getOptiFineVersions is null
                ? services.LiteLoader.GetVersionsAsync(game, ct)
                : Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([]);
        _installComponent = installComponent ?? new StandaloneLoaderInstallService(services).InstallAsync;
        SearchResults.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasSearchResults));
    }

    private static async Task<IReadOnlyList<ForgeVersionInfo>> ForgeDetailsFromInjectedAsync(
        Func<ForgelikeKind, string, Task<IReadOnlyList<string>>> source, string game) =>
        (await source(ForgelikeKind.Forge, game))
            .Select(version => new ForgeVersionInfo(version, null)).ToArray();

    public event EventHandler? VersionInstalled;

    public ObservableCollection<string> GameVersions { get; } = [];
    public ObservableCollection<string> Loaders { get; } = [];
    public ObservableCollection<string> ModInstances { get; } = [];
    public bool HasGameVersions => GameVersions.Count > 0;
    public bool IsModDisabled => !string.IsNullOrWhiteSpace(SelectedModInstance) &&
                                 _modGameVersion is not null &&
                                 _modLoader == "vanilla";
    public event EventHandler? DownloadRequested;
    public event EventHandler? VersionSelectRequested;

    [RelayCommand]
    private void GoDownload() => DownloadRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void GoVersionSelect() => VersionSelectRequested?.Invoke(this, EventArgs.Empty);

    private string? _modGameVersion;
    private string _modLoader = "vanilla";
    private string? _modTargetRoot;
    private string ModTargetRoot => _modTargetRoot ?? _services.Settings.GameDirectory;

    [ObservableProperty]
    private string? _selectedGameVersion;

    [ObservableProperty]
    private string? _selectedModInstance;

    [ObservableProperty]
    private string _modsDirectory = "";

    [ObservableProperty]
    private string _modTargetInfo = "选择 Mod 目标实例";

    public ObservableCollection<LocalMod> LocalMods { get; } = [];

    [ObservableProperty]
    private LocalMod? _selectedLocalMod;

    [ObservableProperty]
    private bool _hasSelectedLocalMod;

    [ObservableProperty]
    private string _toggleModText = "禁用";

    [ObservableProperty]
    private string _localModStatus = "尚未读取实例 Mod";

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _canUpdateSelectedMod;

    [ObservableProperty]
    private bool _canUpdateAllMods;

    [ObservableProperty]
    private bool _isLoaderMode = true;

    [ObservableProperty]
    private bool _isSearchMode;

    public void SetMode(string key)
    {
        IsLoaderMode = key == "loaders";
        IsSearchMode = !IsLoaderMode;
    }

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private string? _selectedLoader;

    [ObservableProperty]
    private string _status = "选择已安装的原版版本，安装 Fabric";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    public ObservableCollection<OptiFineVersion> OptiFineVersions { get; } = [];

    [ObservableProperty]
    private OptiFineVersion? _selectedOptiFineVersion;

    [ObservableProperty]
    private bool _isOptiFineBusy;

    [ObservableProperty]
    private bool _canInstallOptiFine;

    [ObservableProperty]
    private string _optiFineStatus = "选择游戏版本后安装 OptiFine";

    [ObservableProperty]
    private double _optiFineProgress;

    [ObservableProperty]
    private string _optiFineProgressText = "";

    private CancellationTokenSource? _optiFineCts;
    private int _loaderRequest;
    private int _forgelikeRequest;
    private int _optiFineRequest;

    public ObservableCollection<string> ForgelikeKinds { get; } = ["Forge", "NeoForge"];
    public ObservableCollection<string> FabricKinds { get; } = ["Fabric", "Quilt", "LegacyFabric"];
    public ObservableCollection<string> ForgelikeVersions { get; } = [];

    [ObservableProperty]
    private string _selectedForgelikeKind = "Forge";

    [ObservableProperty]
    private string _selectedFabricKind = "Fabric";

    [ObservableProperty]
    private string? _selectedForgelikeVersion;

    [ObservableProperty]
    private string _forgelikeStatus = "选择 Forge/NeoForge 版本";

    [ObservableProperty]
    private bool _isForgelikeBusy;

    [ObservableProperty]
    private bool _canInstallForgelike;

    [ObservableProperty]
    private double _forgelikeProgress;

    [ObservableProperty]
    private string _forgelikeProgressText = "";

    private CancellationTokenSource? _forgelikeCts;
    private Dictionary<string, ModUpdateInfo> _modUpdates = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ModrinthProject> SearchResults { get; } = [];
    public bool HasSearchResults => SearchResults.Count > 0;
    public ObservableCollection<ModVersionItem> ModVersions { get; } = [];
    public ObservableCollection<ResourceVersionGroup> ModVersionGroups { get; } = [];
    public ObservableCollection<string> ModVersionFilters { get; } = ["全部"];
    public bool HasModVersionFilters => ModVersionFilters.Count > 2;
    public ObservableCollection<string> ModSources { get; } = ["Modrinth", "CurseForge"];

    [ObservableProperty]
    private string _selectedModVersionFilter = "全部";

    partial void OnSelectedModVersionFilterChanged(string value)
    {
        if (!_updatingModVersionFilters) ApplyModVersionFilter();
    }

    [ObservableProperty]
    private string _selectedModSource = "Modrinth";

    partial void OnSelectedModSourceChanged(string value)
    {
        _modSearchToken++;
        SearchResults.Clear();
        ModVersions.Clear();
        SelectedModVersion = null;
        SelectedMod = null;
        CanLoadMore = false;
        ModStatus = value == "CurseForge"
            ? "搜索 CurseForge（需在设置中填写 API Key）"
            : "搜索 Modrinth";
        OnPropertyChanged(nameof(ProjectPageText));
        UpdateModCommands();
    }

    public string ProjectPageText => SelectedModSource == "CurseForge" ? "转到 CurseForge" : "转到 Modrinth";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ModrinthProject? _selectedMod;

    [ObservableProperty]
    private ModVersionItem? _selectedModVersion;

    private int _versionLoadToken;
    private readonly List<ModVersionItem> _allModVersions = [];
    private readonly List<ResourceVersionEntry> _modVersionEntries = [];
    private ResourceVersionFilterSet _modVersionFilterSet = ResourceVersionFilterSet.Create([]);
    private bool _updatingModVersionFilters;
    private int _searchOffset;
    private int _modSearchToken;
    private int _modTargetRequest;
    private Task _modVersionLoad = Task.CompletedTask;

    public ResourceFavoritePickerViewModel Favorite { get; }

    [ObservableProperty]
    private int _favoriteOpenRequest;

    public async Task OpenFavoriteAsync(FavoriteResource resource)
    {
        if (IsModBusy) throw new InvalidOperationException("请等待 Mod 搜索或安装结束后再打开收藏。");
        if (!resource.IsValid() || resource.ResourceType != "mod")
            throw new InvalidOperationException("该收藏不是有效的 Mod 项目。");
        SetMode("mods");
        SelectedModSource = resource.Platform;
        _modSearchToken++;
        SelectedMod = null;
        SearchResults.Clear();
        CanLoadMore = false;
        var project = new ModrinthProject(resource.ProjectId, resource.Slug, resource.Title,
            resource.Description, "mod", 0, null, []);
        SearchResults.Add(project);
        SelectedMod = project;
        await WaitForModSelectionAsync();
        if (ReferenceEquals(SelectedMod, project)) FavoriteOpenRequest++;
    }

    internal async Task WaitForModSelectionAsync()
    {
        await _modVersionLoad;
        await _modDependencyLoad;
    }

    [ObservableProperty]
    private string _modStatus = "搜索 Modrinth 上的 Fabric Mod";

    [ObservableProperty]
    private bool _isModBusy;

    [ObservableProperty]
    private bool _canInstallMod;

    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private double _modProgress;

    [ObservableProperty]
    private string _modProgressText = "";

    public async Task LoadAsync()
    {
        var request = ++_instanceLoadRequest;
        var root = _services.Settings.GameDirectory;
        var repository = new VersionRepository(root);
        _loaderLoadKey = null;
        _forgelikeLoadKey = null;
        _optiFineLoadKey = null;
        var installed = await Task.Run(repository.ListInstalled);
        var vanilla = new Dictionary<string, ComponentGameVersion>();
        Task<IReadOnlyList<string>>? officialVersions = null;
        foreach (var version in installed)
        {
            try
            {
                var json = await repository.GetVersionAsync(version.Id);
                if (!MinecraftVersionUtil.IsModable(json))
                {
                    var minecraft = MinecraftVersionUtil.GetBaseGameVersion(version.Id, json);
                    try
                    {
                        if (json.GameVersionIdentity is { IsReliable: false } ||
                            json.GameVersionIdentity?.Source == MinecraftVersionIdentitySource.ProfileId && minecraft != json.Id)
                            vanilla.Add(version.Id, new(null, "无法识别 Minecraft 基座，请先在实例修改页确认版本信息"));
                        else if ((minecraft == version.Id || json.GameVersionIdentity?.Source == MinecraftVersionIdentitySource.ProfileId) &&
                            !(await (officialVersions ??= _getOfficialVersions())).Contains(minecraft))
                            vanilla.Add(version.Id, new(null, "无法识别 Minecraft 基座，请先修复实例版本信息"));
                        else
                            vanilla.Add(version.Id, new(minecraft, null));
                    }
                    catch (Exception exception)
                    {
                        vanilla.Add(version.Id, new(null, "无法核对官方版本目录：" + exception.Message));
                    }
                }
            }
            catch
            {
                // 损坏的实例跳过
            }
        }

        if (request != _instanceLoadRequest || _services.Settings.GameDirectory != root) return;
        var previousGame = SelectedVanillaInstance;
        _componentGameRoot = root;
        _componentGameVersions = vanilla;
        GameVersions.Clear();
        foreach (var id in vanilla.Keys) GameVersions.Add(id);
        OnPropertyChanged(nameof(HasGameVersions));

        SelectedVanillaInstance = previousGame is not null && GameVersions.Contains(previousGame)
            ? previousGame : GameVersions.FirstOrDefault();
        UpdateComponentGameVersion();
        if (SelectedGameVersion is not null)
            await Task.WhenAll(LoadLoadersAsync(), LoadForgelikeVersionsAsync(), LoadOptiFineVersionsAsync());
        else if (SelectedVanillaInstance is null)
            Status = "还没有原版实例，请先在“下载”页安装一个版本";

        if (request != _instanceLoadRequest || _services.Settings.GameDirectory != root) return;
        var previousInstance = SelectedModInstance;
        ModInstances.Clear();
        foreach (var version in installed)
            ModInstances.Add(version.Id);

        SelectedModInstance = ModInstances.FirstOrDefault(id => id == previousInstance)
                             ?? ModInstances.FirstOrDefault(id =>
                                 id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
                             ?? ModInstances.FirstOrDefault();
        if (SelectedModInstance is not null)
            await UpdateModTargetAsync();
    }

    partial void OnSelectedGameVersionChanged(string? value)
    {
        UpdateModCommands();
        _ = LoadLoadersAsync();
        _ = LoadForgelikeVersionsAsync();
        _ = LoadOptiFineVersionsAsync();
    }

    partial void OnSelectedForgelikeKindChanged(string value) => _ = LoadForgelikeVersionsAsync();

    partial void OnSelectedFabricKindChanged(string value) => _ = LoadLoadersAsync();

    partial void OnSelectedForgelikeVersionChanged(string? value) => UpdateForgelikeCommands();

    partial void OnSelectedOptiFineVersionChanged(OptiFineVersion? value) =>
        UpdateOptiFineCommands();

    partial void OnIsOptiFineBusyChanged(bool value) => UpdateOptiFineCommands();

    partial void OnIsForgelikeBusyChanged(bool value) => UpdateForgelikeCommands();

    partial void OnSelectedModChanged(ModrinthProject? value)
    {
        OnPropertyChanged(nameof(HasResourceDetail));
        OnPropertyChanged(nameof(ProjectPageText));
        Favorite.Resource = value is null ? null : new FavoriteResource(
            SelectedModSource, value.Id, "mod", value.Title, value.Description, value.Slug);
        UpdateModCommands();
        _modVersionLoad = LoadModVersionsAsync(value);
    }

    partial void OnSelectedModVersionChanged(ModVersionItem? value)
    {
        foreach (var entry in _modVersionEntries)
            entry.IsSelected = ReferenceEquals(entry.Source, value);
        _modDependencyLoad = RefreshModDependenciesAsync();
    }

    partial void OnSelectedModInstanceChanged(string? value)
    {
        _modSearchToken++;
        _versionLoadToken++;
        ModVersions.Clear();
        SelectedModVersion = null;
        _ = UpdateModTargetAsync();
    }

    partial void OnSelectedLocalModChanged(LocalMod? value)
    {
        HasSelectedLocalMod = value is not null;
        ToggleModText = value?.IsEnabled == true ? "禁用" : "启用";
        UpdateModUpdateCommands();
    }

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        UpdateModUpdateCommands();
    }

    partial void OnModsDirectoryChanged(string value) => RefreshLocalMods();

    partial void OnIsModBusyChanged(bool value) => UpdateModCommands();

    partial void OnSelectedLoaderChanged(string? value) => UpdateCommands();

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && _cts is null && HasCurrentComponentGame && SelectedLoader is not null;
        UpdateModCommands();
        UpdateForgelikeCommands();
    }

    private void UpdateForgelikeCommands()
    {
        CanInstallForgelike = !IsForgelikeBusy && _forgelikeCts is null &&
                              HasCurrentComponentGame &&
                              SelectedForgelikeVersion is not null;
    }

    private void UpdateOptiFineCommands()
    {
        CanInstallOptiFine = !IsOptiFineBusy && _optiFineCts is null &&
                             HasCurrentComponentGame &&
                             SelectedOptiFineVersion is not null;
    }

    private Task LoadOptiFineVersionsAsync()
    {
        var key = _componentGameRoot + ":" + SelectedGameVersion;
        if (key == _optiFineLoadKey && _optiFineLoadTask is not null) return _optiFineLoadTask;
        _optiFineLoadKey = key;
        return _optiFineLoadTask = LoadOptiFineVersionsCoreAsync();
    }

    private async Task LoadOptiFineVersionsCoreAsync()
    {
        var game = SelectedGameVersion;
        var root = _componentGameRoot;
        var request = ++_optiFineRequest;
        _optiFineCatalogError = null;
        OptiFineVersions.Clear();
        SelectedOptiFineVersion = null;
        if (string.IsNullOrWhiteSpace(game)) return;

        if (_optiFineCts is null) OptiFineStatus = $"正在获取 OptiFine {game} 版本…";
        try
        {
            var versions = await _getOptiFineVersions(game);
            if (request != _optiFineRequest || game != SelectedGameVersion || !IsCurrentComponentRoot(root)) return;
            OptiFineVersions.Clear();
            foreach (var version in versions.Take(60)) OptiFineVersions.Add(version);
            var selected = SelectedOptiFineVersion;
            SelectedOptiFineVersion = versions.FirstOrDefault(version => version == selected) ?? OptiFineVersions.FirstOrDefault();
            if (SelectedOptiFineVersion is not null && !OptiFineVersions.Contains(SelectedOptiFineVersion)) OptiFineVersions.Add(SelectedOptiFineVersion);
            if (_optiFineCts is null)
                OptiFineStatus = OptiFineVersions.Count == 0
                    ? $"没有找到 OptiFine {game} 的版本"
                    : $"共 {versions.Count} 个 OptiFine 版本";
        }
        catch (Exception ex)
        {
            if (request == _optiFineRequest && game == SelectedGameVersion && IsCurrentComponentRoot(root))
            {
                _optiFineLoadKey = null;
                _optiFineCatalogError = "OptiFine：" + ex.Message;
                if (_optiFineCts is null) OptiFineStatus = "获取 OptiFine 版本失败：" + ex.Message;
            }
        }
        finally
        {
            if (request == _optiFineRequest)
                UpdateOptiFineCommands();
        }
    }

    [RelayCommand]
    private async Task InstallOptiFineAsync()
    {
        if (!CanInstallOptiFine ||
            SelectedGameVersion is null ||
            SelectedOptiFineVersion is null) return;

        var game = SelectedGameVersion;
        var version = SelectedOptiFineVersion;
        var root = _componentGameRoot;
        IsOptiFineBusy = true;
        OptiFineProgress = 0;
        OptiFineProgressText = "";
        OptiFineStatus = $"正在安装 OptiFine {version.Patch}…";
        _optiFineCts = new CancellationTokenSource();

        try
        {
            EnsureCurrentComponentRoot(root);
            using var operation = _services.GameOperations.Acquire(root, "安装 OptiFine");
            var progress = new Progress<JobProgress>(update =>
            {
                OptiFineProgress = (update.Percent ?? 0) * 100;
                OptiFineProgressText = FormatProgress(update);
            });
            var id = await _installComponent(new(root, game, "OptiFine", version.Patch, version), progress, null, _optiFineCts.Token);

            OptiFineStatus = $"OptiFine 安装完成：{id}";
            OptiFineProgress = 100;
            await SelectInstalledComponentAsync(root, id);
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            OptiFineStatus = "已取消安装";
        }
        catch (Exception ex)
        {
            OptiFineStatus = "OptiFine 安装失败：" + ex.Message;
        }
        finally
        {
            IsOptiFineBusy = false;
            OptiFineProgress = 0;
            OptiFineProgressText = "";
            _optiFineCts.Dispose();
            _optiFineCts = null;
            UpdateOptiFineCommands();
        }
    }

    [RelayCommand]
    private void CancelOptiFine()
    {
        var cancellation = _optiFineCts;
        if (cancellation is null) return;
        OptiFineStatus = "正在取消…";
        cancellation.Cancel();
    }

    public async Task PrepareGameVersionAsync(string gameVersion, bool forceReload = false)
    {
        if (forceReload)
            _loaderLoadKey = _forgelikeLoadKey = _optiFineLoadKey = null;
        _preparingCatalogGame = true;
        try { SelectedVanillaInstance = null; }
        finally { _preparingCatalogGame = false; }
        _componentGameRoot = _services.Settings.GameDirectory;
        ComponentTargetInfo = "Minecraft " + gameVersion;
        SelectedGameVersion = gameVersion;
        await Task.WhenAll(LoadLoadersAsync(), LoadForgelikeVersionsAsync(), LoadOptiFineVersionsAsync());
    }

    private Task LoadForgelikeVersionsAsync()
    {
        var key = _componentGameRoot + ":" + SelectedForgelikeKind + ":" + SelectedGameVersion;
        if (key == _forgelikeLoadKey && _forgelikeLoadTask is not null) return _forgelikeLoadTask;
        _forgelikeLoadKey = key;
        return _forgelikeLoadTask = LoadForgelikeVersionsCoreAsync();
    }

    private async Task LoadForgelikeVersionsCoreAsync()
    {
        var game = SelectedGameVersion;
        var root = _componentGameRoot;
        var request = ++_forgelikeRequest;
        _forgelikeCatalogError = null;
        ForgelikeVersions.Clear();
        SelectedForgelikeVersion = null;
        IsForgelikeBusy = _forgelikeCts is not null;
        if (string.IsNullOrWhiteSpace(game)) return;

        var kind = SelectedForgelikeKind == "NeoForge"
            ? ForgelikeKind.NeoForge
            : ForgelikeKind.Forge;
        IsForgelikeBusy = true;
        if (_forgelikeCts is null) ForgelikeStatus = $"正在获取 {SelectedForgelikeKind} {game} 版本…";
        try
        {
            var versions = await _getForgelikeVersions(kind, game);
            if (request != _forgelikeRequest ||
                game != SelectedGameVersion || !IsCurrentComponentRoot(root) ||
                kind != (SelectedForgelikeKind == "NeoForge"
                    ? ForgelikeKind.NeoForge
                    : ForgelikeKind.Forge))
                return;
            ForgelikeVersions.Clear();
            foreach (var version in versions.Take(60))
                ForgelikeVersions.Add(version);
            SelectedForgelikeVersion = versions.Contains(SelectedForgelikeVersion) ? SelectedForgelikeVersion : ForgelikeVersions.FirstOrDefault();
            if (SelectedForgelikeVersion is not null && !ForgelikeVersions.Contains(SelectedForgelikeVersion)) ForgelikeVersions.Add(SelectedForgelikeVersion);
            if (_forgelikeCts is null)
                ForgelikeStatus = ForgelikeVersions.Count == 0
                    ? $"没有找到 {SelectedForgelikeKind} {game} 的版本"
                    : $"共 {versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            if (request == _forgelikeRequest && game == SelectedGameVersion && IsCurrentComponentRoot(root) &&
                kind == (SelectedForgelikeKind == "NeoForge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge))
            {
                _forgelikeLoadKey = null;
                _forgelikeCatalogError = kind == ForgelikeKind.Forge && ex.Message == "无" ? null : SelectedForgelikeKind + "：" + ex.Message;
                if (_forgelikeCts is null) ForgelikeStatus = "获取版本失败：" + ex.Message;
            }
        }
        finally
        {
            if (request == _forgelikeRequest)
            {
                IsForgelikeBusy = _forgelikeCts is not null;
                UpdateForgelikeCommands();
            }
        }
    }

    [RelayCommand]
    private async Task InstallForgelikeAsync()
    {
        if (!CanInstallForgelike ||
            SelectedGameVersion is null ||
            SelectedForgelikeVersion is null) return;

        var kind = SelectedForgelikeKind == "NeoForge"
            ? ForgelikeKind.NeoForge
            : ForgelikeKind.Forge;
        var game = SelectedGameVersion;
        var version = SelectedForgelikeVersion;
        var root = _componentGameRoot;

        IsForgelikeBusy = true;
        CanInstallForgelike = false;
        ForgelikeProgress = 0;
        ForgelikeProgressText = "";
        ForgelikeStatus = $"正在安装 {SelectedForgelikeKind} {version}…";
        _forgelikeCts = new CancellationTokenSource();

        try
        {
            EnsureCurrentComponentRoot(root);
            using var operation = _services.GameOperations.Acquire(root, "安装 " + kind);
            var progress = new Progress<JobProgress>(update =>
            {
                ForgelikeProgress = (update.Percent ?? 0) * 100;
                ForgelikeProgressText = FormatProgress(update);
            });
            var log = new Progress<string>(line =>
            {
                if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Patching", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    ForgelikeStatus = line;
            });

            var id = await _installComponent(new(root, game, kind.ToString(), version), progress, log, _forgelikeCts.Token);

            ForgelikeStatus = $"{kind} 安装完成：{id}";
            ForgelikeProgress = 100;
            await SelectInstalledComponentAsync(root, id);
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ForgelikeStatus = "已取消安装";
        }
        catch (Exception ex)
        {
            ForgelikeStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            IsForgelikeBusy = false;
            ForgelikeProgress = 0;
            ForgelikeProgressText = "";
            _forgelikeCts.Dispose();
            _forgelikeCts = null;
            UpdateForgelikeCommands();
        }
    }

    [RelayCommand]
    private void CancelForgelike()
    {
        var cancellation = _forgelikeCts;
        if (cancellation is null) return;
        ForgelikeStatus = "正在取消…";
        cancellation.Cancel();
    }

    private void UpdateModCommands()
    {
        CanInstallMod = !IsModBusy &&
                        !IsResolvingModDependencies && _modInstallPlan is not null &&
                        SelectedMod is not null &&
                        _modLoader is "fabric" or "quilt" or "forge" or "neoforge" &&
                        !string.IsNullOrWhiteSpace(ModsDirectory);
    }

    private async Task UpdateModTargetAsync()
    {
        var request = ++_modTargetRequest;
        InvalidateModUpdates();
        var id = SelectedModInstance;
        var gameRoot = _services.Settings.GameDirectory;
        _modGameVersion = null;
        _modLoader = "vanilla";
        ModsDirectory = "";
        SelectedLocalMod = null;
        LocalMods.Clear();
        OnPropertyChanged(nameof(IsModDisabled));
        UpdateModCommands();
        if (string.IsNullOrWhiteSpace(id))
        {
            ModTargetInfo = "未选择实例";
            return;
        }

        try
        {
            var version = await new VersionRepository(gameRoot).GetVersionAsync(id);
            if (request != _modTargetRequest || _services.Settings.GameDirectory != gameRoot) return;
            var gameDirectory = IsolationPolicy.GetGameDirectory(
                gameRoot, id, version, _services.Settings.IsolationMode);
            ModsDirectory = Path.Combine(gameDirectory, "mods");
            _modTargetRoot = gameRoot;
            if (version.GameVersionIdentity is { IsReliable: false })
            {
                ModTargetInfo = "无法精准识别 Minecraft 基座，请先在实例修改页确认版本信息";
                return;
            }
            _modGameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            _modLoader = MinecraftVersionUtil.GetLoader(version);
            ModTargetInfo = $"{id} · Minecraft {_modGameVersion} · {_modLoader}";
        }
        catch (Exception ex)
        {
            if (request == _modTargetRequest && _services.Settings.GameDirectory == gameRoot)
                ModTargetInfo = "读取实例失败：" + ex.Message;
        }
        finally
        {
            if (request == _modTargetRequest && _services.Settings.GameDirectory == gameRoot)
            {
                OnPropertyChanged(nameof(IsModDisabled));
                UpdateModCommands();
                RefreshLocalMods();
                _modVersionLoad = LoadModVersionsAsync(SelectedMod);
            }
        }
    }

    private Task LoadLoadersAsync()
    {
        var key = _componentGameRoot + ":" + SelectedFabricKind + ":" + SelectedGameVersion;
        if (key == _loaderLoadKey && _loaderLoadTask is not null) return _loaderLoadTask;
        _loaderLoadKey = key;
        return _loaderLoadTask = LoadLoadersCoreAsync();
    }

    private async Task LoadLoadersCoreAsync()
    {
        var game = SelectedGameVersion;
        var root = _componentGameRoot;
        var request = ++_loaderRequest;
        _loaderCatalogError = null;
        Loaders.Clear();
        _loaderInfos.Clear();
        SelectedLoader = null;
        IsBusy = _cts is not null;
        if (string.IsNullOrWhiteSpace(game)) return;

        var kind = SelectedFabricKind;
        IsBusy = true;
        if (_cts is null) Status = $"正在获取 {kind} {game} 的 Loader 列表…";
        try
        {
            var loaders = await _getLoaders(kind, game);
            if (request != _loaderRequest || game != SelectedGameVersion || !IsCurrentComponentRoot(root) ||
                kind != SelectedFabricKind) return;
            _loaderInfos.Clear();
            _loaderInfos.AddRange(loaders);

            Loaders.Clear();
            foreach (var loader in loaders.Take(30))
                Loaders.Add(loader.Version);

            SelectedLoader = loaders.Any(loader => loader.Version == SelectedLoader) ? SelectedLoader
                : (loaders.FirstOrDefault(loader => loader.Stable) ?? loaders.FirstOrDefault())?.Version;
            if (SelectedLoader is not null && !Loaders.Contains(SelectedLoader)) Loaders.Add(SelectedLoader);
            if (_cts is null)
                Status = loaders.Count == 0
                    ? $"{kind} 没有支持 Minecraft {game} 的版本"
                    : $"共 {loaders.Count} 个 {kind} Loader，默认选择稳定版";
        }
        catch (Exception ex)
        {
            if (request == _loaderRequest && game == SelectedGameVersion && kind == SelectedFabricKind && IsCurrentComponentRoot(root))
            {
                _loaderLoadKey = null;
                _loaderCatalogError = SelectedFabricKind + "：" + ex.Message;
                if (_cts is null) Status = $"获取 {kind} 版本失败：" + ex.Message;
            }
        }
        finally
        {
            if (request == _loaderRequest)
            {
                IsBusy = _cts is not null;
                UpdateCommands();
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task InstallFabricAsync()
    {
        if (!CanInstall || SelectedGameVersion is null || SelectedLoader is null) return;

        var game = SelectedGameVersion;
        var loader = SelectedLoader;
        var kind = SelectedFabricKind;
        var root = _componentGameRoot;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {kind} {loader}（{game}）…";
        _cts = new CancellationTokenSource();

        try
        {
            EnsureCurrentComponentRoot(root);
            using var operation = _services.GameOperations.Acquire(root, "安装 " + kind);
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });
            var id = await _installComponent(new(root, game, kind, loader), progress, null, _cts.Token);
            Status = $"{kind} 安装完成：{id}";
            Progress = 100;
            await SelectInstalledComponentAsync(root, id);
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = $"已取消 {kind} 安装";
        }
        catch (Exception ex)
        {
            Status = $"{kind} 安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            Progress = 0;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        var cancellation = _cts;
        if (cancellation is null) return;
        Status = "正在取消…";
        cancellation.Cancel();
    }

    private FabricInstaller FabricInstallerFor(string kind) => kind switch
    {
        "Quilt" => _services.Quilt,
        "LegacyFabric" => _services.LegacyFabric,
        _ => _fabric
    };

    private async Task LoadModVersionsAsync(ModrinthProject? project)
    {
        var token = ++_versionLoadToken;
        _allModVersions.Clear();
        ModVersions.Clear();
        SelectedModVersion = null;
        if (project is null)
        {
            ResetModVersionFilters();
            return;
        }
        try
        {
            var game = _modGameVersion;
            var loader = _modLoader;
            if (SelectedModSource == "CurseForge")
            {
                if (!long.TryParse(project.Id, out var id)) return;
                var files = await _services.CurseForge.GetFilesAsync(id, "", null);
                if (token != _versionLoadToken) return;
                foreach (var file in files)
                {
                    var compatible = IsCurseForgeCompatible(file, game, loader);
                    var versions = string.Join(" / ", file.GameVersions.Take(5));
                    _allModVersions.Add(new ModVersionItem(
                        file.DisplayName,
                        string.IsNullOrWhiteSpace(versions) ? file.FileName : $"{file.FileName} · {versions}",
                        null, file, file.GameVersions, compatible));
                }
            }
            else
            {
                var versions = await _services.Modrinth.GetVersionsAsync(project.Id);
                if (token != _versionLoadToken) return;
                foreach (var version in versions
                             .Where(v => v.Files.Count > 0)
                             .OrderByDescending(v => v.DatePublished))
                {
                    var compatible = IsModrinthCompatible(version, game, loader);
                    var gameVersions = string.Join(" / ", version.GameVersions.Take(5));
                    _allModVersions.Add(new ModVersionItem(
                        version.VersionNumber,
                        $"{version.VersionNumber} · {version.VersionType}" +
                        (string.IsNullOrWhiteSpace(gameVersions) ? "" : $" · {gameVersions}"),
                        version, null, version.GameVersions, compatible));
                }
            }
            RebuildModVersionFilters();
        }
        catch (Exception ex)
        {
            if (token == _versionLoadToken)
                ModStatus = "获取版本列表失败：" + ex.Message;
        }
    }

    private void ResetModVersionFilters()
    {
        _allModVersions.Clear();
        _modVersionFilterSet = ResourceVersionFilterSet.Create([]);
        _updatingModVersionFilters = true;
        ModVersionFilters.Clear();
        ModVersionFilters.Add("全部");
        SelectedModVersionFilter = "全部";
        _updatingModVersionFilters = false;
        ModVersions.Clear();
        ModVersionGroups.Clear();
        _modVersionEntries.Clear();
        SelectedModVersion = null;
        OnPropertyChanged(nameof(HasModVersionFilters));
    }

    private void RebuildModVersionFilters()
    {
        _modVersionFilterSet = ResourceVersionFilterSet.Create(
            _allModVersions.SelectMany(version => version.GameVersions ?? []));
        _updatingModVersionFilters = true;
        ModVersionFilters.Clear();
        foreach (var filter in _modVersionFilterSet.Labels) ModVersionFilters.Add(filter);
        var target = string.IsNullOrWhiteSpace(_modGameVersion)
            ? "全部"
            : _modVersionFilterSet.GroupFor(_modGameVersion);
        SelectedModVersionFilter = ModVersionFilters.Contains(target) ? target : "全部";
        _updatingModVersionFilters = false;
        OnPropertyChanged(nameof(HasModVersionFilters));
        ApplyModVersionFilter();
    }

    private void ApplyModVersionFilter()
    {
        var selected = SelectedModVersion;
        ModVersions.Clear();
        foreach (var version in _allModVersions.Where(version =>
                     _modVersionFilterSet.Matches(SelectedModVersionFilter,
                         version.GameVersions ?? [])))
            ModVersions.Add(version);
        SelectedModVersion = ModVersions.FirstOrDefault(version => ReferenceEquals(version, selected))
                              ?? ModVersions.FirstOrDefault();
        RebuildModVersionGroups();
        ModStatus = ModVersions.Count == 0
            ? "当前筛选没有版本"
            : $"共 {ModVersions.Count} 个版本" +
              (SelectedModVersionFilter == "全部" ? "" : $"（筛选：{SelectedModVersionFilter}）");
    }

    private void RebuildModVersionGroups()
    {
        ModVersionGroups.Clear();
        _modVersionEntries.Clear();
        var groups = new Dictionary<string, List<ResourceVersionEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in ModVersions)
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
                if (!_modVersionFilterSet.Matches(SelectedModVersionFilter, [gameVersion])) continue;
                AddEntry(ResourceVersionFilterSet.CardName(gameVersion), captured, detail);
            }
        }

        var target = string.IsNullOrWhiteSpace(_modGameVersion)
            ? null
            : ResourceVersionFilterSet.CardName(_modGameVersion);
        foreach (var pair in groups
                     .OrderBy(pair => pair.Key == target ? 0 : pair.Key == "其他" ? 2 : 1)
                     .ThenByDescending(pair => ResourceVersionFilterSet.SortCode(pair.Key))
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entries = pair.Value
                .OrderByDescending(entry => VersionDate(entry.Source))
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ModVersionGroups.Add(new ResourceVersionGroup(pair.Key, entries,
                pair.Key == target || groups.Count == 1));
        }
        foreach (var entry in _modVersionEntries)
            entry.IsSelected = ReferenceEquals(entry.Source, SelectedModVersion);
        return;

        void AddEntry(string group, ModVersionItem version, string detail)
        {
            if (!groups.TryGetValue(group, out var entries))
                groups[group] = entries = [];
            if (entries.Any(entry => ReferenceEquals(entry.Source, version))) return;
            var entry = new ResourceVersionEntry(version, version.Label, detail,
                version.IsCompatible, new RelayCommand(() => SelectedModVersion = version));
            entries.Add(entry);
            _modVersionEntries.Add(entry);
        }
    }

    private static DateTimeOffset VersionDate(object source) => source switch
    {
        ModVersionItem { Modrinth: { } modrinth } => modrinth.DatePublished,
        ModVersionItem { CurseForge: { } curseForge } => curseForge.FileDate,
        _ => DateTimeOffset.MinValue
    };

    private static bool IsModrinthCompatible(ModrinthVersion version, string? game, string? loader) =>
        (string.IsNullOrWhiteSpace(game) ||
         version.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ||
         version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase));

    private static bool IsCurseForgeCompatible(CurseForgeFile file, string? game, string? loader) =>
        (string.IsNullOrWhiteSpace(game) ||
         file.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ||
         file.GameVersions.Any(value => value.Equals(loader, StringComparison.OrdinalIgnoreCase) ||
                                        value.Contains(loader, StringComparison.OrdinalIgnoreCase)));

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (IsModBusy) return;
        _hasBrowsedMods = true;
        _searchOffset = 0;
        SearchResults.Clear();
        SelectedMod = null;
        CanLoadMore = false;
        await FetchModsAsync(append: false);
    }

    [RelayCommand]
    private async Task LoadMoreModsAsync()
    {
        if (!CanLoadMore || IsModBusy) return;
        await FetchModsAsync(append: true);
    }

    private async Task FetchModsAsync(bool append)
    {
        if (IsModBusy) return;
        var searchToken = ++_modSearchToken;
        var platform = SelectedModSource;
        var game = SearchGameVersion.Trim();
        var loader = SearchLoader == "全部" ? "" : SearchLoader;

        if (platform == "CurseForge" && !_services.CurseForge.IsConfigured)
        {
            ModStatus = "未配置 CurseForge API Key：请在“设置”页填写。";
            return;
        }

        IsModBusy = true;
        ModStatus = $"正在搜索 {SelectedModSource}…";
        if (!append)
        {
            SearchResults.Clear();
            SelectedMod = null;
        }
        try
        {
            if (platform == "CurseForge")
            {
                var result = await _services.CurseForge.SearchAsync(
                    SearchText, game, loader, 20, default,
                    CurseForgeClient.ClassIdMods, loader.Length > 0, _searchOffset);
                if (searchToken != _modSearchToken) return;
                foreach (var project in result.Projects)
                {
                    SearchResults.Add(new ModrinthProject(
                        project.Id.ToString(), project.Slug, project.Name,
                        project.Summary, "mod",
                        (int)Math.Min(project.Downloads, int.MaxValue),
                        project.LogoUrl, []));
                }
                ModStatus = $"CurseForge 共 {result.TotalCount} 个结果";
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalCount;
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    SearchText, game, loader, 20, default, "mod", _searchOffset);
                if (searchToken != _modSearchToken) return;
                foreach (var project in result.Projects)
                    SearchResults.Add(project);
                ModStatus = $"Modrinth 共 {result.TotalHits} 个结果";
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalHits;
            }

        }
        catch (Exception ex)
        {
            if (searchToken == _modSearchToken) ModStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsModBusy = false;
            UpdateModCommands();
        }
    }

    [RelayCommand]
    private void CopyProjectName()
    {
        var name = SelectedMod?.Title;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(name);
            ModStatus = "已复制名称：" + name;
        }
    }

    [RelayCommand]
    private void OpenSelectedModPage()
    {
        if (SelectedMod is null) return;
        var selected = SelectedMod;
        var url = SelectedModSource == "CurseForge"
            ? string.IsNullOrWhiteSpace(selected.Slug)
                ? null
                : $"https://www.curseforge.com/minecraft/mc-mods/{selected.Slug}"
            : string.IsNullOrWhiteSpace(selected.Slug)
                ? null
                : $"https://modrinth.com/{selected.ProjectType}/{selected.Slug}";
        if (url is null)
        {
            ModStatus = "该项目没有可用的项目页链接";
            return;
        }
        try
        {
            WMCL.App.Services.ExternalLauncher.Open(url);
            ModStatus = "已在浏览器中打开项目页";
        }
        catch (Exception ex)
        {
            ModStatus = "打开项目页失败：" + ex.Message;
        }
    }


    [RelayCommand]
    private void RefreshLocalMods()
    {
        LocalMods.Clear();
        foreach (var mod in LocalModService.List(ModsDirectory))
            LocalMods.Add(mod);

        SelectedLocalMod = LocalMods.FirstOrDefault();
        var enabled = LocalMods.Count(m => m.IsEnabled);
        LocalModStatus = LocalMods.Count == 0
            ? "该实例 mods 目录为空"
            : $"{LocalMods.Count} 个 Mod，已启用 {enabled} 个";
    }

    public async Task SelectModInstanceAsync(string instance)
    {
        if (!ModInstances.Contains(instance)) return;
        SelectedModInstance = instance;
        await UpdateModTargetAsync();
    }

    [RelayCommand]
    private async Task CheckModUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        var target = CaptureModUpdateTarget();
        if (target is null)
        {
            UpdateStatus = "请选择带 Fabric/Forge 的实例";
            return;
        }

        InvalidateModUpdates();
        using var cancellation = new CancellationTokenSource();
        _modUpdateCheckCancellation = cancellation;
        IsCheckingUpdates = true;
        UpdateStatus = "正在检查 Mod 更新…";
        try
        {
            var check = await _services.ModUpdates.CheckDetailedAsync(
                target.Directory, target.Minecraft, target.Loader, ct: cancellation.Token);
            var updates = check.Updates;
            if (!IsCurrentModUpdateTarget(target) || cancellation.IsCancellationRequested) return;
            ValidateModUpdatePaths(target, updates);
            _modUpdatesTarget = target;
            _modUpdates = updates.ToDictionary(
                update => update.FilePath, StringComparer.OrdinalIgnoreCase);

            var selectedPath = SelectedLocalMod?.FilePath;
            for (var index = 0; index < LocalMods.Count; index++)
            {
                var mod = LocalMods[index];
                var update = _modUpdates.GetValueOrDefault(mod.FilePath);
                LocalMods[index] = mod with { UpdateVersion = update?.Latest.VersionNumber };
            }

            SelectedLocalMod = LocalMods.FirstOrDefault(mod =>
                                  string.Equals(mod.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                              ?? LocalMods.FirstOrDefault();
            UpdateStatus = check.Summary;
            UpdateModUpdateCommands();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentModUpdateTarget(target)) UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            _modUpdateCheckCancellation = null;
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllModsAsync()
    {
        if (IsCheckingUpdates || _modUpdates.Count == 0 ||
            _modUpdatesTarget is not { } target || !IsCurrentModUpdateTarget(target)) return;
        await ApplyModUpdatesAsync(target, _modUpdates.Values.ToArray());
    }

    [RelayCommand]
    private async Task UpdateSelectedModAsync()
    {
        if (IsCheckingUpdates || _modUpdatesTarget is not { } target || !IsCurrentModUpdateTarget(target) ||
            SelectedLocalMod is null ||
            !_modUpdates.TryGetValue(SelectedLocalMod.FilePath, out var update))
            return;

        await ApplyModUpdatesAsync(target, [update]);
    }

    [RelayCommand]
    private void ToggleLocalMod()
    {
        if (SelectedLocalMod is null) return;
        try
        {
            using var operation = _services.GameOperations.Acquire(ModTargetRoot, "切换 Mod 状态");
            var target = !SelectedLocalMod.IsEnabled;
            LocalModService.SetEnabled(ModsDirectory, SelectedLocalMod, target);
            LocalModStatus = $"{SelectedLocalMod.DisplayName} 已{(target ? "启用" : "禁用")}";
            RefreshLocalMods();
        }
        catch (Exception ex)
        {
            LocalModStatus = "操作失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteLocalModAsync()
    {
        if (SelectedLocalMod is null) return;
        var root = ModTargetRoot;
        var selected = SelectedLocalMod;
        if (!await DialogService.ConfirmAsync(
                "删除 Mod",
                $"确定删除 {SelectedLocalMod.DisplayName}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除 Mod");
            var name = selected.DisplayName;
            LocalModService.Delete(selected);
            LocalModStatus = $"已删除 {name}";
            RefreshLocalMods();
        }
        catch (Exception ex)
        {
            LocalModStatus = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        var directory = string.IsNullOrWhiteSpace(ModsDirectory)
            ? Path.Combine(_services.Settings.GameDirectory, "mods")
            : ModsDirectory;
        Directory.CreateDirectory(directory);
        OpenPath(directory);
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0.0}%";
        var speed = progress.BytesPerSecond is > 0
            ? $" · {progress.BytesPerSecond / 1048576.0:0.0} MB/s"
            : "";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : $" · {progress.Detail}";
        return $"{progress.Stage} {percent}{speed}{detail}".Trim();
    }

    private static void OpenPath(string path)
    {
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


public sealed record ModVersionItem(
    string Label,
    string Detail,
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge,
    IReadOnlyList<string>? GameVersions = null,
    bool IsCompatible = true);
