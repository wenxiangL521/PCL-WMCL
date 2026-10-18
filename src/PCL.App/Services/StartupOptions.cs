using PCL.Core;

namespace PCL.App.Services;

/// <summary>开发/测试用启动参数：--game-dir &lt;路径&gt;、--auto-launch &lt;版本&gt;。</summary>
public static class StartupOptions
{
    public static string? GameDirectory { get; private set; }
    public static string? StartPage { get; private set; }
    public static string? ModrinthSearch { get; private set; }
    public static string? ModrinthInstall { get; private set; }
    public static bool MicrosoftLogin { get; private set; }
    public static string? AutoInstallVersion { get; private set; }
    public static string? AutoLaunchVersion { get; private set; }

    public static void Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-dir" when i + 1 < args.Length:
                    GameDirectory = PclPaths.Expand(args[++i]);
                    break;
                case "--page" when i + 1 < args.Length:
                    StartPage = args[++i];
                    break;
                case "--modrinth-search" when i + 1 < args.Length:
                    ModrinthSearch = args[++i];
                    break;
                case "--microsoft-login":
                    MicrosoftLogin = true;
                    break;
                case "--modrinth-install" when i + 1 < args.Length:
                    ModrinthInstall = args[++i];
                    break;
                case "--auto-install" when i + 1 < args.Length:
                    AutoInstallVersion = args[++i];
                    break;
                case "--auto-launch" when i + 1 < args.Length:
                    AutoLaunchVersion = args[++i];
                    break;
            }
        }
    }
}
