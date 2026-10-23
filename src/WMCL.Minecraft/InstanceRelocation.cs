using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Core.Logging;

namespace WMCL.Minecraft;

internal static class InstanceRelocation
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string LibraryDirectory(string gameRoot, string id) =>
        FileUtil.SafeChildPath(gameRoot, "libraries/wmcl-packs/" + id);

    internal static void Relocate(string gameRoot, string sourceId, string targetId, bool clone, Action? beforeCommit = null)
    {
        var root = Path.GetFullPath(gameRoot);
        var source = FileUtil.SafeChildPath(root, "versions/" + sourceId);
        var target = FileUtil.SafeChildPath(root, "versions/" + targetId);
        var sourceLibraries = LibraryDirectory(root, sourceId);
        var targetLibraries = LibraryDirectory(root, targetId);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (TargetExists(source, target) || TargetExists(sourceLibraries, targetLibraries))
            throw new IOException("实例或专属库目录已存在：" + targetId);
        var sameLibraryDirectory = Path.Exists(targetLibraries);
        EnsureRegularPath(root, source);
        EnsureRegularTree(source);
        EnsureRegularPath(root, sourceLibraries);
        if (Directory.Exists(sourceLibraries)) EnsureRegularTree(sourceLibraries);
        EnsureRegularPath(root, target);
        EnsureRegularPath(root, targetLibraries);
        var metadata = BuildMetadata(root, sourceId, targetId, source, target, sourceLibraries, targetLibraries);
        var dependents = clone ? [] : InstanceDependencies.PlanRename(root, sourceId, targetId);
        var temporary = Path.Combine(root, ".wmcl-relocate-" + Guid.NewGuid().ToString("N"));
        var stagedInstance = Path.Combine(temporary, "instance");
        var stagedLibraries = Path.Combine(temporary, "libraries");
        var originalLibraries = Path.Combine(temporary, "original-libraries");
        var undo = new Stack<Action>();
        var undoDependents = new Stack<Action>();
        var movedSource = false;
        var publishedLibraries = false;
        var movedLibraries = false;
        var publishedInstance = false;
        var committed = false;
        var rolledBack = false;
        try
        {
            Directory.CreateDirectory(temporary);
            var stagedDependents = dependents.Select((change, index) =>
                (Change: change, Next: Path.Combine(temporary, "reference-" + index),
                    Backup: Path.Combine(temporary, "reference-backup-" + index))).ToArray();
            foreach (var item in stagedDependents)
            {
                File.WriteAllBytes(item.Next, item.Change.Updated);
                File.WriteAllBytes(item.Backup, item.Change.Original);
            }
            if (Directory.Exists(sourceLibraries)) CopyDirectory(sourceLibraries, stagedLibraries);
            if (clone) CopyDirectory(source, stagedInstance);
            else
            {
                Directory.Move(source, stagedInstance);
                movedSource = true;
            }
            RenameChild(sourceId + ".json", targetId + ".json");
            RenameChild(sourceId + ".jar", targetId + ".jar");
            RenameChild(sourceId + "-natives", targetId + "-natives");
            foreach (var (relative, bytes) in metadata)
            {
                var path = FileUtil.SafeChildPath(stagedInstance, relative);
                var original = File.ReadAllBytes(path);
                undo.Push(() => File.WriteAllBytes(path, original));
                File.WriteAllBytes(path, bytes);
            }
            EnsureRegularPath(root, target);
            EnsureRegularPath(root, targetLibraries);
            foreach (var item in stagedDependents)
            {
                EnsureRegularPath(root, item.Change.Path);
                if (!File.ReadAllBytes(item.Change.Path).AsSpan().SequenceEqual(item.Change.Original))
                    throw new IOException("依赖实例的元数据已变化，请重试：" + item.Change.Path);
                File.Move(item.Next, item.Change.Path, overwrite: true);
                undoDependents.Push(() => File.Move(item.Backup, item.Change.Path, overwrite: true));
            }
            if (Directory.Exists(stagedLibraries))
            {
                if (sameLibraryDirectory)
                {
                    Directory.Move(sourceLibraries, originalLibraries);
                    movedLibraries = true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetLibraries)!);
                Directory.Move(stagedLibraries, targetLibraries);
                publishedLibraries = true;
            }
            Directory.Move(stagedInstance, target);
            publishedInstance = true;
            beforeCommit?.Invoke();
            committed = true;
        }
        catch (Exception failure)
        {
            try
            {
                while (undoDependents.TryPop(out var restoreDependent)) restoreDependent();
                if (publishedInstance) Directory.Move(target, stagedInstance);
                if (publishedLibraries) Directory.Delete(targetLibraries, recursive: true);
                if (movedLibraries) Directory.Move(originalLibraries, sourceLibraries);
                if (movedSource)
                {
                    while (undo.TryPop(out var restore)) restore();
                    Directory.Move(stagedInstance, source);
                }
                rolledBack = true;
            }
            catch (Exception rollback)
            {
                throw new AggregateException("实例迁移失败且回滚未完成，原始数据保留于：" + temporary, failure, rollback);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(temporary) && (committed || rolledBack))
                Directory.Delete(temporary, recursive: true);
        }
        if (!clone && !sameLibraryDirectory) CleanUnusedLibraries(root, sourceId);

        void RenameChild(string oldName, string newName)
        {
            var oldPath = Path.Combine(stagedInstance, oldName);
            if (!Path.Exists(oldPath)) return;
            var newPath = Path.Combine(stagedInstance, newName);
            var directory = Directory.Exists(oldPath);
            var intermediate = Path.Combine(temporary, Guid.NewGuid().ToString("N"));
            Move(oldPath, intermediate);
            undo.Push(() => Move(intermediate, oldPath));
            Move(intermediate, newPath);
            undo.Pop();
            undo.Push(() =>
            {
                Move(newPath, intermediate);
                Move(intermediate, oldPath);
            });

            void Move(string from, string to)
            {
                if (directory) Directory.Move(from, to);
                else File.Move(from, to);
            }
        }
    }

    private static Dictionary<string, byte[]> BuildMetadata(string root, string sourceId, string targetId,
        string source, string target, string sourceLibraries, string targetLibraries)
    {
        var profilePath = Path.Combine(source, sourceId + ".json");
        var profile = ReadObject(profilePath);
        if (string.IsNullOrWhiteSpace(profile["clientVersion"]?.GetValue<string>()) &&
            string.IsNullOrWhiteSpace(profile["inheritsFrom"]?.GetValue<string>()))
            profile["clientVersion"] = new VersionRepository(root).GetVersionAsync(sourceId).GetAwaiter().GetResult().BaseGameVersion;
        profile["id"] = targetId;
        if (profile["jar"]?.GetValue<string>() == sourceId) profile["jar"] = targetId;
        if (profile["downloads"]?["client"] is JsonObject client && client["path"]?.GetValue<string>() == sourceId + ".jar")
            client["path"] = targetId + ".jar";
        foreach (var library in profile["libraries"]?.AsArray() ?? [])
        {
            if (library?["downloads"] is not JsonObject downloads) continue;
            RewriteLibrary(downloads["artifact"]);
            foreach (var download in downloads["classifiers"]?.AsObject() ?? []) RewriteLibrary(download.Value);
        }
        var metadata = new Dictionary<string, byte[]> { [targetId + ".json"] = Bytes(profile) };
        foreach (var relative in new[] { "WMCL/instance.json", "PCL/instance.json" })
        {
            if (!File.Exists(Path.Combine(source, relative))) continue;
            var settings = ReadObject(Path.Combine(source, relative));
            foreach (var property in settings.ToArray())
                if (property.Key.Equals("JavaPath", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("LogoPath", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("PluginDirectory", StringComparison.OrdinalIgnoreCase))
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var path))
                        settings[property.Key] = MapAbsolute(path);
            metadata[relative] = Bytes(settings);
        }
        foreach (var relative in new[] { "WMCL/install.json", "PCL/install.json" })
        {
            if (!File.Exists(Path.Combine(source, relative))) continue;
            var manifest = ReadObject(Path.Combine(source, relative));
            if (manifest["OverridesRoot"] is JsonValue overrideValue && overrideValue.TryGetValue<string>(out var overrides) &&
                !string.IsNullOrWhiteSpace(overrides))
            {
                var full = Path.GetFullPath(overrides);
                if (!full.Equals(root, PathComparison) && !full.Equals(source, PathComparison))
                    throw new InvalidDataException("覆写清单的目标不属于当前实例，无法迁移。");
                manifest["OverridesRoot"] = MapAbsolute(overrides);
            }
            if (manifest["SourceArchive"] is JsonValue archiveValue && archiveValue.TryGetValue<string>(out var archive))
                manifest["SourceArchive"] = MapAbsolute(archive);
            foreach (var file in manifest["ManagedFiles"]?.AsArray() ?? [])
            {
                var original = file!["RelativePath"]!.GetValue<string>();
                var path = InstallManifestStore.ManagedFilePath(root, original, sourceId);
                var mapped = MapAbsolute(path);
                var local = Path.GetRelativePath(target, mapped).Replace('\\', '/');
                if (local == sourceId + ".json") local = targetId + ".json";
                else if (local == sourceId + ".jar") local = targetId + ".jar";
                else if (local.StartsWith(sourceId + "-natives/", StringComparison.Ordinal))
                    local = targetId + "-natives/" + local[(sourceId.Length + "-natives/".Length)..];
                if (path.StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
                    mapped = FileUtil.SafeChildPath(target, local);
                file["RelativePath"] = Path.GetRelativePath(root, mapped).Replace('\\', '/');
                if (!metadata.TryGetValue(local, out var changed)) continue;
                file["Sha1"] = Convert.ToHexStringLower(SHA1.HashData(changed));
                file["Url"] = null;
                file["ArchivePath"] = null;
                file["ArchiveId"] = null;
            }
            manifest["LastModifiedUtc"] = JsonValue.Create(DateTimeOffset.UtcNow);
            metadata[relative] = Bytes(manifest);
        }
        return metadata;

        string MapAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return path;
            var full = Path.GetFullPath(path);
            foreach (var (oldRoot, newRoot) in new[] { (source, target), (sourceLibraries, targetLibraries) })
            {
                if (full.Equals(oldRoot, PathComparison)) return newRoot;
                if (full.StartsWith(oldRoot + Path.DirectorySeparatorChar, PathComparison))
                    return FileUtil.SafeChildPath(newRoot, Path.GetRelativePath(oldRoot, full));
            }
            return path;
        }

        void RewriteLibrary(JsonNode? download)
        {
            if (download?["path"] is not JsonValue value || !value.TryGetValue<string>(out var path)) return;
            var prefix = "wmcl-packs/" + sourceId + "/";
            if (!path.Replace('\\', '/').StartsWith(prefix, PathComparison)) return;
            var full = FileUtil.SafeChildPath(Path.Combine(root, "libraries"), path);
            EnsureRegularPath(root, full);
            download["path"] = Path.GetRelativePath(Path.Combine(root, "libraries"), MapAbsolute(full)).Replace('\\', '/');
        }
    }

    internal static void CleanUnusedLibraries(string gameRoot, string id)
    {
        var root = Path.GetFullPath(gameRoot);
        var libraries = LibraryDirectory(root, id);
        if (!Directory.Exists(libraries)) return;
        try
        {
            EnsureRegularPath(root, libraries);
            EnsureRegularTree(libraries);
            var prefix = "wmcl-packs/" + id + "/";
            foreach (var directory in Directory.EnumerateFileSystemEntries(Path.Combine(root, "versions")))
            {
                EnsureRegularPath(root, directory);
                if (!Directory.Exists(directory)) continue;
                foreach (var relative in new[] { Path.GetFileName(directory) + ".json", "WMCL/install.json", "PCL/install.json" })
                {
                    var path = Path.Combine(directory, relative);
                    if (!File.Exists(path)) continue;
                    EnsureRegularPath(root, path);
                    if (ContainsReference(JsonNode.Parse(File.ReadAllText(path)), prefix)) return;
                }
            }
            Directory.Delete(libraries, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("保留无法确认未使用的实例库：" + libraries + "（" + ex.Message + "）");
        }
    }

    private static bool ContainsReference(JsonNode? node, string prefix) => node switch
    {
        JsonValue value => value.TryGetValue<string>(out var text) && text.Replace('\\', '/').Contains(prefix, PathComparison),
        JsonObject properties => properties.Any(property => ContainsReference(property.Value, prefix)),
        JsonArray items => items.Any(item => ContainsReference(item, prefix)),
        _ => false
    };

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("实例元数据不是对象：" + path);

    private static byte[] Bytes(JsonObject node) => Encoding.UTF8.GetBytes(node.ToJsonString(JsonOptions));

    private static bool TargetExists(string source, string target) => Path.Exists(target) &&
        !(source.Equals(target, StringComparison.OrdinalIgnoreCase) &&
          !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(target)!)
              .Any(path => Path.GetFileName(path).Equals(Path.GetFileName(target), StringComparison.Ordinal)));

    private static void EnsureRegularPath(string root, string path)
    {
        for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
            if (new FileInfo(current).LinkTarget is not null)
                throw new InvalidDataException("实例迁移路径包含符号链接：" + path);
    }

    private static void EnsureRegularTree(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (new FileInfo(path).LinkTarget is not null)
                throw new InvalidDataException("实例迁移不支持符号链接：" + path);
            if (Directory.Exists(path)) EnsureRegularTree(path);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if (new FileInfo(path).LinkTarget is not null)
                throw new InvalidDataException("实例复制路径包含符号链接：" + path);
            var destination = Path.Combine(target, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyDirectory(path, destination);
            else File.Copy(path, destination);
        }
    }
}
