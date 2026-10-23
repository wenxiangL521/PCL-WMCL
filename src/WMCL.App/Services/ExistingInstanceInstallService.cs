using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.Services;

internal sealed class ExistingInstanceInstallService(
    string gameRoot, string isolationMode, Func<string, ICombinedInstallBackend> backendFactory)
{
    private static readonly string[] ComponentKeys =
        ["minecraft", "fabric", "quilt", "legacyfabric", "forge", "neoforge", "liteloader", "optifine", "fabric-api", "qsl", "legacy-fabric-api", "optifabric"];
    private static readonly HashSet<string> Addons = new(["optifine", "fabric-api", "qsl", "legacy-fabric-api", "optifabric"], StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Mutation(string Target, string? Source, string? OriginalHash);

    internal async Task<CombinedInstallResult> InstallAsync(CombinedInstallPlan plan,
        Action<CombinedInstallStep, CombinedInstallStepState, JobProgress> report, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(gameRoot);
        var id = plan.InstanceId;
        InstanceManager.ValidateName(id);
        var directory = FileUtil.SafeChildPath(root, "versions/" + id);
        var profile = CheckedPath("versions/" + id + "/" + id + ".json");
        var originalProfile = await File.ReadAllBytesAsync(profile, ct);
        var manifestPath = InstallManifestStore.GetPath(root, id);
        var legacyManifest = Path.Combine(directory, "PCL/install.json");
        if (!File.Exists(manifestPath) && File.Exists(legacyManifest)) manifestPath = legacyManifest;
        EnsureRegularPath(manifestPath);
        var originalManifest = File.Exists(manifestPath) ? await File.ReadAllBytesAsync(manifestPath, ct) : null;
        var settingsPath = InstanceSettingsStore.PathFor(directory);
        var legacySettings = Path.Combine(directory, "PCL/instance.json");
        if (!File.Exists(settingsPath) && File.Exists(legacySettings)) settingsPath = legacySettings;
        EnsureRegularPath(settingsPath);
        var originalSettings = File.Exists(settingsPath) ? await File.ReadAllBytesAsync(settingsPath, ct) : null;
        var manifest = originalManifest is null ? new JsonObject() :
            JsonNode.Parse(originalManifest) as JsonObject ?? throw new InvalidDataException("实例安装清单不是对象");
        InstallManifestStore.ValidateSchemaVersion(manifest["SchemaVersion"]?.GetValue<int>() ?? InstallManifestStore.SupportedSchemaVersion);
        var originalVersion = await new VersionRepository(root).GetVersionAsync(id, ct);
        var dataDirectory = IsolationPolicy.GetGameDirectory(root, id, originalVersion, isolationMode);
        var temporary = Path.Combine(root, ".wmcl-components-" + Guid.NewGuid().ToString("N"));
        var stageRoot = Path.Combine(temporary, "game");
        var mutations = new Dictionary<string, Mutation>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var undo = new Stack<Action>();
        var completed = false;
        var restored = false;
        var applying = false;
        try
        {
            Directory.CreateDirectory(stageRoot);
            var stagedPlan = plan with { VanillaInstalled = false };
            var result = await new CombinedInstallService(stageRoot, IsolationPolicy.Isolated, backendFactory(stageRoot))
                .InstallAsync(stagedPlan, report, ct);
            if (result.Errors.Count > 0)
                throw new InvalidOperationException("附加组件安装失败，原实例未更改：" + string.Join("；", result.Errors));
            var installed = InstallManifestStore.Load(stageRoot, id) ?? throw new InvalidDataException("缺少暂存安装清单");
            var managed = (manifest["ManagedFiles"]?.Deserialize<List<InstallManifestFile>>() ?? [])
                .Select(file => file with { RelativePath = file.RelativePath.Replace('\\', '/') }).ToList();
            foreach (var file in managed) CheckedPath(file.RelativePath);
            foreach (var file in managed.Where(file => file.Component is not null && Addons.Contains(file.Component)))
            {
                var target = CheckedPath(file.RelativePath);
                if (!target.StartsWith(Path.Combine(dataDirectory, "mods") + Path.DirectorySeparatorChar, PathComparison))
                    throw new InvalidDataException("附加组件清单不属于当前游戏目录：" + file.RelativePath);
                if (File.Exists(target) && !string.Equals(await FileUtil.Sha1Async(target, ct), file.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("已安装组件被修改，无法自动替换：" + file.RelativePath);
                await AddAsync(target, null);
            }
            var newManaged = new List<InstallManifestFile>();
            var prefix = "versions/" + id + "/mods/";
            foreach (var file in installed.ManagedFiles)
            {
                var source = InstallManifestStore.ManagedFilePath(stageRoot, file.RelativePath, id);
                var relative = file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
                    ? Path.GetRelativePath(root, FileUtil.SafeChildPath(Path.Combine(dataDirectory, "mods"), file.RelativePath[prefix.Length..])).Replace('\\', '/')
                    : file.RelativePath;
                var target = CheckedPath(relative);
                if (file.Component is not null && Addons.Contains(file.Component) && File.Exists(target) && !mutations.ContainsKey(target) &&
                    !string.Equals(await FileUtil.Sha1Async(target, ct), file.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("目标已存在同名的非受管组件文件，无法自动覆盖：" + relative);
                await AddAsync(target, source);
                newManaged.Add(file with { RelativePath = relative });
            }
            var nextPaths = newManaged.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
            var nativePrefix = "versions/" + id + "/natives/";
            foreach (var file in managed.Where(file => file.RelativePath.StartsWith(nativePrefix, StringComparison.Ordinal) && !nextPaths.Contains(file.RelativePath)))
            {
                var target = CheckedPath(file.RelativePath);
                if (File.Exists(target) && !string.Equals(await FileUtil.Sha1Async(target, ct), file.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("旧原生库已被修改，无法自动移除：" + file.RelativePath);
                await AddAsync(target, null);
            }
            foreach (var folder in new[] { "libraries", "assets" })
            {
                var shared = Path.Combine(stageRoot, folder);
                if (!Directory.Exists(shared)) continue;
                foreach (var source in Directory.EnumerateFiles(shared, "*", SearchOption.AllDirectories))
                {
                    EnsureRegularPath(source);
                    var relative = Path.GetRelativePath(stageRoot, source);
                    var target = FileUtil.SafeChildPath(root, relative);
                    EnsureRegularPath(target);
                    if (File.Exists(target))
                    {
                        if (await FileUtil.Sha1Async(target, ct) != await FileUtil.Sha1Async(source, ct))
                            throw new IOException("共享依赖已存在且内容不同：" + relative);
                    }
                    else await AddAsync(target, source);
                }
            }
            var components = manifest["Components"] as JsonObject ?? new JsonObject();
            foreach (var key in components.Select(pair => pair.Key).Where(key => ComponentKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray()) components.Remove(key);
            foreach (var pair in installed.Components) components[pair.Key] = pair.Value;
            manifest["Components"] = components;
            var metadata = manifest["ComponentMetadata"] as JsonObject ?? new JsonObject();
            foreach (var key in metadata.Select(pair => pair.Key).Where(key => ComponentKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray()) metadata.Remove(key);
            foreach (var pair in installed.ComponentMetadata) metadata[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
            manifest["ComponentMetadata"] = metadata;
            var replaced = newManaged.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
            managed.RemoveAll(file => replaced.Contains(file.RelativePath) || file.RelativePath.StartsWith(nativePrefix, StringComparison.Ordinal) ||
                (file.Component is not null && Addons.Contains(file.Component)));
            managed.AddRange(newManaged);
            manifest["LastModifiedUtc"] = JsonValue.Create(DateTimeOffset.UtcNow);

            var nextVersion = await new VersionRepository(stageRoot).GetVersionAsync(id, ct);
            var nextData = IsolationPolicy.GetGameDirectory(root, id, nextVersion, isolationMode);
            if (!nextData.Equals(dataDirectory, PathComparison))
            {
                var settings = originalSettings is not null ? JsonNode.Parse(originalSettings) as JsonObject : new JsonObject();
                if (settings is null) throw new InvalidDataException("实例设置不是对象");
                var key = settings.Select(pair => pair.Key).FirstOrDefault(key => key.Equals("IsolationMode", StringComparison.OrdinalIgnoreCase)) ?? "IsolationMode";
                settings[key] = dataDirectory.Equals(directory, PathComparison) ? IsolationPolicy.Isolated : IsolationPolicy.Global;
                await AddJsonAsync(settingsPath, settings, "settings.json");
                var relative = Path.GetRelativePath(root, settingsPath).Replace('\\', '/');
                var position = managed.FindIndex(file => file.RelativePath == relative);
                if (position >= 0) managed[position] = managed[position] with
                {
                    Sha1 = await FileUtil.Sha1Async(Path.Combine(temporary, "settings.json"), ct),
                    Url = null,
                    ArchivePath = null,
                    ArchiveId = null
                };
            }
            if (manifest["OverridesRoot"] is JsonValue overrideValue && overrideValue.TryGetValue<string>(out var overridesRoot) &&
                !string.IsNullOrWhiteSpace(overridesRoot))
            {
                var full = Path.GetFullPath(overridesRoot);
                if (!full.Equals(root, PathComparison) && !full.Equals(directory, PathComparison))
                    throw new InvalidDataException("覆写清单的目标不属于当前实例");
                var excluded = manifest["OverridesExcludedPaths"] as JsonArray ?? new JsonArray();
                foreach (var path in mutations.Keys.Append(manifestPath))
                {
                    var relative = Path.GetRelativePath(full, path).Replace('\\', '/');
                    if (!relative.StartsWith("../", StringComparison.Ordinal) && !excluded.Any(item => item?.GetValue<string>() == relative))
                        excluded.Add(relative);
                }
                manifest["OverridesExcludedPaths"] = excluded;
            }
            manifest["ManagedFiles"] = JsonSerializer.SerializeToNode(managed, JsonOptions);
            await AddJsonAsync(manifestPath, manifest, "manifest.json");

            applying = true;
            report(CombinedInstallStep.Apply, CombinedInstallStepState.Running, new JobProgress("应用安装更改", 0));
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(profile) || !originalProfile.AsSpan().SequenceEqual(File.ReadAllBytes(profile)) ||
                (originalManifest is null ? File.Exists(manifestPath) : !File.Exists(manifestPath) || !originalManifest.AsSpan().SequenceEqual(File.ReadAllBytes(manifestPath))))
                throw new IOException("安装期间实例身份或清单已变化，原实例未更改，请重新打开安装页");
            if (originalSettings is null ? File.Exists(settingsPath) : !File.Exists(settingsPath) || !originalSettings.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)))
                throw new IOException("安装期间实例设置已变化，原实例未更改，请重新打开安装页");
            foreach (var mutation in mutations.Values) await VerifyAsync(mutation);
            var backupDirectory = Path.Combine(directory, "WMCL/install-backups", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            EnsureRegularPath(backupDirectory);
            Directory.CreateDirectory(backupDirectory);
            undo.Push(() => Directory.Delete(backupDirectory, true));
            foreach (var filename in new[] { id + ".json", id + ".jar" })
                if (File.Exists(Path.Combine(directory, filename))) File.Copy(Path.Combine(directory, filename), Path.Combine(backupDirectory, filename));
            var index = 0;
            foreach (var mutation in mutations.Values.OrderBy(mutation => mutation.Target == profile ? 1 : mutation.Target == manifestPath ? 2 : 0))
            {
                ct.ThrowIfCancellationRequested();
                await VerifyAsync(mutation);
                var backup = Path.Combine(temporary, "backup-" + index++);
                var existed = File.Exists(mutation.Target);
                if (existed) File.Move(mutation.Target, backup);
                undo.Push(() =>
                {
                    if (File.Exists(mutation.Target)) File.Delete(mutation.Target);
                    if (existed) File.Move(backup, mutation.Target);
                });
                if (mutation.Source is null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(mutation.Target)!);
                File.Move(mutation.Source, mutation.Target);
            }
            if (plan.RenameTo is { } renamed)
            {
                ct.ThrowIfCancellationRequested();
                result = result with { InstanceId = renamed };
                InstanceManager.Rename(root, id, renamed, ReportCompleted);
            }
            else ReportCompleted();
            completed = true;
            return result;
        }
        catch (Exception failure)
        {
            try
            {
                while (undo.TryPop(out var restore)) restore();
                restored = true;
            }
            catch (Exception rollback)
            {
                throw new AggregateException("安装更改失败且回滚未完成，备份保留于：" + temporary, failure, rollback);
            }
            if (applying)
            {
                try
                {
                    report(CombinedInstallStep.Apply,
                        failure is OperationCanceledException ? CombinedInstallStepState.Cancelled : CombinedInstallStepState.Failed,
                        new JobProgress("应用安装更改", Detail: failure.Message));
                }
                catch (Exception notification)
                {
                    throw new AggregateException("安装更改失败，原实例已回滚；报告进度失败", failure, notification);
                }
            }
            throw;
        }
        finally
        {
            if ((completed || restored) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }

        void ReportCompleted() =>
            report(CombinedInstallStep.Apply, CombinedInstallStepState.Completed, new JobProgress("应用安装更改", 1));

        async Task AddAsync(string target, string? source)
        {
            EnsureRegularPath(target);
            mutations[target] = new Mutation(target, source, File.Exists(target) ? await FileUtil.Sha1Async(target, ct) : null);
        }

        async Task AddJsonAsync(string target, JsonObject value, string filename)
        {
            var source = Path.Combine(temporary, filename);
            await File.WriteAllTextAsync(source, value.ToJsonString(JsonOptions), ct);
            await AddAsync(target, source);
        }

        async Task VerifyAsync(Mutation mutation)
        {
            EnsureRegularPath(mutation.Target);
            var hash = File.Exists(mutation.Target) ? await FileUtil.Sha1Async(mutation.Target, ct) : null;
            if (hash != mutation.OriginalHash) throw new IOException("安装期间目标文件已变化：" + mutation.Target);
        }

        string CheckedPath(string relative) => InstallManifestStore.ManagedFilePath(root, relative, id);

        void EnsureRegularPath(string path)
        {
            for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
                if (new FileInfo(current).LinkTarget is not null) throw new InvalidDataException("安装路径包含符号链接：" + path);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
