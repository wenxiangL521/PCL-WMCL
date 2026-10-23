using System.Text.Json;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modrinth;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Services;

internal static class InstalledComponentResolver
{
    public static string? Loader(InstallManifest? manifest, string minecraft, string kind, VersionJson profile)
    {
        if (kind is "无" or "OptiFine") return null;
        var installed = manifest?.Components.GetValueOrDefault(kind.ToLowerInvariant());
        if (installed is null)
        {
            string[] prefixes = kind switch
            {
                "Quilt" => ["org.quiltmc:quilt-loader:"],
                "Fabric" => ["net.fabricmc:fabric-loader:"],
                "LegacyFabric" => ["net.legacyfabric:fabric-loader:", "net.fabricmc:fabric-loader:"],
                "NeoForge" => ["net.neoforged:neoforge:", "net.neoforged:forge:", "net.neoforged:fmlloader:"],
                _ => ["net.minecraftforge:forge:", "net.minecraftforge:minecraftforge:", "net.minecraftforge:fmlloader:"]
            };
            var versions = profile.Libraries.Where(library => prefixes.Any(prefix =>
                    library.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(library => library.Name.Split(':').ElementAtOrDefault(2)).Distinct(StringComparer.Ordinal).ToArray();
            if (kind == "NeoForge")
            {
                var arguments = profile.Arguments?.Game.SelectMany(argument => argument.IsLiteral && argument.Rules is null
                    ? argument.Values : [string.Empty]).ToArray() ?? [];
                foreach (var name in new[] { "--fml.neoForgeVersion", "--fml.forgeVersion" })
                {
                    var declared = arguments.Zip(arguments.Skip(1)).Where(pair => pair.First == name)
                        .Select(pair => pair.Second).Distinct(StringComparer.Ordinal).ToArray();
                    if (declared.Length == 0) continue;
                    versions = declared;
                    break;
                }
            }
            if (versions.Length == 1) installed = versions[0];
        }
        if (kind is "Forge" or "NeoForge" && installed?.StartsWith(minecraft + "-", StringComparison.Ordinal) == true)
            installed = installed[(minecraft.Length + 1)..];
        if (string.IsNullOrWhiteSpace(installed) || installed == "未知版本" ||
            string.Equals(installed, "unknown", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"无法确定 {kind} 的版本，无法修改此实例，请先核对安装清单和版本支持库。");
        return installed;
    }

    public static ModrinthVersion? Api(InstallManifest? manifest, string minecraft, string kind,
        IEnumerable<ModrinthVersion> catalog)
    {
        var project = ApiComponentService.Project(kind);
        if (manifest is null || !manifest.Components.TryGetValue(project, out var installed)) return null;
        if (string.IsNullOrWhiteSpace(installed)) throw new InvalidDataException(project + " 原版本信息为空");
        var saved = Metadata<ModrinthVersion>(manifest, project);
        var candidates = saved is null ? catalog : [saved];
        var matches = candidates.Where(version => version.GameVersions is not null && version.Loaders is not null &&
            version.Files is not null && version.Dependencies is not null && !string.IsNullOrWhiteSpace(version.Id) &&
            ApiComponentService.IsCompatible(version, minecraft, kind) &&
            (version.VersionNumber == installed || version.Id == installed || version.Name == installed) &&
            HasApiFile(manifest, project, version)).DistinctBy(version => version.Id).ToArray();
        if (saved is null && matches.Length > 1)
        {
            var sourced = matches.Where(version =>
            {
                var primary = version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.First();
                return manifest.ManagedFiles.Any(file => file.Component == project && file.Url == primary.Url &&
                    string.Equals(file.Sha1, primary.Sha1, StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            if (sourced.Length > 0) matches = sourced;
        }
        return ApiComponentService.Snapshot(RequireSingle(matches, ApiComponentService.Name(kind), installed));
    }

    public static OptiFabricFile? OptiFabric(InstallManifest? manifest, string minecraft,
        IEnumerable<OptiFabricFile> catalog)
    {
        if (manifest is null || !manifest.Components.TryGetValue("optifabric", out var installed)) return null;
        if (string.IsNullOrWhiteSpace(installed)) throw new InvalidDataException("OptiFabric 原版本信息为空");
        var saved = Metadata<OptiFabricFile>(manifest, "optifabric");
        var candidates = saved is null ? catalog : [saved];
        var matches = candidates.Where(file => file.Id > 0 && file.Size > 0 && file.GameVersions is not { IsDefault: true } &&
            file.FileName is not null && file.FileName.StartsWith("optifabric-", StringComparison.Ordinal) &&
            file.FileName.EndsWith(".jar", StringComparison.Ordinal) && file.FileName.Length > "optifabric-.jar".Length &&
            !file.FileName.Contains('/') && !file.FileName.Contains('\\') && OptiFabricService.IsCompatible(file, minecraft) &&
            (file.Version == installed || file.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == installed || file.DisplayName == installed) &&
            OptiFabricHash(manifest, file) is not null)
            .Select(file => file with { Sha1 = OptiFabricHash(manifest, file) }).DistinctBy(file => file.Id).ToArray();
        return RequireSingle(matches, "OptiFabric", installed);
    }

    public static OptiFineVersion? OptiFine(InstallManifest? manifest, string minecraft,
        IEnumerable<OptiFineVersion> catalog)
    {
        if (manifest is null || !manifest.Components.TryGetValue("optifine", out var installed)) return null;
        if (string.IsNullOrWhiteSpace(installed)) throw new InvalidDataException("OptiFine 原版本信息为空");
        var saved = Metadata<OptiFineVersion>(manifest, "optifine");
        var candidates = saved is null ? catalog : [saved];
        var matches = candidates.Where(version => version.Minecraft == minecraft && version.Patch == installed &&
            !string.IsNullOrWhiteSpace(version.Type) && !string.IsNullOrWhiteSpace(version.FileName) &&
            !version.FileName.Contains('/') && !version.FileName.Contains('\\'))
            .DistinctBy(version => (version.Type, version.FileName)).ToArray();
        return RequireSingle(matches, "OptiFine", installed);
    }

    public static OptiFineVersion OptiFineFromCoordinate(string minecraft, string coordinate,
        IEnumerable<OptiFineVersion> catalog)
    {
        var matches = catalog.Where(version => version.Minecraft == minecraft &&
            $"{version.Minecraft}_{version.Type}_{version.Patch}" == coordinate &&
            !string.IsNullOrWhiteSpace(version.Type) && !string.IsNullOrWhiteSpace(version.FileName) &&
            !version.FileName.Contains('/') && !version.FileName.Contains('\\'))
            .DistinctBy(version => (version.Type, version.FileName)).ToArray();
        return RequireSingle(matches, "OptiFine", coordinate);
    }

    private static bool HasApiFile(InstallManifest manifest, string project, ModrinthVersion version)
    {
        var primary = version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.First();
        return manifest.ManagedFiles.Any(file => file.Component == project && ValidHash(file.Sha1) &&
            string.Equals(file.Sha1, primary.Sha1, StringComparison.OrdinalIgnoreCase));
    }

    private static string? OptiFabricHash(InstallManifest manifest, OptiFabricFile file)
    {
        var hashes = manifest.ManagedFiles.Where(managed => managed.Component == "optifabric" &&
                ValidHash(managed.Sha1) && managed.Url == file.DownloadUrl)
            .Select(managed => managed.Sha1.ToLowerInvariant()).Distinct().ToArray();
        return hashes.Length == 1 && (file.Sha1 is null || string.Equals(file.Sha1, hashes[0], StringComparison.OrdinalIgnoreCase))
            ? hashes[0] : null;
    }

    private static bool ValidHash(string hash) => hash is { Length: 40 } && hash.All(char.IsAsciiHexDigit);

    private static T RequireSingle<T>(T[] matches, string name, string version) => matches.Length == 1 ? matches[0]
        : throw new InvalidDataException(matches.Length == 0
            ? $"无法确认已安装的 {name} {version} 的原始文件，请核对安装清单和组件来源后重试"
            : $"已安装的 {name} {version} 对应多个文件，无法确定原始版本");

    private static T? Metadata<T>(InstallManifest manifest, string project) where T : class
    {
        if (!manifest.ComponentMetadata.TryGetValue(project, out var metadata)) return null;
        try
        {
            return metadata.Deserialize<T>() ?? throw new InvalidDataException($"{project} 原始组件信息为空");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{project} 原始组件信息无效", exception);
        }
    }
}
