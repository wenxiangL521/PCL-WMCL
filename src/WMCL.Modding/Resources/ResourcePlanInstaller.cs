using System.Collections.Concurrent;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;

namespace WMCL.Modding.Resources;

public sealed record ResourceInstallTarget(ResourceInstallEntry Entry, string Directory);
public sealed record ResourceInstallResult(IReadOnlyList<string> Files, IReadOnlyList<string> ResolvedFiles)
{
    public string? RecoveryDirectory { get; init; }
    public string? RecoveryWarning { get; init; }
}
public sealed record ResourceInstallReplacement(string Path, string Sha1);

public sealed class ResourcePlanInstaller(DownloadManager downloads)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);

    public Task<ResourceInstallResult> InstallModsAsync(ResourceInstallPlan plan, string directory,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (plan.Entries.Any(entry => entry.ResourceType != "mod"))
            throw new InvalidOperationException("Mod 安装计划包含其他资源类型。");
        return InstallAsync(plan.Entries.Select(entry => new ResourceInstallTarget(entry, directory)).ToArray(), progress, ct);
    }

    public async Task<ResourceInstallResult> InstallAsync(IReadOnlyList<ResourceInstallTarget> targets,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default,
        Func<CancellationToken, Task>? validateTarget = null,
        IReadOnlyList<ResourceInstallReplacement>? replacements = null,
        Action<string>? recycleBackup = null)
    {
        var files = new Dictionary<string, ResourceInstallEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (target.Entry.FileName != FileUtil.SafeFileName(target.Entry.FileName) || target.Entry.FileName.Contains(':'))
                throw new InvalidDataException("资源文件名无效：" + target.Entry.FileName);
            var path = FileUtil.SafeChildPath(target.Directory, target.Entry.FileName);
            if (files.TryGetValue(path, out var previous))
            {
                if (previous.Size != target.Entry.Size || previous.Sha1 is null || target.Entry.Sha1 is null ||
                    !previous.Sha1.Equals(target.Entry.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("安装目标文件名冲突：" + path);
            }
            else files.Add(path, target.Entry);
        }
        var oldFiles = new Dictionary<string, ResourceInstallReplacement>(StringComparer.OrdinalIgnoreCase);
        foreach (var replacement in replacements ?? [])
        {
            var path = Path.GetFullPath(replacement.Path);
            if (!files.Keys.Any(destination => string.Equals(Path.GetDirectoryName(destination), Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase)) ||
                replacement.Sha1.Length != 40 || !replacement.Sha1.All(char.IsAsciiHexDigit) || !oldFiles.TryAdd(path, replacement))
                throw new InvalidOperationException("资源替换快照无效：" + path);
        }
        var locks = new List<SemaphoreSlim>();
        var staging = oldFiles.Count == 0
            ? Path.Combine(Path.GetTempPath(), "wmcl-resource-plan-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(Path.GetDirectoryName(oldFiles.Keys.First())!, ".wmcl-resource-update-" + Guid.NewGuid().ToString("N"));
        var created = new List<(string Path, string Hash)>();
        var reused = new List<(string Path, ResourceInstallEntry Entry)>();
        var backups = new List<(string Path, string Backup)>();
        var preserveStaging = false;
        var backupDirectory = Path.Combine(staging, "WMCL Mod 更新备份 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
        try
        {
            foreach (var directory in files.Keys.Select(path => Path.GetDirectoryName(path)!).Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                var gate = DirectoryLocks.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(ct).ConfigureAwait(false);
                locks.Add(gate);
            }
            var pending = new List<(string Destination, string Staged, ResourceInstallEntry Entry)>();
            foreach (var (path, entry) in files)
            {
                ct.ThrowIfCancellationRequested();
                CheckDestination(path);
                if (File.Exists(path) && entry.Sha1 is not null && !oldFiles.ContainsKey(path))
                {
                    if (!await MatchesAsync(path, entry, ct).ConfigureAwait(false))
                        throw new IOException("同名 Mod/资源文件已存在且内容不同，请先处理该文件：" + path);
                    reused.Add((path, entry));
                    continue;
                }
                pending.Add((path, Path.Combine(staging, pending.Count.ToString(), entry.FileName), entry));
            }
            await ValidateOldFilesAsync().ConfigureAwait(false);
            var adapter = new ProgressAdapter(update => progress?.Report(new JobProgress(
                "下载 Mod / 资源及必需依赖", update.Percent, (long)update.BytesPerSecond, Detail: update.CurrentFile)));
            await downloads.DownloadAsync(pending.Select(item => new DownloadItem
            {
                Destination = item.Staged,
                Urls = item.Entry.Urls,
                Size = item.Entry.Size,
                Sha1 = item.Entry.Sha1
            }), new DownloadOptions { MaxConcurrency = 4, VerifyExisting = true }, adapter, ct).ConfigureAwait(false);
            foreach (var item in pending)
            {
                if (!await MatchesAsync(item.Staged, item.Entry, ct).ConfigureAwait(false))
                    throw new InvalidDataException("资源文件大小或哈希不匹配：" + item.Entry.FileName);
                CheckDestination(item.Destination);
                if (File.Exists(item.Destination) && !oldFiles.ContainsKey(item.Destination) &&
                    !string.Equals(await FileUtil.Sha1Async(item.Staged, ct).ConfigureAwait(false),
                        await FileUtil.Sha1Async(item.Destination, ct).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("同名 Mod/资源文件已存在且内容不同：" + item.Destination);
            }
            if (validateTarget is not null) await validateTarget(ct).ConfigureAwait(false);
            foreach (var item in reused)
            {
                CheckDestination(item.Path);
                if (!await MatchesAsync(item.Path, item.Entry, ct).ConfigureAwait(false))
                    throw new IOException("复用的资源文件在下载期间已变化：" + item.Path);
            }
            await ValidateOldFilesAsync().ConfigureAwait(false);
            foreach (var (path, _) in oldFiles)
            {
                ct.ThrowIfCancellationRequested();
                var backup = Path.Combine(backupDirectory, Path.GetFileName(path));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(path, backup, overwrite: false);
                backups.Add((path, backup));
            }
            foreach (var item in pending)
            {
                ct.ThrowIfCancellationRequested();
                CheckDestination(item.Destination);
                if (File.Exists(item.Destination))
                {
                    if (!string.Equals(await FileUtil.Sha1Async(item.Staged, ct).ConfigureAwait(false),
                            await FileUtil.Sha1Async(item.Destination, ct).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("安装期间目标文件发生变化：" + item.Destination);
                    continue;
                }
                var hash = await FileUtil.Sha1Async(item.Staged, ct).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                File.Move(item.Staged, item.Destination, overwrite: false);
                created.Add((item.Destination, hash));
                progress?.Report(new JobProgress("应用资源文件", (double)created.Count / pending.Count, Detail: item.Entry.FileName));
            }
            ct.ThrowIfCancellationRequested();
            var result = new ResourceInstallResult(created.Select(item => item.Path).ToArray(), files.Keys.ToArray());
            if (backups.Count > 0 && recycleBackup is not null)
            {
                try { recycleBackup(backupDirectory); }
                catch (Exception exception)
                {
                    preserveStaging = true;
                    return result with { RecoveryDirectory = backupDirectory, RecoveryWarning = "旧版 Mod 未能移入回收站：" + exception.Message };
                }
            }
            return result;
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var item in created.AsEnumerable().Reverse())
            {
                try
                {
                    CheckDestination(item.Path);
                    if (File.Exists(item.Path))
                    {
                        if (FileUtil.Sha1(item.Path) != item.Hash)
                            throw new IOException("回滚时资源文件已被修改：" + item.Path);
                        File.Delete(item.Path);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("回收资源文件失败：" + exception.Message);
                    if (oldFiles.Count > 0) rollbackErrors.Add(exception);
                }
            }
            foreach (var item in backups.AsEnumerable().Reverse())
            {
                try
                {
                    CheckDestination(item.Path);
                    File.Move(item.Backup, item.Path, overwrite: false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors.Add(exception);
                }
            }
            if (rollbackErrors.Count > 0)
            {
                preserveStaging = true;
                throw new AggregateException("资源更新失败且回滚未完成，原始备份保留于：" + staging,
                    new[] { failure }.Concat(rollbackErrors));
            }
            throw;
        }
        finally
        {
            foreach (var gate in locks.AsEnumerable().Reverse()) gate.Release();
            try { if (!preserveStaging && Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warn("回收资源暂存目录失败：" + exception.Message);
            }
        }

        async Task ValidateOldFilesAsync()
        {
            foreach (var (path, snapshot) in oldFiles)
            {
                CheckDestination(path);
                if (!File.Exists(path) || !string.Equals(await FileUtil.Sha1Async(path, ct).ConfigureAwait(false), snapshot.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("更新前原始资源已变化，请重新检查更新：" + path);
            }
        }
    }

    private static void CheckDestination(string path)
    {
        if (Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new IOException("资源目标是目录或符号链接：" + path);
    }

    private static async Task<bool> MatchesAsync(string path, ResourceInstallEntry entry, CancellationToken ct) =>
        File.Exists(path) && new FileInfo(path).Length == entry.Size &&
        (entry.Sha1 is null || string.Equals(await FileUtil.Sha1Async(path, ct).ConfigureAwait(false), entry.Sha1, StringComparison.OrdinalIgnoreCase));

    private sealed class ProgressAdapter(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
