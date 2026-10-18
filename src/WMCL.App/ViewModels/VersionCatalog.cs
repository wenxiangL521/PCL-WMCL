using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public sealed class VersionCatalog(VersionRepository repository)
{
    public async Task<IReadOnlyList<VersionItem>> LoadAsync(
        int maxRemote = 120,
        CancellationToken ct = default)
    {
        var installed = repository.ListInstalled();
        var manifest = await repository.GetManifestAsync(ct: ct);
        var installedIds = installed.Select(v => v.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var latestRelease = manifest.Latest.Release;
        var latestSnapshot = manifest.Latest.Snapshot;
        var result = new List<VersionItem>();

        foreach (var version in installed.OrderByDescending(v => v.ReleaseTime ?? ""))
        {
            var category = "vanilla";
            try
            {
                var json = await repository.GetVersionAsync(version.Id, ct).ConfigureAwait(false);
                category = MinecraftVersionUtil.GetLoader(json);
            }
            catch
            {
                // 损坏的实例仍显示，分类退回原版
            }
            result.Add(new VersionItem
            {
                Id = version.Id,
                Type = version.Type ?? "release",
                ReleaseTime = version.ReleaseTime,
                IsInstalled = true,
                IsLatest = version.Id == latestRelease || version.Id == latestSnapshot,
                Category = category
            });
        }

        foreach (var version in manifest.Versions.Take(maxRemote))
        {
            if (installedIds.Contains(version.Id)) continue;
            result.Add(new VersionItem
            {
                Id = version.Id,
                Type = version.Type,
                ReleaseTime = version.ReleaseTime,
                IsLatest = version.Id == latestRelease || version.Id == latestSnapshot
            });
        }

        return result;
    }
}
