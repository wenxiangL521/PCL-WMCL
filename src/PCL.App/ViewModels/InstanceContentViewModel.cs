using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Minecraft;
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
    private bool _hasShaderPack;

    [ObservableProperty]
    private bool _hasWorld;

    [ObservableProperty]
    private string _gameDirectory = "";

    [ObservableProperty]
    private string _status = "选择实例以查看资源包、光影和存档";

    public ObservableCollection<string> DownloadTypes { get; } = ["资源包", "光影"];
    public ObservableCollection<string> ImportTypes { get; } = ["资源包", "光影", "存档"];

    [ObservableProperty]
    private string _selectedImportType = "资源包";

    [ObservableProperty]
    private string _importPath = "";
    public ObservableCollection<ModrinthProject> DownloadResults { get; } = [];

    [ObservableProperty]
    private string _selectedDownloadType = "资源包";

    [ObservableProperty]
    private string _downloadSearchText = "";

    [ObservableProperty]
    private ModrinthProject? _selectedDownloadProject;

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

    partial void OnSelectedDownloadProjectChanged(ModrinthProject? value) =>
        UpdateDownloadCommands();

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

    partial void OnSelectedResourcePackChanged(InstanceContentItem? value) =>
        HasResourcePack = value is not null;

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
            GameDirectory = MinecraftVersionUtil.GetGameDirectory(
                _services.Settings.GameDirectory, id, version);
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

        var projectType = SelectedDownloadType == "光影" ? "shader" : "resourcepack";
        IsDownloadBusy = true;
        DownloadStatus = $"正在搜索 Modrinth {SelectedDownloadType}…";
        try
        {
            var result = await _services.Modrinth.SearchAsync(
                DownloadSearchText, _gameVersion, "fabric", 20, default, projectType);
            DownloadResults.Clear();
            foreach (var project in result.Projects) DownloadResults.Add(project);
            SelectedDownloadProject = DownloadResults.FirstOrDefault();
            DownloadStatus = $"共 {result.TotalHits} 个结果";
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
    private async Task InstallDownloadAsync()
    {
        if (SelectedDownloadProject is null || IsDownloadBusy) return;

        var projectType = SelectedDownloadType == "光影" ? "shader" : "resourcepack";
        var target = Path.Combine(
            GameDirectory,
            projectType == "shader" ? "shaderpacks" : "resourcepacks");
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
            var result = await _services.Modrinth.InstallContentAsync(
                SelectedDownloadProject.Id, _gameVersion, target, projectType, progress);
            DownloadStatus = result.Files.Count == 0
                ? "文件已存在，无需重复安装"
                : $"已安装：{string.Join("，", result.Files)}";
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

    private void UpdateDownloadCommands()
    {
        CanDownload = !IsDownloadBusy && SelectedDownloadProject is not null;
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
