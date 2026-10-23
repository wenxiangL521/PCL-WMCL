using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;

namespace WMCL.App.ViewModels;

public sealed record ResourceBatchRow(string Title, string Detail, string State);

public partial class ResourceFavoriteBatchViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Func<string?>? _preferredInstance;
    private FavoriteResource[] _selection = [];
    private ResourceBatchPlan? _plan;
    private CancellationTokenSource? _operation;
    private int _generation;
    private int _targetGeneration;
    private bool _planInvalidatedDuringInstall;
    private Task _targetLoad = Task.CompletedTask;

    public ResourceFavoriteBatchViewModel(AppServices services, Func<string?>? preferredInstance = null)
    {
        _services = services;
        _preferredInstance = preferredInstance;
        services.GameDirectoryChanged += (_, _) =>
        {
            InvalidatePlan();
            _targetLoad = LoadTargetsAsync();
        };
    }

    public ObservableCollection<string> Instances { get; } = [];
    public ObservableCollection<string> Saves { get; } = [];
    public ObservableCollection<ResourceBatchRow> Rows { get; } = [];

    [ObservableProperty]
    private bool _isOpen;
    [ObservableProperty]
    private string? _selectedInstance;
    [ObservableProperty]
    private string? _selectedSave;
    [ObservableProperty]
    private bool _isLoadingTarget;
    [ObservableProperty]
    private bool _isChecking;
    [ObservableProperty]
    private bool _isInstalling;
    [ObservableProperty]
    private string _targetInfo = "请选择安装目标";
    [ObservableProperty]
    private string _status = "检查后将列出具体文件、前置及跳过原因。";
    [ObservableProperty]
    private double _progress;
    [ObservableProperty]
    private string _progressText = "";

    public bool NeedsSave => _selection.Any(resource => resource.ResourceType == "datapack");
    public bool IsBusy => IsChecking || IsInstalling;
    public bool CanCheck => _selection.Length > 0 && SelectedInstance is not null && !IsLoadingTarget && !IsBusy &&
                            (!NeedsSave || SelectedSave is not null);
    public bool CanInstall => _plan is { Artifacts.Length: > 0 } && !IsBusy && !IsLoadingTarget;
    public string SelectionText => $"为选中的 {_selection.Length} 个收藏安装适用于目标实例的最新版本";

    public void SetSelection(FavoriteResource[] selection)
    {
        if (_selection.SequenceEqual(selection)) return;
        _selection = selection;
        InvalidatePlan();
        OnPropertyChanged(nameof(NeedsSave));
        OnPropertyChanged(nameof(SelectionText));
        NotifyCommands();
    }

    partial void OnSelectedInstanceChanged(string? value) => _targetLoad = LoadSelectedTargetAsync();
    partial void OnSelectedSaveChanged(string? value) => InvalidatePlan();
    partial void OnIsCheckingChanged(bool value) => NotifyCommands();
    partial void OnIsInstallingChanged(bool value) => NotifyCommands();
    partial void OnIsLoadingTargetChanged(bool value) => NotifyCommands();

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanInstall));
    }

    private void InvalidatePlan()
    {
        if (IsInstalling)
        {
            _planInvalidatedDuringInstall = true;
            return;
        }
        _generation++;
        if (IsChecking) _operation?.Cancel();
        _plan = null;
        Rows.Clear();
        Status = "检查后将列出具体文件、前置及跳过原因。";
        NotifyCommands();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsBusy || _selection.Length == 0) return;
        IsOpen = true;
        InvalidatePlan();
        await LoadTargetsAsync();
    }

    private async Task LoadTargetsAsync()
    {
        var previous = SelectedInstance ?? _preferredInstance?.Invoke();
        Instances.Clear();
        foreach (var instance in _services.Repository.ListInstalled()) Instances.Add(instance.Id);
        SelectedInstance = previous is not null && Instances.Contains(previous) ? previous : Instances.FirstOrDefault();
        _targetLoad = LoadSelectedTargetAsync();
        await _targetLoad;
        if (Instances.Count == 0) Status = "还没有可用的目标实例，请先安装游戏。";
    }

    private async Task LoadSelectedTargetAsync()
    {
        var generation = ++_targetGeneration;
        InvalidatePlan();
        Saves.Clear();
        SelectedSave = null;
        TargetInfo = "请选择安装目标";
        IsLoadingTarget = false;
        if (SelectedInstance is not { } instance) return;
        IsLoadingTarget = true;
        try
        {
            var target = await _services.ResourceBatch.GetTargetAsync(instance);
            if (generation != _targetGeneration) return;
            TargetInfo = target.Detail + "\n" + target.GameDirectory;
            var savesDirectory = Path.Combine(target.GameDirectory, "saves");
            if (Directory.Exists(savesDirectory))
                foreach (var path in Directory.EnumerateDirectories(savesDirectory).Order(StringComparer.OrdinalIgnoreCase))
                    if (!Path.GetFileName(path).StartsWith(".wmcl-", StringComparison.Ordinal)) Saves.Add(Path.GetFileName(path));
            SelectedSave = Saves.FirstOrDefault();
            if (NeedsSave && SelectedSave is null) Status = "此实例没有存档，请先创建存档后再安装数据包。";
        }
        catch (Exception exception)
        {
            if (generation == _targetGeneration) Status = "读取目标失败：" + exception.Message;
        }
        finally
        {
            if (generation == _targetGeneration) IsLoadingTarget = false;
        }
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        await _targetLoad;
        if (!CanCheck || SelectedInstance is not { } instance) return;
        InvalidatePlan();
        var generation = _generation;
        var selected = _selection.ToArray();
        var save = NeedsSave ? SelectedSave : null;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsChecking = true;
        Status = "正在检查版本与必需前置…";
        try
        {
            var plan = await _services.ResourceBatch.BuildAsync(selected, instance, save, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            _plan = plan;
            foreach (var artifact in plan.Artifacts) Rows.Add(new(artifact.Title, artifact.Detail, "待安装"));
            foreach (var skipped in plan.Skipped) Rows.Add(new(skipped.Title, skipped.Reason, "跳过"));
            Status = $"将安装 {plan.Artifacts.Length} 项资源（含 {plan.Artifacts.Count(item => item.Entry.IsDependency)} 项前置），跳过 {plan.Skipped.Length} 项。请确认目标与文件后开始安装。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation) Status = "已取消安装检查";
        }
        catch (Exception exception)
        {
            if (generation == _generation) Status = "无法生成安装计划：" + exception.Message;
        }
        finally
        {
            _operation = null;
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!CanInstall || _plan is not { } plan) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _planInvalidatedDuringInstall = false;
        IsInstalling = true;
        Progress = 0;
        Status = "正在安装收藏资源及必需前置…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Detail ?? update.Stage;
            });
            var result = await _services.ResourceBatch.InstallAsync(plan, progress, cancellation.Token);
            Rows.Clear();
            foreach (var outcome in result.Outcomes)
                Rows.Add(new(outcome.Artifact.Title, outcome.Detail, outcome.State switch
                {
                    "installed" => "已安装",
                    "reused" => "已存在",
                    "canceled" => "已取消",
                    _ => "失败"
                }));
            foreach (var skipped in plan.Skipped) Rows.Add(new(skipped.Title, skipped.Reason, "跳过"));
            Status = result.Summary + $" · 跳过 {plan.Skipped.Length} 项";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "已取消批量安装"; }
        catch (Exception exception) { Status = "批量安装失败：" + exception.Message; }
        finally
        {
            _operation = null;
            if (_planInvalidatedDuringInstall) _plan = null;
            IsInstalling = false;
            Progress = 0;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    private void Close()
    {
        if (!IsBusy) IsOpen = false;
    }
}
