using WMCL.Core.IO;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

/// <summary>
/// 旧版本（1.6 及更早）资源索引需要把 assets/objects 映射到 virtual/&lt;index&gt;
/// 或游戏目录/resources。Minecraft 1.7+ 不再需要。
/// </summary>
public static class LegacyAssetExtractor
{
    public static async Task<int> EnsureAsync(
        AssetIndex index,
        string assetsDirectory,
        string gameDirectory,
        string assetIndexId,
        CancellationToken ct = default)
    {
        if (!index.Virtual && !index.MapToResources) return 0;
        var root = index.MapToResources
            ? Path.Combine(gameDirectory, "resources")
            : FileUtil.SafeChildPath(Path.Combine(assetsDirectory, "virtual"), assetIndexId);
        var copied = 0;
        foreach (var (assetPath, asset) in index.Objects)
        {
            ct.ThrowIfCancellationRequested();
            var destination = FileUtil.SafeChildPath(root, assetPath);
            if (FileUtil.GetLength(destination) == asset.Size && string.Equals(
                    await FileUtil.Sha1Async(destination, ct).ConfigureAwait(false), asset.Hash,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            var source = FileUtil.SafeChildPath(Path.Combine(assetsDirectory, "objects"), asset.Hash[..2] + "/" + asset.Hash);
            GameFileVerificationService.EnsureRepairSafe(gameDirectory, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var part = destination + ".part";
            try
            {
                GameFileVerificationService.EnsureRepairSafe(gameDirectory, destination);
                await using (var input = File.OpenRead(source))
                await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None,
                                 81920, FileOptions.Asynchronous))
                {
                    await input.CopyToAsync(output, ct).ConfigureAwait(false);
                }
                if (FileUtil.GetLength(part) != asset.Size || !string.Equals(
                        await FileUtil.Sha1Async(part, ct).ConfigureAwait(false), asset.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"旧版资源映射校验失败：{destination}");
                ct.ThrowIfCancellationRequested();
                GameFileVerificationService.EnsureRepairSafe(gameDirectory, destination);
                FileUtil.MoveReplace(part, destination);
                copied++;
            }
            finally
            {
                FileUtil.DeleteIfExists(part);
            }
        }
        return copied;
    }

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
