using System.IO.Compression;
using PCL.Core.IO;

namespace PCL.Minecraft;

/// <summary>从原始整合包重新解压 overrides，用于修复缺失/损坏的整合包覆写文件。</summary>
public static class OverridesRepair
{
    public static async Task<int> RepairAsync(
        string gameRoot,
        string versionId,
        CancellationToken ct = default)
    {
        var manifest = InstallManifestStore.Load(gameRoot, versionId)
                       ?? throw new InvalidOperationException("没有安装清单，无法修复 overrides。");
        if (string.IsNullOrWhiteSpace(manifest.SourceArchive) ||
            string.IsNullOrWhiteSpace(manifest.OverridesRoot))
            throw new InvalidOperationException("清单没有记录原始整合包/overrides 根目录。");
        if (!File.Exists(manifest.SourceArchive))
            throw new FileNotFoundException("原始整合包已不存在。", manifest.SourceArchive);

        var prefix = (manifest.OverridesPrefix ?? "").Replace('\\', '/').TrimStart('/');
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        var root = Path.GetFullPath(manifest.OverridesRoot);
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(manifest.SourceArchive);
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            var name = entry.FullName.Replace('\\', '/');
            if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = prefix.Length == 0 ? name : name[prefix.Length..];
            if (relative.Length == 0) continue;
            var destination = FileUtil.SafeChildPath(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            count++;
        }
        return count;
    }
}
