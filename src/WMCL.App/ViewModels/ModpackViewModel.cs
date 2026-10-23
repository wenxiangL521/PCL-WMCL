using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modpack;
using WMCL.Modding.Modrinth;

namespace WMCL.App.ViewModels;

public partial class ModpackViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private int _instanceLoadRequest;
    private readonly ModpackImportWorkflow? _imports;
    private CancellationTokenSource? _cts;

    public ModpackViewModel(AppServices services, ModpackImportWorkflow? imports = null)
    {
        _services = services;
        Favorite = new ResourceFavoritePickerViewModel(services.ResourceFavorites);
        _imports = imports;
        _gameVersion = "1.21.1";
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    public event EventHandler? InstanceInstalled;
    public ObservableCollection<ModpackItem> Results { get; } = [];
    public bool HasResults => Results.Count > 0;
    public ObservableCollection<ModpackVersionItem> ModpackVersions { get; } = [];
    public ObservableCollection<string> Platforms { get; } = ["Modrinth", "CurseForge"];

    [ObservableProperty]
    private string _selectedPlatform = "Modrinth";

    [ObservableProperty]
    private string _gameVersion = "1.21.1";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ModpackItem? _selectedProject;

    [ObservableProperty]
    private ModpackVersionItem? _selectedModpackVersion;

    private int _versionLoadToken;
    private int _searchOffset;
    private int _searchToken;
    private Task _versionLoad = Task.CompletedTask;

    public ResourceFavoritePickerViewModel Favorite { get; }

    public Task OpenFavoriteAsync(FavoriteResource resource)
    {
        if (IsBusy) throw new InvalidOperationException("请等待整合包搜索或安装结束后再打开收藏。");
        if (!resource.IsValid() || resource.ResourceType != "modpack")
            throw new InvalidOperationException("该收藏不是有效的整合包项目。");
        SelectedPlatform = resource.Platform;
        _searchToken++;
        SelectedProject = null;
        Results.Clear();
        CanLoadMore = false;
        var project = new ModpackItem(resource.Title, resource.Description, resource.Slug, 0,
            resource.Platform, resource.Platform == "Modrinth" ? resource.ProjectId : null,
            resource.Platform == "CurseForge" ? long.Parse(resource.ProjectId) : 0);
        Results.Add(project);
        SelectedProject = project;
        return _versionLoad;
    }

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private bool _includeOptionalFiles = true;

    [ObservableProperty]
    private string _localPath = "";

    [ObservableProperty]
    private string _status = "搜索 Modrinth / CurseForge 整合包";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    partial void OnSelectedProjectChanged(ModpackItem? value)
    {
        OnPropertyChanged(nameof(HasResourceDetail));
        OnPropertyChanged(nameof(ProjectPageText));
        Favorite.Resource = value is null ? null : new FavoriteResource(value.Platform,
            value.ModrinthId ?? value.CurseForgeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "modpack", value.Title, value.Description, value.Slug);
        UpdateCommands();
        _versionLoad = LoadModpackVersionsAsync(value);
    }

    partial void OnSelectedModpackVersionChanged(ModpackVersionItem? value) =>
        UpdateCommands();

    partial void OnSelectedPlatformChanged(string value)
    {
        _searchToken++;
        Results.Clear();
        ModpackVersions.Clear();
        SelectedModpackVersion = null;
        SelectedProject = null;
        CanLoadMore = false;
        Status = value == "CurseForge"
            ? "搜索 CurseForge 整合包（需要 API Key）"
            : "搜索 Modrinth 整合包";
    }

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    partial void OnGameVersionChanged(string value) => _versionLoad = LoadModpackVersionsAsync(SelectedProject);

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedProject is not null;
        CanCancel = IsBusy;
    }

    public async Task LoadAsync()
    {
        var request = ++_instanceLoadRequest;
        var root = _services.Settings.GameDirectory;
        var repository = new VersionRepository(root);
        var installed = repository.ListInstalled();
        var vanilla = new List<string>();
        foreach (var version in installed)
        {
            try
            {
                var json = await repository.GetVersionAsync(version.Id);
                if (MinecraftVersionUtil.IsModable(json)) continue;
                vanilla.Add(version.Id);
            }
            catch
            {
                // 损坏的实例跳过
            }
        }
        if (request == _instanceLoadRequest && _services.Settings.GameDirectory == root && !IsBusy)
            GameVersion = vanilla.FirstOrDefault(id => id == GameVersion) ?? vanilla.FirstOrDefault() ?? "";
    }

    private async Task LoadModpackVersionsAsync(ModpackItem? project)
    {
        var token = ++_versionLoadToken;
        var game = GameVersion;
        ModpackVersions.Clear();
        SelectedModpackVersion = null;
        if (project is null) return;
        try
        {
            if (project.Platform == "CurseForge")
            {
                var files = await _services.CurseForge.GetFilesAsync(
                    project.CurseForgeId, game, null);
                if (token != _versionLoadToken) return;
                foreach (var file in files)
                    ModpackVersions.Add(new ModpackVersionItem(
                        file.DisplayName, file.FileName, null, file));
            }
            else
            {
                var versions = await _services.Modrinth.GetVersionsAsync(project.ModrinthId!);
                if (token != _versionLoadToken) return;
                foreach (var version in versions
                             .Where(v => string.IsNullOrWhiteSpace(game) || v.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase))
                             .Where(v => v.Files.Count > 0)
                             .OrderByDescending(v => v.DatePublished))
                    ModpackVersions.Add(new ModpackVersionItem(
                        version.VersionNumber,
                        $"{version.VersionNumber} · {version.VersionType}",
                        version, null));
            }
            SelectedModpackVersion = ModpackVersions.FirstOrDefault();
            Status = ModpackVersions.Count == 0
                ? "没有找到兼容的整合包版本"
                : $"共 {ModpackVersions.Count} 个兼容版本";
        }
        catch (Exception ex)
        {
            if (token == _versionLoadToken)
                Status = "获取版本列表失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        _hasBrowsedPacks = true;
        _searchOffset = 0;
        Results.Clear();
        SelectedProject = null;
        CanLoadMore = false;
        await FetchModpacksAsync(append: false);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore || IsBusy) return;
        await FetchModpacksAsync(append: true);
    }

    private async Task FetchModpacksAsync(bool append)
    {
        if (IsBusy) return;
        var searchToken = ++_searchToken;
        IsBusy = true;
        Status = $"正在搜索 {SelectedPlatform} 整合包…";
        try
        {
            if (SelectedPlatform == "CurseForge")
            {
                if (!_services.CurseForge.IsConfigured)
                    throw new InvalidOperationException("未配置 CurseForge API Key，请在设置中填写。");
                var result = await _services.CurseForge.SearchAsync(
                    SearchText, GameVersion, "", 20, default,
                    CurseForgeClient.ClassIdModpacks, false, _searchOffset);
                if (searchToken != _searchToken) return;
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Name, project.Summary, project.Slug, project.Downloads,
                        "CurseForge", null, project.Id, project.LogoUrl));
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalCount;
                Status = $"共 {result.TotalCount} 个 CurseForge 整合包，选择后安装";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    SearchText, GameVersion, "", 20, default, "modpack", _searchOffset);
                if (searchToken != _searchToken) return;
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Title, project.Description, project.Slug, project.Downloads,
                        "Modrinth", project.Id, 0, project.IconUrl));
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalHits;
                Status = $"共 {result.TotalHits} 个 Modrinth 整合包，选择后安装";
            }
        }
        catch (Exception ex)
        {
            if (searchToken == _searchToken) Status = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    public string ProjectPageText => SelectedProject is null
        ? "打开项目页"
        : SelectedProject.Platform == "CurseForge" ? "转到 CurseForge" : "转到 Modrinth";

    [RelayCommand]
    private void CopyProjectName()
    {
        var name = SelectedProject?.Title;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(name);
            Status = "已复制名称：" + name;
        }
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        if (SelectedProject is null) return;
        var selected = SelectedProject;
        var url = selected.Platform == "CurseForge"
            ? string.IsNullOrWhiteSpace(selected.Slug)
                ? null
                : $"https://www.curseforge.com/minecraft/modpacks/{selected.Slug}"
            : string.IsNullOrWhiteSpace(selected.Slug)
                ? null
                : $"https://modrinth.com/modpack/{selected.Slug}";
        if (url is null)
        {
            Status = "该项目没有可用的项目页链接";
            return;
        }
        try
        {
            WMCL.App.Services.ExternalLauncher.Open(url);
            Status = "已在浏览器中打开项目页";
        }
        catch (Exception ex)
        {
            Status = "打开项目页失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedProject is null || IsBusy) return;
        var project = SelectedProject;
        var version = SelectedModpackVersion;
        var gameVersion = GameVersion;
        var includeOptional = IncludeOptionalFiles;
        var name = string.IsNullOrWhiteSpace(InstanceName) ? project.Slug : InstanceName.Trim();
        var root = _services.Settings.GameDirectory;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {SelectedProject.Title}…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装整合包");
            var imports = _imports ?? _services.CreateModpackImports(root);
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var id = await InstallSelectedVersionAsync(imports, project, version, gameVersion, name, includeOptional, progress, _cts.Token);
            Status = $"整合包安装完成：{id}";
            Progress = 100;
            InstanceInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消安装";
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
            UpdateCommands();
        }
    }

    private async Task<string> InstallSelectedVersionAsync(
        ModpackImportWorkflow imports, ModpackItem project, ModpackVersionItem? version, string gameVersion, string name, bool includeOptional,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        if (version?.CurseForge is { } cfFile)
            return await InstallCurseForgePackAsync(imports, cfFile, name, includeOptional, progress, ct);
        if (version?.Modrinth is { } mrVersion)
            return await InstallModrinthVersionAsync(imports, mrVersion, name, includeOptional, progress, ct);
        if (project.Platform == "CurseForge")
        {
            var file = await _services.CurseForge.FindFileAsync(
                           project.CurseForgeId, gameVersion, null, ct)
                       ?? throw new InvalidOperationException(
                           $"CurseForge 整合包 {project.CurseForgeId} 没有适用于 {gameVersion} 的版本。");
            return await InstallCurseForgePackAsync(imports, file, name, includeOptional, progress, ct);
        }
        var versions = await _services.Modrinth.GetVersionsAsync(project.ModrinthId!, ct, gameVersion);
        var latest = versions.Where(item => item.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) && item.Files.Count > 0)
            .OrderByDescending(item => item.DatePublished).FirstOrDefault()
            ?? throw new InvalidOperationException("找不到适用于 Minecraft " + gameVersion + " 的整合包版本。");
        return await InstallModrinthVersionAsync(imports, latest, name, includeOptional, progress, ct);
    }

    private async Task<string> InstallModrinthVersionAsync(ModpackImportWorkflow imports, ModrinthVersion version, string? name, bool includeOptional,
        IProgress<JobProgress> progress, CancellationToken ct)
    {
        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files.First();
        var path = await _services.Modpack.DownloadArchiveAsync(file, progress, ct);
        return await imports.InstallModrinthAsync(path, name, includeOptional, progress, ct);
    }

    private async Task<string> InstallCurseForgePackAsync(
        ModpackImportWorkflow imports,
        CurseForgeFile cfFile,
        string name,
        bool includeOptional,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var path = await _services.CurseForgeModpack.DownloadArchiveAsync(cfFile, progress, ct);
        return await imports.InstallCurseForgeAsync(path, name, includeOptional, progress, ct);
    }

    [RelayCommand]
    private async Task InstallLocalAsync()
    {
        var path = LocalPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || IsBusy) return;
        if (!File.Exists(path))
        {
            Status = "本地整合包文件不存在";
            return;
        }

        var name = string.IsNullOrWhiteSpace(InstanceName) ? null : InstanceName.Trim();
        var includeOptional = IncludeOptionalFiles;
        var root = _services.Settings.GameDirectory;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "正在安装本地整合包…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装本地整合包");
            var imports = _imports ?? _services.CreateModpackImports(root);
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var id = await imports.InstallLocalAsync(path, name, includeOptional, progress, _cts.Token);
            Status = $"整合包安装完成：{id}";
            InstanceInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消安装";
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
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
}


public sealed record ModpackItem(
    string Title,
    string Description,
    string Slug,
    long Downloads,
    string Platform,
    string? ModrinthId,
    long CurseForgeId,
    string? IconUrl = null);


public sealed record ModpackVersionItem(
    string Label,
    string Detail,
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge);
