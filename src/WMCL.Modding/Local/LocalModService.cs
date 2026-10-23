using System.IO.Compression;
using System.Text.Json;

namespace WMCL.Modding.Local;

/// <summary>
/// 本地 Mod 文件。对照 PCL2 的 LocalResourceFile：
/// 禁用时把 foo.jar 改名为 foo.jar.disabled（若已存在则用 .old），启用时改回 foo.jar。
/// </summary>
public sealed record LocalMod(
    string FilePath,
    string FileName,
    string EnabledName,
    bool IsEnabled,
    long Size,
    string? UpdateVersion = null)
{
    public string? ModId { get; init; }
    public string? Version { get; init; }
    public string? MetadataName { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(MetadataName) ? EnabledName : MetadataName;
    public string DetailText => string.Join(" · ", new[] { ModId, Version }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string StateText => IsEnabled ? "已启用" : "已禁用";
    public string UpdateText => UpdateVersion is null ? "" : $"可更新 → {UpdateVersion}";
    public string SizeText => Size switch
    {
        >= 1024 * 1024 => $"{Size / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Size / 1024d:0.0} KB",
        _ => $"{Size} B"
    };
}

public static class LocalModService
{
    private const string DisabledSuffix = ".disabled";
    private const string OldSuffix = ".old";

    public static IReadOnlyList<LocalMod> List(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory)) return [];

        return Directory.EnumerateFiles(modsDirectory)
            .Where(IsModFile)
            .Select(file =>
            {
                var fileName = Path.GetFileName(file);
                var (enabledName, enabled) = ParseName(fileName);
                var metadata = ReadMetadata(file);
                return new LocalMod(
                    file,
                    fileName,
                    enabledName,
                    enabled,
                    new FileInfo(file).Length)
                {
                    ModId = metadata.ModId,
                    Version = metadata.Version,
                    MetadataName = metadata.Name
                };
            })
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SetEnabled(string modsDirectory, LocalMod mod, bool enabled)
    {
        if (mod.IsEnabled == enabled) return;

        var current = Path.Combine(modsDirectory, mod.FileName);
        if (!File.Exists(current))
            throw new FileNotFoundException("Mod 文件不存在。", current);

        if (enabled)
        {
            var target = Path.Combine(modsDirectory, mod.EnabledName);
            if (File.Exists(target) && !string.Equals(target, current, StringComparison.Ordinal))
                throw new IOException($"已存在同名 Mod：{mod.EnabledName}");
            File.Move(current, target);
            return;
        }

        var disabled = Path.Combine(modsDirectory, mod.EnabledName + DisabledSuffix);
        if (File.Exists(disabled))
            disabled = Path.Combine(modsDirectory, mod.EnabledName + OldSuffix);
        if (File.Exists(disabled))
            throw new IOException($"无法禁用：{mod.EnabledName}.disabled / .old 已存在");

        File.Move(current, disabled);
    }

    public static void Delete(LocalMod mod)
    {
        if (File.Exists(mod.FilePath)) File.Delete(mod.FilePath);
    }

    private static (string? ModId, string? Name, string? Version) ReadMetadata(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var fabric = ReadEntry(archive, "fabric.mod.json");
            if (fabric is not null)
            {
                using var document = JsonDocument.Parse(fabric);
                var root = document.RootElement;
                return (Text(root, "id"), Text(root, "name"), Text(root, "version"));
            }

            foreach (var entryName in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
            {
                var toml = ReadEntry(archive, entryName);
                if (toml is null) continue;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in toml.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('[')) continue;
                    var split = line.IndexOf('=');
                    if (split <= 0) continue;
                    var key = line[..split].Trim();
                    var value = line[(split + 1)..].Trim().Trim('"', '\'');
                    values.TryAdd(key, value);
                }
                if (values.Count > 0)
                    return (values.GetValueOrDefault("modId"), values.GetValueOrDefault("displayName"),
                        values.GetValueOrDefault("version"));
            }

            var mcmod = ReadEntry(archive, "mcmod.info");
            if (mcmod is not null)
            {
                using var document = JsonDocument.Parse(mcmod);
                var root = document.RootElement;
                var first = root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray().FirstOrDefault()
                    : root;
                if (first.ValueKind == JsonValueKind.Object)
                    return (Text(first, "modid"), Text(first, "name"), Text(first, "version"));
            }
        }
        catch (InvalidDataException) { }
        catch (IOException) { }
        catch (JsonException) { }
        return (null, null, null);

        static string? ReadEntry(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry is null) return null;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        static string? Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool IsModFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar" + DisabledSuffix, StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar" + OldSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static (string EnabledName, bool IsEnabled) ParseName(string fileName)
    {
        if (fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            return (fileName[..^DisabledSuffix.Length], false);
        if (fileName.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
            return (fileName[..^OldSuffix.Length], false);
        return (fileName, true);
    }
}
