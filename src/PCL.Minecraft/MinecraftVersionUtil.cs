using PCL.Minecraft.Models;

namespace PCL.Minecraft;

/// <summary>
/// 版本目录与隔离判定。对照原版 PCL 的 McInstance.PathIndie：
/// 可装 Mod 的版本默认使用 versions/&lt;id&gt; 作为游戏目录，其余使用 .minecraft 根目录。
/// </summary>
public static class MinecraftVersionUtil
{
    public static bool IsModable(VersionJson version)
    {
        var mainClass = version.MainClass ?? "";
        if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
            mainClass.Contains("quilt", StringComparison.OrdinalIgnoreCase) ||
            mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
            mainClass.Contains("knot", StringComparison.OrdinalIgnoreCase))
            return true;

        return version.Libraries.Any(library =>
            library.Name.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("neoforge", StringComparison.OrdinalIgnoreCase));
    }

    public static string GetLoader(VersionJson version)
    {
        var text = (version.MainClass ?? "") + " " +
                   string.Join(' ', version.Libraries.Select(l => l.Name));
        if (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return "neoforge";
        if (text.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return "quilt";
        if (text.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "fabric";
        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "forge";
        return "vanilla";
    }

    public static string GetBaseGameVersion(string versionId, VersionJson version) =>
        string.IsNullOrWhiteSpace(version.InheritsFrom) ? versionId : version.InheritsFrom;

    public static string GetGameDirectory(string gameRoot, string versionId, VersionJson version) =>
        IsModable(version)
            ? Path.Combine(gameRoot, "versions", versionId)
            : gameRoot;
}
