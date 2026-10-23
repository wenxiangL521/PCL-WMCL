using System.Security.Cryptography;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Modding.Local;
using WMCL.Core.IO;
using WMCL.Modding.Resources;

namespace WMCL.Modding.Modrinth;

public sealed record ModUpdateInfo(
    string FilePath,
    string CurrentVersion,
    ModrinthVersion Latest,
    string? OriginalSha1 = null,
    string? ProjectId = null);

public enum ModUpdateCheckState { UpdateAvailable, UpToDate, Unknown, NoCompatibleVersion, Failed }

public sealed record ModUpdateCheckItem(string FilePath, ModUpdateCheckState State, string? Detail = null);

public sealed record ModUpdateCheckResult(IReadOnlyList<ModUpdateInfo> Updates, IReadOnlyList<ModUpdateCheckItem> Items)
{
    public string Summary => Items.Count == 0 ? "没有已启用的 Mod 可检查"
        : Items.All(item => item.State == ModUpdateCheckState.UpToDate) ? "所有已检查 Mod 均为最新"
        : string.Join("，", Enum.GetValues<ModUpdateCheckState>().Select(state =>
        {
            var count = Items.Count(item => item.State == state);
            var label = state switch
            {
                ModUpdateCheckState.UpdateAvailable => "可更新",
                ModUpdateCheckState.UpToDate => "已最新",
                ModUpdateCheckState.Unknown => "未识别",
                ModUpdateCheckState.NoCompatibleVersion => "无兼容版本",
                _ => "检查失败"
            };
            return count == 0 ? null : $"{label} {count} 项";
        }).Where(value => value is not null));
}

/// <summary>按本地 jar 的 SHA1 反查 Modrinth 版本，再比较同项目最新兼容版本。</summary>
public sealed class ModUpdateService(ModrinthClient client, Action<string>? recycleBackup = null)
{
    public async Task<IReadOnlyList<ModUpdateInfo>> CheckAsync(
        string modsDirectory,
        string gameVersion,
        string loader,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
        => (await CheckDetailedAsync(modsDirectory, gameVersion, loader, progress, ct).ConfigureAwait(false)).Updates;

    public async Task<ModUpdateCheckResult> CheckDetailedAsync(
        string modsDirectory, string gameVersion, string loader,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var mods = LocalModService.List(modsDirectory)
            .Where(mod => mod.IsEnabled)
            .ToList();
        var updates = new List<ModUpdateInfo>();
        var items = new List<ModUpdateCheckItem>();

        for (var index = 0; index < mods.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var mod = mods[index];
            progress?.Report(new JobProgress(
                $"检查 Mod 更新 {index + 1}/{mods.Count}",
                mods.Count == 0 ? 1 : (double)index / mods.Count,
                Detail: mod.DisplayName));

            try
            {
                var sha1 = await ComputeSha1Async(mod.FilePath, ct).ConfigureAwait(false);
                var current = await client.GetVersionByHashAsync(sha1, ct).ConfigureAwait(false);
                if (current is null)
                {
                    items.Add(new(mod.FilePath, ModUpdateCheckState.Unknown));
                    continue;
                }

                var latest = await client.FindCompatibleVersionAsync(
                    current.ProjectId, gameVersion, loader, ct).ConfigureAwait(false);
                if (latest is null)
                {
                    items.Add(new(mod.FilePath, ModUpdateCheckState.NoCompatibleVersion));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(latest.Id) || latest.ProjectId != current.ProjectId)
                    throw new InvalidDataException("更新版本不属于已安装的 Mod 项目。");
                if (string.Equals(latest.Id, current.Id, StringComparison.Ordinal) || latest.DatePublished < current.DatePublished)
                {
                    items.Add(new(mod.FilePath, ModUpdateCheckState.UpToDate));
                    continue;
                }

                updates.Add(new ModUpdateInfo(mod.FilePath, current.VersionNumber, latest, sha1, current.ProjectId));
                items.Add(new(mod.FilePath, ModUpdateCheckState.UpdateAvailable));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"检查 {mod.DisplayName} 更新失败：{ex.Message}");
                items.Add(new(mod.FilePath, ModUpdateCheckState.Failed, ex.Message));
            }
        }

        ct.ThrowIfCancellationRequested();
        return new(updates, items);
    }

    public async Task<ResourceInstallResult> ApplyAsync(IReadOnlyList<ModUpdateInfo> updates, string modsDirectory,
        string gameVersion, string loader, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (updates.Count == 0) throw new InvalidOperationException("没有选定的 Mod 更新。");
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modsDirectory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var replacements = new Dictionary<string, ResourceInstallReplacement>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var update in updates)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(update.FilePath)), directory, comparison) ||
                update.OriginalSha1 is not { Length: 40 } hash || !hash.All(char.IsAsciiHexDigit) ||
                string.IsNullOrWhiteSpace(update.ProjectId) || update.ProjectId != update.Latest.ProjectId)
                throw new InvalidOperationException("Mod 更新快照无效，请重新检查更新。");
            CheckFile(update.FilePath);
            if (!File.Exists(update.FilePath) || !string.Equals(await FileUtil.Sha1Async(update.FilePath, ct).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Mod 文件在检查后已变化，请重新检查更新：" + update.FilePath);
            replacements.Add(update.FilePath, new(update.FilePath, hash));
        }
        var plan = await new ResourceInstallPlanner(client, null).ResolveAsync(updates.Select(update =>
            new ResourceInstallRequest("Modrinth", update.ProjectId!, update.Latest.Name,
                VersionId: update.Latest.Id, Modrinth: update.Latest)).ToArray(), gameVersion, loader, ct).ConfigureAwait(false);
        var projects = plan.Entries.Select(entry => entry.ProjectId).ToHashSet(StringComparer.Ordinal);
        foreach (var mod in LocalModService.List(directory).Where(mod => mod.IsEnabled && !replacements.ContainsKey(mod.FilePath)))
        {
            CheckFile(mod.FilePath);
            var hash = await FileUtil.Sha1Async(mod.FilePath, ct).ConfigureAwait(false);
            var installed = await client.GetVersionByHashAsync(hash, ct).ConfigureAwait(false);
            if (installed is not null && projects.Contains(installed.ProjectId))
                replacements.Add(mod.FilePath, new(mod.FilePath, hash));
        }
        return await client.InstallUpdatePlanAsync(plan, directory, replacements.Values.ToArray(), progress, ct,
            recycleBackup ?? RecycleBin.MoveDirectory).ConfigureAwait(false);
    }

    private static void CheckFile(string path)
    {
        if (Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new IOException("Mod 文件是目录或符号链接：" + path);
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
