using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.App.ViewModels;

public sealed class VersionCatalog(VersionRepository repository)
{
    private static readonly Regex OptiFineVersionPattern = new(
        @"HD_U_([^"":/]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<VersionItem>> LoadInstalledAsync(CancellationToken ct = default)
        => await ReadInstalledAsync(repository.ListInstalled(), ct).ConfigureAwait(false);

    private async Task<List<VersionItem>> ReadInstalledAsync(IReadOnlyList<InstalledVersion> installed, CancellationToken ct,
        string? latestRelease = null, string? latestSnapshot = null)
    {
        var result = new List<VersionItem>();
        foreach (var version in installed.OrderByDescending(version => version.ReleaseTime ?? "",
                     StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var category = "vanilla";
            string? baseGameVersion = null;
            string? optiFineVersion = null;
            var hasOptiFine = false;
            var loaders = VersionLoaderKind.None;
            var hasQuilt = false;
            var reliable = false;
            var source = MinecraftVersionIdentitySource.Unknown;
            string? forgeVersion = null;
            string? neoForgeVersion = null;
            string? fabricVersion = null;
            string? liteLoaderVersion = null;
            string? cleanroomVersion = null;
            string? labyModVersion = null;
            var type = version.Type ?? "release";
            var releaseTime = version.ReleaseTime;
            var description = "";
            Bitmap? logo = null;
            try
            {
                var json = await repository.GetLocalVersionAsync(version.Id, ct).ConfigureAwait(false);
                type = json.Type ?? type;
                releaseTime = json.ReleaseTime ?? releaseTime;
                var identity = json.GameVersionIdentity;
                baseGameVersion = MinecraftVersionUtil.GetBaseGameVersion(version.Id, json);
                reliable = identity is { IsReliable: true } &&
                           string.Equals(identity.Version, baseGameVersion, StringComparison.OrdinalIgnoreCase);
                source = identity?.Source ?? MinecraftVersionIdentitySource.Unknown;
                loaders = DetectLoaders(json);
                hasQuilt = HasQuiltProfile(json);
                (hasOptiFine, optiFineVersion) = DetectOptiFine(json);
                forgeVersion = DetectForgeVersion(json);
                neoForgeVersion = DetectNeoForgeVersion(json);
                fabricVersion = DetectFabricVersion(json);
                liteLoaderVersion = DetectLiteLoaderVersion(json);
                cleanroomVersion = DetectCleanroomVersion(json, loaders);
                labyModVersion = json.LabyModData?.Version;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                category = "error";
                description = "无法读取版本配置";
            }

            var settings = InstanceSettingsStore.Load(version.Directory);
            if (!string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
            {
                try { logo = new Bitmap(settings.LogoPath); }
                catch { logo = null; }
            }
            if (string.IsNullOrWhiteSpace(description)) description = settings.Description;

            var item = new VersionItem
            {
                Id = version.Id,
                Type = type,
                ReleaseTime = releaseTime,
                BaseGameVersion = baseGameVersion,
                IsBaseGameVersionReliable = reliable,
                IdentitySource = source,
                OptiFineVersion = optiFineVersion,
                HasOptiFine = hasOptiFine,
                Loaders = loaders,
                ForgeVersion = forgeVersion,
                NeoForgeVersion = neoForgeVersion,
                FabricVersion = fabricVersion,
                LiteLoaderVersion = liteLoaderVersion,
                CleanroomVersion = cleanroomVersion,
                LabyModVersion = labyModVersion,
                Description = description,
                Logo = logo,
                CardTypeOverride = settings.VersionCardType,
                IsInstalled = true,
                IsLatest = version.Id == latestRelease || version.Id == latestSnapshot,
                Category = category
            };

            if (category != "error")
            {
                item.Category = item.IsOldVersion ? "old" :
                    item.IsFoolVersion ? "fool" :
                    PrimaryCategory(loaders, hasOptiFine, hasQuilt);
                if (string.Equals(baseGameVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    item.Category = "error";
                    item.Description = "PCL 无法识别该版本的 MC 版本号";
                }
            }
            result.Add(item);
        }
        return result;
    }

    public async Task<IReadOnlyList<VersionItem>> LoadAsync(
        int maxRemote = 120,
        bool officialOnly = false,
        CancellationToken ct = default,
        bool forceRefresh = false)
    {
        var installed = repository.ListInstalled();
        var manifest = await repository.GetManifestAsync(forceRefresh, ct, allowCachedFallback: !forceRefresh);
        var installedIds = installed.Select(v => v.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var latestRelease = manifest.Latest.Release;
        var latestSnapshot = manifest.Latest.Snapshot;
        var result = officialOnly ? new List<VersionItem>()
            : await ReadInstalledAsync(installed, ct, latestRelease, latestSnapshot).ConfigureAwait(false);

        foreach (var version in manifest.Versions.Take(maxRemote))
        {
            if (!officialOnly && installedIds.Contains(version.Id)) continue;
            result.Add(new VersionItem
            {
                Id = version.Id,
                Type = version.Type,
                ReleaseTime = version.ReleaseTime,
                IsInstalled = installedIds.Contains(version.Id),
                IsLatest = version.Id == latestRelease || version.Id == latestSnapshot
            });
        }

        return result;
    }

    private static string PrimaryCategory(VersionLoaderKind loaders, bool hasOptiFine, bool jsonHasQuilt)
    {
        if (loaders.HasFlag(VersionLoaderKind.Fabric))
            return jsonHasQuilt && !loaders.HasFlag(VersionLoaderKind.Forge) ? "quilt" : "fabric";
        if (loaders.HasFlag(VersionLoaderKind.Cleanroom)) return "cleanroom";
        if (loaders.HasFlag(VersionLoaderKind.LabyMod)) return "labymod";
        if (loaders.HasFlag(VersionLoaderKind.Forge)) return "forge";
        if (loaders.HasFlag(VersionLoaderKind.NeoForge)) return "neoforge";
        if (loaders.HasFlag(VersionLoaderKind.LiteLoader)) return "liteloader";
        return hasOptiFine ? "optifine" : "vanilla";
    }

    private static bool HasQuiltProfile(VersionJson json) =>
        (json.MainClass ?? "").Contains("quilt", StringComparison.OrdinalIgnoreCase) ||
        json.Libraries.Any(library =>
            library.Name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase));

    private static VersionLoaderKind DetectLoaders(VersionJson json)
    {
        var text = (json.MainClass ?? "") + " " + string.Join(' ', json.Libraries.Select(library => library.Name));
        var loaders = VersionLoaderKind.None;
        if (text.Contains("net.fabricmc:fabric-loader", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("org.quiltmc:quilt-loader", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fabricmc.loader", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("quiltmc.loader", StringComparison.OrdinalIgnoreCase))
            loaders |= VersionLoaderKind.Fabric;
        if (json.Libraries.Any(library =>
                library.Name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.minecraftforge:fmlloader:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.minecraftforge:minecraftforge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.minecraftforge:mergetool:", StringComparison.OrdinalIgnoreCase)))
            loaders |= VersionLoaderKind.Forge;
        if (json.Libraries.Any(library =>
                library.Name.StartsWith("net.neoforge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase)) ||
            text.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase))
            loaders |= VersionLoaderKind.NeoForge;
        if (json.Libraries.Any(library =>
                library.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase)))
            loaders |= VersionLoaderKind.LiteLoader;
        if (json.Libraries.Any(library =>
                library.Name.StartsWith("com.cleanroommc:cleanroom:", StringComparison.OrdinalIgnoreCase)) ||
            text.Contains("top.outlands.foundation", StringComparison.OrdinalIgnoreCase))
            loaders |= VersionLoaderKind.Cleanroom;
        if (json.LabyModData is not null ||
            text.Contains("labymod", StringComparison.OrdinalIgnoreCase))
            loaders |= VersionLoaderKind.LabyMod;
        return loaders;
    }

    private static (bool HasOptiFine, string? Version) DetectOptiFine(VersionJson json)
    {
        var names = json.Libraries.Select(library => library.Name)
            .Concat(json.Arguments?.Game.SelectMany(argument => argument.Values) ?? [])
            .Where(name => name.Contains("optifine", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (names.Count == 0) return (false, null);
        var match = OptiFineVersionPattern.Match(string.Join('\n', names));
        return (true, match.Success ? match.Groups[1].Value : "未知版本");
    }

    private static string? DetectForgeVersion(VersionJson json)
    {
        foreach (var library in json.Libraries)
        {
            if (library.Name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.minecraftforge:fmlloader:", StringComparison.OrdinalIgnoreCase))
            {
                var coordinate = library.Name.Split(':').ElementAtOrDefault(2) ?? "";
                var dash = coordinate.LastIndexOf('-');
                return dash >= 0 ? coordinate[(dash + 1)..] : coordinate;
            }
        }
        return FindArgumentValue(json, "--fml.forgeVersion");
    }

    private static string? DetectNeoForgeVersion(VersionJson json)
    {
        var fromArguments = FindArgumentValue(json, "--fml.neoForgeVersion") ??
                            FindArgumentValue(json, "--fml.forgeVersion");
        if (!string.IsNullOrWhiteSpace(fromArguments)) return fromArguments;
        foreach (var library in json.Libraries)
            if (library.Name.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.neoforge:neoforge:", StringComparison.OrdinalIgnoreCase))
                return library.Name.Split(':').ElementAtOrDefault(2);
        return null;
    }

    private static string? DetectFabricVersion(VersionJson json)
    {
        foreach (var library in json.Libraries)
            if (library.Name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase))
                return library.Name.Split(':').ElementAtOrDefault(2)?.Replace("+build", "");
        return null;
    }

    private static string? DetectCleanroomVersion(VersionJson json, VersionLoaderKind loaders)
    {
        if (!loaders.HasFlag(VersionLoaderKind.Cleanroom)) return null;
        foreach (var library in json.Libraries)
        {
            if (!library.Name.StartsWith("com.cleanroommc:cleanroom:", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = library.Name.Split(':');
            return parts.Length >= 3 ? parts[2] : "未知版本";
        }
        return "未知版本";
    }

    private static string? DetectLiteLoaderVersion(VersionJson json) =>
        json.Libraries.FirstOrDefault(library =>
                library.Name.StartsWith("com.mumfrey:liteloader:", StringComparison.OrdinalIgnoreCase))?
            .Name.Split(':').ElementAtOrDefault(2);

    private static string? FindArgumentValue(VersionJson json, string name)
    {
        var arguments = json.Arguments?.Game;
        if (arguments is null) return null;
        for (var index = 0; index + 1 < arguments.Count; index++)
            if (arguments[index].IsLiteral && arguments[index].Values.FirstOrDefault() == name)
                return arguments[index + 1].Values.FirstOrDefault();
        return null;
    }
}
