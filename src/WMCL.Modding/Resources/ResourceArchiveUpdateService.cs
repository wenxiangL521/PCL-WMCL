using System.Collections.Immutable;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Modding.Modrinth;

namespace WMCL.Modding.Resources;

/// <summary>Hash-identifies resource archives and reuses the verified replacement transaction used by Mod updates.</summary>
public sealed class ResourceArchiveUpdateService(ModrinthClient client, Action<string>? recycleBackup = null)
{
    public async Task<ModUpdateCheckResult> CheckAsync(string directory, string gameVersion, string resourceType,
        CancellationToken ct = default)
    {
        ValidateType(resourceType);
        var updates = new List<ModUpdateInfo>();
        var states = new List<ModUpdateCheckItem>();
        if (!Directory.Exists(directory)) return new(updates, states);
        foreach (var path in Directory.EnumerateFiles(directory).Where(path =>
                     path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ValidateFile(directory, path);
                var hash = await FileUtil.Sha1Async(path, ct).ConfigureAwait(false);
                var current = await client.GetVersionByHashAsync(hash, ct).ConfigureAwait(false);
                if (current is null) { states.Add(new(path, ModUpdateCheckState.Unknown, "当前来源无法识别，不会覆盖文件。")); continue; }
                var latest = await client.FindCompatibleVersionAsync(current.ProjectId, gameVersion, "", ct, resourceType).ConfigureAwait(false);
                if (latest is null) { states.Add(new(path, ModUpdateCheckState.NoCompatibleVersion)); continue; }
                if (latest.ProjectId != current.ProjectId || string.IsNullOrWhiteSpace(latest.Id))
                    throw new InvalidDataException("更新项目与已安装资源不一致。");
                if (latest.Id == current.Id || latest.DatePublished < current.DatePublished)
                { states.Add(new(path, ModUpdateCheckState.UpToDate)); continue; }
                updates.Add(new(path, current.VersionNumber, latest, hash, current.ProjectId));
                states.Add(new(path, ModUpdateCheckState.UpdateAvailable));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { states.Add(new(path, ModUpdateCheckState.Failed, exception.Message)); }
        }
        return new(updates, states);
    }

    public async Task<ResourceInstallResult> ApplyAsync(ModUpdateInfo update, string directory, string gameVersion,
        string resourceType, CancellationToken ct = default)
    {
        ValidateType(resourceType);
        ValidateFile(directory, update.FilePath);
        if (update.OriginalSha1 is not { Length: 40 } hash || !hash.All(char.IsAsciiHexDigit) ||
            string.IsNullOrWhiteSpace(update.ProjectId) || update.ProjectId != update.Latest.ProjectId)
            throw new InvalidDataException("更新快照无效，请重新检查。");
        if (!string.Equals(await FileUtil.Sha1Async(update.FilePath, ct).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("资源在检查后已改变，请重新检查更新。");
        var plan = await new ResourceInstallPlanner(client, null).ResolveAsync(
            [new ResourceInstallRequest("Modrinth", update.ProjectId, update.Latest.Name,
                ResourceType: resourceType, VersionId: update.Latest.Id, Modrinth: update.Latest)], gameVersion, "", ct).ConfigureAwait(false);
        // Keep the original archive name: options.txt and disabled suffixes reference it.
        var entries = plan.Entries.Select(entry => entry with { FileName = Path.GetFileName(update.FilePath) }).ToImmutableArray();
        var prepared = plan with { Entries = entries };
        return await client.InstallUpdatePlanAsync(prepared, directory,
            [new ResourceInstallReplacement(update.FilePath, hash)], null, ct,
            recycleBackup ?? RecycleBin.MoveDirectory).ConfigureAwait(false);
    }

    private static void ValidateType(string type)
    {
        if (type is not ("resourcepack" or "shader" or "datapack"))
            throw new InvalidOperationException("存档及未知资源不会原地替换，以保护游玩数据。");
    }
    private static void ValidateFile(string directory, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Path.GetDirectoryName(Path.GetFullPath(path)) != root || !File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new IOException("资源不是目标目录内的普通归档文件。");
    }
}
