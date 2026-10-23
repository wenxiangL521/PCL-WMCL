using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Minecraft;
using WMCL.Core.Jobs;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Fabric;

namespace WMCL.App.ViewModels;

/// <summary>
/// “下载 → 原版游戏”安装页。组合安装只暴露 PCL2 允许的组合；
/// 加载器安装后复制 OptiFine/OptiFabric/Fabric API 到同一实例，而不是生成多个实例。
/// </summary>
public partial class InstallViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Func<string, ICombinedInstallBackend>? _installBackendFactory;

    public InstallViewModel(DownloadViewModel download, ModsViewModel mods, AppServices services)
        : this(download, mods, services, null)
    {
    }

    internal InstallViewModel(DownloadViewModel download, ModsViewModel mods, AppServices services,
        Func<string, ICombinedInstallBackend>? installBackendFactory,
        OptiFabricSelectionViewModel? optiFabricSelection = null,
        ApiSelectionViewModel? apiSelection = null,
        Func<string, string, Task<bool>>? confirmReset = null,
        ComponentRowActionService? rowActions = null,
        Func<string, string, Task<bool>>? confirmModification = null,
        Func<string, string, Task>? showInstanceError = null)
    {
        Download = download;
        Mods = mods;
        _services = services;
        _rowActions = rowActions ?? new ComponentRowActionService(services.Optifine);
        _installBackendFactory = installBackendFactory;
        if (confirmModification is not null || confirmReset is not null)
            _confirmModification = confirmModification ?? confirmReset!;
        if (showInstanceError is not null) _showInstanceError = showInstanceError;
        services.GameDirectoryChanged += (_, _) =>
        {
            CancelComponentRowActions();
            OnInstallGameDirectoryChanged();
        };
        OptiFabricSelection = optiFabricSelection ?? new OptiFabricSelectionViewModel(services.Optifabric);
        OptiFabricSelection.PropertyChanged += (_, _) =>
        {
            RefreshAdditionalCards();
            NotifyInstallHints();
        };
        ApiSelection = apiSelection ?? new ApiSelectionViewModel(new ApiComponentService(services.Modrinth));
        ApiSelection.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ApiSummary));
            RefreshAdditionalCards();
            NotifyInstallHints();
        };
        InitializeComponentCards();
        ApiSelection.Versions.CollectionChanged += (_, _) => RefreshAdditionalCards();
        OptiFabricSelection.Versions.CollectionChanged += (_, _) => RefreshAdditionalCards();
        VersionCatalog = new VersionCatalogViewModel();
        VersionCatalog.Finished += (_, version) =>
        {
            if (!CanEditInstall) return;
            if (version is not null)
            {
                if (IsExistingInstance)
                {
                    ChangeExistingMinecraft(version);
                    IsChoosingVersion = false;
                    return;
                }
                if (!IsExistingInstance)
                {
                    _isInstanceNameEdited = false;
                    UseVanilla();
                    InstallFabricApi = true;
                    InstallOptiFabric = true;
                    CombinedStatus = "选择 Minecraft 版本与加载器后一次安装";
                }
                if (_instanceTarget is { } target) _instanceTarget = target with { Minecraft = version.Id };
                _hasChosenVersion = true;
                Download.SelectedVersion = version;
                UpdateInstanceName();
                _selectionLoad = PrepareSelectedVersionAsync();
            }
            IsChoosingVersion = false;
        };
        Download.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadViewModel.SelectedVersion))
            {
                CancelComponentRowActions();
                ClearStaleLiteLoaderSelection();
                UpdateInstanceName();
                _selectionLoad = PrepareSelectedVersionAsync();
                NotifyInstallHints();
            }
            if (e.PropertyName == nameof(DownloadViewModel.IsBusy)) NotifyInstallAction();
        };
        _selectionLoad = PrepareSelectedVersionAsync();
        NotifyComponentFavorites();
        Mods.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ModsViewModel.SelectedLoader)
                or nameof(ModsViewModel.SelectedForgelikeVersion)
                or nameof(ModsViewModel.SelectedOptiFineVersion))
            {
                NotifyComponentSummaries();
                NotifyInstallHints();
            }
            if (e.PropertyName == nameof(ModsViewModel.SelectedFabricKind) &&
                IsFabricBase &&
                !string.Equals(SelectedBaseLoader, Mods.SelectedFabricKind,
                    StringComparison.Ordinal))
                SelectedBaseLoader = Mods.SelectedFabricKind;
        };
        UpdateInstanceName();
    }

    public string FabricFavoriteGlyph =>
        IsFavoriteComponent("Fabric") ? "★ 已收藏" : "☆ 收藏";
    public string ForgeFavoriteGlyph =>
        IsFavoriteComponent("Forge") ? "★ 已收藏" : "☆ 收藏";
    public string NeoForgeFavoriteGlyph =>
        IsFavoriteComponent("NeoForge") ? "★ 已收藏" : "☆ 收藏";
    public string OptiFineFavoriteGlyph =>
        IsFavoriteComponent("OptiFine") ? "★ 已收藏" : "☆ 收藏";

    internal bool IsFavoriteComponent(string kind) =>
        _services.Settings.FavoriteDownloadComponents.Contains(kind, StringComparer.OrdinalIgnoreCase);

    public void ToggleComponentFavorite(string kind)
    {
        var favorites = _services.Settings.FavoriteDownloadComponents;
        var index = favorites.FindIndex(item =>
            string.Equals(item, kind, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) favorites.RemoveAt(index);
        else favorites.Add(kind);
        _services.SaveSettings();
        NotifyComponentFavorites();
    }

    public bool HasFavoriteComponents =>
        _services.Settings.FavoriteDownloadComponents.Count > 0;

    public string FavoriteComponentsText =>
        string.Join(" · ", _services.Settings.FavoriteDownloadComponents);

    private void NotifyComponentFavorites()
    {
        RefreshComponentCards();
        OnPropertyChanged(nameof(HasFavoriteComponents));
        OnPropertyChanged(nameof(FavoriteComponentsText));
        OnPropertyChanged(nameof(FabricFavoriteGlyph));
        OnPropertyChanged(nameof(ForgeFavoriteGlyph));
        OnPropertyChanged(nameof(NeoForgeFavoriteGlyph));
        OnPropertyChanged(nameof(OptiFineFavoriteGlyph));
    }

    public string FabricSummary =>
        IsFabricBase
            ? $"{SelectedBaseLoader} {Mods.SelectedLoader ?? "未选择"}"
            : "";
    public string ForgeSummary =>
        SelectedBaseLoader == "Forge" ? Mods.SelectedForgelikeVersion ?? "未选择" : "";
    public string NeoForgeSummary =>
        SelectedBaseLoader == "NeoForge" ? Mods.SelectedForgelikeVersion ?? "未选择" : "";
    public string OptiFineSummary =>
        SelectedBaseLoader == "OptiFine"
            ? Mods.SelectedOptiFineVersion?.Patch ?? "未选择"
            : "";
    public bool IsFabricSelected => IsFabricFamily(SelectedBaseLoader);
    public bool IsPlainFabricBase => SelectedBaseLoader == "Fabric";
    public string ApiName => ApiComponentService.Name(SelectedBaseLoader);
    public string FabricApiLabel => "同时安装 " + ApiName;
    public string ApiSummary => InstallFabricApi
        ? ApiSelection.SelectedVersion?.VersionNumber ?? (ApiSelection.IsBusy ? "获取中…" : "未选择")
        : "可以添加";
    public bool IsForgeSelected => SelectedBaseLoader == "Forge";
    public bool IsNeoForgeSelected => SelectedBaseLoader == "NeoForge";
    public bool IsOptiFineSelected => IsOptiFineBase || InstallOptiFineWithBase;

    private void NotifyComponentSummaries()
    {
        RefreshComponentCards();
        UpdateInstanceName();
        OnPropertyChanged(nameof(FabricSummary));
        OnPropertyChanged(nameof(ForgeSummary));
        OnPropertyChanged(nameof(NeoForgeSummary));
        OnPropertyChanged(nameof(OptiFineSummary));
    }

    private async Task PrepareSelectedVersionAsync(bool initializeInstance = false)
    {
        if (_suspendPreparation || Download.SelectedVersion is not { } version || (!_hasChosenVersion && !version.IsInstalled)) return;
        if (initializeInstance)
        {
            await PrepareInstanceCatalogsAsync(version.Id);
            return;
        }
        PrepareComponentCards();
        var api = IsFabricBase ? ApiSelection.PrepareAsync(version.Id, SelectedBaseLoader) : Task.CompletedTask;
        if (!IsFabricBase) ApiSelection.Clear();
        await Task.WhenAll(Mods.PrepareGameVersionAsync(version.Id), OptiFabricSelection.PrepareAsync(version.Id), api);
    }

    public DownloadViewModel Download { get; }
    public VersionCatalogViewModel VersionCatalog { get; }
    public ModsViewModel Mods { get; }
    public OptiFabricSelectionViewModel OptiFabricSelection { get; }
    public ApiSelectionViewModel ApiSelection { get; }

    public bool ShowOptiFabricOptions => IsPlainFabricBase && InstallOptiFineWithBase;
    public bool HasInstallHint => InstallHint.Length > 0;
    public string InstallHint
    {
        get
        {
            var hints = new List<string>();
            if (IsFabricBase && !InstallFabricApi)
                hints.Add($"未选择 {FabricApiLabel.Replace("同时安装 ", "")}，部分 Mod 可能无法运行。");
            if (IsFabricBase && InstallFabricApi && !ApiSelection.IsBusy && ApiSelection.SelectedVersion is null)
                hints.Add($"尚未选到可用的 {ApiName}，请选择版本或重试，也可清除该组件。");
            if (ShowOptiFabricOptions)
            {
                if (OptiFabricService.IsOriginsVersion(Download.SelectedVersion?.Id ?? ""))
                    hints.Add("Minecraft 1.14 / 1.15 需手动安装 OptiFabric Origins，否则 OptiFine 无法工作。");
                else if (!InstallOptiFabric)
                    hints.Add("Fabric 与 OptiFine 同时安装时需要 OptiFabric，否则 OptiFine 无法工作。");
                else if (!OptiFabricSelection.IsBusy && OptiFabricSelection.SelectedVersion is null)
                    hints.Add("尚未选到可用的 OptiFabric，加载完成后选择版本或重试。");
            }
            if (Download.SelectedVersion is { } game)
            {
                if (InstallOptiFineWithBase && CanAddOptiFine && !LoaderCompatibility.IsAtMost(game.Id, "1.15.2"))
                    hints.Add("OptiFine 与一部分 Mod 的兼容性不佳，请谨慎安装。");
                var families = new List<LoaderFamily>();
                if (SelectedBaseLoader != "无") families.Add(Enum.Parse<LoaderFamily>(SelectedBaseLoader));
                if (InstallOptiFineWithBase) families.Add(LoaderFamily.OptiFine);
                if (SelectedLiteLoader is not null) families.Add(LoaderFamily.LiteLoader);
                var error = LoaderCompatibility.Validate(families, game.Id,
                    Mods.SelectedOptiFineVersion?.ForgeVersion, Mods.SelectedForgelikeVersion);
                if (error is not null) hints.Add(error);
            }
            return string.Join(Environment.NewLine, hints);
        }
    }

    private void NotifyInstallHints()
    {
        NotifyInstallAction();
        OnPropertyChanged(nameof(ShowOptiFabricOptions));
        OnPropertyChanged(nameof(InstallHint));
        OnPropertyChanged(nameof(HasInstallHint));
    }

    [ObservableProperty]
    private bool _isChoosingVersion = true;

    private bool _hasChosenVersion;

    public void ShowSelectedVersion()
    {
        if (Download.SelectedVersion is null) return;
        UpdateInstanceName();
        _hasChosenVersion = true;
        IsChoosingVersion = false;
        _ = PrepareSelectedVersionAsync();
    }

    [RelayCommand]
    private async Task OpenVersionCatalogAsync()
    {
        if (!CanEditInstall) return;
        CapturePendingComponents();
        IsChoosingVersion = true;
        VersionCatalog.Open(Download.Versions, _hasChosenVersion ? Download.SelectedVersion : null);
        if (Download.Versions.Count == 0) await RefreshVersionCatalogAsync();
    }

    [RelayCommand]
    private async Task RefreshVersionCatalogAsync()
    {
        VersionCatalog.IsLoading = true;
        VersionCatalog.ErrorMessage = "";
        try
        {
            await Download.LoadAsync();
            if (!IsChoosingVersion) return;
            VersionCatalog.Open(Download.Versions, _hasChosenVersion ? Download.SelectedVersion : null);
            VersionCatalog.ErrorMessage = Download.LoadError;
        }
        finally { VersionCatalog.IsLoading = false; }
    }

    public event EventHandler<InstallCompletedEventArgs>? InstallCompleted;
    public event EventHandler? ExistingInstallSubmitted;
    public event EventHandler<InstanceChangeEventArgs>? InstancesChanged;
    public event EventHandler<TaskEntry>? TaskFinished;

    public ObservableCollection<InstallStage> Stages { get; } = [];

    public ObservableCollection<string> LoaderKinds { get; } =
    [
        "无", "Fabric", "Forge", "NeoForge", "OptiFine",
        "Fabric + OptiFine", "Forge + OptiFine"
    ];

    public ObservableCollection<string> BaseLoaderKinds { get; } =
        ["无", "Fabric", "Quilt", "LegacyFabric", "Forge", "NeoForge", "OptiFine"];

    [ObservableProperty]
    private string _selectedLoaderKind = "无";

    [ObservableProperty]
    private string _selectedBaseLoader = "无";

    [ObservableProperty]
    private bool _installOptiFineWithBase;

    [ObservableProperty]
    private bool _installFabricApi = true;

    [RelayCommand]
    private void ClearApi()
    {
        InstallFabricApi = false;
        ApiCard.IsExpanded = false;
    }

    [ObservableProperty]
    private bool _installOptiFabric = true;

    public bool CanAddOptiFine =>
        SelectedBaseLoader is "Fabric" or "Forge";

    internal static bool IsFabricFamily(string kind) =>
        kind is "Fabric" or "Quilt" or "LegacyFabric";

    public bool IsFabricBase => IsFabricFamily(SelectedBaseLoader);
    public bool IsForgelikeBase => SelectedBaseLoader is "Forge" or "NeoForge";
    public bool IsOptiFineBase => SelectedBaseLoader == "OptiFine";

    public void SelectComponent(string kind)
    {
        if (kind == "Fabric" && IsFabricFamily(Mods.SelectedFabricKind))
            kind = Mods.SelectedFabricKind;
        if (SelectedBaseLoader == kind)
        {
            CollapseComponents();
            return;
        }

        SelectedBaseLoader = kind;
        var card = kind switch
        {
            "Forge" => ForgeCard,
            "NeoForge" => NeoForgeCard,
            "OptiFine" => OptiFineCard,
            _ => FabricCard
        };
        card.IsExpanded = card.CanExpand;
    }

    public void UseVanilla()
    {
        SetLiteLoaderSelection(null);
        SelectedBaseLoader = "无";
        CollapseComponents();
    }

    private void CollapseComponents()
    {
        foreach (var card in ComponentCards) card.IsExpanded = false;
    }

    partial void OnSelectedBaseLoaderChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddOptiFine));
        OnPropertyChanged(nameof(IsFabricBase));
        OnPropertyChanged(nameof(IsForgelikeBase));
        OnPropertyChanged(nameof(IsOptiFineBase));
        OnPropertyChanged(nameof(IsFabricSelected));
        OnPropertyChanged(nameof(IsPlainFabricBase));
        OnPropertyChanged(nameof(FabricApiLabel));
        OnPropertyChanged(nameof(ApiName));
        OnPropertyChanged(nameof(IsForgeSelected));
        OnPropertyChanged(nameof(IsNeoForgeSelected));
        OnPropertyChanged(nameof(IsOptiFineSelected));
        NotifyComponentSummaries();
        if (!CanAddOptiFine) InstallOptiFineWithBase = false;
        if (IsForgelikeBase)
            Mods.SelectedForgelikeKind = value;
        if (IsFabricFamily(value) &&
            !string.Equals(Mods.SelectedFabricKind, value, StringComparison.Ordinal))
            Mods.SelectedFabricKind = value;
        _selectionLoad = PrepareSelectedVersionAsync();
        SyncLoaderKind();
        NotifyInstallHints();
    }

    partial void OnInstallOptiFineWithBaseChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOptiFineSelected));
        NotifyComponentSummaries();
        UpdateInstanceName();
        if (value && OptiFabricService.IsOriginsVersion(Download.SelectedVersion?.Id ?? ""))
            InstallOptiFabric = false;
        SyncLoaderKind();
        NotifyInstallHints();
    }

    partial void OnInstallFabricApiChanged(bool value)
    {
        RefreshAdditionalCards();
        OnPropertyChanged(nameof(ApiSummary));
        NotifyInstallHints();
    }
    partial void OnInstallOptiFabricChanged(bool value)
    {
        RefreshAdditionalCards();
        NotifyInstallHints();
    }

    private void SyncLoaderKind()
    {
        SelectedLoaderKind = CanAddOptiFine && InstallOptiFineWithBase
            ? $"{SelectedBaseLoader} + OptiFine"
            : SelectedBaseLoader;
    }

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private bool _isCombinedBusy;

    [ObservableProperty]
    private double _combinedProgress;

    [ObservableProperty]
    private string _combinedProgressText = "";

    [ObservableProperty]
    private string _combinedStatus = "选择 Minecraft 版本与加载器后一次安装";

    private CancellationTokenSource? _combinedCts;

    [RelayCommand]
    private void CancelCombined() => _combinedCts?.Cancel();

    [RelayCommand]
    private async Task InstallCombinedAsync()
    {
        if (!CanApplyInstall) return;
        var target = Download.SelectedVersion;
        if (target is null)
        {
            CombinedStatus = "请先选择 Minecraft 版本";
            return;
        }

        Stages.Clear();
        CombinedProgress = 0;
        CombinedProgressText = "";
        var taskTitle = "安装 " + InstanceName.Trim();
        var existingInstance = IsExistingInstance;
        var recordTask = true;
        var outcome = "失败";
        try
        {
            var instanceTarget = _instanceTarget;
            var gameRoot = instanceTarget?.Root ?? _services.Settings.GameDirectory;
            if (instanceTarget is not null && gameRoot != _services.Settings.GameDirectory)
                throw new InvalidOperationException("游戏目录已切换，请重新打开目标实例");
            using var operation = _services.GameOperations.Acquire(gameRoot, "安装组件");
            var renameOnly = instanceTarget is not null && IsOriginalMinecraftReliable && IsOriginalSelection && HasInstanceNameChange;
            var baseLoader = SelectedBaseLoader;
            if (baseLoader != "无" && Mods.SelectedGameVersion != target.Id)
                throw new InvalidOperationException("请等待所选 Minecraft 的组件版本列表加载完成");
            var optifine = baseLoader == "OptiFine" || InstallOptiFineWithBase
                ? Mods.SelectedOptiFineVersion : null;
            if ((baseLoader == "OptiFine" || InstallOptiFineWithBase) && optifine is null)
                throw new InvalidOperationException("请先选择 OptiFine 版本");
            var plan = CombinedInstallPlan.Create(gameRoot, target.Id, target.IsInstalled,
                baseLoader, IsFabricFamily(baseLoader) ? Mods.SelectedLoader : Mods.SelectedForgelikeVersion,
                optifine, InstallFabricApi, InstallOptiFabric, InstanceName, OptiFabricSelection.SelectedVersion,
                ApiSelection.SelectedVersion, instanceTarget?.Id, SelectedLiteLoader);
            if (instanceTarget is not null)
            {
                InstallManifestStore.ValidateSchemaVersion(InstallManifestStore.Load(gameRoot, instanceTarget.Id)?.SchemaVersion ?? InstallManifestStore.SupportedSchemaVersion);
                var originalTarget = CaptureModificationTarget(gameRoot, instanceTarget.Id);
                var warnings = ModificationWarnings(plan);
                if (warnings.Count > 0)
                {
                    IsCombinedBusy = true;
                    if (!await _confirmModification("修改确认", "请注意：\n• " + string.Join("\n• ", warnings)))
                    {
                        recordTask = false;
                        return;
                    }
                    if (_rootChangedDuringInstall || _services.Settings.GameDirectory != gameRoot || _instanceTarget != instanceTarget ||
                        _pendingInstanceChanges.Any(change => change.Remap(instanceTarget.Id) != instanceTarget.Id) ||
                        !ModificationTargetMatches(originalTarget))
                        throw new InvalidOperationException("确认期间目标实例已变化，请重新打开修改页");
                }
                if (!_services.Settings.HintModifyInstanceWarningAcknowledged)
                {
                    _services.Settings.HintModifyInstanceWarningAcknowledged = true;
                    _services.SaveSettings();
                }
            }
            var mirror = _services.Mirror;
            var concurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64);
            var isolation = _services.Settings.IsolationMode;
            ICombinedInstallBackend Backend(string root) => _installBackendFactory?.Invoke(root) ?? new CombinedInstallBackend(
                root, mirror, concurrency, _services.JavaProvisioner, _services.Optifine, _services.Modrinth, _services.Optifabric);
            var steps = renameOnly ? [CombinedInstallStep.Apply] : plan.Steps.Append(CombinedInstallStep.Apply).ToArray();
            var stages = steps.ToDictionary(step => step, step => new InstallStage(plan.Title(step)));
            foreach (var step in steps) Stages.Add(stages[step]);
            _combinedCts = new CancellationTokenSource();
            IsCombinedBusy = true;
            CombinedStatus = "正在安装 " + plan.InstanceId + "……";
            if (instanceTarget is null)
            {
                _hasChosenVersion = false;
                VersionCatalog.Open(Download.Versions, null);
                IsChoosingVersion = true;
            }
            else ExistingInstallSubmitted?.Invoke(this, EventArgs.Empty);
            void Report(CombinedInstallStep step, CombinedInstallStepState state, JobProgress update)
            {
                var stage = stages[step];
                stage.IsActive = state == CombinedInstallStepState.Running;
                stage.IsDone = state == CombinedInstallStepState.Completed;
                stage.IsFailed = state == CombinedInstallStepState.Failed;
                stage.IsCancelled = state == CombinedInstallStepState.Cancelled;
                stage.Progress = state == CombinedInstallStepState.Completed ? 100
                    : Math.Clamp((update.Percent ?? 0) * 100, 0, 100);
                stage.Detail = state == CombinedInstallStepState.Cancelled ? "安装已取消"
                    : string.IsNullOrWhiteSpace(update.Detail) ? update.Stage : update.Detail;
                if (state == CombinedInstallStepState.Running)
                {
                    CombinedStatus = $"正在安装 {stage.Title}…";
                    CombinedProgressText = FormatProgress(update);
                }
                UpdateCombinedProgress();
            }
            CombinedInstallResult result;
            if (renameOnly)
            {
                Report(CombinedInstallStep.Apply, CombinedInstallStepState.Running, new JobProgress("重命名实例", 0));
                var cancellation = _combinedCts.Token;
                await Task.Run(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    WMCL.Minecraft.InstanceManager.Rename(gameRoot, instanceTarget!.Id, plan.RenameTo!);
                }, cancellation);
                result = new(plan.RenameTo!, []);
                Report(CombinedInstallStep.Apply, CombinedInstallStepState.Completed, new JobProgress("重命名实例", 1));
            }
            else result = instanceTarget is null
                ? await new NewInstanceInstallService(gameRoot, isolation, Backend).InstallAsync(plan, Report, _combinedCts.Token)
                : await new ExistingInstanceInstallService(gameRoot, isolation, Backend).InstallAsync(plan, Report, _combinedCts.Token);
            if (instanceTarget is null && plan.InstanceId == target.Id && gameRoot == _services.Settings.GameDirectory)
            {
                target.IsInstalled = true;
                Download.RefreshSelectedVersionInfo();
            }
            CombinedStatus = instanceTarget is null ? result.Status : "已应用安装更改：" + result.InstanceId + "（存档与配置保留）";
            if (instanceTarget is not null && result.InstanceId != instanceTarget.Id)
            {
                if (_instanceTarget == instanceTarget)
                {
                    _instanceTarget = instanceTarget with { Id = result.InstanceId };
                    _isInstanceNameEdited = false;
                    UpdateInstanceName();
                }
                InstancesChanged?.Invoke(this, new(gameRoot, instanceTarget.Id, result.InstanceId, InstanceChangeKind.Renamed));
            }
            if (instanceTarget is not null && result.Errors.Count == 0)
            {
                if (result.InstalledOptiFabric is { } optifabric) OptiFabricSelection.SelectedVersion = optifabric;
                CaptureComponentBaseline(plan with { OptiFabric = result.InstalledOptiFabric ?? plan.OptiFabric });
            }
            if (result.Errors.Count == 0) CombinedProgress = 100;
            outcome = result.Errors.Count == 0 ? "完成" : "失败";
            InstallCompleted?.Invoke(this, new(gameRoot, result.InstanceId, instanceTarget is not null && result.InstanceId != instanceTarget.Id));
        }
        catch (OperationCanceledException)
        {
            outcome = "已取消";
            CombinedStatus = existingInstance ? "已取消安装更改，原实例未更改" : "已取消组合安装，原有文件未更改";
        }
        catch (Exception exception)
        {
            CombinedStatus = "组合安装失败：" + exception.Message;
        }
        finally
        {
            var completedTask = new TaskEntry(taskTitle, CombinedStatus, CombinedProgress, outcome, null,
                Stages.Select(stage => new TaskEntry(stage.Title, stage.Detail, stage.Progress, stage.PercentText, null)).ToArray(), 3);
            IsCombinedBusy = false;
            CombinedProgressText = "";
            _combinedCts?.Dispose();
            _combinedCts = null;
            if (recordTask) TaskFinished?.Invoke(this, completedTask);
        }
    }

    private void UpdateCombinedProgress()
    {
        var totalWeight = Stages.Sum(stage => stage.Weight);
        CombinedProgress = totalWeight <= 0 ? 0
            : Stages.Sum(stage => Math.Clamp(stage.IsDone ? 100 : stage.Progress, 0, 100)
                * stage.Weight) / totalWeight;
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0}%";
        return $"{progress.Stage} {percent} {progress.Detail}".Trim();
    }
}
