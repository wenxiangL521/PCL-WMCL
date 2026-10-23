using System.IO.Compression;
using WMCL.Core.IO;
using WMCL.Modding.CurseForge;

namespace WMCL.Modding.Modpack;

internal static class CurseForgePackContent
{
    public sealed record ExtractedFile(string Path, string ArchivePath);

    public static string TargetFolder(CurseForgeFile file)
    {
        var modules = file.Modules ?? [];
        if (modules.Count == 0 || file.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            modules.Any(module => module.Equals("META-INF", StringComparison.OrdinalIgnoreCase) ||
                                  module.Equals("mcmod.info", StringComparison.OrdinalIgnoreCase) ||
                                  module.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase)))
            return "mods";
        if (modules.Contains("pack.mcmeta", StringComparer.OrdinalIgnoreCase)) return "resourcepacks";
        if (modules.Contains("shaders", StringComparer.OrdinalIgnoreCase)) return "shaderpacks";
        return "saves";
    }

    public static IReadOnlyList<ExtractedFile> ExtractWorlds(string path, string instanceDirectory, string id, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            ModpackContentPaths.Validate(entry.FullName, instanceDirectory, id);
            if (!names.Add(entry.FullName.Replace('\\', '/')) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("CurseForge 地图包含重复路径或符号链接：" + entry.FullName);
        }
        var roots = entries.Where(entry => entry.FullName == "level.dat" || entry.FullName.EndsWith("/level.dat", StringComparison.Ordinal))
            .Select(entry => entry.FullName[..^"level.dat".Length]).ToArray();
        if (roots.Length == 0) throw new InvalidDataException("CurseForge 地图缺少 level.dat：" + Path.GetFileName(path));
        if (roots.Any(root => roots.Any(other => root != other && other.StartsWith(root, StringComparison.Ordinal))))
            throw new InvalidDataException("CurseForge 地图包含互相嵌套的存档根目录。");
        var extracted = new List<ExtractedFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var name = root.Length == 0 ? Path.GetFileNameWithoutExtension(path) : root.TrimEnd('/').Split('/')[^1];
            var world = "saves/" + name + "/";
            if (!destinations.Add(world)) throw new InvalidDataException("CurseForge 地图目录重名：" + name);
            var planned = entries.Where(entry => entry.FullName.StartsWith(root, StringComparison.Ordinal))
                .Select(entry => (Entry: entry, Relative: world + entry.FullName[root.Length..])).ToArray();
            foreach (var item in planned) ModpackContentPaths.Validate(item.Relative, instanceDirectory, id);
            foreach (var item in planned)
            {
                ct.ThrowIfCancellationRequested();
                var destination = FileUtil.SafeChildPath(instanceDirectory, item.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                item.Entry.ExtractToFile(destination, overwrite: false);
                extracted.Add(new ExtractedFile(destination, item.Entry.FullName));
            }
        }
        return extracted;
    }
}
