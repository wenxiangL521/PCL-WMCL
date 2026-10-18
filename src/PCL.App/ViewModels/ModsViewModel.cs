using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Launch.Java;
using PCL.Minecraft;
using PCL.Modding.CurseForge;
using PCL.Modding.Fabric;
using PCL.Modding.Local;
using PCL.Modding.OptiFine;
using PCL.Modding.Forgelike;
using PCL.Modding.Modrinth;

namespace PCL.App.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly FabricInstaller _fabric;
    private readonly ForgelikeInstaller _forgelike;
    private readonly OptiFineService _optifine;
    private readonly List<FabricLoaderInfo> _loaderInfos = [];
    private CancellationTokenSource? _cts;

    public ModsViewModel(AppServices services)
    {
        _services = services;
        _fabric = services.Fabric;
        _forgelike = services.Forgelike;
        _optifine = services.Optifine;
    }

    public event EventHandler? VersionInstalled;

    public ObservableCollection<string> GameVersions { get; } = [];
    public ObservableCollection<string> Loaders { get; } = [];
    public ObservableCollection<string> ModInstances { get; } = [];
    public bool HasGameVersions => GameVersions.Count > 0;

    private string? _modGameVersion;
    private string _modLoader = "vanilla";

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
    private bool _loadingOptiFine;

    public ObservableCollection<string> ForgelikeKinds { get; } = ["Forge", "NeoForge"];
    public ObservableCollection<string> ForgelikeVersions { get; } = [];

    [ObservableProperty]
    private string _selectedForgelikeKind = "Forge";

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
    public ObservableCollection<ModVersionItem> ModVersions { get; } = [];
    public ObservableCollection<string> ModSources { get; } = ["Modrinth", "CurseForge"];

    [ObservableProperty]
    private string _selectedModSource = "Modrinth";

    partial void OnSelectedModSourceChanged(string value)
    {
        SearchResults.Clear();
        ModVersions.Clear();
        SelectedModVersion = null;
        SelectedMod = null;
        ModStatus = value == "CurseForge"
            ? "搜索 CurseForge（需在设置中填写 API Key）"
            : "搜索 Modrinth";
        UpdateModCommands();
    }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ModrinthProject? _selectedMod;

    [ObservableProperty]
    private ModVersionItem? _selectedModVersion;

    private int _versionLoadToken;

    [ObservableProperty]
    private string _modStatus = "搜索 Modrinth 上的 Fabric Mod";

    [ObservableProperty]
    private bool _isModBusy;

    [ObservableProperty]
    private bool _canInstallMod;

    [ObservableProperty]
    private double _modProgress;

    [ObservableProperty]
    private string _modProgressText = "";

    public async Task LoadAsync()
    {
        var installed = await Task.Run(() => _services.Repository.ListInstalled());
        var vanilla = new List<string>();
        foreach (var version in installed)
        {
            try
            {
                var json = await _services.Repository.GetVersionAsync(version.Id);
                if (!MinecraftVersionUtil.IsModable(json)) vanilla.Add(version.Id);
            }
            catch
            {
                // 损坏的实例跳过
            }
        }

        GameVersions.Clear();
        foreach (var id in vanilla) GameVersions.Add(id);
        OnPropertyChanged(nameof(HasGameVersions));

        SelectedGameVersion = GameVersions.FirstOrDefault();
        if (SelectedGameVersion is not null)
        {
            await LoadLoadersAsync();
            await LoadForgelikeVersionsAsync();
            await LoadOptiFineVersionsAsync();
        }
        else
            Status = "还没有原版实例，请先在“下载”页安装一个版本";

        ModInstances.Clear();
        foreach (var version in installed)
            ModInstances.Add(version.Id);

        SelectedModInstance = ModInstances.FirstOrDefault(id =>
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

    partial void OnSelectedForgelikeVersionChanged(string? value) => UpdateForgelikeCommands();

    partial void OnSelectedOptiFineVersionChanged(OptiFineVersion? value) =>
        UpdateOptiFineCommands();

    partial void OnIsOptiFineBusyChanged(bool value) => UpdateOptiFineCommands();

    partial void OnIsForgelikeBusyChanged(bool value) => UpdateForgelikeCommands();

    partial void OnSelectedModChanged(ModrinthProject? value)
    {
        UpdateModCommands();
        _ = LoadModVersionsAsync(value);
    }

    partial void OnSelectedModVersionChanged(ModVersionItem? value) => UpdateModCommands();

    partial void OnSelectedModInstanceChanged(string? value) => _ = UpdateModTargetAsync();

    partial void OnSelectedLocalModChanged(LocalMod? value)
    {
        HasSelectedLocalMod = value is not null;
        ToggleModText = value?.IsEnabled == true ? "禁用" : "启用";
        CanUpdateSelectedMod = value?.UpdateVersion is not null && !IsCheckingUpdates;
    }

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        CanUpdateSelectedMod = SelectedLocalMod?.UpdateVersion is not null && !value;
        CanUpdateAllMods = _modUpdates.Count > 0 && !value;
    }

    partial void OnModsDirectoryChanged(string value) => RefreshLocalMods();

    partial void OnIsModBusyChanged(bool value) => UpdateModCommands();

    partial void OnSelectedLoaderChanged(string? value) => UpdateCommands();

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedGameVersion is not null && SelectedLoader is not null;
        UpdateModCommands();
        UpdateForgelikeCommands();
    }

    private void UpdateForgelikeCommands()
    {
        CanInstallForgelike = !IsForgelikeBusy &&
                              SelectedGameVersion is not null &&
                              SelectedForgelikeVersion is not null;
    }

    private void UpdateOptiFineCommands()
    {
        CanInstallOptiFine = !IsOptiFineBusy &&
                             SelectedGameVersion is not null &&
                             SelectedOptiFineVersion is not null;
    }

    private async Task LoadOptiFineVersionsAsync()
    {
        var game = SelectedGameVersion;
        if (string.IsNullOrWhiteSpace(game) || _loadingOptiFine) return;

        _loadingOptiFine = true;
        OptiFineStatus = $"正在获取 OptiFine {game} 版本…";
        try
        {
            var versions = await _optifine.GetVersionsAsync(game);
            OptiFineVersions.Clear();
            foreach (var version in versions.Take(60)) OptiFineVersions.Add(version);
            SelectedOptiFineVersion = OptiFineVersions.FirstOrDefault();
            OptiFineStatus = OptiFineVersions.Count == 0
                ? $"没有找到 OptiFine {game} 的版本"
                : $"共 {versions.Count} 个 OptiFine 版本";
        }
        catch (Exception ex)
        {
            OptiFineStatus = "获取 OptiFine 版本失败：" + ex.Message;
        }
        finally
        {
            _loadingOptiFine = false;
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
        IsOptiFineBusy = true;
        OptiFineProgress = 0;
        OptiFineProgressText = "";
        OptiFineStatus = $"正在安装 OptiFine {version.Patch}…";
        _optiFineCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                OptiFineProgress = (update.Percent ?? 0) * 100;
                OptiFineProgressText = FormatProgress(update);
            });
            var java = await _services.JavaProvisioner.EnsureAsync(
                JavaLocator.InferMajorVersion(game), progress, _optiFineCts.Token);
            var id = await _optifine.InstallAsync(
                game, version.Patch, java.Path, progress, null, _optiFineCts.Token);

            OptiFineStatus = $"OptiFine 安装完成：{id}";
            OptiFineProgress = 100;
            if (!ModInstances.Contains(id)) ModInstances.Add(id);
            SelectedModInstance = id;
            await UpdateModTargetAsync();
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
        _optiFineCts?.Cancel();
        OptiFineStatus = "正在取消…";
    }

    public async Task PrepareGameVersionAsync(string gameVersion)
    {
        SelectedGameVersion = gameVersion;
        await LoadLoadersAsync();
        await LoadForgelikeVersionsAsync();
        await LoadOptiFineVersionsAsync();
    }

    private async Task LoadForgelikeVersionsAsync()
    {
        var game = SelectedGameVersion;
        if (string.IsNullOrWhiteSpace(game) || IsForgelikeBusy) return;

        var kind = SelectedForgelikeKind == "NeoForge"
            ? ForgelikeKind.NeoForge
            : ForgelikeKind.Forge;
        IsForgelikeBusy = true;
        ForgelikeStatus = $"正在获取 {SelectedForgelikeKind} {game} 版本…";
        try
        {
            var versions = await _forgelike.GetVersionsAsync(kind, game);
            ForgelikeVersions.Clear();
            foreach (var version in versions.Take(60))
                ForgelikeVersions.Add(version);
            SelectedForgelikeVersion = ForgelikeVersions.FirstOrDefault();
            ForgelikeStatus = ForgelikeVersions.Count == 0
                ? $"没有找到 {SelectedForgelikeKind} {game} 的版本"
                : $"共 {versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            ForgelikeStatus = "获取版本失败：" + ex.Message;
        }
        finally
        {
            IsForgelikeBusy = false;
            UpdateForgelikeCommands();
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

        IsForgelikeBusy = true;
        CanInstallForgelike = false;
        ForgelikeProgress = 0;
        ForgelikeProgressText = "";
        ForgelikeStatus = $"正在安装 {SelectedForgelikeKind} {version}…";
        _forgelikeCts = new CancellationTokenSource();

        try
        {
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

            var java = await _services.JavaProvisioner.EnsureAsync(
                JavaLocator.InferMajorVersion(game), progress, _forgelikeCts.Token);
            var id = await _forgelike.InstallAsync(
                kind, game, version, java.Path, progress, log, _forgelikeCts.Token);

            ForgelikeStatus = $"{SelectedForgelikeKind} 安装完成：{id}";
            ForgelikeProgress = 100;
            if (!ModInstances.Contains(id)) ModInstances.Add(id);
            SelectedModInstance = id;
            await UpdateModTargetAsync();
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
        _forgelikeCts?.Cancel();
        ForgelikeStatus = "正在取消…";
    }

    private void UpdateModCommands()
    {
        CanInstallMod = !IsModBusy &&
                        SelectedMod is not null &&
                        _modLoader is "fabric" or "quilt" or "forge" or "neoforge" &&
                        !string.IsNullOrWhiteSpace(ModsDirectory);
    }

    private async Task UpdateModTargetAsync()
    {
        var id = SelectedModInstance;
        if (string.IsNullOrWhiteSpace(id))
        {
            ModsDirectory = Path.Combine(_services.Settings.GameDirectory, "mods");
            ModTargetInfo = "未选择实例";
            _modGameVersion = null;
            _modLoader = "vanilla";
            UpdateModCommands();
            return;
        }

        try
        {
            var version = await _services.Repository.GetVersionAsync(id);
            var gameRoot = _services.Settings.GameDirectory;
            var gameDirectory = MinecraftVersionUtil.GetGameDirectory(gameRoot, id, version);
            ModsDirectory = Path.Combine(gameDirectory, "mods");
            _modGameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            _modLoader = MinecraftVersionUtil.GetLoader(version);
            ModTargetInfo = $"{id} · Minecraft {_modGameVersion} · {_modLoader}";
        }
        catch (Exception ex)
        {
            ModTargetInfo = "读取实例失败：" + ex.Message;
            _modLoader = "vanilla";
        }
        finally
        {
            UpdateModCommands();
            RefreshLocalMods();
        }
    }

    private async Task LoadLoadersAsync()
    {
        var game = SelectedGameVersion;
        if (string.IsNullOrWhiteSpace(game) || IsBusy) return;

        IsBusy = true;
        Status = $"正在获取 Fabric {game} 的 Loader 列表…";
        try
        {
            var loaders = await _fabric.GetLoadersAsync(game);
            _loaderInfos.Clear();
            _loaderInfos.AddRange(loaders);

            Loaders.Clear();
            foreach (var loader in loaders.Take(30))
                Loaders.Add(loader.Version);

            SelectedLoader = (loaders.FirstOrDefault(l => l.Stable) ?? loaders.FirstOrDefault())?.Version;
            Status = loaders.Count == 0
                ? $"Fabric 没有支持 Minecraft {game} 的版本"
                : $"共 {loaders.Count} 个 Loader，默认选择稳定版";
        }
        catch (Exception ex)
        {
            Status = "获取 Fabric 版本失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
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
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 Fabric {loader}（{game}）…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });
            var id = await _fabric.InstallAsync(game, loader, progress, _cts.Token);
            Status = $"Fabric 安装完成：{id}";
            Progress = 100;
            if (!ModInstances.Contains(id)) ModInstances.Add(id);
            SelectedModInstance = id;
            await UpdateModTargetAsync();
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消 Fabric 安装";
        }
        catch (Exception ex)
        {
            Status = "Fabric 安装失败：" + ex.Message;
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
        _cts?.Cancel();
        Status = "正在取消…";
    }

    private async Task LoadModVersionsAsync(ModrinthProject? project)
    {
        var token = ++_versionLoadToken;
        ModVersions.Clear();
        SelectedModVersion = null;
        if (project is null) return;
        try
        {
            var game = _modGameVersion ?? SelectedGameVersion;
            if (string.IsNullOrWhiteSpace(game) || _modLoader == "vanilla") return;
            if (SelectedModSource == "CurseForge")
            {
                if (!long.TryParse(project.Id, out var id)) return;
                var files = await _services.CurseForge.GetFilesAsync(id, game, _modLoader);
                if (token != _versionLoadToken) return;
                foreach (var file in files)
                    ModVersions.Add(new ModVersionItem(
                        file.DisplayName, file.FileName, null, file));
            }
            else
            {
                var versions = await _services.Modrinth.GetVersionsAsync(project.Id);
                if (token != _versionLoadToken) return;
                foreach (var version in versions
                             .Where(v => v.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase))
                             .Where(v => v.Loaders.Contains(_modLoader, StringComparer.OrdinalIgnoreCase))
                             .Where(v => v.Files.Count > 0)
                             .OrderByDescending(v => v.DatePublished))
                    ModVersions.Add(new ModVersionItem(
                        version.VersionNumber,
                        $"{version.VersionNumber} · {version.VersionType}",
                        version, null));
            }
            SelectedModVersion = ModVersions.FirstOrDefault();
            ModStatus = ModVersions.Count == 0
                ? "没有找到兼容的版本"
                : $"共 {ModVersions.Count} 个兼容版本";
        }
        catch (Exception ex)
        {
            if (token == _versionLoadToken)
                ModStatus = "获取版本列表失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        var game = _modGameVersion ?? SelectedGameVersion;
        if (string.IsNullOrWhiteSpace(game) || _modLoader == "vanilla")
        {
            ModStatus = "请选择一个带 Fabric/Forge 的实例";
            return;
        }

        if (SelectedModSource == "CurseForge" && !_services.CurseForge.IsConfigured)
        {
            ModStatus = "未配置 CurseForge API Key：请在“设置”页填写。";
            return;
        }

        IsModBusy = true;
        ModStatus = $"正在搜索 {SelectedModSource}…";
        SearchResults.Clear();
        try
        {
            if (SelectedModSource == "CurseForge")
            {
                var result = await _services.CurseForge.SearchAsync(
                    SearchText, game, _modLoader);
                foreach (var project in result.Projects)
                {
                    SearchResults.Add(new ModrinthProject(
                        project.Id.ToString(),
                        project.Slug,
                        project.Name,
                        project.Summary,
                        "mod",
                        (int)Math.Min(project.Downloads, int.MaxValue),
                        project.LogoUrl,
                        []));
                }

                ModStatus = $"CurseForge 共 {result.TotalCount} 个结果";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(SearchText, game, _modLoader);
                foreach (var project in result.Projects)
                    SearchResults.Add(project);
                ModStatus = $"Modrinth 共 {result.TotalHits} 个结果";
            }

            SelectedMod = SearchResults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ModStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsModBusy = false;
            UpdateModCommands();
        }
    }

    [RelayCommand]
    private async Task InstallModAsync()
    {
        if (SelectedMod is null || SelectedGameVersion is null || IsModBusy) return;

        var mod = SelectedMod;
        var game = _modGameVersion;
        if (string.IsNullOrWhiteSpace(game) || _modLoader == "vanilla")
        {
            ModStatus = "请选择一个带 Fabric/Forge 的实例";
            return;
        }

        IsModBusy = true;
        ModProgress = 0;
        ModProgressText = "";
        ModStatus = $"正在安装 {mod.Title}…";

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                ModProgress = (update.Percent ?? 0) * 100;
                ModProgressText = FormatProgress(update);
            });
            IReadOnlyList<string> files;
            if (SelectedModSource == "CurseForge")
            {
                if (!long.TryParse(mod.Id, out var curseForgeId))
                    throw new InvalidOperationException("无效的 CurseForge Mod ID。");
                var result = SelectedModVersion?.CurseForge is { } selected
                    ? await _services.CurseForge.InstallSelectedAsync(
                        curseForgeId, selected.Id, game, _modLoader, ModsDirectory, progress)
                    : await _services.CurseForge.InstallAsync(
                        curseForgeId, game, _modLoader, ModsDirectory, progress);
                files = result.Files;
            }
            else
            {
                if (SelectedModVersion?.Modrinth is { } selected)
                {
                    var file = selected.Files.FirstOrDefault(f => f.Primary)
                               ?? selected.Files.First();
                    await _services.Modrinth.InstallFileAsync(file, ModsDirectory, progress);
                    files = [file.Filename];
                }
                else
                {
                    var result = await _services.Modrinth.InstallAsync(
                        mod.Id, game, ModsDirectory, _modLoader, progress);
                    files = result.Files;
                }
            }

            RefreshLocalMods();
            ModStatus = files.Count == 0
                ? "没有需要下载的文件（可能已安装）"
                : $"已安装：{string.Join("，", files)}";
            ModProgress = 100;
        }
        catch (Exception ex)
        {
            ModStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            IsModBusy = false;
            ModProgress = 0;
            ModProgressText = "";
            UpdateModCommands();
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
        if (IsCheckingUpdates || string.IsNullOrWhiteSpace(ModsDirectory) ||
            string.IsNullOrWhiteSpace(_modGameVersion) || _modLoader == "vanilla")
        {
            UpdateStatus = "请选择带 Fabric/Forge 的实例";
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatus = "正在检查 Mod 更新…";
        try
        {
            var updates = await _services.ModUpdates.CheckAsync(
                ModsDirectory, _modGameVersion, _modLoader);
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
            UpdateStatus = updates.Count == 0
                ? "所有 Mod 均为最新"
                : $"发现 {updates.Count} 个可更新 Mod";
            CanUpdateAllMods = updates.Count > 0;
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllModsAsync()
    {
        if (IsCheckingUpdates || _modUpdates.Count == 0) return;

        IsCheckingUpdates = true;
        var pending = _modUpdates.Values.ToList();
        var updated = 0;
        UpdateStatus = $"正在更新 {pending.Count} 个 Mod…";
        try
        {
            foreach (var update in pending)
            {
                var file = update.Latest.Files.FirstOrDefault(f => f.Primary)
                           ?? update.Latest.Files.FirstOrDefault();
                if (file is null) continue;

                var oldPath = update.FilePath;
                var destination = Path.Combine(ModsDirectory, file.Filename);
                await _services.Modrinth.InstallFileAsync(
                    file, ModsDirectory, new Progress<JobProgress>(_ => { }));
                if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(oldPath))
                    File.Delete(oldPath);
                _modUpdates.Remove(oldPath);
                updated++;
            }

            RefreshLocalMods();
            UpdateStatus = $"已更新 {updated} 个 Mod";
        }
        catch (Exception ex)
        {
            UpdateStatus = "全部更新失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
            CanUpdateAllMods = _modUpdates.Count > 0;
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedModAsync()
    {
        if (SelectedLocalMod is null ||
            !_modUpdates.TryGetValue(SelectedLocalMod.FilePath, out var update))
            return;

        var file = update.Latest.Files.FirstOrDefault(f => f.Primary)
                   ?? update.Latest.Files.FirstOrDefault();
        if (file is null)
        {
            UpdateStatus = "最新版本没有可下载文件";
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatus = $"正在更新 {SelectedLocalMod.DisplayName} → {update.Latest.VersionNumber}";
        try
        {
            var oldPath = SelectedLocalMod.FilePath;
            var destination = Path.Combine(ModsDirectory, file.Filename);
            var downloaded = await _services.Modrinth.InstallFileAsync(
                file, ModsDirectory, new Progress<JobProgress>(_ => { }));
            if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(oldPath))
                File.Delete(oldPath);

            _modUpdates.Remove(SelectedLocalMod.FilePath);
            RefreshLocalMods();
            UpdateStatus = downloaded
                ? $"已更新到 {update.Latest.VersionNumber}"
                : "新版本文件已存在";
        }
        catch (Exception ex)
        {
            UpdateStatus = "更新失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void ToggleLocalMod()
    {
        if (SelectedLocalMod is null) return;
        try
        {
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
        if (!await DialogService.ConfirmAsync(
                "删除 Mod",
                $"确定删除 {SelectedLocalMod.DisplayName}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            var name = SelectedLocalMod.DisplayName;
            LocalModService.Delete(SelectedLocalMod);
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
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", path)
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", path)
                    : new ProcessStartInfo("xdg-open", path);
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
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
    CurseForgeFile? CurseForge);
