namespace WMCL.Core;

/// <summary>
/// 跨平台目录约定。Linux 遵循 XDG，Windows/macOS 使用系统约定。
/// </summary>
public static class WmclPaths
{
    public static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigDirectory => Path.Combine(GetBase(
        "XDG_CONFIG_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(HomeDirectory, ".config")), "WMCL");

    public static string DataDirectory => Path.Combine(GetBase(
        "XDG_DATA_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(HomeDirectory, ".local", "share")), "WMCL");

    public static string CacheDirectory => Path.Combine(GetBase(
        "XDG_CACHE_HOME",
        OperatingSystem.IsWindows() ? Path.Combine(Path.GetTempPath(), "WMCL")
            : Path.Combine(HomeDirectory, ".cache")), "WMCL");

    public static string DefaultGameDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
        : Path.Combine(HomeDirectory, ".minecraft");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public static string JavaDirectory => Path.Combine(DataDirectory, "java");
    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static string LegacyConfigDirectory => Path.Combine(GetBase(
        "XDG_CONFIG_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(HomeDirectory, ".config")), "PCL");

    public static string LegacyDataDirectory => Path.Combine(GetBase(
        "XDG_DATA_HOME",
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(HomeDirectory, ".local", "share")), "PCL");

    public static string LegacyCacheDirectory => Path.Combine(GetBase(
        "XDG_CACHE_HOME",
        OperatingSystem.IsWindows() ? Path.Combine(Path.GetTempPath(), "PCL")
            : Path.Combine(HomeDirectory, ".cache")), "PCL");

    public static void EnsureCreated()
    {
        MigrateLegacy(LegacyConfigDirectory, ConfigDirectory);
        MigrateLegacy(LegacyDataDirectory, DataDirectory);
        MigrateLegacy(LegacyCacheDirectory, CacheDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(JavaDirectory);
    }

    /// <summary>首次升级到 WMCL 时，把旧 PCL 数据目录整体迁移过来；新目录已存在则不合并。</summary>
    private static void MigrateLegacy(string oldPath, string newPath)
    {
        if (!Directory.Exists(oldPath) || Directory.Exists(newPath)) return;
        try
        {
            var parent = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            Directory.Move(oldPath, newPath);
        }
        catch (IOException)
        {
            try { CopyDirectory(oldPath, newPath); } catch { /* 保留旧目录，下次重试 */ }
        }
        catch (UnauthorizedAccessException)
        {
            // 无权限迁移时继续使用新目录，旧数据保留
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
