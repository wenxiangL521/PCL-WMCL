using System.IO.Compression;
using System.Text.Json;

namespace WMCL.Modding.Local;

public sealed record JarInJarScanResult(
    IReadOnlyList<JarInJarEntry> Entries,
    IReadOnlyList<string> Warnings);

public sealed record JarInJarEntry(
    string Path,
    string? ModId,
    string? Name,
    string? Version,
    long Size,
    int Depth,
    IReadOnlyList<JarInJarEntry> Children,
    IReadOnlyList<string> Depends,
    IReadOnlyList<string> Breaks)
{
    public string FileName => System.IO.Path.GetFileName(Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FileName : Name!;
    public string Detail => string.Join(" · ", new[] { ModId, Version }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    public string RelationText => Depends.Count == 0 && Breaks.Count == 0
        ? ""
        : string.Join("；", new[]
        {
            Depends.Count == 0 ? null : "依赖 " + string.Join("、", Depends),
            Breaks.Count == 0 ? null : "不兼容 " + string.Join("、", Breaks)
        }.Where(value => value is not null));
    public string SizeText => Size switch
    {
        >= 1024 * 1024 => $"{Size / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Size / 1024d:0.0} KB",
        _ => $"{Size} B"
    };
}

/// <summary>只读扫描 Jar 内嵌 Jar（Fabric/Quilt/Forge/NeoForge 常见目录），不做依赖/冲突分析。</summary>
public static class JarInJarService
{
    private const int MaxDepth = 3;
    private const int MaxEntries = 256;
    private const long MaxNestedBytes = 64L * 1024 * 1024;

    public static IReadOnlyList<JarInJarEntry> Scan(string jarPath) => ScanDetailed(jarPath).Entries;

    public static JarInJarScanResult ScanDetailed(string jarPath)
    {
        if (!File.Exists(jarPath)) throw new FileNotFoundException("Mod 文件不存在。", jarPath);
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var budget = new Budget();
            var entries = ScanArchive(archive, 0, budget);
            var warnings = new List<string>();
            var hostId = ReadHostId(archive);
            if (budget.Truncated)
                warnings.Add($"内嵌 Jar 超过 {MaxEntries} 项，仅列出前 {MaxEntries} 项。");
            foreach (var group in Flatten(entries)
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.ModId))
                         .GroupBy(entry => entry.ModId!, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                var versions = group.Select(entry => entry.Version)
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                warnings.Add(versions.Length > 1
                    ? $"重复 Mod ID：{group.Key}（{group.Count()} 个内嵌 Jar，版本 {string.Join("、", versions)}）"
                    : $"重复 Mod ID：{group.Key}（{group.Count()} 个内嵌 Jar）");
            }
            var all = Flatten(entries).ToArray();
            var provided = all.Select(entry => entry.ModId)
                .Append(hostId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in all)
            {
                foreach (var dependency in entry.Depends.Where(IsModDependency))
                    if (!provided.Contains(dependency))
                        warnings.Add($"内嵌 {entry.DisplayName} 依赖 {dependency}，但宿主/内嵌 Jar 未提供。");
                foreach (var conflict in entry.Breaks.Where(IsModDependency))
                    if (provided.Contains(conflict) &&
                        !string.Equals(conflict, entry.ModId, StringComparison.OrdinalIgnoreCase))
                        warnings.Add($"内嵌 {entry.DisplayName} 声明与 {conflict} 不兼容。");
            }
            return new JarInJarScanResult(entries, warnings);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("不是有效的 Jar/ZIP：" + Path.GetFileName(jarPath), ex);
        }
    }

    private static IEnumerable<JarInJarEntry> Flatten(IEnumerable<JarInJarEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var child in Flatten(entry.Children)) yield return child;
        }
    }

    private sealed class Budget
    {
        public int Remaining = MaxEntries;
        public bool Truncated;
    }

    private static List<JarInJarEntry> ScanArchive(ZipArchive archive, int depth, Budget budget)
    {
        var result = new List<JarInJarEntry>();
        foreach (var entry in CandidateEntries(archive, depth))
        {
            if (budget.Remaining-- <= 0)
            {
                budget.Truncated = true;
                break;
            }
            if (entry.Length is <= 0 or > MaxNestedBytes) continue;
            byte[] bytes;
            try
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }
            catch (InvalidDataException)
            {
                continue;
            }
            var metadata = ReadMetadata(bytes);
            IReadOnlyList<JarInJarEntry> children = [];
            if (depth + 1 < MaxDepth)
            {
                try
                {
                    using var nested = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                    children = ScanArchive(nested, depth + 1, budget);
                }
                catch (InvalidDataException)
                {
                    // 不是可读 ZIP：保留条目但不递归
                }
            }
            result.Add(new JarInJarEntry(entry.FullName, metadata.Id, metadata.Name,
                metadata.Version, entry.Length, depth, children, metadata.Depends, metadata.Breaks));
        }
        return result;
    }

    private static IEnumerable<ZipArchiveEntry> CandidateEntries(ZipArchive archive, int depth)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                !IsNestedDirectory(entry.FullName)) continue;
            if (seen.Add(entry.FullName)) yield return entry;
        }
        foreach (var path in DeclaredNestedPaths(archive))
        {
            if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !seen.Add(path)) continue;
            var entry = archive.GetEntry(path);
            if (entry is not null) yield return entry;
        }
    }

    private static bool IsNestedDirectory(string path) =>
        path.StartsWith("META-INF/jars/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("META-INF/jarjar/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("META-INF/libraries/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("META-INF/fabric/", StringComparison.OrdinalIgnoreCase);

    private static List<string> DeclaredNestedPaths(ZipArchive archive)
    {
        var result = new List<string>();
        foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json", "META-INF/jarjar/metadata.json" })
        {
            var entry = archive.GetEntry(name);
            if (entry is null || entry.Length > 4 * 1024 * 1024) continue;
            string json;
            try
            {
                using var reader = new StreamReader(entry.Open());
                json = reader.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(json);
                if (name == "fabric.mod.json" &&
                    document.RootElement.TryGetProperty("jars", out var jars))
                {
                    foreach (var item in jars.EnumerateArray())
                        if (item.TryGetProperty("file", out var file) && file.GetString() is { Length: > 0 } path)
                            result.Add(path);
                }
                else if (name == "META-INF/jarjar/metadata.json" &&
                         document.RootElement.TryGetProperty("jars", out var jarJars))
                {
                    foreach (var item in jarJars.EnumerateArray())
                        if (item.TryGetProperty("path", out var path) && path.GetString() is { Length: > 0 } value)
                            result.Add(value);
                }
            }
            catch (JsonException)
            {
                // 损坏元数据不阻塞扫描
            }
        }
        return result;
    }

    private sealed record Metadata(string? Id, string? Name, string? Version,
        IReadOnlyList<string> Depends, IReadOnlyList<string> Breaks);

    private static Metadata ReadMetadata(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (ReadJson(archive, "fabric.mod.json") is { } fabric)
            {
                return new Metadata(GetString(fabric, "id"), GetString(fabric, "name"),
                    GetString(fabric, "version"), ReadObjectKeys(fabric, "depends"),
                    ReadObjectKeys(fabric, "breaks"));
            }
            if (ReadJson(archive, "quilt.mod.json") is { } quilt &&
                quilt.TryGetProperty("quilt_loader", out var loader) &&
                loader.ValueKind == JsonValueKind.Object)
            {
                string? name = null;
                if (loader.TryGetProperty("metadata", out var metadata) &&
                    metadata.ValueKind == JsonValueKind.Object)
                    name = GetString(metadata, "name");
                return new Metadata(GetString(loader, "id"), name, GetString(loader, "version"),
                    ReadArrayIds(loader, "depends"), ReadArrayIds(loader, "breaks"));
            }
            var toml = ReadText(archive, "META-INF/mods.toml") ??
                       ReadText(archive, "META-INF/neoforge.mods.toml");
            if (toml is not null)
            {
                var (id, name, version) = ReadToml(toml);
                return new Metadata(id, name, version, [], []);
            }
        }
        catch (InvalidDataException)
        {
        }
        catch (JsonException)
        {
        }
        return new Metadata(null, null, null, [], []);
    }

    private static string? ReadHostId(ZipArchive archive)
    {
        if (ReadJson(archive, "fabric.mod.json") is { } fabric) return GetString(fabric, "id");
        if (ReadJson(archive, "quilt.mod.json") is { } quilt &&
            quilt.TryGetProperty("quilt_loader", out var loader) &&
            loader.ValueKind == JsonValueKind.Object)
            return GetString(loader, "id");
        var toml = ReadText(archive, "META-INF/mods.toml") ??
                   ReadText(archive, "META-INF/neoforge.mods.toml");
        return toml is null ? null : ReadToml(toml).Id;
    }

    private static IReadOnlyList<string> ReadObjectKeys(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Select(item => item.Name).ToArray()
            : [];

    private static IReadOnlyList<string> ReadArrayIds(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object ? GetString(item, "id") : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
    }

    private static bool IsModDependency(string id) =>
        id is not ("minecraft" or "fabricloader" or "java" or "quilt_loader");

    private static JsonElement? ReadJson(ZipArchive archive, string path)
    {
        var text = ReadText(archive, path);
        if (text is null) return null;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string? ReadText(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null || entry.Length > 4 * 1024 * 1024) return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static (string? Id, string? Name, string? Version) ReadToml(string text)
    {
        string? id = null, name = null, version = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim().Trim('"');
            if (key == "modId" && id is null) id = value;
            else if (key == "displayName" && name is null) name = value;
            else if (key == "version" && version is null) version = value;
        }
        return (id, name, version);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
