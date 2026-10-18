namespace PCL.Core;

/// <summary>
/// 跨平台目录约定。Linux 遵循 XDG，Windows/macOS 使用系统约定。
/// </summary>
public static class PclPaths
{
    public static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigDirectory => Path.Combine(GetBase(
        "XDG_CONFIG_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(HomeDirectory, ".config")), "PCL");

    public static string DataDirectory => Path.Combine(GetBase(
        "XDG_DATA_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(HomeDirectory, ".local", "share")), "PCL");

    public static string CacheDirectory => Path.Combine(GetBase(
        "XDG_CACHE_HOME",
        OperatingSystem.IsWindows() ? Path.Combine(Path.GetTempPath(), "PCL")
            : Path.Combine(HomeDirectory, ".cache")), "PCL");

    public static string DefaultGameDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
        : Path.Combine(HomeDirectory, ".minecraft");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public static string JavaDirectory => Path.Combine(DataDirectory, "java");
    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(JavaDirectory);
    }

    private static string GetBase(string envName, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>把用户输入的路径展开为绝对路径（支持 ~）。</summary>
    public static string Expand(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return DefaultGameDirectory;
        if (path == "~") return HomeDirectory;
        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.GetFullPath(Path.Combine(HomeDirectory, path[2..]));
        return Path.GetFullPath(path);
    }
}
