using PCL.Minecraft.Models;

namespace PCL.Minecraft;

/// <summary>
/// 旧版本（1.6 及更早）资源索引需要把 assets/objects 映射到 virtual/&lt;index&gt;
/// 或游戏目录/resources。Minecraft 1.7+ 不再需要。
/// </summary>
public static class LegacyAssetExtractor
{
    public static int Extract(
        AssetIndex index,
        string assetsDirectory,
        string gameDirectory,
        string assetIndexId)
    {
        if (!index.Virtual && !index.MapToResources) return 0;
        var root = index.MapToResources
            ? Path.Combine(gameDirectory, "resources")
            : Path.Combine(assetsDirectory, "virtual", assetIndexId);
        var objects = Path.Combine(assetsDirectory, "objects");
        var copied = 0;
        foreach (var (assetPath, asset) in index.Objects)
        {
            if (string.IsNullOrWhiteSpace(asset.Hash) || asset.Hash.Length < 2) continue;
            var source = Path.Combine(objects, asset.Hash[..2], asset.Hash);
            if (!File.Exists(source)) continue;
            var relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                continue; // 防止索引中的路径穿越
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            copied++;
        }
        return copied;
    }
}
