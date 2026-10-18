using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core.Jobs;
using PCL.Minecraft;
using PCL.Modding.CurseForge;
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
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    partial void OnSelectedProjectChanged(ModpackItem? value) =>
        UpdateCommands();

    partial void OnSelectedPlatformChanged(string value)
    {
        Results.Clear();
        SelectedProject = null;
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

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = $"正在搜索 {SelectedPlatform} 整合包…";
        try
        {
            Results.Clear();
            if (SelectedPlatform == "CurseForge")
            {
                if (!_services.CurseForge.IsConfigured)
                    throw new InvalidOperationException("未配置 CurseForge API Key，请在设置中填写。");
                var result = await _services.CurseForge.SearchAsync(
                    SearchText, GameVersion, "", 20, default,
                    CurseForgeClient.ClassIdModpacks, false);
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Name, project.Summary, project.Slug, project.Downloads,
                        "CurseForge", null, project.Id));
                SelectedProject = Results.FirstOrDefault();
                Status = $"共 {result.TotalCount} 个 CurseForge 整合包，选择后安装";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    SearchText, GameVersion, "", 20, default, "modpack");
                foreach (var project in result.Projects)
                    Results.Add(new ModpackItem(
                        project.Title, project.Description, project.Slug, project.Downloads,
                        "Modrinth", project.Id, 0));
                SelectedProject = Results.FirstOrDefault();
                Status = $"共 {result.TotalHits} 个 Modrinth 整合包，选择后安装";
            }
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
            var id = SelectedProject.Platform == "CurseForge"
                ? await _services.CurseForgeModpack.InstallFromProjectAsync(
                    SelectedProject.CurseForgeId, GameVersion, name, progress, _cts.Token)
                : await _services.Modpack.InstallFromModrinthAsync(
                    SelectedProject.ModrinthId!, GameVersion, name, progress, _cts.Token);
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
            if (IsCurseForgePack(path))
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
