using PCL.Core;
using PCL.Download;
using PCL.Launch;
using PCL.Launch.Java;
using PCL.Minecraft;

namespace PCL.App.Services;

/// <summary>应用级服务组合根（M2 先手工组装，后续可换 DI）。</summary>
public sealed class AppServices
{
    public AppServices(AppSettings settings)
    {
        Settings = settings;
        Repository = new VersionRepository(settings.GameDirectory);
        Installer = new VersionInstaller(Repository);
    }

    public AppSettings Settings { get; }
    public VersionRepository Repository { get; }
    public VersionInstaller Installer { get; }
    public JavaLocator JavaLocator { get; } = new();
    public GameLauncher GameLauncher { get; } = new();

    public MirrorMode Mirror => Settings.Mirror.ToLowerInvariant() switch
    {
        "official" => MirrorMode.Official,
        "bmclapi" => MirrorMode.Bmclapi,
        _ => MirrorMode.Auto
    };

    public void SaveSettings() => SettingsService.Save(Settings);
}
