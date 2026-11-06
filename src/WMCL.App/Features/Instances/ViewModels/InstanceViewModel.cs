using WMCL.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using Avalonia.Threading;

namespace WMCL.App.Features.Instances;

public partial class InstanceViewModel : ViewModelBase, IDisposable
{
    private readonly IInstallServices _services;
    private int _instanceLoadRequest;
    private int _serverLoadRequest;
    private readonly McbbsModpackUpdater? _modpackUpdater;
    private readonly Func<string, string, string, Task<string?>> _prompt;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly ServerLauncher _serverLauncher = new();
    private IDisposable? _serverOperation;
    private int _serverSession;
    private string _serverPropertiesRoot = "";
    private string _serverPropertiesInstanceId = "";

    public InstanceViewModel(IInstallServices services, McbbsModpackUpdater? modpackUpdater = null,
        Func<string, string, string, Task<string?>>? prompt = null,
        Func<string, string, Task<bool>>? confirm = null)
    {
        services.Features.Changed += (_, _) => RefreshSectionVisibility();
        _services = services;
        _services.GameDirectoryChanged += OnGameDirectoryChanged;
        _modpackUpdater = modpackUpdater;
        _prompt = prompt ?? DialogService.PromptAsync;
        _confirm = confirm ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        _serverLauncher.OutputReceived += line =>
            UiDispatcher.Post(() => AppendServerOutput(line));
        _serverLauncher.Exited += code =>
        {
            var session = _serverSession;
            Interlocked.Exchange(ref _serverOperation, null)?.Dispose();
            UiDispatcher.Post(() =>
            {
                if (session != _serverSession) return;
                IsServerRunning = false;
                AppendServerOutput($"[退出码 {code}]");
                ServerFilesStatus = $"服务端已退出（{code}）";
            });
        };
    }

    public ObservableCollection<VersionItem> Instances { get; } = [];

    [ObservableProperty]
    private int _selectedSectionIndex;

    [ObservableProperty]
    private ViewModelBase? _embeddedPage;

    [ObservableProperty]
    private bool _isSectionPreparing;

    [ObservableProperty]
    private string _selectedInstanceGameDirectory = "";

