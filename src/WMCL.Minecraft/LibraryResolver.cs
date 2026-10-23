using WMCL.Core.IO;
using WMCL.Download;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

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

            MavenName maven;
            try
            {
                maven = MavenName.Parse(library.Name);
            }
            catch (FormatException)
            {
                continue;
            }

            if (library.Downloads?.Artifact is not null || library.Downloads?.Classifiers is null && library.Natives is null)
                Add(library.Downloads?.Artifact, null, false);
            if (library.Natives is not null && library.Natives.TryGetValue(context.OsName, out var nativeClassifier))
            {
                nativeClassifier = nativeClassifier.Replace("${arch}", context.OsArch is "x86" or "arm32" ? "32" : "64", StringComparison.Ordinal);
                if (library.Downloads?.Classifiers?.TryGetValue(nativeClassifier, out var native) == true)
                    Add(native, nativeClassifier, true);
            }

            void Add(DownloadInfo? info, string? classifier, bool isNative)
            {
                var effectiveName = classifier is null ? maven : maven with { Classifier = classifier };
                var relativePath = info?.Path ?? effectiveName.Path;
                var url = info?.Url;
                if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(library.Url))
                    url = library.Url.TrimEnd('/') + "/" + effectiveName.Path;
                var resolved = new ResolvedLibrary(maven, effectiveName.Classifier, relativePath, url,
                    info?.Sha1, info?.Size, isNative, library.Extract?.Exclude);
                var key = effectiveName.Key;
                if (index.TryGetValue(key, out var existing)) result[existing] = resolved;
                else
                {
                    index[key] = result.Count;
                    result.Add(resolved);
                }
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
            if (!IsHexHash(asset.Hash)) continue;
            var prefix = asset.Hash[..2];
            var destination = FileUtil.SafeChildPath(
                objectsDirectory, prefix + "/" + asset.Hash);
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

    private static bool IsHexHash(string value) =>
        value.Length == 40 && value.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
}
