using System.Text.Json;
using System.Text.RegularExpressions;
using WMCL.Core.IO;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Modding.Modpack;

internal static class HmclModpackComponents
{
    internal sealed record LaunchMetadataResult(
        bool Changed,
        IReadOnlyDictionary<string, string> BundledArchives);

    public static LocalModpackComponents Parse(JsonElement profile, string? manifestGame)
    {
        if (!string.IsNullOrWhiteSpace(Text(profile, "inheritsFrom")))
            throw new NotSupportedException("HMCL pack.json 必须是独立配置，暂不支持包外 inheritsFrom。");
        var components = new LocalModpackComponents();
        var jar = Text(profile, "jar");
        string? patchGame = null;
        var nodes = new List<JsonElement>();
        if (profile.TryGetProperty("patches", out var patches) && patches.ValueKind != JsonValueKind.Null)
        {
            if (patches.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("HMCL patches 必须是数组。");
            foreach (var patch in patches.EnumerateArray().OrderBy(PatchPriority))
            {
                var id = Text(patch, "id");
                var version = Text(patch, "version");
                components.Add(id, version);
                if (id == "game") patchGame = version;
                if (!string.IsNullOrWhiteSpace(Text(patch, "inheritsFrom")) ||
                    patch.TryGetProperty("patches", out _))
                    throw new NotSupportedException("暂不支持 HMCL 组件中的嵌套 patches 或包外继承。");
                nodes.Add(patch);
            }
        }
        if (patches.ValueKind != JsonValueKind.Array || !profile.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.True)
            nodes.Insert(0, profile);
        var game = jar ?? patchGame ?? manifestGame;
        if (string.IsNullOrWhiteSpace(game))
            throw new InvalidDataException("HMCL 整合包缺少游戏版本（jar / game patch / gameVersion）。");
        components.Add("game", game, inferred: true);

        var libraryNodes = nodes.SelectMany(node => node.TryGetProperty("libraries", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray() : [])
            .ToList();
        foreach (var library in libraryNodes) ValidateLibrary(library);
        var libraries = libraryNodes
            .Select(library => Text(library, "name") ?? "")
            .Select(name => name.Split(':')).Where(parts => parts.Length >= 3).ToList();
        var neoVersion = components.GetVersion("neoforge") ??
                         libraries.Where(parts => parts[0] == "net.neoforged" && parts[1] is "neoforge" or "forge")
                             .Select(parts => ForgeVersion(parts[2])).FirstOrDefault() ??
                         FindArgument(nodes, "--fml.neoForgeVersion") ?? FindArgument(nodes, "--fml.forgeVersion");
        var hasNeo = libraries.Any(parts => parts[0] is "net.neoforged" or "net.neoforged.fancymodloader");
        foreach (var parts in libraries)
        {
            switch (parts[0], parts[1])
            {
                case ("net.fabricmc", "fabric-loader"):
                    components.Add("fabric", parts[2], inferred: true);
                    break;
                case ("org.quiltmc", "quilt-loader"):
                    components.Add("quilt", parts[2], inferred: true);
                    break;
                case ("net.minecraftforge", "forge" or "fmlloader") when !hasNeo:
                    components.Add("forge", ForgeVersion(parts[2]), inferred: true);
                    break;
                case ("net.neoforged", "neoforge" or "forge"):
                    components.Add("neoforge", ForgeVersion(parts[2]), inferred: true);
                    break;
                case ("net.neoforged.fancymodloader", "loader" or "core"):
                    if (neoVersion is null)
                        throw new InvalidDataException("HMCL NeoForge 配置缺少 --fml.neoForgeVersion / --fml.forgeVersion。");
                    components.Add("neoforge", neoVersion, inferred: true);
                    break;
                case ("net.legacyfabric", _):
                case ("com.cleanroommc", "cleanroom"):
                case ("com.mumfrey", "liteloader"):
                case ("optifine" or "net.optifine", _):
                    throw new NotSupportedException($"暂不支持 HMCL 组件：{parts[0]}:{parts[1]}。");
            }
        }
        var info = components.Build(LocalModpackKind.Hmcl, "", "", "");
        if (info.FabricLoader is null && info.QuiltLoader is null && info.Forge is null && info.NeoForge is null)
        {
            var main = nodes.Select(node => Text(node, "mainClass")).LastOrDefault(value => value is not null);
            if (main is not null && main is not ("net.minecraft.client.main.Main" or "net.minecraft.client.Minecraft" or "com.mojang.minecraft.RubyDung"))
                throw new NotSupportedException("无法识别 HMCL 配置的加载器入口：" + main);
        }
        return components;
    }

    private static string ForgeVersion(string version)
    {
        var match = Regex.Match(version, @"^[0-9.]+-(?<loader>[0-9.]+)(?:-[0-9.]+)?$");
        return match.Success ? match.Groups["loader"].Value : version;
    }

    public static async Task<LaunchMetadataResult> ApplyLaunchMetadataAsync(
        VersionRepository repository, string id, JsonElement profile, string overridesPrefix, CancellationToken ct)
    {
        var hasPatches = profile.TryGetProperty("patches", out var patches) && patches.ValueKind == JsonValueKind.Array;
        var original = hasPatches && profile.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.True
            ? new VersionJson()
            : profile.Deserialize<VersionJson>() ?? throw new InvalidDataException("HMCL pack.json 无效。");
        if (hasPatches)
        {
            foreach (var patch in patches.EnumerateArray().OrderBy(PatchPriority))
            {
                var patchVersion = patch.Deserialize<VersionJson>()
                    ?? throw new InvalidDataException("HMCL patch 无效。");
                original.Libraries.AddRange(patchVersion.Libraries);
                if (patchVersion.Arguments is not null)
                {
                    original.Arguments ??= new Arguments();
                    original.Arguments.Game.AddRange(patchVersion.Arguments.Game);
                    original.Arguments.Jvm.AddRange(patchVersion.Arguments.Jvm);
                }
                if (!string.IsNullOrWhiteSpace(patchVersion.MinecraftArguments))
                {
                    if (!string.IsNullOrWhiteSpace(original.MinecraftArguments) &&
                        !string.Equals(original.MinecraftArguments, patchVersion.MinecraftArguments, StringComparison.Ordinal))
                        throw new NotSupportedException("HMCL patches 包含冲突的 minecraftArguments。");
                    original.MinecraftArguments = patchVersion.MinecraftArguments;
                }
            }
        }
        var installed = await repository.GetVersionAsync(id, ct).ConfigureAwait(false);
        if (original.MainClass is not null && original.MainClass != installed.MainClass &&
            !(patches.ValueKind == JsonValueKind.Array && patches.GetArrayLength() > 0))
            throw new NotSupportedException($"HMCL 自定义入口与重装组件不一致：{original.MainClass} / {installed.MainClass}。");
        var libraries = installed.Libraries.Select(library => library.Name).ToHashSet(StringComparer.Ordinal);
        var bundledArchives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scopedOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in original.Libraries)
        {
            if (libraries.Contains(library.Name)) continue;
            var parts = library.Name.Split(':');
            if (parts.Length < 3) throw new InvalidDataException("HMCL 库坐标无效：" + library.Name);
            if (installed.Libraries.Any(existing =>
            {
                var existingParts = existing.Name.Split(':');
                return existingParts.Length >= 3 && existingParts[0] == parts[0] && existingParts[1] == parts[1] &&
                       string.Join(':', existingParts.Skip(3)) == string.Join(':', parts.Skip(3));
            }))
                throw new NotSupportedException("HMCL 自定义库与重装组件的版本冲突：" + library.Name);
            var scoped = JsonSerializer.Deserialize<Library>(JsonSerializer.Serialize(library))
                         ?? throw new InvalidDataException("HMCL 库无效：" + library.Name);
            var maven = MavenName.Parse(scoped.Name);
            scoped.Downloads ??= new LibraryDownloads();
            var libraryUrl = scoped.Url;
            if (scoped.Downloads.Artifact is not null || scoped.Downloads.Classifiers is null && scoped.Natives is null)
            {
                scoped.Downloads.Artifact ??= new DownloadInfo();
                ScopeDownload(repository, id, scoped.Downloads.Artifact, maven.Path,
                    scoped.Name, libraryUrl, overridesPrefix, bundledArchives, scopedOwners);
            }
            foreach (var (classifier, download) in scoped.Downloads.Classifiers ?? [])
            {
                if (download is null) throw new InvalidDataException("HMCL 库 classifier 无效：" + classifier);
                ScopeDownload(repository, id, download, (maven with { Classifier = classifier }).Path,
                    scoped.Name + ":" + classifier, libraryUrl, overridesPrefix, bundledArchives, scopedOwners);
            }
            scoped.Url = null;
            installed.Libraries.Add(scoped);
            libraries.Add(library.Name);
        }
        if (original.Arguments is not null)
        {
            installed.Arguments ??= new Arguments();
            installed.Arguments.Game = MergeArguments(installed.Arguments.Game, original.Arguments.Game, "--username");
            installed.Arguments.Jvm = MergeArguments(installed.Arguments.Jvm, original.Arguments.Jvm, "-cp");
        }
        if (!string.IsNullOrWhiteSpace(original.MinecraftArguments))
        {
            if (installed.Arguments is not null)
                throw new NotSupportedException("暂不支持 HMCL 配置同时混用 minecraftArguments 与现代 arguments。");
            installed.MinecraftArguments = original.MinecraftArguments;
        }
        await File.WriteAllTextAsync(Path.Combine(repository.GetVersionDirectory(id), id + ".json"),
            JsonSerializer.Serialize(installed, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        return new LaunchMetadataResult(true, bundledArchives);
    }

    public static async Task<IReadOnlyList<InstallManifestFile>> CollectManagedFilesAsync(
        VersionRepository repository, string id, IReadOnlyDictionary<string, string> bundledArchives,
        CancellationToken ct)
    {
        var profile = await repository.GetVersionAsync(id, ct).ConfigureAwait(false);
        var files = new List<InstallManifestFile>();
        foreach (var library in LibraryResolver.Resolve(profile, RuleContext.CreateDefault()))
        {
            if (!library.RelativePath.StartsWith("wmcl-packs/" + id + "/", StringComparison.OrdinalIgnoreCase)) continue;
            var path = FileUtil.SafeChildPath(Path.Combine(repository.GameDirectory, "libraries"), library.RelativePath);
            if (!File.Exists(path)) throw new FileNotFoundException("HMCL 自定义库未安装：" + library.Name, path);
            if (library.Size is { } size && new FileInfo(path).Length != size)
                throw new InvalidDataException("HMCL 自定义库大小与元数据不一致：" + library.RelativePath);
            var hash = await InstallManifestStore.ComputeFileSha1Async(path, ct);
            if (library.Sha1 is not null && !string.Equals(hash, library.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HMCL 自定义库哈希与元数据不一致：" + library.RelativePath);
            files.Add(new InstallManifestFile(
                "libraries/" + library.RelativePath,
                hash,
                "HMCL-library", library.Url,
                bundledArchives.GetValueOrDefault(library.RelativePath)));
        }
        var recorded = files.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, archivePath) in bundledArchives)
        {
            var relative = "libraries/" + relativePath;
            if (!recorded.Add(relative)) continue;
            var path = FileUtil.SafeChildPath(repository.GameDirectory, relative);
            files.Add(new InstallManifestFile(relative,
                await InstallManifestStore.ComputeFileSha1Async(path, ct), "HMCL-library", ArchivePath: archivePath));
        }
        return files;
    }

    private static void ScopeDownload(
        VersionRepository repository, string id, DownloadInfo download, string fallbackPath,
        string owner, string? libraryUrl, string overridesPrefix,
        Dictionary<string, string> bundledArchives, Dictionary<string, string> scopedOwners)
    {
        var sourcePath = (download.Path ?? fallbackPath).Replace('\\', '/');
        ModpackContentPaths.ValidateLibraryPath(sourcePath);
        var scopedPath = "wmcl-packs/" + id + "/" + sourcePath;
        ModpackContentPaths.ValidateLibraryPath(scopedPath);
        foreach (var (existingPath, existingOwner) in scopedOwners)
        {
            if (existingPath.Equals(scopedPath, StringComparison.OrdinalIgnoreCase) && existingOwner != owner ||
                existingPath.StartsWith(scopedPath + "/", StringComparison.OrdinalIgnoreCase) ||
                scopedPath.StartsWith(existingPath + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HMCL 库文件/目录路径冲突：" + sourcePath);
        }
        scopedOwners[scopedPath] = owner;
        var source = FileUtil.SafeChildPath(
            Path.Combine(repository.GetVersionDirectory(id), "libraries"), sourcePath);
        var destination = FileUtil.SafeChildPath(
            Path.Combine(repository.GameDirectory, "libraries"), scopedPath);
        if (File.Exists(source))
        {
            if (download.Size is { } size && new FileInfo(source).Length != size)
                throw new InvalidDataException("HMCL 内嵌库大小与元数据不一致：" + sourcePath);
            if (download.Sha1 is { } expected && !string.Equals(
                    FileUtil.Sha1(source), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HMCL 内嵌库哈希与元数据不一致：" + sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            FileUtil.MoveReplace(source, destination);
            download.Url = null;
            bundledArchives[scopedPath] = overridesPrefix + "libraries/" + sourcePath;
        }
        else if (string.IsNullOrWhiteSpace(download.Url) && !string.IsNullOrWhiteSpace(libraryUrl))
        {
            download.Url = libraryUrl.TrimEnd('/') + "/" + fallbackPath;
        }
        download.Path = scopedPath;
        ValidateDownload(download);
    }

    private static void ValidateLibrary(JsonElement node)
    {
        var library = node.Deserialize<Library>() ?? throw new InvalidDataException("HMCL 库无效。");
        MavenName maven;
        try { maven = MavenName.Parse(library.Name); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        { throw new InvalidDataException("HMCL 库坐标无效：" + library.Name, ex); }
        ModpackContentPaths.ValidateLibraryPath(maven.Path);
        if (library.Url is not null) ValidateUrl(library.Url);
        ValidateDownload(library.Downloads?.Artifact);
        foreach (var (classifier, download) in library.Downloads?.Classifiers ?? [])
        {
            if (string.IsNullOrWhiteSpace(classifier) || classifier.Contains('/') || classifier.Contains('\\') || download is null)
                throw new InvalidDataException("HMCL classifier 无效：" + classifier);
            ValidateDownload(download);
        }
    }

    private static void ValidateDownload(DownloadInfo? download)
    {
        if (download is null) return;
        if (download.Path is not null) ModpackContentPaths.ValidateLibraryPath(download.Path);
        if (download.Url is not null) ValidateUrl(download.Url);
        if (download.Sha1 is not null && (download.Sha1.Length != 40 || !download.Sha1.All(Uri.IsHexDigit)))
            throw new InvalidDataException("HMCL 库 SHA-1 无效。");
        if (download.Size is < 0) throw new InvalidDataException("HMCL 库大小无效。");
    }

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("HMCL 库下载地址无效：" + url);
    }

    private static int PatchPriority(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("HMCL patch 必须是对象。");
        if (!patch.TryGetProperty("priority", out var priority) || priority.ValueKind == JsonValueKind.Null)
            return int.MinValue;
        if (priority.ValueKind != JsonValueKind.Number || !priority.TryGetInt32(out var value))
            throw new InvalidDataException("HMCL patch priority 必须是整数。");
        return value;
    }

    private static List<ArgumentEntry> MergeArguments(List<ArgumentEntry> installed, List<ArgumentEntry> original, string fullListMarker)
    {
        if (original.Count == 0) return installed;
        if (original.Any(argument => argument.Values.Contains(fullListMarker))) return original;
        return [.. installed, .. original];
    }

    private static string? FindArgument(IEnumerable<JsonElement> nodes, string option)
    {
        foreach (var node in nodes)
        {
            if (!node.TryGetProperty("arguments", out var arguments) ||
                arguments.ValueKind != JsonValueKind.Object ||
                !arguments.TryGetProperty("game", out var game) || game.ValueKind != JsonValueKind.Array)
                continue;
            var values = game.EnumerateArray().ToArray();
            for (var index = 0; index < values.Length - 1; index++)
                if (values[index].ValueKind == JsonValueKind.String && values[index].GetString() == option &&
                    values[index + 1].ValueKind == JsonValueKind.String)
                    return values[index + 1].GetString();
        }
        return null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
