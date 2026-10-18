using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Minecraft;
using PCL.Modding.CurseForge;
using PCL.Modding.Modpack;
using PCL.Modding.Modrinth;

namespace PCL.App.ViewModels;

public partial class ModpackViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;

    public ModpackViewModel(AppServices services)
    {
        _services = services;
        _gameVersion = "1.21.1";
    }

    public event EventHandler? InstanceInstalled;
    public ObservableCollection<ModpackItem> Results { get; } = [];
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

    [ObservableProperty]
    private string _instanceName = "";

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
        UpdateCommands();
        _ = LoadModpackVersionsAsync(value);
    }

    partial void OnSelectedModpackVersionChanged(ModpackVersionItem? value) =>
        UpdateCommands();

    partial void OnSelectedPlatformChanged(string value)
    {
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

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedProject is not null;
        CanCancel = IsBusy;
    }

    public async Task LoadAsync()
    {
        var installed = _services.Repository.ListInstalled();
        foreach (var version in installed)
        {
            try
            {
                var json = await _services.Repository.GetVersionAsync(version.Id);
                if (MinecraftVersionUtil.IsModable(json)) continue;
                GameVersion = version.Id;
                return;
            }
            catch
            {
                // 损坏的实例跳过
            }
        }
    }

    private async Task LoadModpackVersionsAsync(ModpackItem? project)
    {
        var token = ++_versionLoadToken;
        ModpackVersions.Clear();
        SelectedModpackVersion = null;
        if (project is null) return;
        try
        {
            if (project.Platform == "CurseForge")
            {
                var files = await _services.CurseForge.GetFilesAsync(
                    project.CurseForgeId, GameVersion, null);
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
                             .Where(v => v.GameVersions.Contains(GameVersion, StringComparer.OrdinalIgnoreCase))
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
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Name, project.Summary, project.Slug, project.Downloads,
                        "CurseForge", null, project.Id));
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalCount;
                Status = $"共 {result.TotalCount} 个 CurseForge 整合包，选择后安装";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    SearchText, GameVersion, "", 20, default, "modpack", _searchOffset);
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Title, project.Description, project.Slug, project.Downloads,
                        "Modrinth", project.Id, 0));
                _searchOffset += result.Projects.Count;
                CanLoadMore = result.Projects.Count >= 20 && _searchOffset < result.TotalHits;
                Status = $"共 {result.TotalHits} 个 Modrinth 整合包，选择后安装";
            }
            if (!append) SelectedProject = Results.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Status = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedProject is null || IsBusy) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {SelectedProject.Title}…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var name = string.IsNullOrWhiteSpace(InstanceName)
                ? SelectedProject.Slug
                : InstanceName.Trim();
            var id = await InstallSelectedVersionAsync(name, progress, _cts.Token);
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
        string name,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var project = SelectedProject!;
        var version = SelectedModpackVersion;
        if (version?.CurseForge is { } cfFile)
        {
            var directory = Path.Combine(PclPaths.CacheDirectory, "modpacks");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, cfFile.FileName);
            if (!File.Exists(path))
                await PCL.Modding.CurseForge.CurseForgeClient.DownloadFileAsync(
                    cfFile, path, progress, ct);
            return await _services.CurseForgeModpack.InstallFromFileAsync(
                path, name, progress, ct);
        }
        if (version?.Modrinth is { } mrVersion)
        {
            var file = mrVersion.Files.FirstOrDefault(f => f.Primary) ?? mrVersion.Files.First();
            var directory = Path.Combine(PclPaths.CacheDirectory, "modpacks");
            Directory.CreateDirectory(directory);
            await _services.Modrinth.InstallFileAsync(file, directory, progress, ct);
            return await _services.Modpack.InstallFromFileAsync(
                Path.Combine(directory, file.Filename), name, progress, ct);
        }
        return project.Platform == "CurseForge"
            ? await _services.CurseForgeModpack.InstallFromProjectAsync(
                project.CurseForgeId, GameVersion, name, progress, ct)
            : await _services.Modpack.InstallFromModrinthAsync(
                project.ModrinthId!, GameVersion, name, progress, ct);
    }

    [RelayCommand]
    private async Task InstallLocalAsync()
    {
        var path = LocalPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || IsBusy) return;
        if (!File.Exists(path))
        {
            Status = "本地 .mrpack 文件不存在";
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "正在安装本地整合包…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var name = string.IsNullOrWhiteSpace(InstanceName) ? null : InstanceName.Trim();
            string id;
            var localKind = LocalModpackInstaller.Detect(path);
            if (localKind != LocalModpackKind.Unknown)
                id = await _services.LocalModpack.InstallFromFileAsync(
                    path, name, progress, _cts.Token);
            else if (IsCurseForgePack(path))
                id = await _services.CurseForgeModpack.InstallFromFileAsync(
                    path, name, progress, _cts.Token);
            else
                id = await _services.Modpack.InstallFromFileAsync(
                    path, name, progress, _cts.Token);
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

    private static bool IsCurseForgePack(string path)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            return archive.GetEntry("manifest.json") is not null &&
                   archive.GetEntry("modrinth.index.json") is null;
        }
        catch
        {
            return false;
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
    long CurseForgeId);


public sealed record ModpackVersionItem(
    string Label,
    string Detail,
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge);
