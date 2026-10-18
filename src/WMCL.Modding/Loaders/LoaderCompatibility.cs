using System.Globalization;

namespace WMCL.Modding.Loaders;

public enum LoaderFamily
{
    Fabric,
    Forge,
    NeoForge,
    OptiFine,
    LiteLoader
}

/// <summary>
/// PCL2 安装页的加载器组合规则。对照 PageDownloadInstall 的 Load*GetError：
/// Forge/NeoForge/Fabric 互斥；NeoForge/OptiFine 互斥；
/// Forge+OptiFine 需要精确 Forge 版本；Fabric+OptiFine 仅 1.20.4 及以下。
/// </summary>
public static class LoaderCompatibility
{
    public static string? Validate(
        IReadOnlyCollection<LoaderFamily> loaders,
        string minecraft,
        string? requiredForgeVersion = null,
        string? selectedForgeVersion = null)
    {
        var set = loaders.ToHashSet();
        if (set.Count == 0) return null;

        if (set.Contains(LoaderFamily.NeoForge) && set.Count > 1)
            return "NeoForge 不能与 Forge、Fabric 或 OptiFine 同时安装。";

        if (set.Contains(LoaderFamily.Forge) && set.Contains(LoaderFamily.Fabric))
            return "Forge 与 Fabric 不能同时安装。";

        if (set.Contains(LoaderFamily.Forge) && set.Contains(LoaderFamily.OptiFine))
        {
            if (string.IsNullOrWhiteSpace(requiredForgeVersion))
                return "该 OptiFine 版本没有声明兼容的 Forge 版本。";
            if (!string.IsNullOrWhiteSpace(selectedForgeVersion) &&
                !ForgeOptiFineMatch(requiredForgeVersion, selectedForgeVersion))
                return $"OptiFine 需要 Forge {requiredForgeVersion}，当前选择 {selectedForgeVersion}。";
        }

        if (set.Contains(LoaderFamily.Fabric) && set.Contains(LoaderFamily.OptiFine) &&
            !IsAtMost(minecraft, "1.20.4"))
            return "Fabric + OptiFine 仅在 Minecraft 1.20.4 及以下可用，1.20.5+ 没有 OptiFabric。";

        if (set.Contains(LoaderFamily.LiteLoader))
        {
            if (!IsAtMost(minecraft, "1.12.2"))
                return "LiteLoader 仅支持 Minecraft 1.12.2 及以下。";
            if (set.Contains(LoaderFamily.Fabric) || set.Contains(LoaderFamily.NeoForge) ||
                set.Contains(LoaderFamily.OptiFine))
                return "LiteLoader 只能与 Forge 组合。";
        }

        return null;
    }

    public static bool ForgeOptiFineMatch(string requiredForgeVersion, string selectedForgeVersion)
    {
        if (string.IsNullOrWhiteSpace(requiredForgeVersion)) return false;
        if (!string.IsNullOrWhiteSpace(selectedForgeVersion) &&
            string.Equals(requiredForgeVersion, selectedForgeVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        // PCL2 对 "47" 这类只写 revision 的版本比较最后一段；这里兼容完整版本号。
        var requiredRevision = requiredForgeVersion.Contains('.')
            ? requiredForgeVersion
            : requiredForgeVersion;
        return string.Equals(requiredRevision, selectedForgeVersion, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAtMost(string minecraft, string maximum)
    {
        var left = ParseMinecraft(minecraft);
        var right = ParseMinecraft(maximum);
        if (left is null || right is null) return false;
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;
            if (l != r) return l < r;
        }
        return true;
    }

    private static int[]? ParseMinecraft(string value)
    {
        var core = value.Split('-', '+')[0];
        var parts = core.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                return null;
        }
        return result;
    }
}
