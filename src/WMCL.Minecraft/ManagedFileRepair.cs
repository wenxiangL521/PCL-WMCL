using System.IO.Compression;
using WMCL.Core.IO;
using WMCL.Download;

namespace WMCL.Minecraft;

public static class ManagedFileRepair
{
    private sealed record RepairFile(InstallManifestFile File, string Destination, bool PreserveExisting);

    public static async Task<int> RepairAsync(string gameRoot, string versionId,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default, DownloadManager? downloads = null)
    {
        ct.ThrowIfCancellationRequested();
        var manifest = InstallManifestStore.Load(gameRoot, versionId);
        if (manifest is null) return 0;
        var pending = new List<RepairFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localArchiveAvailable = File.Exists(manifest.SourceArchive);
        foreach (var file in manifest.ManagedFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file.Url) && string.IsNullOrWhiteSpace(file.ArchivePath) && file.ArchiveId is null) continue;
            var destination = InstallManifestStore.ManagedFilePath(gameRoot, file.RelativePath, versionId);
            if (file.Url is not null && !IsHttpUrl(file.Url) || !IsSha1(file.Sha1))
                throw new InvalidDataException("受管文件的下载地址或 SHA-1 无效：" + file.RelativePath);
            if (!destinations.Add(destination)) throw new InvalidDataException("受管文件路径重复：" + file.RelativePath);
            var preserveExisting = InstallManifestStore.IsSaveFile(file.RelativePath);
            if (File.Exists(destination) && (preserveExisting || string.Equals(
                    await FileUtil.Sha1Async(destination, ct).ConfigureAwait(false), file.Sha1, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (file.ArchivePath is not null) FileUtil.SafeChildPath(Path.GetTempPath(), file.ArchivePath);
            if (file.ArchiveId is not null)
            {
                if (string.IsNullOrWhiteSpace(file.ArchivePath) ||
                    !manifest.ManagedArchives.TryGetValue(file.ArchiveId, out var source) ||
                    source.Urls is null || source.Urls.Count == 0 || source.Urls.Any(url => !IsHttpUrl(url)) ||
                    source.Sha1 is not null && !IsSha1(source.Sha1) || source.Size is < 0)
                    throw new InvalidDataException("受管归档的来源无效：" + file.RelativePath);
            }
            else if ((!localArchiveAvailable || string.IsNullOrWhiteSpace(file.ArchivePath)) && string.IsNullOrWhiteSpace(file.Url))
                throw new FileNotFoundException("原始整合包已不存在，无法修复内嵌文件。", manifest.SourceArchive);
            pending.Add(new RepairFile(file, destination, preserveExisting));
        }
        if (pending.Count == 0) return 0;

        var temporary = Path.Combine(Path.GetTempPath(), "wmcl-managed-repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            var staged = pending.Select((file, index) => (Repair: file,
                Path: Path.Combine(temporary, "files", index.ToString(), Path.GetFileName(file.Destination)))).ToArray();
            var archivePaths = pending.Where(item => item.File.ArchiveId is not null)
                .Select(item => item.File.ArchiveId!).Distinct(StringComparer.Ordinal)
                .Select((id, index) => (Id: id, Path: Path.Combine(temporary, "archives", index + ".zip")))
                .ToDictionary(item => item.Id, item => item.Path, StringComparer.Ordinal);
            var items = archivePaths.Select(item => new DownloadItem
            {
                Destination = item.Value,
                Urls = manifest.ManagedArchives[item.Key].Urls,
                Sha1 = manifest.ManagedArchives[item.Key].Sha1,
                Size = manifest.ManagedArchives[item.Key].Size
            }).ToList();
            foreach (var item in staged.Where(item => item.Repair.File.ArchiveId is null &&
                         (!localArchiveAvailable || string.IsNullOrWhiteSpace(item.Repair.File.ArchivePath))))
                items.Add(new DownloadItem
                {
                    Destination = item.Path,
                    Urls = [item.Repair.File.Url!],
                    Sha1 = item.Repair.File.Sha1
                });
            if (items.Count > 0)
                await (downloads ?? new DownloadManager()).DownloadAsync(items,
                    new DownloadOptions { MaxConcurrency = 4, VerifyExisting = true }, progress, ct).ConfigureAwait(false);
            foreach (var (id, path) in archivePaths)
                if (manifest.ManagedArchives[id].Size is { } size && new FileInfo(path).Length != size)
                    throw new InvalidDataException("受管归档大小与元数据不一致：" + id);

            foreach (var group in staged.Where(item => item.Repair.File.ArchiveId is not null ||
                         localArchiveAvailable && !string.IsNullOrWhiteSpace(item.Repair.File.ArchivePath))
                         .GroupBy(item => item.Repair.File.ArchiveId))
            {
                using var archive = ZipFile.OpenRead(group.Key is null ? manifest.SourceArchive! : archivePaths[group.Key]);
                var entries = archive.Entries.ToLookup(entry => entry.FullName, StringComparer.Ordinal);
                foreach (var item in group)
                {
                    ct.ThrowIfCancellationRequested();
                    var matches = entries[item.Repair.File.ArchivePath!].ToArray();
                    if (matches.Length != 1 || matches[0].FullName.EndsWith('/') || (matches[0].ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new InvalidDataException("受管归档的文件缺失、重复或无效：" + item.Repair.File.ArchivePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                    await using (var source = matches[0].Open())
                    await using (var target = new FileStream(item.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await source.CopyToAsync(target, ct).ConfigureAwait(false);
                    if (!string.Equals(await FileUtil.Sha1Async(item.Path, ct).ConfigureAwait(false),
                            item.Repair.File.Sha1, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("受管归档的文件哈希不一致：" + item.Repair.File.ArchivePath);
                }
            }

            var restored = 0;
            foreach (var item in staged)
            {
                ct.ThrowIfCancellationRequested();
                InstallManifestStore.ManagedFilePath(gameRoot, item.Repair.File.RelativePath, versionId);
                if (item.Repair.PreserveExisting && File.Exists(item.Repair.Destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(item.Repair.Destination)!);
                try { File.Move(item.Path, item.Repair.Destination, overwrite: !item.Repair.PreserveExisting); }
                catch (IOException) when (item.Repair.PreserveExisting && File.Exists(item.Repair.Destination)) { continue; }
                restored++;
            }
            return restored;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    private static bool IsSha1(string? hash) => hash is { Length: 40 } && hash.All(Uri.IsHexDigit);
}
