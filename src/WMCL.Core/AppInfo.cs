namespace WMCL.Core;

using System.Reflection;

public static class AppInfo
{
    public const string Name = "Plain Craft Launcher (PCL) WMCL";
    public const string FullName = "Plain Craft Launcher (PCL) WMCL — wenxiangL Minecraft Launcher";

    /// <summary>
    /// 版本号取自程序集（csproj 的 Version），发版时只改 csproj，界面与更新检查自动跟随。
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
