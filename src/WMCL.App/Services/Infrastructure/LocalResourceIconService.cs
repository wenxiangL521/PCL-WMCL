using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WMCL.App.Services.Infrastructure;

public enum LocalResourceKind { Mod, ResourcePack, ShaderPack, World, WorldBackup, Folder, File }

internal static class LocalResourceIconService
{
    internal const int MaxIconBytes = 2 * 1024 * 1024;
    internal const int MaxDimension = 1024;
    private const int MaxMetadataBytes = 128 * 1024;
    private const int MaxCacheBytes = 8 * 1024 * 1024;
    private const int MaxCacheEntries = 128;
    private static readonly SemaphoreSlim Readers = new(4);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> Cache = new(StringComparer.Ordinal);
    private static readonly LinkedList<CacheEntry> Recency = new();
    private static int _cacheBytes;
    private sealed record CacheEntry(string Key, byte[]? Bytes);

    internal static async Task<byte[]?> LoadAsync(string? path, LocalResourceKind kind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        await Readers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(path, kind, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { Readers.Release(); }
    }

    private static byte[]? Load(string path, LocalResourceKind kind, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (!IsUnlinked(path)) return null;
            var directory = Directory.Exists(path);
            var info = directory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var candidates = kind == LocalResourceKind.ShaderPack ? new[] { "pack.png", "icon.png" } :
                kind is LocalResourceKind.World or LocalResourceKind.WorldBackup ? new[] { "icon.png" } : new[] { "pack.png" };
            var stamp = directory
                ? string.Join('|', candidates.Select(name => Stamp(Path.Combine(path, name))))
                : Stamp(path);
            var key = $"{kind}:{info.FullName}:{stamp}";
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached))
                {
                    Recency.Remove(cached);
                    Recency.AddFirst(cached);
                    return cached.Value.Bytes;
                }
            }
            byte[]? bytes = null;
            if (directory)
            {
                foreach (var candidate in candidates)
                {
                    var icon = Path.Combine(path, candidate);
                    if (!File.Exists(icon) || !IsUnlinked(icon)) continue;
                    using var stream = File.OpenRead(icon);
                    if (stream.Length > MaxIconBytes) continue;
                    bytes = ValidatePng(ReadBounded(stream, MaxIconBytes));
                    if (bytes is not null) break;
                }
            }
            else
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.Entries.Count > 10000) return null;
                var names = kind == LocalResourceKind.Mod ? ModIcons(archive) : candidates;
                foreach (var name in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SafeEntryName(name)) continue;
                    var entry = archive.GetEntry(name);
                    if (entry is null || IsLink(entry) || entry.Length > MaxIconBytes) continue;
                    using var stream = entry.Open();
                    bytes = ValidatePng(ReadBounded(stream, MaxIconBytes));
                    if (bytes is not null) break;
                }
            }
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var existing)) return existing.Value.Bytes;
                var node = Recency.AddFirst(new CacheEntry(key, bytes));
                Cache.Add(key, node);
                _cacheBytes += bytes?.Length ?? 0;
                while (Cache.Count > MaxCacheEntries || _cacheBytes > MaxCacheBytes)
                {
                    var oldest = Recency.Last!;
                    Recency.RemoveLast();
                    Cache.Remove(oldest.Value.Key);
                    _cacheBytes -= oldest.Value.Bytes?.Length ?? 0;
                }
            }
            return bytes;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Stamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && IsUnlinked(path) ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing";
    }

    private static bool IsUnlinked(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    private static bool SafeEntryName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        !name.Contains('\\') && !name.Contains(':') && !name.Contains('\0') && !name.StartsWith('/') &&
        name.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");

    private static bool IsLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0;

    private static byte[]? ReadBounded(Stream stream, int maximum)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (output.Length < maximum)
        {
            var count = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum - (int)output.Length));
            if (count == 0) return output.ToArray();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static byte[]? ValidatePng(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 24 || bytes.Length > MaxIconBytes ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return null;
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension ? bytes : null;
    }

    private static string? Metadata(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null || IsLink(entry) || entry.Length > MaxMetadataBytes) return null;
        using var stream = entry.Open();
        var bytes = ReadBounded(stream, MaxMetadataBytes);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<string> ModIcons(ZipArchive archive)
    {
        var candidates = new List<string>();
        string? modId = null;
        TryParse(archive, "fabric.mod.json", text => ParseFabric(text, candidates, ref modId));
        foreach (var name in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
            TryParse(archive, name, text => ParseToml(text, candidates, ref modId));
        TryParse(archive, "mcmod.info", text => ParseLegacy(text, candidates, ref modId));

        // 声明的路径常带前导斜杠（如 /mousetweaks_logo.png），必须去掉才能匹配 zip 条目。
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeEntryName(candidate);
            if (normalized is not null) yield return normalized;
        }

        // 大量 NeoForge/旧版 Mod 不声明图标，按约定位置兜底查找。
        foreach (var name in new[] { "logo.png", "icon.png", "pack.png" }) yield return name;
        var lowerId = modId?.ToLowerInvariant();
        var icons = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Where(name => name.Count(c => c == '/') is 2 or 3)
            .Where(name => name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: name, Segments: name.Split('/')))
            .Where(item => item.Segments.Length >= 3 && item.Segments[1].Length > 0 &&
                           (item.Segments[^1].Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
                            item.Segments[^1].Equals("logo.png", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => lowerId is not null && item.Segments[1].Equals(lowerId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Segments.Length)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name);
        foreach (var name in icons.Take(8)) yield return name;
    }

    private static void TryParse(ZipArchive archive, string entryName, Action<string> parse)
    {
        var text = Metadata(archive, entryName);
        if (text is null) return;
        try { parse(text); }
        catch (JsonException) { }
        catch (FormatException) { }
    }

    private static void ParseFabric(string json, List<string> candidates, ref string? modId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            modId ??= id.GetString();
        if (!document.RootElement.TryGetProperty("icon", out var icon)) return;
        if (icon.ValueKind == JsonValueKind.String) candidates.Add(icon.GetString()!);
        else if (icon.ValueKind == JsonValueKind.Object)
            candidates.AddRange(icon.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .OrderBy(property => int.TryParse(property.Name, out var size) ? Math.Abs((long)size - 24) : long.MaxValue)
                .Select(property => property.Value.GetString()!));
    }

    private static void ParseToml(string toml, List<string> candidates, ref string? modId)
    {
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length < 2 || value[0] is not ('"' or '\'')) continue;
            var end = value.IndexOf(value[0], 1);
            if (end <= 1) continue;
            var text = value[1..end];
            if (key == "logoFile") candidates.Add(text);
            else if (key == "modId") modId ??= text;
        }
    }

    private static void ParseLegacy(string json, List<string> candidates, ref string? modId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("modList", out var mods)) root = mods;
        if (root.ValueKind == JsonValueKind.Array) root = root.EnumerateArray().FirstOrDefault();
        if (root.ValueKind != JsonValueKind.Object) return;
        if (root.TryGetProperty("logoFile", out var logo) && logo.ValueKind == JsonValueKind.String)
            candidates.Add(logo.GetString()!);
        if (modId is null && root.TryGetProperty("modid", out var legacyId) && legacyId.ValueKind == JsonValueKind.String)
            modId = legacyId.GetString();
    }

    private static string? NormalizeEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Replace('\\', '/').TrimStart('/');
        while (trimmed.StartsWith("./", StringComparison.Ordinal)) trimmed = trimmed[2..];
        return trimmed.Length == 0 ? null : trimmed;
    }
}
