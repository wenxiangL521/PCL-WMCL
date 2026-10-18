using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core.Jobs;
using PCL.Minecraft;
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
    public ObservableCollection<ModrinthProject> Results { get; } = [];

    [ObservableProperty]
    private string _gameVersion = "1.21.1";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ModrinthProject? _selectedProject;

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private string _localPath = "";

    [ObservableProperty]
    private string _status = "搜索 Modrinth 整合包（Fabric / Forge / NeoForge）";

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

    partial void OnSelectedProjectChanged(ModrinthProject? value) =>
        UpdateCommands();

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
        Status = "正在搜索 Modrinth 整合包…";
        try
        {
            var result = await _services.Modrinth.SearchAsync(
                SearchText, GameVersion, "", 20, default, "modpack");
            Results.Clear();
            foreach (var project in result.Projects) Results.Add(project);
            SelectedProject = Results.FirstOrDefault();
            Status = $"共 {result.TotalHits} 个整合包，选择后安装";
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
            var id = await _services.Modpack.InstallFromModrinthAsync(
                SelectedProject.Id, GameVersion, name, progress, _cts.Token);
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
            var id = await _services.Modpack.InstallFromFileAsync(
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

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "正在取消…";
    }
}
