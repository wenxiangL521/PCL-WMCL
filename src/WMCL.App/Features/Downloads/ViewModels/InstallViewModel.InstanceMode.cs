using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Minecraft;

namespace WMCL.App.Features.Downloads;

public partial class InstallViewModel
{
    private sealed record InstanceTarget(string Root, string Id, string Minecraft);
    private InstanceTarget? _instanceTarget;
    private (string Root, string Id)? _initializationTarget;
    private int _instanceRequest;
    private bool _suspendPreparation;
    private bool _rootChangedDuringInstall;
    private bool _instanceUsesGlobalDirectory;
    private readonly List<InstanceChangeEventArgs> _pendingInstanceChanges = [];
    private Task _selectionLoad = Task.CompletedTask;
    private Func<string, string, Task<bool>> _confirmModification = (title, message) => DialogService.ConfirmAsync(title, message, confirmText: "开始修改");
    private Func<string, string, Task> _showInstanceError =
        (title, message) => DialogService.ShowMessageAsync(title, message, isWarning: true);

    [ObservableProperty]
    private bool _isPreparingInstance;

    [ObservableProperty]
    private string _instanceInitializationError = "";

    [ObservableProperty]
    private double _instanceInitializationProgress;

    public bool HasInstanceInitializationError => !string.IsNullOrEmpty(InstanceInitializationError);
    public bool IsInstanceInitializationVisible => IsPreparingInstance || HasInstanceInitializationError;
    public bool ShowInstallAction => !IsChoosingVersion && !IsInstanceInitializationVisible;

