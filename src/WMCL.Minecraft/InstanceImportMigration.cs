using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;

namespace WMCL.Minecraft;

internal static class InstanceImportMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string RepairArchive = "WMCL/import-repair.zip";

    internal static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains(':') ||
            path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("导入包包含非法相对路径：" + path);
    }

    internal static string MapLocal(string relative, string sourceId, string targetId)
    {
        ValidateRelative(relative);
        if (relative.Equals(sourceId + ".json", StringComparison.OrdinalIgnoreCase)) return targetId + ".json";
        if (relative.Equals(sourceId + ".jar", StringComparison.OrdinalIgnoreCase)) return targetId + ".jar";
        var natives = sourceId + "-natives/";
        return relative.StartsWith(natives, StringComparison.Ordinal)
            ? targetId + "-natives/" + relative[natives.Length..] : relative;
    }

    internal static async Task RewriteAsync(string staged, string target, string sourceId, string targetId,
        string? minecraft, CancellationToken ct)
    {
        var profilePath = Path.Combine(staged, targetId + ".json");
        if (!File.Exists(profilePath)) throw new InvalidDataException("导入包中没有版本 JSON。");
        var profile = ReadObject(profilePath);
        profile["id"] = targetId;
        if (profile["jar"] is JsonValue jarValue && jarValue.TryGetValue<string>(out var jar))
        {
            ValidateRelative(jar);
            if (jar.Contains('/')) throw new InvalidDataException("客户端 jar 引用不是版本 id：" + jar);
            if (jar == sourceId) profile["jar"] = targetId;
        }
        if (profile["inheritsFrom"]?.GetValue<string>() == sourceId)
            throw new InvalidDataException("导入 profile 依赖源实例，导出包不包含该依赖。");
        if (profile["clientVersion"] is null && profile["inheritsFrom"] is null && !string.IsNullOrWhiteSpace(minecraft))
            profile["clientVersion"] = minecraft;
        if (profile["downloads"]?["client"] is JsonObject client && client["path"] is JsonValue clientPath)
        {
            var path = clientPath.GetValue<string>().Replace('\\', '/');
            if (path == sourceId + ".jar") path = targetId + ".jar";
            ValidateRelative(path);
            client["path"] = path;
        }
        foreach (var library in profile["libraries"]?.AsArray() ?? [])
        {
            if (library?["downloads"] is not JsonObject downloads) continue;
            RewriteLibrary(downloads["artifact"]);
            foreach (var classifier in downloads["classifiers"]?.AsObject() ?? []) RewriteLibrary(classifier.Value);
        }
        await WriteObjectAsync(profilePath, profile, ct).ConfigureAwait(false);
        var changed = new HashSet<string>(StringComparer.Ordinal) { targetId + ".json" };
        foreach (var relative in new[] { "WMCL/instance.json", "PCL/instance.json" })
        {
            var path = Path.Combine(staged, relative);
            if (!File.Exists(path)) continue;
            var settings = ReadObject(path);
            foreach (var property in settings.ToArray())
            {
                if (!property.Key.Equals("JavaPath", StringComparison.OrdinalIgnoreCase) &&
                    !property.Key.Equals("LogoPath", StringComparison.OrdinalIgnoreCase) &&
                    !property.Key.Equals("PluginDirectory", StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value is not JsonValue value || !value.TryGetValue<string>(out var setting)) continue;
                var normalized = setting.Replace('\\', '/');
                var marker = "/versions/" + sourceId;
                var index = normalized.LastIndexOf(marker, StringComparison.Ordinal);
                if (index < 0 || normalized.Length > index + marker.Length && normalized[index + marker.Length] != '/') continue;
                var local = normalized[(index + marker.Length)..].TrimStart('/');
                settings[property.Key] = local.Length == 0 ? target : FileUtil.SafeChildPath(target, MapLocal(local, sourceId, targetId));
            }
            await WriteObjectAsync(path, settings, ct).ConfigureAwait(false);
            changed.Add(relative);
        }

        var manifests = new List<(string Path, JsonObject Manifest)>();
        foreach (var relative in new[] { "WMCL/install.json", "PCL/install.json" })
        {
            var path = Path.Combine(staged, relative);
            if (!File.Exists(path)) continue;
            var manifest = ReadObject(path);
            InstallManifestStore.ValidateSchemaVersion(manifest["SchemaVersion"]?.GetValue<int>() ?? 1);
            var files = new JsonArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest["ManagedFiles"]?.AsArray() ?? [])
            {
                ct.ThrowIfCancellationRequested();
                var original = file?["RelativePath"]?.GetValue<string>()?.Replace('\\', '/')
                               ?? throw new InvalidDataException("受管文件缺少相对路径。");
                ValidateRelative(original);
                var prefix = "versions/" + sourceId + "/";
                if (!original.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (original.StartsWith("versions/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("安装清单指向其他实例：" + original);
                    continue;
                }
                var local = MapLocal(original[prefix.Length..], sourceId, targetId);
                if (local is "WMCL/install.json" or "PCL/install.json" or RepairArchive) continue;
                var exported = FileUtil.SafeChildPath(staged, local);
                if (!File.Exists(exported)) continue;
                if (!seen.Add(local)) throw new InvalidDataException("受管文件路径重复：" + original);
                var mapped = file!.DeepClone();
                mapped["RelativePath"] = "versions/" + targetId + "/" + local;
                var hash = await InstallManifestStore.ComputeFileSha1Async(exported, ct).ConfigureAwait(false);
                if (changed.Contains(local)) mapped["Sha1"] = hash;
                var matches = string.Equals(mapped["Sha1"]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase);
                mapped["Url"] = null;
                mapped["ArchiveId"] = null;
                mapped["ArchivePath"] = matches ? "instance/" + local : null;
                files.Add(mapped);
            }
            manifest["ManagedFiles"] = files;
            manifest["ManagedArchives"] = new JsonObject();
            manifest["SourceArchive"] = Path.Combine(target, RepairArchive);
            manifest["OverridesRoot"] = target;
            manifest["OverridesPrefix"] = "instance/";
            manifest["OverridesPrefixes"] = new JsonArray("instance/");
            manifest["OverridesExcludedPaths"] = new JsonArray();
            manifest["LastModifiedUtc"] = JsonValue.Create(DateTimeOffset.UtcNow);
            manifests.Add((path, manifest));
        }
        if (manifests.Count == 0) return;
        var repairPath = Path.Combine(staged, RepairArchive);
        if (Path.Exists(repairPath)) File.Delete(repairPath);
        Directory.CreateDirectory(Path.GetDirectoryName(repairPath)!);
        using (var repair = ZipFile.Open(repairPath, ZipArchiveMode.Create))
        {
            var locals = manifests.SelectMany(item => item.Manifest["ManagedFiles"]!.AsArray())
                .Where(file => file?["ArchivePath"] is not null)
                .Select(file => file!["ArchivePath"]!.GetValue<string>()).Distinct(StringComparer.Ordinal);
            foreach (var archiveEntry in locals)
            {
                ct.ThrowIfCancellationRequested();
                var entry = repair.CreateEntry(archiveEntry, CompressionLevel.Optimal);
                await using var input = File.OpenRead(FileUtil.SafeChildPath(staged, archiveEntry["instance/".Length..]));
                await using var output = entry.Open();
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }
        }
        foreach (var (path, manifest) in manifests)
            await WriteObjectAsync(path, manifest, ct).ConfigureAwait(false);

        void RewriteLibrary(JsonNode? download)
        {
            if (download?["path"] is not JsonValue value || !value.TryGetValue<string>(out var path)) return;
            path = path.Replace('\\', '/');
            ValidateRelative(path);
            var prefix = "wmcl-packs/" + sourceId + "/";
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (download?["url"] is not JsonValue urlValue || !urlValue.TryGetValue<string>(out var url) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new InvalidDataException("导出包未包含实例专属库且没有有效下载来源：" + path);
                download!["path"] = "wmcl-packs/" + targetId + "/" + path[prefix.Length..];
            }
            else
            {
                if (path.StartsWith("wmcl-packs/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("导入 profile 引用了其他实例专属库：" + path);
                download!["path"] = path;
            }
        }
    }

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException("实例元数据不是对象：" + path);

    private static Task WriteObjectAsync(string path, JsonObject value, CancellationToken ct) =>
        File.WriteAllTextAsync(path, value.ToJsonString(JsonOptions), ct);
}
