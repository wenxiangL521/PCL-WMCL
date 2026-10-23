using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using WMCL.Core.Http;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;

namespace WMCL.Modding.Modpack;

public sealed record McbbsUpdateResult(string? Version, int Downloaded, int Deleted, IReadOnlyList<string> Preserved);

public sealed class McbbsModpackUpdater(
    VersionRepository repository, CurseForgeClient? curseForge = null,
    DownloadManager? downloads = null, HttpClient? http = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CurseForgeClient _curseForge = curseForge ?? new CurseForgeClient();
    private readonly DownloadManager _downloads = downloads ?? new DownloadManager(http);

    private sealed record Mutation(string Path, string? StagedPath, string? OriginalHash);

    public async Task<McbbsUpdateResult> UpdateAsync(
        string versionId, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
        => (await ExecuteAsync(versionId, false, progress, ct).ConfigureAwait(false))!;

    public Task<McbbsUpdateResult?> PrepareLaunchAsync(
        string versionId, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
        => ExecuteAsync(versionId, true, progress, ct);

    private async Task<McbbsUpdateResult?> ExecuteAsync(
        string versionId, bool requiredOnly, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        InstanceManager.ValidateName(versionId);
        var gameRoot = Path.GetFullPath(repository.GameDirectory);
        var directory = FileUtil.SafeChildPath(gameRoot, "versions/" + versionId);
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await UpdateCoreAsync(gameRoot, directory, versionId, requiredOnly, progress, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<McbbsUpdateResult?> UpdateCoreAsync(
        string gameRoot, string directory, string id, bool requiredOnly,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureRegularPath(directory, "WMCL/install.json");
        var manifestPath = InstallManifestStore.GetPath(gameRoot, id);
        var originalManifestHash = await ExistingHashAsync(manifestPath, ct).ConfigureAwait(false);
        var install = InstallManifestStore.Load(gameRoot, id);
        if (install is null && requiredOnly) return null;
        if (install is null) throw new InvalidOperationException("没有安装清单，无法更新整合包。");
        if (install.ModpackKind != "MCBBS" && install.Components.GetValueOrDefault("local-pack") != "Mcbbs")
        {
            if (requiredOnly) return null;
            throw new InvalidOperationException("所选实例不是 MCBBS 整合包。");
        }
        var baseline = ReadBaseline(install, directory, id);
        if (requiredOnly && !baseline.ForceUpdate) return null;
        if (string.IsNullOrWhiteSpace(baseline.FileApi))
            throw new InvalidOperationException("该整合包没有 fileApi，无法检查远程更新。");

        progress?.Report(new JobProgress("检查整合包更新", 0.02));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        string json;
        try
        {
            json = await (http ?? Http.Client).GetStringAsync(
                baseline.FileApi.TrimEnd('/') + "/manifest.json", timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("检查整合包更新超时，请检查网络后重试。");
        }
        var remote = McbbsManifestDocument.Parse(json, directory, id);
        if (remote.ManifestType != "minecraftModpack" || remote.ManifestVersion != 1 ||
            !remote.Root.TryGetProperty("files", out _))
            throw new InvalidDataException("远程 MCBBS manifest 缺少格式或 files 声明。");
        ValidateContentUpdate(baseline, remote);
        var fileApi = string.IsNullOrWhiteSpace(remote.FileApi) ? baseline.FileApi : remote.FileApi;

        var oldFiles = new List<McbbsManifestFile>();
        foreach (var file in baseline.Files)
            oldFiles.Add(file.Type == "curse" && file.TargetPath is null
                ? await McbbsManifestDocument.ResolveCurseAsync(file, _curseForge, ct).ConfigureAwait(false)
                : file);
        baseline = baseline.WithFiles(oldFiles, directory, id);
        var oldById = baseline.Files.ToDictionary(file => file.Identity, StringComparer.OrdinalIgnoreCase);
        var oldByPath = baseline.Files.ToDictionary(file => file.TargetPath!, StringComparer.OrdinalIgnoreCase);
        var managed = install.ManagedFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var packHashes = new Dictionary<string, string>(install.ModpackFileHashes, StringComparer.OrdinalIgnoreCase);
        string Relative(string path) => "versions/" + id + "/" + path;
        string? Expected(McbbsManifestFile file)
        {
            var hash = file.Hash ?? packHashes.GetValueOrDefault(file.TargetPath!) ??
                       managed.GetValueOrDefault(Relative(file.TargetPath!))?.Sha1;
            if (hash is not null && (hash.Length != 40 || !hash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("整合包基准 SHA-1 无效：" + file.TargetPath);
            return hash;
        }

        var resolved = new List<McbbsManifestFile>();
        foreach (var declaration in remote.Files)
        {
            var file = declaration;
            if (file.Type == "curse")
            {
                if (oldById.TryGetValue(file.Identity, out var old))
                    file = file with
                    {
                        FileName = string.IsNullOrWhiteSpace(old.FileName) ? file.FileName : old.FileName,
                        Url = string.IsNullOrWhiteSpace(old.Url) ? file.Url : old.Url,
                        ResolvedHash = Expected(old)
                    };
                if (file.TargetPath is null || string.IsNullOrWhiteSpace(file.Url) &&
                    !File.Exists(FileUtil.SafeChildPath(directory, file.TargetPath)))
                    file = await McbbsManifestDocument.ResolveCurseAsync(file, _curseForge, ct).ConfigureAwait(false);
            }
            resolved.Add(file);
        }
        remote = remote.WithFiles(resolved, directory, id, fileApi);
        var targets = resolved.Select(file => file.TargetPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touched = oldByPath.Keys.Concat(targets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in touched)
        {
            ModpackContentPaths.Validate(path, directory, id);
            if (path.Split('/')[0].Equals("WMCL", StringComparison.OrdinalIgnoreCase) ||
                path.Split('/')[0].Equals("PCL", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("整合包更新不能覆盖启动器目录：" + path);
            EnsureRegularPath(directory, path);
        }

        var stagedDirectory = Path.Combine(directory, "WMCL", "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagedDirectory);
        var retainStaging = false;
        try
        {
            var changes = new List<Mutation>();
            var preserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nextManaged = install.ManagedFiles.Where(file =>
                    !touched.Contains(Path.GetRelativePath(directory, FileUtil.SafeChildPath(gameRoot, file.RelativePath)).Replace('\\', '/')))
                .ToList();
            var nextHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var downloaded = 0;
            foreach (var file in resolved)
            {
                ct.ThrowIfCancellationRequested();
                var path = file.TargetPath!;
                if (oldByPath.TryGetValue(path, out var oldAtPath) && oldAtPath.TargetPath != path)
                    throw new InvalidDataException("整合包更新不能只更改路径大小写：" + path);
                var destination = FileUtil.SafeChildPath(directory, path);
                var actual = await ExistingHashAsync(destination, ct).ConfigureAwait(false);
                var oldHash = oldAtPath is null ? null : Expected(oldAtPath);
                var newHash = file.Hash ?? file.ResolvedHash;
                var url = file.Type == "addon" ? McbbsPackContent.AddonUrl(fileApi!, path) : file.Url;
                if (newHash is not null) nextHashes[path] = newHash;
                if (actual is not null && !SameHash(actual, newHash) && !file.Force && !SameHash(actual, oldHash))
                {
                    preserved.Add(path);
                    continue;
                }
                string? archivePath = null;
                if (actual is null || !SameHash(actual, newHash))
                {
                    if (string.IsNullOrWhiteSpace(url))
                        throw new InvalidDataException("整合包文件没有可用下载地址：" + path);
                    var staged = Path.Combine(stagedDirectory, downloaded.ToString());
                    var fileProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                        "下载整合包更新", 0.1 + (downloaded + (update.Percent ?? 0)) / Math.Max(1, resolved.Count) * 0.75,
                        null, null, path)));
                    await _downloads.DownloadAsync([new DownloadItem
                    {
                        Destination = staged, Urls = [url], Sha1 = newHash, Size = file.ResolvedSize
                    }], new DownloadOptions { VerifyExisting = true }, fileProgress, ct).ConfigureAwait(false);
                    if (file.ResolvedSize is { } size && new FileInfo(staged).Length != size)
                        throw new InvalidDataException("整合包更新文件大小不一致：" + path);
                    newHash = await FileUtil.Sha1Async(staged, ct).ConfigureAwait(false);
                    nextHashes[path] = newHash;
                    changes.Add(new Mutation(path, staged, actual));
                    downloaded++;
                }
                else if (url is null && managed.TryGetValue(Relative(path), out var previous))
                {
                    archivePath = previous.ArchivePath ??
                        (previous.Component == "overrides" && install.OverridesPrefix is not null
                            ? install.OverridesPrefix.TrimEnd('/') + "/" + path
                            : null);
                }
                nextManaged.Add(new InstallManifestFile(Relative(path), newHash!, "MCBBS", url, archivePath));
            }

            var deleted = 0;
            foreach (var old in baseline.Files.Where(file => !targets.Contains(file.TargetPath!)))
            {
                var path = old.TargetPath!;
                var actual = await ExistingHashAsync(FileUtil.SafeChildPath(directory, path), ct).ConfigureAwait(false);
                if (actual is null) continue;
                if (!SameHash(actual, Expected(old)))
                {
                    preserved.Add(path);
                    continue;
                }
                changes.Add(new Mutation(path, null, actual));
                deleted++;
            }

            install.ModpackKind = "MCBBS";
            install.ModpackManifestJson = remote.RawJson;
            install.ModpackFileHashes = nextHashes;
            install.ManagedFiles = nextManaged;
            install.OverridesExcludedPaths = install.OverridesExcludedPaths.Concat(touched)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            install.LastModifiedUtc = DateTimeOffset.UtcNow;
            var stagedManifest = Path.Combine(stagedDirectory, "install.json");
            await File.WriteAllTextAsync(stagedManifest, JsonSerializer.Serialize(install,
                new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
            changes.Add(new Mutation("WMCL/install.json", stagedManifest, originalManifestHash));
            progress?.Report(new JobProgress("应用整合包更新", 0.9));
            await CommitAsync(directory, stagedDirectory, changes, progress, ct,
                () => retainStaging = true).ConfigureAwait(false);
            progress?.Report(new JobProgress("整合包内容更新完成", 1));
            return new McbbsUpdateResult(remote.Version, downloaded, deleted, preserved.Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            if (!retainStaging && Directory.Exists(stagedDirectory)) Directory.Delete(stagedDirectory, true);
        }
    }

    private static McbbsManifestDocument ReadBaseline(InstallManifest install, string directory, string id)
    {
        if (!string.IsNullOrWhiteSpace(install.ModpackManifestJson))
            return McbbsManifestDocument.Parse(install.ModpackManifestJson, directory, id);
        if (!File.Exists(install.SourceArchive))
            throw new FileNotFoundException("旧清单需要原始整合包才能建立更新基准。", install.SourceArchive);
        using var archive = ZipFile.OpenRead(install.SourceArchive);
        using var reader = new StreamReader(LocalModpackInstaller.MetadataEntry(archive, LocalModpackKind.Mcbbs).Open());
        return McbbsManifestDocument.Parse(reader.ReadToEnd(), directory, id);
    }

    private static void ValidateContentUpdate(McbbsManifestDocument baseline, McbbsManifestDocument remote)
    {
        var old = LocalModpackInstaller.ParseMcbbs(baseline.Root);
        var next = LocalModpackInstaller.ParseMcbbs(remote.Root);
        if ((old.Minecraft, old.FabricLoader, old.QuiltLoader, old.Forge, old.NeoForge) !=
            (next.Minecraft, next.FabricLoader, next.QuiltLoader, next.Forge, next.NeoForge))
            throw new NotSupportedException("该更新更改了游戏或加载器版本，请作为新实例导入整合包。");
        foreach (var field in new[] { "libraries", "launchInfo", "settings", "serverInfo", "sandbox", "antiCheating" })
        {
            baseline.Root.TryGetProperty(field, out var oldValue);
            remote.Root.TryGetProperty(field, out var newValue);
            if (oldValue.ValueKind == JsonValueKind.Undefined && newValue.ValueKind == JsonValueKind.Undefined) continue;
            if (oldValue.ValueKind == JsonValueKind.Undefined || newValue.ValueKind == JsonValueKind.Undefined ||
                !JsonElement.DeepEquals(oldValue, newValue))
                throw new NotSupportedException("该内容更新更改了 " + field + "，请作为新实例导入整合包。");
        }
    }

    private static void EnsureRegularPath(string directory, string relative)
    {
        var destination = FileUtil.SafeChildPath(directory, relative);
        var current = Path.GetPathRoot(destination)!;
        foreach (var part in destination[current.Length..].Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget is not null)
                throw new InvalidDataException("整合包更新路径包含符号链接：" + relative);
            if (current != destination && File.Exists(current) || current == destination && Directory.Exists(current))
                throw new InvalidDataException("整合包更新存在文件与目录冲突：" + relative);
        }
    }

    private static bool SameHash(string? first, string? second) =>
        first is not null && second is not null && first.Equals(second, StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ExistingHashAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await FileUtil.Sha1Async(path, ct).ConfigureAwait(false) : null;

    private static async Task CommitAsync(
        string directory, string stagedDirectory, IReadOnlyList<Mutation> changes,
        IProgress<JobProgress>? progress, CancellationToken ct, Action retainStaging)
    {
        foreach (var change in changes)
        {
            EnsureRegularPath(directory, change.Path);
            var hash = await ExistingHashAsync(FileUtil.SafeChildPath(directory, change.Path), ct).ConfigureAwait(false);
            if (!string.Equals(hash, change.OriginalHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("更新期间文件发生变化，已取消应用：" + change.Path);
        }
        var applied = new List<(string Destination, string Backup, bool HadOriginal)>();
        try
        {
            foreach (var change in changes)
            {
                ct.ThrowIfCancellationRequested();
                EnsureRegularPath(directory, change.Path);
                var destination = FileUtil.SafeChildPath(directory, change.Path);
                var hash = await ExistingHashAsync(destination, ct).ConfigureAwait(false);
                if (!string.Equals(hash, change.OriginalHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("应用期间文件发生变化，已取消更新：" + change.Path);
                var backup = Path.Combine(stagedDirectory, "backup-" + applied.Count);
                var exists = File.Exists(destination);
                if (exists) File.Move(destination, backup);
                applied.Add((destination, backup, exists));
                if (change.StagedPath is not null) FileUtil.MoveReplace(change.StagedPath, destination);
                progress?.Report(new JobProgress("应用整合包更新", 0.9 + applied.Count / (double)changes.Count * 0.09,
                    null, null, change.Path));
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var entry in applied.AsEnumerable().Reverse())
            {
                try
                {
                    FileUtil.DeleteIfExists(entry.Destination);
                    if (entry.HadOriginal) FileUtil.MoveReplace(entry.Backup, entry.Destination);
                }
                catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
            }
            if (errors.Count == 1) throw;
            retainStaging();
            throw new AggregateException("更新失败且部分文件无法回滚，备份保留于：" + stagedDirectory, errors);
        }
    }

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
