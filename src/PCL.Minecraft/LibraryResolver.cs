using PCL.Download;
using PCL.Minecraft.Models;

namespace PCL.Minecraft;

public sealed record ResolvedLibrary(
    MavenName Name,
    string? Classifier,
    string RelativePath,
    string? Url,
    string? Sha1,
    long? Size,
    bool IsNative,
    IReadOnlyList<string>? Exclude);

public static class LibraryResolver
{
    public static IReadOnlyList<ResolvedLibrary> Resolve(VersionJson version, RuleContext context)
    {
        var result = new List<ResolvedLibrary>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var library in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules, context)) continue;

            DownloadInfo? info;
            string? classifier = null;
            var isNative = false;

            if (library.Natives is not null &&
                library.Natives.TryGetValue(context.OsName, out var nativeClassifier))
            {
                classifier = nativeClassifier;
                if (library.Downloads?.Classifiers is null ||
                    !library.Downloads.Classifiers.TryGetValue(nativeClassifier, out info))
                    continue;
                isNative = true;
            }
            else
            {
                info = library.Downloads?.Artifact;
            }

            MavenName maven;
            try
            {
                maven = MavenName.Parse(library.Name);
            }
            catch (FormatException)
            {
                continue;
            }

            var relativePath = info?.Path ?? maven.Path;
            var url = info?.Url;
            if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(library.Url))
                url = library.Url.TrimEnd('/') + "/" + maven.Path;

            if (string.IsNullOrEmpty(url)) continue;

            var resolved = new ResolvedLibrary(
                maven, maven.Classifier ?? classifier, relativePath, url, info?.Sha1, info?.Size, isNative,
                library.Extract?.Exclude);

            // 现代版本的 natives 是独立条目（classifier 写在 Maven 名字里），
            // 必须把 Maven classifier 也计入去重键，否则会覆盖同名 base jar。
            var effectiveClassifier = maven.Classifier ?? classifier ?? "";
            var key = $"{maven.Group}:{maven.Artifact}:{effectiveClassifier}";
            if (index.TryGetValue(key, out var existing))
                result[existing] = resolved;
            else
            {
                index[key] = result.Count;
                result.Add(resolved);
            }
        }

        return result;
    }

    public static IReadOnlyList<ResolvedLibrary> ResolveArtifacts(
        VersionJson version, RuleContext context) =>
        Resolve(version, context).Where(l => !l.IsNative).ToList();

    public static IReadOnlyList<ResolvedLibrary> ResolveNatives(
        VersionJson version, RuleContext context) =>
        Resolve(version, context).Where(l => l.IsNative).ToList();
}

public static class AssetResolver
{
    public static IReadOnlyList<DownloadItem> BuildItems(
        AssetIndex index,
        string assetsDirectory,
        MirrorMode mirror = MirrorMode.Auto)
    {
        var objectsDirectory = Path.Combine(assetsDirectory, "objects");
        var items = new Dictionary<string, DownloadItem>(StringComparer.Ordinal);

        foreach (var (_, asset) in index.Objects)
        {
            if (string.IsNullOrEmpty(asset.Hash) || asset.Hash.Length < 2) continue;
            var prefix = asset.Hash[..2];
            var destination = Path.Combine(objectsDirectory, prefix, asset.Hash);
            if (items.ContainsKey(destination)) continue;

            items[destination] = DownloadItem.Create(
                $"https://resources.download.minecraft.net/{prefix}/{asset.Hash}",
                destination,
                asset.Hash,
                asset.Size,
                mirror);
        }

        return items.Values.ToList();
    }
}
