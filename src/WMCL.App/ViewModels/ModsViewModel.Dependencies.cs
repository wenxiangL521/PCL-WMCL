using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Core.Jobs;
using WMCL.Modding.Resources;

namespace WMCL.App.ViewModels;

public partial class ModsViewModel
{
    private int _modDependencyRequest;
    private Task _modDependencyLoad = Task.CompletedTask;
    private ResourceInstallPlan? _modInstallPlan;
    private CancellationTokenSource? _modDependencyCancellation;
    private CancellationTokenSource? _modInstallCancellation;

    public ObservableCollection<ResourceInstallEntry> ModDependencies { get; } = [];
    public bool HasModDependencies => ModDependencies.Count > 0;
    public bool IsInstallingMod => _modInstallCancellation is not null;

    [ObservableProperty]
    private string _modDependencyStatus = "选择具体版本后检查前置资源";

    [ObservableProperty]
    private bool _isResolvingModDependencies;

    [ObservableProperty]
    private bool _hasModDependencyError;

    private async Task RefreshModDependenciesAsync()
    {
        var request = ++_modDependencyRequest;
        _modDependencyCancellation?.Cancel();
        _modInstallPlan = null;
        ModDependencies.Clear();
        OnPropertyChanged(nameof(HasModDependencies));
        HasModDependencyError = false;
        IsResolvingModDependencies = false;
        ModDependencyStatus = "选择具体版本后检查前置资源";
        UpdateModCommands();
        var project = SelectedMod;
        var version = SelectedModVersion;
        var platform = SelectedModSource;
        var game = _modGameVersion;
        var loader = _modLoader;
        if (project is null || version is null || string.IsNullOrWhiteSpace(game) || loader == "vanilla") return;
        using var cancellation = new CancellationTokenSource();
        _modDependencyCancellation = cancellation;
        IsResolvingModDependencies = true;
        ModDependencyStatus = "正在检查必需前置及版本兼容性…";
        try
        {
            var plan = await _services.ResourcePlanner.ResolveAsync(
                [new ResourceInstallRequest(platform, project.Id, project.Title, Modrinth: version.Modrinth, CurseForge: version.CurseForge)],
                game, loader, cancellation.Token);
            if (request != _modDependencyRequest) return;
            _modInstallPlan = plan;
            foreach (var dependency in plan.Entries.Where(entry => entry.IsDependency)) ModDependencies.Add(dependency);
            OnPropertyChanged(nameof(HasModDependencies));
            ModDependencyStatus = ModDependencies.Count == 0 ? "此版本未声明额外的必需前置"
                : $"此版本声明 {ModDependencies.Count} 项必需前置，安装时一并处理";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (request == _modDependencyRequest)
            {
                HasModDependencyError = true;
                ModDependencyStatus = "无法安装此版本：" + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_modDependencyCancellation, cancellation)) _modDependencyCancellation = null;
            if (request == _modDependencyRequest)
            {
                IsResolvingModDependencies = false;
                UpdateModCommands();
            }
        }
    }

    [RelayCommand]
    private async Task RetryModDependenciesAsync()
    {
        if (IsModBusy || IsResolvingModDependencies) return;
        _modDependencyLoad = RefreshModDependenciesAsync();
        await _modDependencyLoad;
    }

    [RelayCommand]
    private async Task InstallModAsync()
    {
        if (!CanInstallMod || _modInstallPlan is not { } plan || SelectedMod is not { } project) return;
        var directory = ModsDirectory;
        var root = ModTargetRoot;
        var platform = SelectedModSource;
        using var cancellation = new CancellationTokenSource();
        _modInstallCancellation = cancellation;
        OnPropertyChanged(nameof(IsInstallingMod));
        IsModBusy = true;
        ModProgress = 0;
        ModProgressText = "";
        ModStatus = $"正在安装 {project.Title} 及其必需前置…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装 Mod");
            var progress = new Progress<JobProgress>(update =>
            {
                ModProgress = (update.Percent ?? 0) * 100;
                ModProgressText = FormatProgress(update);
            });
            var files = platform == "CurseForge"
                ? (await _services.CurseForge.InstallPlanAsync(plan, directory, progress, cancellation.Token)).Files
                : (await _services.Modrinth.InstallPlanAsync(plan, directory, progress, cancellation.Token)).Files;
            RefreshLocalMods();
            ModStatus = files.Count == 0 ? "所选版本及必需前置均已安装，无需重复下载"
                : $"已安装：{string.Join("，", files)}";
            ModProgress = 100;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ModStatus = "已取消 Mod 安装"; }
        catch (Exception exception) { ModStatus = "安装失败：" + exception.Message; }
        finally
        {
            _modInstallCancellation = null;
            OnPropertyChanged(nameof(IsInstallingMod));
            IsModBusy = false;
            ModProgress = 0;
            ModProgressText = "";
            UpdateModCommands();
        }
    }

    [RelayCommand]
    private void CancelModInstall() => _modInstallCancellation?.Cancel();
}
