using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

/// <summary>
/// 版本目录与隔离判定。对照原版 PCL2 的 McInstance.PathIndie：
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
            mainClass.Contains("knot", StringComparison.OrdinalIgnoreCase) ||
            mainClass.Contains("launchwrapper", StringComparison.OrdinalIgnoreCase))
            return true;

        return version.Libraries.Any(library =>
            library.Name.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("optifine", StringComparison.OrdinalIgnoreCase) ||
            library.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase));
    }

    public static string GetLoader(VersionJson version)
    {
        var text = (version.MainClass ?? "") + " " +
                   string.Join(' ', version.Libraries.Select(l => l.Name));
        if (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return "neoforge";
        if (text.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return "quilt";
        if (text.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "fabric";
        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "forge";
        if (text.Contains("optifine", StringComparison.OrdinalIgnoreCase)) return "optifine";
        if (version.Libraries.Any(library => library.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase)))
            return "liteloader";
        return "vanilla";
    }

    public static string GetBaseGameVersion(string versionId, VersionJson version) =>
        !string.IsNullOrWhiteSpace(version.BaseGameVersion) ? version.BaseGameVersion :
        !string.IsNullOrWhiteSpace(version.InheritsFrom) ? version.InheritsFrom : versionId;

    public static string GetGameDirectory(
        string gameRoot,
        string versionId,
        VersionJson version,
        string? isolationMode = null) =>
        IsolationPolicy.Normalize(isolationMode) switch
        {
            IsolationPolicy.Isolated => Path.Combine(gameRoot, "versions", versionId),
            IsolationPolicy.Global => gameRoot,
            _ => IsModable(version)
                ? Path.Combine(gameRoot, "versions", versionId)
                : gameRoot
        };
}
