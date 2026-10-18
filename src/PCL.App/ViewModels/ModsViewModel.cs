using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Modding.Fabric;
using PCL.Modding.Modrinth;

namespace PCL.App.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly FabricInstaller _fabric;
    private readonly List<FabricLoaderInfo> _loaderInfos = [];
    private CancellationTokenSource? _cts;

    public ModsViewModel(AppServices services)
    {
        _services = services;
        _fabric = services.Fabric;
    }

    public event EventHandler? VersionInstalled;

    public ObservableCollection<string> GameVersions { get; } = [];
    public ObservableCollection<string> Loaders { get; } = [];
    public string ModsDirectory => Path.Combine(_services.Settings.GameDirectory, "mods");
    public bool HasGameVersions => GameVersions.Count > 0;

    [ObservableProperty]
    private string? _selectedGameVersion;

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

    public ObservableCollection<ModrinthProject> SearchResults { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ModrinthProject? _selectedMod;

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
        var vanilla = installed
            .Where(v => !v.Id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Id)
            .ToList();

        GameVersions.Clear();
        foreach (var id in vanilla) GameVersions.Add(id);
        OnPropertyChanged(nameof(HasGameVersions));

        SelectedGameVersion = GameVersions.FirstOrDefault();
        if (SelectedGameVersion is not null)
            await LoadLoadersAsync();
        else
            Status = "还没有原版实例，请先在“下载”页安装一个版本";
    }

    partial void OnSelectedGameVersionChanged(string? value)
    {
        UpdateModCommands();
        _ = LoadLoadersAsync();
    }

    partial void OnSelectedModChanged(ModrinthProject? value) => UpdateModCommands();

    partial void OnIsModBusyChanged(bool value) => UpdateModCommands();

    partial void OnSelectedLoaderChanged(string? value) => UpdateCommands();

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedGameVersion is not null && SelectedLoader is not null;
        UpdateModCommands();
    }

    private void UpdateModCommands()
    {
        CanInstallMod = !IsModBusy && SelectedMod is not null && SelectedGameVersion is not null;
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

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        var game = SelectedGameVersion;
        if (string.IsNullOrWhiteSpace(game))
        {
            ModStatus = "请先选择一个游戏版本";
            return;
        }

        IsModBusy = true;
        ModStatus = "正在搜索 Modrinth…";
        SearchResults.Clear();
        try
        {
            var result = await _services.Modrinth.SearchAsync(SearchText, game);
            foreach (var project in result.Projects)
                SearchResults.Add(project);
            SelectedMod = SearchResults.FirstOrDefault();
            ModStatus = $"共 {result.TotalHits} 个结果";
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
        var game = SelectedGameVersion;
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
            var result = await _services.Modrinth.InstallAsync(
                mod.Id, game, ModsDirectory, "fabric", progress);
            ModStatus = result.Files.Count == 0
                ? "没有需要下载的文件（可能已安装）"
                : $"已安装：{string.Join("，", result.Files)}";
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
    private void OpenModsFolder()
    {
        Directory.CreateDirectory(ModsDirectory);
        OpenPath(ModsDirectory);
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
