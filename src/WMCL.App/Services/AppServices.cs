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

namespace WMCL.App.Services;

/// <summary>应用级服务组合根（M2 先手工组装，后续可换 DI）。</summary>
public sealed class AppServices
{
    public AppServices(AppSettings settings)
    {
        Settings = settings;
        SettingsService.NormalizeFolders(settings);
        Repository = new VersionRepository(settings.GameDirectory);
        Installer = new VersionInstaller(Repository);
        JavaLocator = new JavaLocator();
        JavaProvisioner = new JavaProvisioner(JavaLocator);
        Fabric = new FabricInstaller(Repository, Installer);
        Forgelike = new ForgelikeInstaller(Repository, Installer);
        Modrinth = new ModrinthClient();
        ModUpdates = new ModUpdateService(Modrinth);
        Optifine = new OptiFineService(Repository, Installer);
        Modpack = new ModpackInstaller(
            Repository, Installer, Fabric, Modrinth, Forgelike,
            async (major, token) =>
                (await JavaProvisioner.EnsureAsync(major, null, token)).Path);
        var curseForgeKey = string.IsNullOrWhiteSpace(settings.CurseForgeApiKey)
            ? WmclEnvironment.Get("CURSEFORGE_API_KEY") ?? ""
            : settings.CurseForgeApiKey;
        CurseForge = new CurseForgeClient(curseForgeKey);
        CurseForgeModpack = new CurseForgeModpackInstaller(
            Repository, Installer, Fabric, Forgelike, CurseForge,
            async (major, token) =>
                (await JavaProvisioner.EnsureAsync(major, null, token)).Path,
            settings.IsolationMode);
        LocalModpack = new LocalModpackInstaller(
            Repository, Installer, Fabric, Forgelike,
            async (major, token) =>
                (await JavaProvisioner.EnsureAsync(major, null, token)).Path,
            settings.IsolationMode);
    }

    public event EventHandler<string>? GameDirectoryChanged;

    public AppSettings Settings { get; }
    public VersionRepository Repository { get; }
    public VersionInstaller Installer { get; }
    public JavaLocator JavaLocator { get; }
    public JavaProvisioner JavaProvisioner { get; }
    public FabricInstaller Fabric { get; }
    public ForgelikeInstaller Forgelike { get; }
    public ModrinthClient Modrinth { get; }
    public ModUpdateService ModUpdates { get; }
    public ModpackInstaller Modpack { get; }
    public OptiFineService Optifine { get; }
    public CurseForgeClient CurseForge { get; }
    public CurseForgeModpackInstaller CurseForgeModpack { get; }
    public LocalModpackInstaller LocalModpack { get; }
    public GameLauncher GameLauncher { get; } = new();

    public MirrorMode Mirror => Settings.Mirror.ToLowerInvariant() switch
    {
        "official" => MirrorMode.Official,
        "bmclapi" => MirrorMode.Bmclapi,
        _ => MirrorMode.Auto
    };

    public void SaveSettings() => SettingsService.Save(Settings);

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
