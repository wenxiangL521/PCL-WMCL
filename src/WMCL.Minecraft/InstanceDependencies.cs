using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Logging;

namespace WMCL.Minecraft;

internal static class InstanceDependencies
{
    internal sealed record Change(string Path, byte[] Original, byte[] Updated);
    private sealed record Dependent(string Id, string Path, byte[] Original, JsonObject Profile);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static IReadOnlyList<Change> PlanRename(string gameRoot, string sourceId, string targetId)
    {
        var changes = new List<Change>();
        foreach (var dependent in ReadDependents(gameRoot, sourceId))
        {
            foreach (var property in new[] { "inheritsFrom", "jar" })
                if (References(dependent.Profile[property], sourceId)) dependent.Profile[property] = targetId;
            var updated = Bytes(dependent.Profile);
            changes.Add(new Change(dependent.Path, dependent.Original, updated));
            foreach (var relative in new[] { "WMCL/install.json", "PCL/install.json" })
            {
                var path = Path.Combine(Path.GetDirectoryName(dependent.Path)!, relative);
                if (!File.Exists(path)) continue;
                InstallManifestStore.ManagedFilePath(gameRoot, Path.GetRelativePath(gameRoot, path), dependent.Id);
                var original = File.ReadAllBytes(path);
                var manifest = JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("安装清单不是对象：" + path);
                var changed = false;
                foreach (var file in manifest["ManagedFiles"]?.AsArray() ?? [])
                {
                    if (file?["RelativePath"] is not JsonValue value || !value.TryGetValue<string>(out var filePath)) continue;
                    var full = InstallManifestStore.ManagedFilePath(gameRoot, filePath, dependent.Id);
                    if (!full.Equals(dependent.Path, Comparison)) continue;
                    file["Sha1"] = Convert.ToHexStringLower(SHA1.HashData(updated));
                    file["Url"] = null;
                    file["ArchivePath"] = null;
                    file["ArchiveId"] = null;
                    changed = true;
                }
                if (!changed) continue;
                manifest["LastModifiedUtc"] = JsonValue.Create(DateTimeOffset.UtcNow);
                changes.Add(new Change(path, original, Bytes(manifest)));
            }
        }
        return changes;
    }

    internal static void EnsureCanDelete(string gameRoot, string id)
    {
        var dependents = ReadDependents(gameRoot, id).Select(dependent => dependent.Id).ToArray();
        if (dependents.Length > 0)
            throw new InvalidOperationException("无法删除，以下实例仍依赖此实例：" + string.Join("、", dependents));
    }

    private static IEnumerable<Dependent> ReadDependents(string gameRoot, string sourceId)
    {
        var root = Path.GetFullPath(gameRoot);
        var versions = Path.Combine(root, "versions");
        if (!Directory.Exists(versions)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var id = Path.GetFileName(directory);
            if (id.Equals(sourceId, Comparison)) continue;
            var path = Path.Combine(directory, id + ".json");
            if (!File.Exists(path)) continue;
            byte[] original;
            JsonObject? profile;
            try
            {
                InstallManifestStore.ManagedFilePath(root, Path.GetRelativePath(root, path), id);
                original = File.ReadAllBytes(path);
                profile = JsonNode.Parse(original) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                Log.Warn("跳过无法读取继承关系的实例：" + id + "（" + ex.Message + "）");
                continue;
            }
            if (profile is not null && (References(profile["inheritsFrom"], sourceId) || References(profile["jar"], sourceId)))
                yield return new Dependent(id, path, original, profile);
        }
    }

    private static bool References(JsonNode? node, string id) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Equals(id, Comparison);

    private static byte[] Bytes(JsonObject node) => Encoding.UTF8.GetBytes(node.ToJsonString(JsonOptions));
}
