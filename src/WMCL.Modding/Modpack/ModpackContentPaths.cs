using System.IO.Compression;
using WMCL.Core.IO;

namespace WMCL.Modding.Modpack;

internal static class ModpackContentPaths
{
    public static void Validate(string path, string directory, string id)
    {
        FileUtil.SafeChildPath(directory, path);
        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(part => string.IsNullOrEmpty(part) || part == ".") ||
            normalized.Contains(':') || normalized.Equals(id + ".json", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(id + ".jar", StringComparison.OrdinalIgnoreCase) ||
            new[] { "WMCL/install.json", "WMCL/instance.json", "PCL/install.json", "PCL/instance.json" }
                .Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包文件路径无效或覆盖启动器元数据：" + path);
    }

    public static void ValidateOverrides(ZipArchive archive, string prefix, string directory, string id)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;
        FileUtil.SafeChildPath(directory, prefix.TrimEnd('/', '\\'));
        var normalized = prefix.Replace('\\', '/').TrimEnd('/') + "/";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith('/')))
        {
            var relative = entry.FullName[normalized.Length..];
            Validate(relative, directory, id);
            if (!seen.Add(relative.Replace('\\', '/'))) throw new InvalidDataException("整合包覆写路径重复：" + relative);
        }
    }

    public static void ValidateLibraryPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') ||
            normalized.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new InvalidDataException("MCBBS library 路径无效：" + path);
    }
}