    internal Task InstanceTargetPreparationTask { get; private set; } = Task.CompletedTask;
    private int _targetDirectoryRequest;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        if (value == 9) InstanceTargetPreparationTask = PrepareTargetDirectoryAsync();
    }

    // 实例设置侧栏分区也参与“功能隐藏”；索引仍与 ShowSection 一一对应。
    public bool ShowOverviewSection => SectionVisible(FeatureCatalog.InstanceOverview);
    public bool ShowSettingsSection => SectionVisible(FeatureCatalog.InstanceSettings);
    public bool ShowModifySection => SectionVisible(FeatureCatalog.InstanceModify);
    public bool ShowModsSection => SectionVisible(FeatureCatalog.InstanceMods);
    public bool ShowExportSection => SectionVisible(FeatureCatalog.InstanceExport);
    public bool ShowServerSection => SectionVisible(FeatureCatalog.InstanceServer);
    public bool ShowResourcePackSection => SectionVisible(FeatureCatalog.InstanceResourcePacks);
    public bool ShowSaveSection => SectionVisible(FeatureCatalog.InstanceSaves);
    public bool ShowShaderSection => SectionVisible(FeatureCatalog.InstanceShaders);
    public bool ShowScreenshotSection => SectionVisible(FeatureCatalog.InstanceScreenshots);

    private static readonly string[] SectionKeys =
    [
        FeatureCatalog.InstanceOverview, FeatureCatalog.InstanceSettings, FeatureCatalog.InstanceModify,
        FeatureCatalog.InstanceMods, FeatureCatalog.InstanceExport, FeatureCatalog.InstanceServer,
        FeatureCatalog.InstanceResourcePacks, FeatureCatalog.InstanceSaves, FeatureCatalog.InstanceShaders,
        FeatureCatalog.InstanceScreenshots
    ];

    private bool SectionVisible(string key) => _services.Features.IsVisible(key);

    private void RefreshSectionVisibility()
    {
        OnPropertyChanged(nameof(ShowOverviewSection));
        OnPropertyChanged(nameof(ShowSettingsSection));
        OnPropertyChanged(nameof(ShowModifySection));
        OnPropertyChanged(nameof(ShowModsSection));
        OnPropertyChanged(nameof(ShowExportSection));
        OnPropertyChanged(nameof(ShowServerSection));
        OnPropertyChanged(nameof(ShowResourcePackSection));
        OnPropertyChanged(nameof(ShowSaveSection));
        OnPropertyChanged(nameof(ShowShaderSection));
        OnPropertyChanged(nameof(ShowScreenshotSection));
        var current = SectionKeys[Math.Clamp(SelectedSectionIndex, 0, SectionKeys.Length - 1)];
        if (SectionVisible(current)) return;
        var next = Array.FindIndex(SectionKeys, SectionVisible);
        if (next >= 0) SelectedSectionIndex = next;
    }

    private void OnGameDirectoryChanged(object? sender, string gameDirectory)
    {
        InstanceTargetPreparationTask = PrepareTargetDirectoryAsync();
        EmbeddedPage = null;
        _ = LoadServersAsync(SelectedInstance);
    }

    private async Task PrepareTargetDirectoryAsync()
    {
        var request = ++_targetDirectoryRequest;
        var root = GameDirectory;
        var id = SelectedInstance?.Id;
        SelectedInstanceGameDirectory = "";
        if (id is null) return;
        try
        {
            var version = await new VersionRepository(root).GetLocalVersionAsync(id);
            if (request != _targetDirectoryRequest || GameDirectory != root || SelectedInstance?.Id != id) return;
            SelectedInstanceGameDirectory = IsolationPolicy.GetGameDirectory(root, id, version, _services.Settings.IsolationMode);
        }
        catch
        {
            if (request == _targetDirectoryRequest && GameDirectory == root && SelectedInstance?.Id == id)
                Status = "无法读取所选实例的游戏目录";
        }
    }
    private readonly List<VersionItem> _allInstances = [];
    public ObservableCollection<string> CategoryOptions { get; } =
        ["全部", "未分类", "原版", "整合包", "生存", "创造", "测试"];
    public ObservableCollection<string> InstanceCategoryOptions { get; } =
        ["未分类", "原版", "整合包", "生存", "创造", "测试"];
    public ObservableCollection<string> VersionCardTypeOptions { get; } =
        new(WMCL.App.ViewModels.Instances.VersionCardType.Options);

    [ObservableProperty]
    private string _selectedCategoryFilter = "全部";
    public ObservableCollection<ServerEntry> Servers { get; } = [];
    public event EventHandler<InstanceChangeEventArgs>? InstancesChanged;
    public event EventHandler? InstanceSettingsSaved;
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasInstances => Instances.Count > 0;
    public string SelectedInstanceTitle => SelectedInstance?.Id ?? "未选择实例";
    public string SelectedInstanceInfo => SelectedInstance is null
        ? "从上方选择一个实例"
        : SelectedInstance.Subtitle;
    public string SelectedInstancePath => SelectedInstance is null
        ? GameDirectory
        : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneInstanceCommand), nameof(RenameInstanceCommand), nameof(DeleteInstanceCommand))]
    private VersionItem? _selectedInstance;

    [ObservableProperty]
    private ServerEntry? _selectedServer;

    [ObservableProperty]
    private string _newServerName = "";

    [ObservableProperty]
    private string _newServerAddress = "";

    [ObservableProperty]
    private string _serverStatus = "选择实例后管理服务器列表";

    [ObservableProperty]
    private string _serverFilesStatus = "";

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _serverConsole = "";

    [ObservableProperty]
    private string _serverCommand = "";

    [ObservableProperty]
    private string _serverPropertiesText = "";

    [ObservableProperty]
    private string _serverPropertiesPath = "";

    [ObservableProperty]
    private string _serverPingText = "";

    [ObservableProperty]
    private bool _isServerPinging;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _serverFavicon;

    [ObservableProperty]
    private IReadOnlyList<WMCL.Minecraft.MotdRun>? _serverMotdRuns;

    public bool HasServerMotdRuns => ServerMotdRuns is { Count: > 0 };

    partial void OnServerMotdRunsChanged(IReadOnlyList<WMCL.Minecraft.MotdRun>? value) =>
        OnPropertyChanged(nameof(HasServerMotdRuns));

    [ObservableProperty]
    private IReadOnlyList<WMCL.Minecraft.ServerModEntry>? _serverModList;

    public bool HasServerModList => ServerModList is { Count: > 0 };

    partial void OnServerModListChanged(IReadOnlyList<WMCL.Minecraft.ServerModEntry>? value) =>
        OnPropertyChanged(nameof(HasServerModList));

    [ObservableProperty]
    private string _serverModInfoType = "";

    public bool HasServerModInfo => !string.IsNullOrWhiteSpace(ServerModInfoType);

    partial void OnServerModInfoTypeChanged(string value) =>
        OnPropertyChanged(nameof(HasServerModInfo));

    public bool HasServerFavicon => ServerFavicon is not null;

    partial void OnServerFaviconChanged(Avalonia.Media.Imaging.Bitmap? value) =>
        OnPropertyChanged(nameof(HasServerFavicon));

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _useGlobalMemory = true;

    [ObservableProperty]
    private decimal _instanceMemoryMb = 4096;

    [ObservableProperty]
    private bool _useGlobalJava = true;

    [ObservableProperty]
    private string _instanceJavaPath = "";

    public ObservableCollection<JavaRuntime> InstanceJavaRuntimes { get; } = [];

    [ObservableProperty]
    private JavaRuntime? _selectedInstanceJavaRuntime;

    partial void OnSelectedInstanceJavaRuntimeChanged(JavaRuntime? value)
    {
        if (value is null || UseGlobalJava) return;
        InstanceJavaPath = value.Path;
    }

    [RelayCommand]
    private async Task RefreshInstanceJavaAsync()
    {
        var runtimes = await _services.JavaLocator.DiscoverAsync(forceRefresh: true);
        InstanceJavaRuntimes.Clear();
        foreach (var runtime in runtimes)
            InstanceJavaRuntimes.Add(runtime);
        SelectedInstanceJavaRuntime = InstanceJavaRuntimes.FirstOrDefault(runtime =>
            string.Equals(runtime.Path, InstanceJavaPath, StringComparison.OrdinalIgnoreCase));
        InstanceJavaStatus = InstanceJavaRuntimes.Count == 0
            ? "未发现 Java 运行时，可手动填写路径或在设置页安装。"
            : $"发现 {InstanceJavaRuntimes.Count} 个 Java 运行时。";
    }

    [ObservableProperty]
    private string _instanceJavaStatus = "";

    [ObservableProperty]
    private string _instanceJvmArgs = "";

    [ObservableProperty]
    private string _instanceGameArgs = "";

    [ObservableProperty]
    private string _instanceInitialHeap = "0";

    [ObservableProperty]
    private string _instanceGcStrategy = "";

    public List<string> GcStrategies { get; } =
        ["", "Serial", "G1", "ZGC", "Parallel", "Shenandoah"];

    [ObservableProperty]
    private bool _useGlobalWindow = true;

    [ObservableProperty]
    private decimal _instanceWidth = 854;

    [ObservableProperty]
    private decimal _instanceHeight = 480;

    [ObservableProperty]
    private string _instanceLogoPath = "";

    [ObservableProperty]
    private string _instanceDescription = "";

    [ObservableProperty]
    private string _instanceCategory = "未分类";

    [ObservableProperty]
    private string _instanceVersionCardType = "自动";

    [ObservableProperty]
    private bool _instanceFavorite;

    [ObservableProperty]
    private string _instanceIsolation = "跟随全局";

    public ObservableCollection<string> IsolationOptions { get; } = ["跟随全局", "隔离", "不隔离"];

    [ObservableProperty]
    private string _instanceServer = "";

    [ObservableProperty]
    private decimal _instanceServerPort = 25565;

    [ObservableProperty]
    private string _instanceAuthServer = "";

    [ObservableProperty]
    private string _instanceAuthServerName = "";

    [ObservableProperty]
    private string _instancePluginDirectory = "";

    [ObservableProperty]
    private string _instancePreLaunchCommand = "";

    [ObservableProperty]
    private bool _instancePreLaunchWait = true;

    public bool CanEditWindow => !UseGlobalWindow;

    partial void OnUseGlobalWindowChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditWindow));

    [ObservableProperty]
    private string _exportPath = "";

    [ObservableProperty]
    private string _exportFormat = "mrpack (Modrinth)";

    public List<string> ExportFormats { get; } = ["mrpack (Modrinth)", "mcbpack (Mcbbs)"];

    public List<string> ExportModes { get; } = ["实例备份 (.zip)", "整合包 (.mrpack)"];

    [ObservableProperty]
    private string _exportMode = "实例备份 (.zip)";

    public bool IsModpackExport => ExportMode?.StartsWith("整合包", StringComparison.Ordinal) == true;
    public string ExportButtonText => IsExporting ? "正在导出…" : IsModpackExport ? "导出整合包" : "导出实例备份";
    public string ExportExtension => !IsModpackExport ? ".zip" : ExportFormat == "mcbpack (Mcbbs)" ? ".mcbpack" : ".mrpack";
    public string ExportExplanation => IsModpackExport
        ? "用于分享和在其他启动器安装。可按类别或具体文件选择；整合包索引和加载器依赖始终保留。存档和 WMCL 配置默认不包含。"
        : "用于恢复当前实例，可选择包含 Mod、配置和游玩存档；通过“导入整合包”入口恢复。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportButtonText))]
    private bool _isExporting;

    partial void OnExportFormatChanged(string value) => OnPropertyChanged(nameof(ExportExtension));

    private bool _shareExportScopeInitialized;

    partial void OnExportModeChanged(string value)
    {
        if (IsModpackExport && !_shareExportScopeInitialized)
        {
            _shareExportScopeInitialized = true;
            ExportSaves = false;
            ExportWmcl = false;
        }
        OnPropertyChanged(nameof(IsModpackExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(ExportExtension));
        OnPropertyChanged(nameof(ExportExplanation));
        ExportScopeChanged();
    }

    /// <summary>One entry point so the export card only needs a format selector and a single button.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsExporting) return;
        if (SelectedInstance is null) { Status = "请先选择要导出的实例。"; return; }
        IsExporting = true;
        try
        {
            if (IsModpackExport) await ExportModpackAsync();
            else await ExportInstanceAsync();
        }
        finally { IsExporting = false; }
    }

    // ── 回收站 ──

    public bool IsTrashEnabled => _services.Settings.EnableTrash;

    [ObservableProperty]
    private bool _showTrash;

    [ObservableProperty]
    private string _trashStatus = "";

    private List<TrashEntry> _trashEntries = [];

    public List<TrashEntry> TrashEntries
    {
        get => _trashEntries;
        set
        {
            SetProperty(ref _trashEntries, value);
            OnPropertyChanged(nameof(HasTrashEntries));
        }
    }

    public bool HasTrashEntries => TrashEntries.Count > 0;

    [RelayCommand]
    private void ToggleTrash()
    {
        ShowTrash = !ShowTrash;
        if (ShowTrash) RefreshTrash();
    }

    [RelayCommand]
    private void RefreshTrash()
    {
        TrashEntries = TrashManager.ListEntries();
        TrashStatus = TrashEntries.Count > 0
            ? $"回收站中有 {TrashEntries.Count} 个实例"
            : "回收站为空";
    }

    [RelayCommand]
    private async Task RestoreFromTrash(TrashEntry entry)
    {
        try
        {
            TrashManager.Restore(entry.Path, GameDirectory);
            TrashStatus = $"已恢复实例：{entry.VersionId}";
            RefreshTrash();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            TrashStatus = "恢复失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void PermanentDeleteFromTrash(TrashEntry entry)
    {
        try
        {
            TrashManager.PermanentDelete(entry.Path);
            TrashStatus = $"已永久删除：{entry.VersionId}";
            RefreshTrash();
        }
        catch (Exception ex)
        {
            TrashStatus = "永久删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ClearTrash()
    {
        try
        {
            TrashManager.ClearAll();
            TrashStatus = "回收站已清空";
            RefreshTrash();
        }
        catch (Exception ex)
        {
            TrashStatus = "清空回收站失败：" + ex.Message;
        }
    }

    [ObservableProperty]
    private string _manifestStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRepairManifest))]
    [NotifyCanExecuteChangedFor(nameof(UpdateModpackContentCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneInstanceCommand), nameof(RenameInstanceCommand), nameof(DeleteInstanceCommand))]
    private bool _isRepairingManifest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRepairManifest))]
    [NotifyCanExecuteChangedFor(nameof(UpdateModpackContentCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneInstanceCommand), nameof(RenameInstanceCommand), nameof(DeleteInstanceCommand))]
    private bool _isUpdatingModpack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRepairManifest))]
    [NotifyCanExecuteChangedFor(nameof(UpdateModpackContentCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneInstanceCommand), nameof(RenameInstanceCommand), nameof(DeleteInstanceCommand))]
    private bool _isManagingInstance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateModpackContentCommand))]
    private bool _isMcbbsModpack;

    public bool CanRepairManifest => !IsRepairingManifest && !IsUpdatingModpack && !IsManagingInstance;
    private bool CanManageInstance() => SelectedInstance is not null && CanRepairManifest;
    private bool CanUpdateModpackContent() => SelectedInstance is not null && IsMcbbsModpack && CanRepairManifest;

    [ObservableProperty]
    private bool _exportMods = true;

    [ObservableProperty]
    private bool _exportConfigs = true;

    [ObservableProperty]
    private bool _exportResourcePacks = true;

    [ObservableProperty]
    private bool _exportShaderPacks = true;

    [ObservableProperty]
    private bool _exportSaves = true;

    [ObservableProperty]
    private bool _exportWmcl = true;

    [ObservableProperty]
    private bool _exportOptions = true;

    [ObservableProperty]
    private string _importPath = "";

    [ObservableProperty]
    private bool _isImporting;

    public bool CanEditMemory => !UseGlobalMemory;
    public bool CanEditJava => !UseGlobalJava;

    partial void OnUseGlobalMemoryChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditMemory));

    partial void OnUseGlobalJavaChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditJava));

    partial void OnSelectedInstanceChanged(VersionItem? value)
    {
        ResetExportFileSelection();
        InstanceTargetPreparationTask = PrepareTargetDirectoryAsync();
        OnPropertyChanged(nameof(SelectedInstanceTitle));
        OnPropertyChanged(nameof(SelectedInstanceInfo));
        OnPropertyChanged(nameof(SelectedInstancePath));
        try
        {
            var manifest = value is null ? null : InstallManifestStore.Load(GameDirectory, value.Id);
            IsMcbbsModpack = manifest?.ModpackKind == "MCBBS" ||
                            manifest?.Components.GetValueOrDefault("local-pack") == "Mcbbs";
        }
        catch { IsMcbbsModpack = false; }
        LoadInstanceSettings(value);
        // 切换实例时清空服务端设置，避免旧实例的 server.properties 被误保存
        _serverPropertiesRoot = "";
        _serverPropertiesInstanceId = "";
        ServerPropertiesPath = "";
        ServerPropertiesText = "";
        _ = LoadServersAsync(value);
    }

    public async Task LoadAsync()
    {
        var request = ++_instanceLoadRequest;
        var root = GameDirectory;
        var repository = new VersionRepository(root);
        Status = "正在扫描实例…";
        try
        {
            var installed = await Task.Run(repository.ListInstalled);
            if (request != _instanceLoadRequest || GameDirectory != root) return;
            _allInstances.Clear();
            foreach (var version in installed)
            {
                var item = new VersionItem
                {
                    Id = version.Id,
                    Type = version.Type ?? "release",
                    ReleaseTime = version.ReleaseTime,
                    IsInstalled = true
                };
                var settings = InstanceSettingsStore.Load(
                    Path.Combine(root, "versions", version.Id));
                item.IsFavorite = settings.IsFavorite;
                item.Description = settings.Description;
                if (!string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
                {
                    try { item.Logo = new Avalonia.Media.Imaging.Bitmap(settings.LogoPath); }
                    catch { item.Logo = null; }
                }
                item.Category = settings.Category;
                EnsureCategoryOptions(settings.Category);
                _allInstances.Add(item);
            }
            SortInstances();

            if (SelectedInstance is not null) LoadInstanceSettings(SelectedInstance);
            Status = Instances.Count == 0 ? "还没有安装任何版本" : $"共 {Instances.Count} 个实例";
        }
        catch (Exception ex)
        {
            if (request == _instanceLoadRequest && GameDirectory == root) Status = "扫描失败：" + ex.Message;
        }
        finally
        {
            if (request == _instanceLoadRequest && GameDirectory == root) OnPropertyChanged(nameof(HasInstances));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private void LoadInstanceSettings(VersionItem? instance)
    {
        if (instance is null) return;

        try
        {
            LoadInstanceSettingsCore(instance);
        }
        catch (Exception ex)
        {
            Status = "读取实例设置失败：" + ex.Message;
        }
    }

    private void LoadInstanceSettingsCore(VersionItem instance)
    {
        var settings = InstanceSettingsStore.Load(
            Path.Combine(GameDirectory, "versions", instance.Id));
        UseGlobalMemory = settings.MemoryMb is null;
        InstanceMemoryMb = settings.MemoryMb ?? _services.Settings.MemoryMb;
        UseGlobalJava = string.IsNullOrWhiteSpace(settings.JavaPath);
        InstanceJavaPath = settings.JavaPath ?? "";
        _ = RefreshInstanceJavaAsync();
        InstanceJvmArgs = settings.ExtraJvmArgs;
        InstanceGameArgs = settings.ExtraGameArgs;
        InstanceInitialHeap = settings.InitialHeapMb > 0 ? settings.InitialHeapMb.ToString() : "0";
        InstanceGcStrategy = settings.GcStrategy;
        var window = WMCL.App.ViewModels.Instances.InstanceWindowState.From(
            settings.Width, settings.Height, _services.Settings.Width, _services.Settings.Height);
        UseGlobalWindow = window.UseGlobal;
        InstanceWidth = window.Width;
        InstanceHeight = window.Height;
        InstanceDescription = settings.Description;
        var identity = WMCL.App.ViewModels.Instances.InstanceIdentity.From(
            settings.Description, settings.Category, settings.IsFavorite, settings.LogoPath);
        InstanceCategory = identity.Category;
        InstanceVersionCardType = WMCL.App.ViewModels.Instances.VersionCardType.Label(settings.VersionCardType);
        InstanceFavorite = settings.IsFavorite;
        InstanceLogoPath = settings.LogoPath;
        InstanceIsolation = WMCL.App.ViewModels.Instances.IsolationLabel.ToLabel(settings.IsolationMode);
        var serverSettings = WMCL.App.ViewModels.Instances.InstanceServerSettings.From(
            settings.Server, settings.Port, settings.AuthServer, settings.AuthServerName,
            settings.PluginDirectory, settings.PreLaunchCommand, settings.PreLaunchWait);
        InstanceServer = serverSettings.Server;
        InstanceServerPort = serverSettings.Port;
        InstanceAuthServer = serverSettings.AuthServer;
        InstanceAuthServerName = settings.AuthServerName;
        InstancePluginDirectory = settings.PluginDirectory;
        InstancePreLaunchCommand = settings.PreLaunchCommand;
        InstancePreLaunchWait = settings.PreLaunchWait;
    }

    public void Dispose()
    {
        _services.GameDirectoryChanged -= OnGameDirectoryChanged;
        ++_targetDirectoryRequest;
        ++_serverLoadRequest;
        StartServerCancelCommand.Execute(null);
        _serverLauncher.Dispose();
        if (!StartServerCommand.IsRunning)
            Interlocked.Exchange(ref _serverOperation, null)?.Dispose();
    }

}
