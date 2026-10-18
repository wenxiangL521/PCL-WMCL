using PCL.Core;
using PCL.Download;
using PCL.Launch;
using PCL.Launch.Java;
using PCL.Minecraft;
using PCL.Modding.Fabric;
using PCL.Modding.CurseForge;
using PCL.Modding.Forgelike;
using PCL.Modding.Modpack;
using PCL.Modding.OptiFine;
using PCL.Modding.Modrinth;

namespace PCL.App.Services;

/// <summary>应用级服务组合根（M2 先手工组装，后续可换 DI）。</summary>
public sealed class AppServices
{
    public AppServices(AppSettings settings)
    {
        Settings = settings;
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
            ? Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY") ?? ""
            : settings.CurseForgeApiKey;
        CurseForge = new CurseForgeClient(curseForgeKey);
    }

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
    public GameLauncher GameLauncher { get; } = new();

    public MirrorMode Mirror => Settings.Mirror.ToLowerInvariant() switch
    {
        "official" => MirrorMode.Official,
        "bmclapi" => MirrorMode.Bmclapi,
        _ => MirrorMode.Auto
    };

    public void SaveSettings() => SettingsService.Save(Settings);
}
