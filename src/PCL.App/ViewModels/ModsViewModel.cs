using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Modding.Fabric;

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

    partial void OnSelectedGameVersionChanged(string? value) => _ = LoadLoadersAsync();

    partial void OnSelectedLoaderChanged(string? value) => UpdateCommands();

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedGameVersion is not null && SelectedLoader is not null;
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
