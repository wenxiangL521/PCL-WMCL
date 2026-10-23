using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Download;
using WMCL.Modding.CurseForge;

namespace WMCL.Modding.Modpack;

internal sealed record McbbsContentFile(McbbsManifestFile Declaration, DownloadItem? Download)
{
    public string Path => Declaration.TargetPath!;
}

internal sealed record McbbsContentPlan(McbbsManifestDocument Manifest, IReadOnlyList<McbbsContentFile> Files);

internal static class McbbsPackContent
{
    public static async Task<McbbsContentPlan> PlanAsync(
        ZipArchive archive, ZipArchiveEntry metadata, LocalModpackInfo info, string directory, string id,
        CurseForgeClient client, CancellationToken ct)
    {
        await using var metadataStream = metadata.Open();
        using var document = await JsonDocument.ParseAsync(metadataStream, cancellationToken: ct).ConfigureAwait(false);
        var manifest = McbbsManifestDocument.Parse(document.RootElement, document.RootElement.GetRawText(), directory, id);
        var overrides = archive.Entries.Where(entry => !entry.FullName.EndsWith('/') &&
                entry.FullName.StartsWith(info.OverridesFolder, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.FullName[info.OverridesFolder.Length..].Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var planned = new List<McbbsContentFile>();
        foreach (var declared in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var file = declared;
            DownloadItem? download = null;
            if (file.Type == "addon")
            {
                var bundled = FindOverride(overrides, file.Path!, info.OverridesFolder);
                var matches = false;
                if (bundled is not null)
                {
                    await using var source = bundled.Open();
                    var hash = Convert.ToHexStringLower(await SHA1.HashDataAsync(source, ct).ConfigureAwait(false));
                    matches = hash.Equals(file.Hash, StringComparison.OrdinalIgnoreCase);
                }
                if (!matches)
                {
                    if (string.IsNullOrWhiteSpace(manifest.FileApi))
                        throw new InvalidDataException("MCBBS 文件缺失或哈希不匹配，且没有 fileApi：" + file.Path);
                    download = DownloadItem.Create(AddonUrl(manifest.FileApi, file.Path!),
                        FileUtil.SafeChildPath(directory, file.Path!), file.Hash, mirror: MirrorMode.Official);
                }
            }
            else if (string.IsNullOrWhiteSpace(file.FileName) ||
                     FindOverride(overrides, "mods/" + file.FileName, info.OverridesFolder) is null)
            {
                file = await McbbsManifestDocument.ResolveCurseAsync(file, client, ct).ConfigureAwait(false);
                if (FindOverride(overrides, file.TargetPath!, info.OverridesFolder) is null)
                    download = new DownloadItem
                    {
                        Destination = FileUtil.SafeChildPath(directory, file.TargetPath!),
                        Urls = [file.Url!],
                        Sha1 = file.ResolvedHash,
                        Size = file.ResolvedSize
                    };
            }
            planned.Add(new McbbsContentFile(file, download));
        }
        var resolved = manifest.WithFiles(planned.Select(file => file.Declaration), directory, id);
        var targets = overrides.Keys.Concat(planned.Select(file => file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var separator = target.IndexOf('/');
            while (separator >= 0)
            {
                if (targets.Contains(target[..separator])) throw new InvalidDataException("MCBBS 文件与目录路径冲突：" + target);
                separator = target.IndexOf('/', separator + 1);
            }
        }
        return new McbbsContentPlan(resolved, planned);
    }

    internal static string AddonUrl(string fileApi, string path) =>
        fileApi.TrimEnd('/') + "/overrides/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static ZipArchiveEntry? FindOverride(Dictionary<string, ZipArchiveEntry> overrides, string path, string prefix)
    {
        if (!overrides.TryGetValue(path, out var entry)) return null;
        if (entry.FullName[prefix.Length..].Replace('\\', '/') != path)
            throw new InvalidDataException("MCBBS 文件声明与覆写的路径大小写不一致：" + path);
        return entry;
    }
}
