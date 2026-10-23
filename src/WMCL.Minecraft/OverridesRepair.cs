using System.IO.Compression;
using WMCL.Core.IO;

namespace WMCL.Minecraft;

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

        var root = Path.GetFullPath(manifest.OverridesRoot);
        var instanceRoot = Path.GetFullPath(Path.Combine(gameRoot, "versions", versionId));
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!root.Equals(Path.GetFullPath(gameRoot), pathComparison) && !root.Equals(instanceRoot, pathComparison))
            throw new InvalidDataException("覆写清单的目标不属于当前实例，请核对重命名或克隆后的清单。");
        Directory.CreateDirectory(root);
        var prefixes = manifest.OverridesPrefixes.Count > 0
            ? manifest.OverridesPrefixes
            : [manifest.OverridesPrefix ?? ""];
        var managedOverrides = manifest.OverridesPrefixes.Count > 0
            ? manifest.ManagedFiles.Where(file => file.Component == "overrides")
                .Select(file => Path.GetFullPath(FileUtil.SafeChildPath(gameRoot, file.RelativePath)))
                .ToHashSet(StringComparer.Ordinal)
            : null;
        var comparison = manifest.OverridesPrefixes.Count > 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var archive = ZipFile.OpenRead(manifest.SourceArchive);
        var count = 0;
        foreach (var sourcePrefix in prefixes)
        {
            var prefix = sourcePrefix.Replace('\\', '/').TrimStart('/');
            if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                var name = entry.FullName.Replace('\\', '/');
                if (prefix.Length > 0 && !name.StartsWith(prefix, comparison))
                    continue;
                var relative = prefix.Length == 0 ? name : name[prefix.Length..];
                if (relative.Length == 0) continue;
                if (manifest.OverridesExcludedPaths.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
                var destination = FileUtil.SafeChildPath(root, relative);
                if (managedOverrides is not null && !managedOverrides.Contains(destination)) continue;
                var gameRelative = Path.GetRelativePath(gameRoot, destination);
                InstallManifestStore.ManagedFilePath(gameRoot, gameRelative, versionId);
                var save = InstallManifestStore.IsSaveFile(gameRelative);
                if (save && File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: !save);
                count++;
            }
        }
        return count;
    }
}
