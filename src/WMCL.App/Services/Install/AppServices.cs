using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modpack;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Modrinth;
using WMCL.Modding.Resources;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.Services;

/// <summary>应用级服务组合根（M2 先手工组装，后续可换 DI）。</summary>
public sealed partial class AppServices : IModpackServices
{
    private readonly Action<AppSettings> _saveSettings;
    private readonly HttpClient? _modpackUpdateHttp;
    private readonly Func<string, string, InstallOptions, IProgress<JobProgress>?, CancellationToken, Task> _ensureGameFiles;

    public AppServices(AppSettings settings, ModrinthClient? modrinth = null, CurseForgeClient? curseForge = null,
        Action<AppSettings>? saveSettings = null, HttpClient? resourceDownloadHttp = null,
        HttpClient? modpackUpdateHttp = null, Action<string>? recycleModBackup = null,
        GameOperationCoordinator? coordinator = null,
        GameLauncher? gameLauncher = null,
        TaskRunStore? taskRunStore = null,
        Func<string, string, InstallOptions, IProgress<JobProgress>?, CancellationToken, Task>? ensureGameFiles = null)
    {
        _saveSettings = saveSettings ?? SettingsService.Save;
        _modpackUpdateHttp = modpackUpdateHttp;
        _ensureGameFiles = ensureGameFiles ?? ((root, id, options, progress, ct) =>
            new GameFileVerificationService(new VersionRepository(root)).EnsureAsync(id, options, progress, ct));
        Settings = settings;
        GameOperations = coordinator ?? GameOperationCoordinator.Shared;
        GameLauncher = gameLauncher ?? new GameLauncher();
        TaskRunStore = taskRunStore ?? new TaskRunStore();
        SettingsService.NormalizeFolders(settings);
        ResourceFavorites = new ResourceFavoritesService(settings, SaveSettings);
        Features = new FeatureVisibility(settings, SaveSettings);
        Repository = new VersionRepository(settings.GameDirectory);
        Installer = new VersionInstaller(Repository);
        JavaLocator = new JavaLocator();
        JavaProvisioner = new JavaProvisioner(JavaLocator);
        LaunchPreparation = new LaunchPreparationService(JavaLocator, JavaProvisioner);
        Fabric = new FabricInstaller(Repository, Installer, FabricFamilyKind.Fabric);
        Quilt = new FabricInstaller(Repository, Installer, FabricFamilyKind.Quilt);
        LegacyFabric = new FabricInstaller(Repository, Installer, FabricFamilyKind.LegacyFabric);
        Forgelike = new ForgelikeInstaller(Repository, Installer);
        Modrinth = modrinth ?? new ModrinthClient();
        ModUpdates = new ModUpdateService(Modrinth, recycleModBackup);
        ResourceLookup = new ResourceLookupService(Modrinth);
        News = new MinecraftNewsService();
        Optifine = new OptiFineService(Repository, Installer);
        Optifabric = new OptiFabricService();
        Modpack = new ModpackInstaller(
            Repository, Installer, Fabric, Modrinth, Forgelike, null,
            quilt: Quilt, javaRequirementResolver: ResolveModpackJavaAsync);
        CurseForge = curseForge ?? new CurseForgeClient(LauncherSecrets.CurseForgeApiKey);
        ModCatalog = new ModCatalogService(Modrinth, CurseForge);
        ResourcePlanner = new ResourceInstallPlanner(Modrinth, CurseForge);
        ResourceBatch = new ResourceBatchInstallService(this, new ResourcePlanInstaller(new DownloadManager(resourceDownloadHttp)));
        McbbsUpdates = new McbbsModpackUpdater(Repository, CurseForge, http: modpackUpdateHttp);
        CurseForgeModpack = new CurseForgeModpackInstaller(
            Repository, Installer, Fabric, Forgelike, CurseForge, null,
            settings.IsolationMode, quilt: Quilt, javaRequirementResolver: ResolveModpackJavaAsync);
        LocalModpack = new LocalModpackInstaller(
            Repository, Installer, Fabric, Forgelike, null,
            settings.IsolationMode, Quilt, CurseForge,
            javaRequirementResolver: ResolveModpackJavaAsync);
    }

