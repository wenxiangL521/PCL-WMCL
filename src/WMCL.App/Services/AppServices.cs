using WMCL.Core;
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
public sealed class AppServices
{
    private readonly Action<AppSettings> _saveSettings;
    private readonly HttpClient? _modpackUpdateHttp;

    public AppServices(AppSettings settings, ModrinthClient? modrinth = null, CurseForgeClient? curseForge = null,
        Action<AppSettings>? saveSettings = null, HttpClient? resourceDownloadHttp = null,
        HttpClient? modpackUpdateHttp = null, Action<string>? recycleModBackup = null)
    {
        _saveSettings = saveSettings ?? SettingsService.Save;
        _modpackUpdateHttp = modpackUpdateHttp;
        Settings = settings;
        SettingsService.NormalizeFolders(settings);
        ResourceFavorites = new ResourceFavoritesService(settings, SaveSettings);
        Repository = new VersionRepository(settings.GameDirectory);
        Installer = new VersionInstaller(Repository);
        JavaLocator = new JavaLocator();
        JavaProvisioner = new JavaProvisioner(JavaLocator);
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
        var curseForgeKey = string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)
            ? WmclEnvironment.Get("CURSEFORGE_API_KEY") ?? ""
            : settings.CurseForgeApiKey;
        CurseForge = curseForge ?? new CurseForgeClient(curseForgeKey);
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
    public GameOperationCoordinator GameOperations => GameOperationCoordinator.Shared;
    public ResourceFavoritesService ResourceFavorites { get; }
    public VersionRepository Repository { get; }
    public VersionInstaller Installer { get; }
    public JavaLocator JavaLocator { get; }
    public JavaProvisioner JavaProvisioner { get; }
    public FabricInstaller Fabric { get; }
    public FabricInstaller Quilt { get; }
    public FabricInstaller LegacyFabric { get; }
    public ForgelikeInstaller Forgelike { get; }
    public ModrinthClient Modrinth { get; }
    public ModUpdateService ModUpdates { get; }
    public ResourceLookupService ResourceLookup { get; }
    public MinecraftNewsService News { get; }
    public ModpackInstaller Modpack { get; }
    public OptiFineService Optifine { get; }
    public OptiFabricService Optifabric { get; }
    public LiteLoaderService LiteLoader { get; } = new();
    public CurseForgeClient CurseForge { get; }
    public ResourceInstallPlanner ResourcePlanner { get; }
    public ResourceBatchInstallService ResourceBatch { get; }
    public CurseForgeModpackInstaller CurseForgeModpack { get; }
    public LocalModpackInstaller LocalModpack { get; }
    public McbbsModpackUpdater McbbsUpdates { get; }
    public GameLauncher GameLauncher { get; } = new();

    public MirrorMode Mirror => Settings.Mirror.ToLowerInvariant() switch
    {
        "official" => MirrorMode.Official,
        "bmclapi" => MirrorMode.Bmclapi,
        _ => MirrorMode.Auto
    };

    public void SaveSettings() => _saveSettings(Settings);

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
            new LocalModpackInstaller(repository, installer, fabric, forgelike, Java, Settings.IsolationMode, quilt, CurseForge));
    }

    public void ApplyInstanceChange(InstanceChangeEventArgs change)
    {
        if (change.Kind == InstanceChangeKind.Cloned) return;
        foreach (var list in new[] { GetFavoriteVersions(change.GameDirectory), GetHiddenVersions(change.GameDirectory) })
        {
            for (var index = list.Count - 1; index >= 0; index--)
            {
                var remapped = change.Remap(list[index]);
                if (remapped is null) list.RemoveAt(index);
                else list[index] = remapped;
            }
        }
        SaveSettings();
    }

    public void SetGameDirectory(string path)
    {
        path = WmclPaths.Expand(path);
        if (!Settings.GameFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            Settings.GameFolders.Add(path);
        var changed = !string.Equals(Settings.GameDirectory, path, StringComparison.OrdinalIgnoreCase);
        Settings.GameDirectory = path;
        Repository.SetGameDirectory(path);
        SaveSettings();
        if (changed) GameDirectoryChanged?.Invoke(this, path);
    }

    public List<string> GetFavoriteVersions(string folder)
    {
        folder = WmclPaths.Expand(folder);
        if (!Settings.FavoriteVersionsByFolder.TryGetValue(folder, out var list))
        {
            list = [];
            Settings.FavoriteVersionsByFolder[folder] = list;
        }
        return list;
    }

    public List<string> GetHiddenVersions(string folder)
    {
        folder = WmclPaths.Expand(folder);
        if (!Settings.HiddenVersionsByFolder.TryGetValue(folder, out var list))
        {
            list = [];
            Settings.HiddenVersionsByFolder[folder] = list;
        }
        return list;
    }

    public void RenameGameFolder(string path, string? name)
    {
        var full = WmclPaths.Expand(path);
        if (string.IsNullOrWhiteSpace(name))
            Settings.GameFolderNames.Remove(full);
        else
            Settings.GameFolderNames[full] = name.Trim();
        SaveSettings();
    }

    public void RemoveGameFolder(string path)
    {
        Settings.GameFolders.RemoveAll(folder =>
            string.Equals(folder, path, StringComparison.OrdinalIgnoreCase));
        if (Settings.GameFolders.Count == 0)
            Settings.GameFolders.Add(WmclPaths.DefaultGameDirectory);
        if (string.Equals(Settings.GameDirectory, path, StringComparison.OrdinalIgnoreCase))
        {
            SetGameDirectory(Settings.GameFolders[0]);
            return;
        }
        SaveSettings();
    }
}
