using System.Security.Cryptography;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Modding.Local;

namespace PCL.Modding.Modrinth;

public sealed record ModUpdateInfo(
    string FilePath,
    string CurrentVersion,
    ModrinthVersion Latest);

/// <summary>按本地 jar 的 SHA1 反查 Modrinth 版本，再比较同项目最新兼容版本。</summary>
public sealed class ModUpdateService(ModrinthClient client)
{
    public async Task<IReadOnlyList<ModUpdateInfo>> CheckAsync(
        string modsDirectory,
        string gameVersion,
        string loader,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var mods = LocalModService.List(modsDirectory)
            .Where(mod => mod.IsEnabled)
            .ToList();
        var updates = new List<ModUpdateInfo>();

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
                if (current is null) continue;

                var latest = await client.FindCompatibleVersionAsync(
                    current.ProjectId, gameVersion, loader, ct).ConfigureAwait(false);
                if (latest is null || string.Equals(latest.Id, current.Id, StringComparison.Ordinal))
                    continue;

                updates.Add(new ModUpdateInfo(mod.FilePath, current.VersionNumber, latest));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"检查 {mod.DisplayName} 更新失败：{ex.Message}");
            }
        }

        return updates;
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