    private async Task<string> ResolveModpackJavaAsync(ModpackJavaRequest request, CancellationToken ct) =>
        (await JavaProvisioner.EnsureAsync(
            JavaRequirement.ForInstall(request.Minecraft, request.Loader, request.LoaderVersion),
            null, ct)).Path;

    public event EventHandler<string>? GameDirectoryChanged;

    public AppSettings Settings { get; }
    /// <summary>游戏操作协调器。默认进程内单例，测试/多实例可传入独立实例。</summary>
    public GameOperationCoordinator GameOperations { get; }
    public ResourceFavoritesService ResourceFavorites { get; }
    public VersionRepository Repository { get; }
    public VersionInstaller Installer { get; }
    public JavaLocator JavaLocator { get; }
    public JavaProvisioner JavaProvisioner { get; }
    public LaunchPreparationService LaunchPreparation { get; }
    public FabricInstaller Fabric { get; }
    public FabricInstaller Quilt { get; }
    public FabricInstaller LegacyFabric { get; }
    public ForgelikeInstaller Forgelike { get; }
    public ModrinthClient Modrinth { get; }
    public ModUpdateService ModUpdates { get; }
    internal ResourceLookupService ResourceLookup { get; }
    internal MinecraftNewsService News { get; }
    public ModpackInstaller Modpack { get; }
    public OptiFineService Optifine { get; }
    public OptiFabricService Optifabric { get; }
    public LiteLoaderService LiteLoader { get; } = new();
    public CurseForgeClient CurseForge { get; }
    public ModCatalogService ModCatalog { get; }
    public ResourceInstallPlanner ResourcePlanner { get; }
    public ResourceBatchInstallService ResourceBatch { get; }
    public CurseForgeModpackInstaller CurseForgeModpack { get; }
    internal LocalModpackInstaller LocalModpack { get; }
    internal McbbsModpackUpdater McbbsUpdates { get; }
    public GameLauncher GameLauncher { get; }
    internal TaskRunStore TaskRunStore { get; }

    public MirrorMode Mirror => Settings.Mirror.ToLowerInvariant() switch
    {
        "official" => MirrorMode.Official,
        "bmclapi" => MirrorMode.Bmclapi,
        _ => MirrorMode.Auto
    };

    public FeatureVisibility Features { get; }

    public void SaveSettings() => _saveSettings(Settings);

    public Task EnsureGameFilesAsync(string root, string versionId, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default) => _ensureGameFiles(root, versionId, new InstallOptions
        {
            Mirror = Mirror,
            MaxConcurrency = Math.Clamp(Settings.DownloadThreads, 1, 64),
            VerifyExisting = true
        }, progress, ct);

    public McbbsModpackUpdater CreateModpackUpdater(string root) =>
        new(new VersionRepository(root), CurseForge, http: _modpackUpdateHttp);

    public ModpackImportWorkflow CreateModpackImports(string root)
    {
        var repository = new VersionRepository(root);
        var installer = new VersionInstaller(repository);
        var fabric = new FabricInstaller(repository, installer);
        var quilt = new FabricInstaller(repository, installer, FabricFamilyKind.Quilt);
        var forgelike = new ForgelikeInstaller(repository, installer);
        async Task<string> Java(int major, CancellationToken ct) => (await JavaProvisioner.EnsureAsync(major, null, ct)).Path;
        return new ModpackImportWorkflow(
            new ModpackInstaller(repository, installer, fabric, Modrinth, forgelike, Java, quilt),
            new CurseForgeModpackInstaller(repository, installer, fabric, forgelike, CurseForge, Java,
                Settings.IsolationMode, quilt: quilt),
            new LocalModpackInstaller(repository, installer, fabric, forgelike, Java, Settings.IsolationMode, quilt, CurseForge),
            importBackup: async (path, name, progress, ct) =>
                (await InstanceImporter.ImportAsync(root, path, name, progress: progress, ct: ct)).Id);
    }

}
