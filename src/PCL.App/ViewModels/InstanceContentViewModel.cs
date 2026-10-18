using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Minecraft;
using PCL.Modding.CurseForge;
using PCL.Modding.Local;
using PCL.Modding.Modrinth;

namespace PCL.App.ViewModels;

public partial class InstanceContentViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private string _gameVersion = "";

    public InstanceContentViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<string> Instances { get; } = [];
    public ObservableCollection<InstanceContentItem> ResourcePacks { get; } = [];
    public ObservableCollection<InstanceContentItem> ShaderPacks { get; } = [];
    public ObservableCollection<InstanceContentItem> Worlds { get; } = [];

    [ObservableProperty]
    private string? _selectedInstance;

    [ObservableProperty]
    private InstanceContentItem? _selectedResourcePack;

    [ObservableProperty]
    private InstanceContentItem? _selectedShaderPack;

    [ObservableProperty]
    private InstanceContentItem? _selectedWorld;

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
        SelectedImportType = ContentTypeLabel;
        SelectedDownloadType = ShowWorlds ? "世界" : ContentTypeLabel;
        OnPropertyChanged(nameof(IsWorldMode));
        OnPropertyChanged(nameof(RequiresDataPackWorld));
        OnPropertyChanged(nameof(ContentTypeLabel));
        OnPropertyChanged(nameof(DownloadExample));
        DownloadStatus = ShowDataPacks ? "搜索数据包（Modrinth / CurseForge）"
            : ShowShaders ? "搜索光影（Modrinth / CurseForge）" : "搜索资源包（Modrinth / CurseForge）";
    }

    public ObservableCollection<string> DownloadTypes { get; } = ["资源包", "光影", "数据包", "世界", "插件"];
    public ObservableCollection<string> ImportTypes { get; } = ["资源包", "光影", "存档"];

    [ObservableProperty]
    private string _selectedImportType = "资源包";

    [ObservableProperty]
    private string _importPath = "";
    public ObservableCollection<ResourceItem> DownloadResults { get; } = [];
    public ObservableCollection<ResourceVersionItem> ResourceVersions { get; } = [];
    public ObservableCollection<string> Platforms { get; } = ["Modrinth", "CurseForge"];

    [ObservableProperty]
    private ResourceVersionItem? _selectedResourceVersion;

    private int _versionLoadToken;

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
        UpdateDownloadCommands();
        _ = LoadResourceVersionsAsync(value);
    }

    partial void OnSelectedResourceVersionChanged(ResourceVersionItem? value) =>
        UpdateDownloadCommands();

    private async Task LoadResourceVersionsAsync(ResourceItem? project)
    {
        var token = ++_versionLoadToken;
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        if (project is null) return;
        try
        {
            if (project.Platform == "CurseForge")
            {
                var files = await _services.CurseForge.GetFilesAsync(
                    project.CurseForgeId, _gameVersion, null);
                if (token != _versionLoadToken) return;
                foreach (var file in files)
                    ResourceVersions.Add(new ResourceVersionItem(
                        file.DisplayName, file.FileName, null, file));
            }
            else
            {
                var versions = await _services.Modrinth.GetVersionsAsync(project.ModrinthId!);
                if (token != _versionLoadToken) return;
                foreach (var version in versions
                             .Where(v => v.GameVersions.Contains(_gameVersion, StringComparer.OrdinalIgnoreCase))
                             .Where(v => v.Files.Count > 0)
                             .OrderByDescending(v => v.DatePublished))
                    ResourceVersions.Add(new ResourceVersionItem(
                        version.VersionNumber,
                        $"{version.VersionNumber} · {version.VersionType}",
                        version, null));
            }
            SelectedResourceVersion = ResourceVersions.FirstOrDefault();
            DownloadStatus = ResourceVersions.Count == 0
                ? "没有找到兼容的版本"
                : $"共 {ResourceVersions.Count} 个兼容版本";
        }
        catch (Exception ex)
        {
            if (token == _versionLoadToken)
                DownloadStatus = "获取版本列表失败：" + ex.Message;
        }
    }

    partial void OnSelectedPlatformChanged(string value)
    {
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
        var installed = await Task.Run(() => _services.Repository.ListInstalled());
        Instances.Clear();
        foreach (var version in installed) Instances.Add(version.Id);

        if (SelectedInstance is null || !Instances.Contains(SelectedInstance))
            SelectedInstance = Instances.FirstOrDefault(id =>
                                 id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
                             ?? Instances.FirstOrDefault();

        if (SelectedInstance is null)
            Status = "还没有已安装实例";
        else
            await UpdateAsync();
    }

    partial void OnSelectedInstanceChanged(string? value) => _ = UpdateAsync();

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
        var id = SelectedInstance;
        if (string.IsNullOrWhiteSpace(id)) return;

        try
        {
            var version = await _services.Repository.GetVersionAsync(id);
            GameDirectory = IsolationPolicy.GetGameDirectory(
                _services.Settings.GameDirectory, id, version, _services.Settings.IsolationMode);
            _gameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            Refresh();
        }
        catch (Exception ex)
        {
            Status = "读取实例失败：" + ex.Message;
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

        Status = $"{ResourcePacks.Count} 个资源包 · {ShaderPacks.Count} 个光影 · {Worlds.Count} 个存档";
    }

    [RelayCommand]
    private async Task DeleteResourcePackAsync()
    {
        if (SelectedResourcePack is null) return;
        if (!await DialogService.ConfirmAsync(
                "删除资源包",
                $"确定删除 {SelectedResourcePack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            var name = SelectedResourcePack.Name;
            InstanceContentService.Delete(SelectedResourcePack);
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
        if (!await DialogService.ConfirmAsync(
                "删除光影",
                $"确定删除 {SelectedShaderPack.Name}？" + Environment.NewLine +
                "此操作不可恢复。"))
            return;

        try
        {
            var name = SelectedShaderPack.Name;
            InstanceContentService.Delete(SelectedShaderPack);
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
        if (!await DialogService.ConfirmAsync(
                "删除存档",
                $"确定删除存档 {SelectedWorld.Name}？" + Environment.NewLine +
                "此操作不可恢复，建议先备份。"))
            return;

        try
        {
            var name = SelectedWorld.Name;
            InstanceContentService.Delete(SelectedWorld);
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
            var destination = Path.Combine(
                PclPaths.DataDirectory, "backups", "worlds");
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
        if (string.IsNullOrWhiteSpace(ImportPath)) return;
        var kind = SelectedImportType switch
        {
            "光影" => InstanceContentKind.ShaderPack,
            "存档" => InstanceContentKind.World,
            _ => InstanceContentKind.ResourcePack
        };

        try
        {
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
        if (IsDownloadBusy || string.IsNullOrWhiteSpace(GameDirectory)) return;
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

    private async Task FetchDownloadsAsync(bool append)
    {
        if (IsDownloadBusy || string.IsNullOrWhiteSpace(GameDirectory)) return;
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
                    DownloadSearchText, _gameVersion, "", 20, default, classId, false,
                    _resourceSearchOffset);
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Name, project.Summary, "CurseForge", null, project.Id));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalCount;
                DownloadStatus = $"共 {result.TotalCount} 个 CurseForge 结果";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    DownloadSearchText, _gameVersion,
                    projectType == "plugin" ? "" : "fabric",
                    20, default, projectType, _resourceSearchOffset);
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Title, project.Description, "Modrinth", project.Id, 0));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalHits;
                DownloadStatus = $"共 {result.TotalHits} 个 Modrinth 结果";
            }
            if (!append) SelectedDownloadProject = DownloadResults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DownloadStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsDownloadBusy = false;
            UpdateDownloadCommands();
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
            PCL.App.Services.ExternalLauncher.Open(url);
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
            "plugin" => Path.Combine(GameDirectory, "plugins"),
            _ => Path.Combine(GameDirectory, "resourcepacks")
        };
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装 {SelectedDownloadProject.Title}…";
        try
        {
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
                        SelectedDownloadProject.CurseForgeId, selected.Id, target, progress)
                    : await _services.CurseForge.InstallContentAsync(
                        SelectedDownloadProject.CurseForgeId, _gameVersion, target,
                        SelectedDownloadType switch
                        {
                            "光影" => CurseForgeClient.ClassIdShaders,
                            "数据包" => CurseForgeClient.ClassIdDataPacks,
                            _ => CurseForgeClient.ClassIdResourcePacks
                        }, progress);
                files = result.Files;
            }
            else
            {
                var result = SelectedResourceVersion?.Modrinth is { } selected
                    ? await _services.Modrinth.InstallContentVersionAsync(
                        selected, target, projectType, progress)
                    : await _services.Modrinth.InstallContentAsync(
                        SelectedDownloadProject.ModrinthId!, _gameVersion, target, projectType, progress);
                files = result.Files;
            }
            DownloadStatus = files.Count == 0
                ? "文件已存在，无需重复安装"
                : $"已安装：{string.Join("，", files)}";
            DownloadProgress = 100;
            Refresh();
        }
        catch (Exception ex)
        {
            DownloadStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private async Task InstallWorldAsync()
    {
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装世界 {SelectedDownloadProject!.Title}…";
        string? temp = null;
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = update.Detail ?? update.Stage;
            });
            var file = SelectedResourceVersion?.CurseForge
                       ?? await _services.CurseForge.FindFileAsync(
                           SelectedDownloadProject.CurseForgeId, _gameVersion, null)
                       ?? throw new InvalidOperationException("没有找到适用于当前版本的世界文件。");
            temp = Path.Combine(Path.GetTempPath(), "pcl-world-" + Guid.NewGuid().ToString("N") + ".zip");
            await CurseForgeClient.DownloadFileAsync(file, temp, progress, default);
            var destination = WorldInstaller.Install(
                temp, Path.Combine(GameDirectory, "saves"), SelectedDownloadProject.Title);
            DownloadStatus = "世界已安装：" + Path.GetFileName(destination);
            DownloadProgress = 100;
            Refresh();
        }
        catch (Exception ex)
        {
            DownloadStatus = "世界安装失败：" + ex.Message;
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private void UpdateDownloadCommands()
    {
        CanDownload = !IsDownloadBusy &&
                      SelectedDownloadProject is not null &&
                      (!RequiresDataPackWorld || !string.IsNullOrWhiteSpace(SelectedDataPackWorld));
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


public sealed record ResourceItem(
    string Title,
    string Description,
    string Platform,
    string? ModrinthId,
    long CurseForgeId);


public sealed record ResourceVersionItem(
    string Label,
    string Detail,
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge);