    partial void OnInstanceInitializationErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstanceInitializationError));
        NotifyInstanceInitialization();
    }

    private void NotifyInstanceInitialization()
    {
        OnPropertyChanged(nameof(IsInstanceInitializationVisible));
        OnPropertyChanged(nameof(ShowInstallAction));
        NotifyInstallAction();
        RetryInstanceInitializationCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetryInstanceInitialization() => HasInstanceInitializationError && !IsPreparingInstance &&
        _initializationTarget is { } target && target.Root == _services.Settings.GameDirectory;

    [RelayCommand(CanExecute = nameof(CanRetryInstanceInitialization))]
    private async Task RetryInstanceInitializationAsync()
    {
        if (CanRetryInstanceInitialization() && _initializationTarget is { } target)
            await OpenInstanceAsync(target.Root, target.Id);
    }

    internal void CancelInstanceInitialization()
    {
        if (IsPreparingInstance) ClearInstanceTarget("实例信息读取已取消");
    }

    public bool IsExistingInstance => _instanceTarget is not null;
    public string? TargetInstanceId => _instanceTarget?.Id;
    public string TargetInstanceInfo => _instanceTarget is { } target
        ? $"修改实例：{target.Id} · Minecraft {target.Minecraft}。保留原游戏目录、存档与配置；核心文件保存在实例的 WMCL/install-backups。" +
          (_instanceUsesGlobalDirectory ? "当前使用共享游戏目录，组件 Mod 的更改也会影响使用此目录的其他实例。" : "")
        : "";
    public string InstallActionText => IsExistingInstance ? "开始修改" : "开始下载";
    public bool CanApplyInstall => !IsCombinedBusy && !IsInstanceInitializationVisible && !IsRestoringMinecraft && !IsRechoosingComponents && !Download.IsBusy && Download.SelectedVersion is not null && !HasInstanceNameError &&
        (!IsExistingInstance || HasComponentBaseline && (_pendingMinecraft is null || _pendingComponents.Count == 0) && (!IsOriginalMinecraftReliable || !IsOriginalSelection || HasInstanceNameChange));
    public bool CanEditInstall => !IsCombinedBusy && !IsInstanceInitializationVisible && !IsRestoringMinecraft;

    private void NotifyInstallAction()
    {
        OnPropertyChanged(nameof(IsExistingInstance));
        OnPropertyChanged(nameof(TargetInstanceId));
        OnPropertyChanged(nameof(TargetInstanceInfo));
        OnPropertyChanged(nameof(InstallActionText));
        OnPropertyChanged(nameof(CanApplyInstall));
        OnPropertyChanged(nameof(CanEditInstall));
        OnPropertyChanged(nameof(InstanceNameError));
        OnPropertyChanged(nameof(HasInstanceNameError));
        OnPropertyChanged(nameof(HasInstanceNameChange));
        OnPropertyChanged(nameof(OriginalInstanceNameText));
        OnPropertyChanged(nameof(InstallLogoMargin));
        OnPropertyChanged(nameof(InstallNameMargin));
        NotifyMinecraftComparison();
        if (ForgeCard is not null)
            foreach (var card in AllComponentCards) card.NotifyComparisonState();
    }

    partial void OnIsPreparingInstanceChanged(bool value) => NotifyInstanceInitialization();

    partial void OnIsChoosingVersionChanged(bool value) => OnPropertyChanged(nameof(ShowInstallAction));

    partial void OnIsCombinedBusyChanged(bool value)
    {
        if (!value)
        {
            foreach (var change in _pendingInstanceChanges.ToArray()) ApplyInstanceChange(change);
            _pendingInstanceChanges.Clear();
            if (_rootChangedDuringInstall) ClearInstanceTarget("游戏目录已切换，请重新选择目标实例");
            _rootChangedDuringInstall = false;
        }
        NotifyInstallAction();
    }

    private void OnInstallGameDirectoryChanged()
    {
        foreach (var card in ComponentCards) card.Cancel();
        ClearStaleLiteLoaderSelection();
        if (IsCombinedBusy) _rootChangedDuringInstall = true;
        else if (IsExistingInstance || _initializationTarget is not null) ClearInstanceTarget("游戏目录已切换，请重新选择目标实例");
    }

    private void ClearInstanceTarget(string status)
    {
        ++_instanceRequest;
        foreach (var card in ComponentCards) card.Cancel();
        _initializationTarget = null;
        InstanceInitializationError = "";
        InstanceInitializationProgress = 0;
        _instanceTarget = null;
        ClearComponentBaseline();
        SetLiteLoaderSelection(null);
        Download.PreserveSelection = false;
        IsPreparingInstance = false;
        _hasChosenVersion = false;
        Download.SelectedVersion = null;
        SelectedBaseLoader = "无";
        InstallOptiFineWithBase = false;
        InstallFabricApi = false;
        InstallOptiFabric = false;
        ApiSelection.Clear();
        OptiFabricSelection.SelectedVersion = null;
        IsChoosingVersion = true;
        CombinedStatus = status;
        NotifyInstallAction();
    }

    internal void ApplyInstanceChange(InstanceChangeEventArgs change)
    {
        if (_instanceTarget is not { } target || target.Root != change.GameDirectory) return;
        if (IsCombinedBusy)
        {
            _pendingInstanceChanges.Add(change);
            return;
        }
        var id = change.Remap(target.Id);
        if (id == target.Id) return;
        if (id is null) ClearInstanceTarget("目标实例已删除，请重新选择版本或实例");
        else
        {
            var preserveEditedName = HasInstanceNameChange;
            _instanceTarget = target with { Id = id };
            if (!preserveEditedName) _isInstanceNameEdited = false;
            UpdateInstanceName();
            NotifyInstallAction();
        }
    }

    public void ShowDownloadCatalog()
    {
        if (IsCombinedBusy) return;
        SetLiteLoaderSelection(null);
        ++_instanceRequest;
        _initializationTarget = null;
        InstanceInitializationError = "";
        InstanceInitializationProgress = 0;
        IsPreparingInstance = false;
        _instanceTarget = null;
        ClearComponentBaseline();
        Download.PreserveSelection = false;
        _hasChosenVersion = false;
        _isInstanceNameEdited = false;
        UpdateInstanceName();
        NotifyInstallAction();
        OpenVersionCatalogCommand.Execute(null);
    }

    public async Task OpenInstanceAsync(string root, string id)
    {
        if (IsCombinedBusy || Download.IsBusy)
        {
            CombinedStatus = "请等待当前安装或校验结束";
            return;
        }
        var request = ++_instanceRequest;
        _initializationTarget = (root, id);
        InstanceInitializationError = "";
        InstanceInitializationProgress = 0;
        _isInstanceNameEdited = false;
        ClearComponentBaseline();
        SetLiteLoaderSelection(null);
        IsPreparingInstance = true;
        IsChoosingVersion = false;
        CombinedStatus = "正在读取实例安装信息…";
        try
        {
            InstanceManager.ValidateName(id);
            if (!File.Exists(Path.Combine(root, "versions", id, id + ".json"))) throw new FileNotFoundException("实例已不存在");
            var manifest = await Task.Run(() => InstallManifestStore.Load(root, id)).ConfigureAwait(true);
            InstallManifestStore.ValidateSchemaVersion(manifest?.SchemaVersion ?? InstallManifestStore.SupportedSchemaVersion);
            var version = await new VersionRepository(root).GetVersionAsync(id);
            if (request != _instanceRequest || _services.Settings.GameDirectory != root) return;
            if (!File.Exists(Path.Combine(root, "versions", id, id + ".json"))) throw new FileNotFoundException("实例身份已变化，请重新打开安装页");
            var minecraft = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            var identity = version.GameVersionIdentity ?? new(minecraft, false, MinecraftVersionIdentitySource.Unknown);
            if (minecraft == id && identity.Source is MinecraftVersionIdentitySource.Unknown or MinecraftVersionIdentitySource.ClientVersion &&
                Download.IsOfficialVersion(minecraft) == false)
                throw new InvalidDataException("无法识别此实例的 Minecraft 基座，请先修复实例版本信息");
            var components = manifest?.Components ?? [];
            var loader = BaseLoaderKinds.FirstOrDefault(kind => components.ContainsKey(kind.ToLowerInvariant())) ??
                (MinecraftVersionUtil.GetLoader(version) switch
                {
                    "fabric" => version.Libraries.Any(library => library.Name.StartsWith("net.legacyfabric:", StringComparison.Ordinal)) ? "LegacyFabric" : "Fabric",
                    "quilt" => "Quilt",
                    "forge" => "Forge",
                    "neoforge" => "NeoForge",
                    "optifine" => "OptiFine",
                    _ => "无"
                });
            var loaderVersion = InstalledComponentResolver.Loader(manifest, minecraft, loader, version);
            var optifineCoordinates = components.ContainsKey("optifine") ? [] : version.Libraries
                .Where(library => library.Name.StartsWith("optifine:OptiFine:", StringComparison.OrdinalIgnoreCase))
                .Select(library => library.Name.Split(':').ElementAtOrDefault(2)).Distinct(StringComparer.Ordinal).ToArray();
            if (optifineCoordinates.Length > 1 || optifineCoordinates.Any(string.IsNullOrWhiteSpace) ||
                loader == "OptiFine" && !components.ContainsKey("optifine") && optifineCoordinates.Length == 0)
                throw new InvalidDataException("无法确定 OptiFine 的版本，无法修改此实例，请先核对版本支持库。");
            _instanceTarget = new InstanceTarget(root, id, minecraft);
            UpdateInstanceName();
            _instanceUsesGlobalDirectory = IsolationPolicy.GetGameDirectory(root, id, version, _services.Settings.IsolationMode) == root;
            Download.PreserveSelection = true;
            _suspendPreparation = true;
            try
            {
                SelectedBaseLoader = loader;
                InstallOptiFineWithBase = loader is "Fabric" or "Forge" && (components.ContainsKey("optifine") || optifineCoordinates.Length > 0);
                InstallFabricApi = components.ContainsKey(ApiProjectFor(loader));
                InstallOptiFabric = components.ContainsKey("optifabric");
                Download.SelectedVersion = new VersionItem
                {
                    Id = minecraft,
                    Type = version.Type ?? "release",
                    ReleaseTime = version.ReleaseTime,
                    IsInstalled = File.Exists(Path.Combine(root, "versions", minecraft, minecraft + ".json")) &&
                                  File.Exists(Path.Combine(root, "versions", minecraft, minecraft + ".jar"))
                };
                _hasChosenVersion = true;
                IsChoosingVersion = false;
            }
            finally { _suspendPreparation = false; }
            await (_selectionLoad = PrepareSelectedVersionAsync(initializeInstance: true));
            if (request != _instanceRequest || _services.Settings.GameDirectory != root) return;
            if (identity.Source == MinecraftVersionIdentitySource.Unknown)
                identity = identity with { IsReliable = true, Source = MinecraftVersionIdentitySource.OfficialCatalog };
            if (loaderVersion is not null && IsFabricBase)
            {
                if (!Mods.Loaders.Contains(loaderVersion)) Mods.Loaders.Add(loaderVersion);
                Mods.SelectedLoader = loaderVersion;
            }
            else if (loaderVersion is not null && IsForgelikeBase)
            {
                if (!Mods.ForgelikeVersions.Contains(loaderVersion)) Mods.ForgelikeVersions.Add(loaderVersion);
                Mods.SelectedForgelikeVersion = loaderVersion;
            }
            if (components.ContainsKey("optifine"))
                Mods.SelectedOptiFineVersion = InstalledComponentResolver.OptiFine(manifest, minecraft, Mods.OptiFineVersions);
            else if (optifineCoordinates.Length == 1)
                Mods.SelectedOptiFineVersion = InstalledComponentResolver.OptiFineFromCoordinate(minecraft, optifineCoordinates[0]!, Mods.OptiFineVersions);
            ApiSelection.SelectedVersion = InstalledComponentResolver.Api(manifest, minecraft, loader, ApiSelection.Versions);
            OptiFabricSelection.SelectedVersion = InstalledComponentResolver.OptiFabric(manifest, minecraft, OptiFabricSelection.Versions);
            var liteLoader = await RestoreLiteLoaderAsync(root, minecraft, version, manifest);
            if (request != _instanceRequest || _services.Settings.GameDirectory != root) return;
            SetLiteLoaderSelection(liteLoader);
            var originalComponents = new Dictionary<string, string>(components);
            if (ApiSelection.SelectedVersion is { } originalApi)
                originalComponents[ApiProjectFor(loader)] = originalApi.VersionNumber;
            if (OptiFabricSelection.SelectedVersion is { } originalOptiFabric)
                originalComponents["optifabric"] = originalOptiFabric.Version;
            if (optifineCoordinates.Length == 1)
                originalComponents["optifine"] = Mods.SelectedOptiFineVersion!.Patch;
            CaptureComponentBaseline(minecraft, loader, loaderVersion, originalComponents, liteLoader, identity);
            InstanceInitializationProgress = 1;
            CombinedStatus = "修改实例名称或组件后开始修改；存档与配置保留";
        }
        catch (Exception exception)
        {
            if (request == _instanceRequest && _services.Settings.GameDirectory == root)
            {
                ClearInstanceTarget("读取实例失败：" + exception.Message);
                _initializationTarget = (root, id);
                InstanceInitializationError = exception.Message;
                IsChoosingVersion = false;
            }
        }
        finally
        {
            if (request == _instanceRequest) IsPreparingInstance = false;
            NotifyInstallAction();
        }
    }

    private static string ApiProjectFor(string loader) => loader switch
    {
        "Quilt" => "qsl",
        "LegacyFabric" => "legacy-fabric-api",
        _ => "fabric-api"
    };

    private readonly Dictionary<string, InstallComponentChoice> _pendingComponents = [];
    private (string Root, string Minecraft)? _pendingMinecraft;
    private int _pendingComponentRequest;
    private bool _applyingPendingChoice;

    [ObservableProperty]
    private bool _isRechoosingComponents;

    internal Task PendingComponentSelectionTask { get; private set; } = Task.CompletedTask;

    partial void OnIsRechoosingComponentsChanged(bool value) => NotifyInstallAction();

    private void CapturePendingComponents()
    {
        if (!IsExistingInstance) return;
        foreach (var card in AllComponentCards)
            if (CurrentComponentChoice(card.Kind) is { } choice) _pendingComponents[card.Kind] = choice;
    }

    private void ClearPendingComponents()
    {
        ++_pendingComponentRequest;
        _pendingMinecraft = null;
        _pendingComponents.Clear();
        IsRechoosingComponents = false;
    }

    private void CancelPendingComponent(string kind)
    {
        if (_applyingPendingChoice) return;
        _pendingComponents.Remove(kind);
        if (kind is "Forge" or "NeoForge" || IsFabricFamily(kind))
            foreach (var pending in _pendingComponents.Keys.Where(pending =>
                         pending is "Forge" or "NeoForge" || IsFabricFamily(pending)).ToArray())
                _pendingComponents.Remove(pending);
        OnPropertyChanged(nameof(CanApplyInstall));
    }

    private void ChangeExistingMinecraft(VersionItem version)
    {
        if (Download.SelectedVersion?.Id == version.Id) return;
        CapturePendingComponents();
        ++_pendingComponentRequest;
        _pendingMinecraft = null;
        IsRechoosingComponents = false;
        _suspendPreparation = true;
        try
        {
            SelectedBaseLoader = "无";
            InstallOptiFineWithBase = false;
            InstallFabricApi = false;
            InstallOptiFabric = false;
            SetLiteLoaderSelection(null);
            Mods.SelectedLoader = null;
            Mods.SelectedForgelikeVersion = null;
            Mods.SelectedOptiFineVersion = null;
            if (_instanceTarget is { } target) _instanceTarget = target with { Minecraft = version.Id };
            _hasChosenVersion = true;
            Download.SelectedVersion = version;
        }
        finally { _suspendPreparation = false; }
        _selectionLoad = PrepareSelectedVersionAsync();
        _pendingMinecraft = (_services.Settings.GameDirectory, version.Id);
        ResumePendingComponents();
    }

    internal void ResumePendingComponents()
    {
        if (IsRechoosingComponents || _suspendPreparation || IsRestoringMinecraft || !CanEditInstall ||
            _pendingMinecraft is not { } key || !IsCurrentComponentCatalog(key.Root, key.Minecraft) ||
            _pendingComponents.Count == 0) return;
        PendingComponentSelectionTask = RechoosePendingComponentsAsync(key, _pendingComponentRequest);
    }

    private async Task RechoosePendingComponentsAsync((string Root, string Minecraft) key, int request)
    {
        IsRechoosingComponents = true;
        try
        {
            foreach (var kind in new[] { "Fabric", "Quilt", "LegacyFabric", "Forge", "NeoForge", "LiteLoader", "OptiFine",
                         "Fabric API", "Quilted Fabric API / QSL", "Legacy Fabric API", "OptiFabric" })
            {
                if (!_pendingComponents.TryGetValue(kind, out var original)) continue;
                if (kind == "OptiFine" && _pendingComponents.Keys.Any(pending =>
                        pending is "Forge" or "NeoForge" or "LiteLoader" || IsFabricFamily(pending)) ||
                    IsApiKind(kind) && _pendingComponents.Keys.Any(IsFabricFamily) ||
                    kind == "OptiFabric" && _pendingComponents.Keys.Any(pending => IsFabricFamily(pending) || pending == "OptiFine")) continue;
                var card = AllComponentCards.FirstOrDefault(candidate => candidate.Kind == kind);
                if (card is null) continue;
                if (!IsComponentVisible(kind) || ComponentIncompatibility(kind) is not null)
                {
                    _pendingComponents.Remove(kind);
                    continue;
                }
                if (card.IsAdditional)
                    await (kind == "OptiFabric" ? OptiFabricSelection.PrepareAsync(key.Minecraft) : ApiSelection.PrepareAsync(key.Minecraft, SelectedBaseLoader));
                else
                    await Task.WhenAll(card.PrepareAsync(key.Root, key.Minecraft), Mods.PrepareInstallComponentAsync(kind));
                if (request != _pendingComponentRequest || !IsCurrentComponentCatalog(key.Root, key.Minecraft)) return;
                if (!_pendingComponents.TryGetValue(kind, out var pending) || !ReferenceEquals(pending, original)) continue;
                if (card.Error.Length > 0)
                {
                    if (kind == "Forge" && card.Error == "无") _pendingComponents.Remove(kind);
                    continue;
                }
                _pendingComponents.Remove(kind);
                if (ComponentIncompatibility(kind) is not null) continue;
                var choices = card.Entries.Select(entry => entry.Choice).ToArray();
                var exact = choices.FirstOrDefault(choice => PendingVersionMatches(original, choice));
                var fallback = IsApiKind(kind) ? choices.OrderByDescending(choice => choice.Api?.DatePublished).FirstOrDefault()
                    : kind == "OptiFabric" ? choices.OrderByDescending(choice => choice.OptiFabric?.Published).FirstOrDefault()
                    : choices.FirstOrDefault();
                if ((exact ?? fallback) is not { } selected) continue;
                _applyingPendingChoice = true;
                try
                {
                    await SelectComponentVersionAsync(kind, selected);
                    if (IsFabricFamily(kind))
                    {
                        InstallFabricApi = _pendingComponents.Keys.Any(IsApiKind);
                        InstallOptiFabric = _pendingComponents.ContainsKey("OptiFabric");
                    }
                }
                finally { _applyingPendingChoice = false; }
            }
        }
        finally
        {
            if (request == _pendingComponentRequest)
            {
                NotifyComponentSummaries();
                NotifyInstallHints();
                IsRechoosingComponents = false;
            }
        }
    }

    private static bool PendingVersionMatches(InstallComponentChoice original, InstallComponentChoice candidate) =>
        original.Api is { } api ? candidate.Api is { } otherApi &&
            (otherApi.VersionNumber == api.VersionNumber || otherApi.Id == api.Id)
        : original.OptiFabric is { } optifabric ? candidate.OptiFabric is { } otherOptifabric &&
            (otherOptifabric.Version == optifabric.Version || otherOptifabric.Id == optifabric.Id)
        : original.OptiFine is { } optifine ? candidate.OptiFine is { } otherOptifine &&
            otherOptifine.Type == optifine.Type && otherOptifine.Patch == optifine.Patch
        : original.Version == candidate.Version;
}
