using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Modding.Modpack;

internal sealed class MmcModpackMetadata
{
    private readonly List<JsonElement> _patches = [];
    private readonly Dictionary<string, ZipArchiveEntry> _bundled = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Library> _libraries = [];
    private readonly Dictionary<string, (ZipArchiveEntry? Source, DownloadInfo? Download)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _versions = new(StringComparer.Ordinal);

    public LocalModpackComponents Components { get; } = new();
    public bool HasChanges => _patches.Count > 0 || _bundled.Count > 0;

    public static MmcModpackMetadata Read(ZipArchive archive, JsonElement root, string baseFolder)
    {
        var result = new MmcModpackMetadata();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MMC 整合包缺少 components 数组。");
        ValidateFormat(root);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(baseFolder + "libraries/", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith('/')))
        {
            var path = entry.FullName[(baseFolder.Length + "libraries/".Length)..].Replace('\\', '/');
            ValidatePath(path);
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || !result._bundled.TryAdd(path, entry))
                throw new InvalidDataException("MMC 库路径重复或包含符号链接：" + path);
            result.RegisterFile(path, entry, null);
        }
        var patches = new List<(JsonElement Patch, int Order, int Index)>();
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object) throw new InvalidDataException("MMC 组件条目无效。");
            var uid = Text(component, "uid");
            if (string.IsNullOrWhiteSpace(uid) || uid != FileUtil.SafeFileName(uid))
                throw new InvalidDataException("MMC 组件缺少有效 uid。");
            ValidatePath(uid);
            var entries = archive.Entries.Where(entry => entry.FullName.Equals(baseFolder + "patches/" + uid + ".json", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length > 1) throw new InvalidDataException("MMC 补丁路径重复：" + uid);
            JsonElement patch = default;
            if (entries.Length == 1)
            {
                using var stream = entries[0].Open();
                using var document = JsonDocument.Parse(stream);
                patch = document.RootElement.Clone();
                ValidateFormat(patch);
                if (Text(patch, "uid") != uid) throw new InvalidDataException("MMC 补丁 uid 与组件不一致：" + uid);
            }
            var version = Text(component, "version");
            var patchVersion = patch.ValueKind == JsonValueKind.Object ? Text(patch, "version") : null;
            if (version is not null && patchVersion is not null && version != patchVersion)
                throw new InvalidDataException("MMC 补丁与组件版本不一致：" + uid);
            version ??= patchVersion;
            if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("MMC 组件缺少版本：" + uid);
            if (result._versions.Keys.Any(existing => existing.Equals(uid, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("MMC 组件重复：" + uid);
            result._versions.Add(uid, version);
            switch (uid)
            {
                case "net.minecraft": result.Components.Add("game", version); break;
                case "net.minecraftforge": result.Components.Add("forge", version); break;
                case "net.neoforged": result.Components.Add("neoforge", version); break;
                case "net.fabricmc.fabric-loader": result.Components.Add("fabric", version); break;
                case "org.quiltmc.quilt-loader": result.Components.Add("quilt", version); break;
                case "org.lwjgl":
                case "org.lwjgl3": break;
                default:
                    if (patch.ValueKind != JsonValueKind.Object) throw new NotSupportedException("暂不支持没有本地补丁的 MMC 组件：" + uid);
                    break;
            }
            if (patch.ValueKind == JsonValueKind.Object)
            {
                var order = 0;
                if (patch.TryGetProperty("order", out var orderElement) &&
                    (orderElement.ValueKind != JsonValueKind.Number || !orderElement.TryGetInt32(out order)))
                    throw new InvalidDataException("MMC 补丁 order 必须是整数。");
                patches.Add((patch, order, patches.Count));
            }
        }
        foreach (var patch in patches.OrderBy(item => item.Order).ThenBy(item => item.Index).Select(item => item.Patch))
        {
            result.ValidatePatch(patch);
            result._patches.Add(patch);
        }
        return result;
    }

    private void ValidatePatch(JsonElement patch)
    {
        foreach (var property in new[] { "mainJar", "jarMods", "mavenFiles", "-libraries", "-tweakers", "-jvmArgs" })
            if (patch.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null &&
                !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0))
                throw new NotSupportedException("暂不支持 MMC 补丁字段：" + property);
        foreach (var requirement in Array(patch, "requires"))
        {
            var uid = Text(requirement, "uid");
            var required = Text(requirement, "equals");
            if (uid is null || !_versions.TryGetValue(uid, out var version) || required is not null && version != required)
                throw new InvalidDataException("MMC 补丁依赖缺失或版本不匹配：" + uid);
        }
        if (Text(patch, "mainClass") is { } mainClass && string.IsNullOrWhiteSpace(mainClass))
            throw new InvalidDataException("MMC mainClass 不能为空。");
        if (Text(patch, "minecraftArguments") is { } legacyArguments) SplitArguments(legacyArguments);
        foreach (var name in new[] { "+jvmArgs", "+tweakers", "+traits" }) Strings(patch, name);
        foreach (var trait in Strings(patch, "+traits"))
            if (trait is not ("FirstThreadOnMacOS" or "texturepacks" or "no-texturepacks" or "XR:Initial"))
                throw new NotSupportedException("暂不支持 MMC 启动特性：" + trait);
        JavaVersion(patch);
        if (patch.TryGetProperty("arguments", out var modern))
        {
            var arguments = modern.Deserialize<Arguments>() ?? throw new InvalidDataException("MMC arguments 无效。");
            if (arguments.Game is null || arguments.Jvm is null || arguments.Game.Concat(arguments.Jvm)
                    .Any(argument => argument is null || argument.Values is null || argument.Values.Count == 0 || argument.Values.Any(value => value is null)))
                throw new InvalidDataException("MMC arguments 不能包含空参数声明。");
        }
        if (patch.TryGetProperty("assetIndex", out var assets))
        {
            var index = assets.Deserialize<AssetIndexInfo>() ?? throw new InvalidDataException("MMC assetIndex 无效。");
            ValidatePath(index.Id);
            ValidateUrl(index.Url);
            ValidateDownload(new DownloadInfo { Sha1 = index.Sha1, Size = index.Size });
        }
        foreach (var property in new[] { "libraries", "+libraries" })
            foreach (var node in Array(patch, property))
            {
                var library = node.Deserialize<Library>() ?? throw new InvalidDataException("MMC 库无效。");
                var maven = MavenName.Parse(library.Name);
                ValidatePath(maven.Path);
                var hint = Text(node, "MMC-hint") ?? Text(node, "hint");
                if (hint is not (null or "local")) throw new NotSupportedException("暂不支持 MMC 库提示：" + hint);
                var filename = Text(node, "MMC-filename") ?? Text(node, "filename");
                if (library.Url is not null) ValidateUrl(library.Url);
                var repository = library.Url ?? "https://libraries.minecraft.net/";
                library.Downloads ??= new LibraryDownloads();
                if (library.Downloads.Artifact is not null || library.Downloads.Classifiers is null)
                    library.Downloads.Artifact = PrepareDownload(library.Downloads.Artifact ?? new DownloadInfo(),
                        maven.Path, repository, hint == "local", filename);
                foreach (var (classifier, download) in library.Downloads.Classifiers ?? [])
                {
                    ValidatePath(classifier);
                    if (classifier.Contains('/') || classifier.Contains('\\') || download is null)
                        throw new InvalidDataException("MMC classifier 无效：" + classifier);
                    PrepareDownload(download, (maven with { Classifier = classifier }).Path, repository, hint == "local", null);
                }
                library.Url = null;
                _libraries.Add(library);
            }
    }

    private DownloadInfo PrepareDownload(DownloadInfo download, string mavenPath, string repository, bool local, string? filename)
    {
        ValidateDownload(download);
        var path = (download.Path ?? mavenPath).Replace('\\', '/');
        var lookup = local ? filename ?? System.IO.Path.GetFileName(path) : path;
        ValidatePath(path);
        ValidatePath(lookup);
        _bundled.TryGetValue(lookup, out var source);
        if (source is null && local) _bundled.TryGetValue(path, out source);
        if (local && source is null) throw new InvalidDataException("MMC 缺少内嵌本地库：" + lookup);
        download.Path = path;
        download.Url = source is null ? download.Url ?? repository.TrimEnd('/') + "/" + mavenPath : null;
        if (download.Url is not null) ValidateUrl(download.Url);
        RegisterFile(path, source, download);
        return download;
    }

    private void RegisterFile(string path, ZipArchiveEntry? source, DownloadInfo? download)
    {
        foreach (var existing in _files.Keys)
        {
            if (existing.Equals(path, StringComparison.OrdinalIgnoreCase) && existing != path ||
                existing.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MMC 库路径大小写或文件/目录冲突：" + path);
        }
        if (_files.TryGetValue(path, out var previous))
        {
            if (previous.Source != source || previous.Download is { } declared && download is not null &&
                (source is null && declared.Url != download.Url || declared.Sha1 is not null && download.Sha1 is not null &&
                    !declared.Sha1.Equals(download.Sha1, StringComparison.OrdinalIgnoreCase) ||
                    declared.Size is not null && download.Size is not null && declared.Size != download.Size))
                throw new InvalidDataException("MMC 库文件声明冲突：" + path);
            if (download is not null && previous.Download is { } metadata)
            {
                download.Sha1 ??= metadata.Sha1;
                download.Size ??= metadata.Size;
            }
            download ??= previous.Download;
        }
        _files[path] = (source, download);
    }

    public async Task ValidateBundledFilesAsync(CancellationToken ct)
    {
        foreach (var item in _files.Values.Where(item => item.Source is not null && item.Download is not null))
        {
            ct.ThrowIfCancellationRequested();
            var source = item.Source!;
            if (item.Download!.Size is { } size && size != source.Length)
                throw new InvalidDataException("MMC 内嵌库长度与元数据不一致：" + source.FullName);
            if (item.Download.Sha1 is not { } expected) continue;
            await using var stream = source.Open();
            var actual = Convert.ToHexStringLower(await System.Security.Cryptography.SHA1.HashDataAsync(stream, ct).ConfigureAwait(false));
            if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MMC 内嵌库哈希与元数据不一致：" + source.FullName);
        }
    }

    public async Task<IReadOnlyList<InstallManifestFile>> ApplyAsync(
        VersionRepository repository, string id, CancellationToken ct)
    {
        if (!HasChanges) return [];
        var profile = await repository.GetVersionAsync(id, ct).ConfigureAwait(false);
        profile = VersionMerger.Clone(profile);
        var scope = "wmcl-packs/" + id + "/";
        foreach (var library in profile.Libraries)
        {
            var maven = MavenName.Parse(library.Name);
            var artifactPath = library.Downloads?.Artifact?.Path ?? maven.Path;
            if (_bundled.TryGetValue(artifactPath, out var source))
            {
                library.Downloads ??= new LibraryDownloads();
                library.Downloads.Artifact ??= new DownloadInfo();
                RegisterFile(artifactPath, source, library.Downloads.Artifact);
                library.Downloads.Artifact.Path = scope + artifactPath;
                library.Downloads.Artifact.Url = null;
                library.Url = null;
            }
            foreach (var (classifier, download) in library.Downloads?.Classifiers ?? [])
            {
                var nativePath = download.Path ?? (maven with { Classifier = classifier }).Path;
                if (!_bundled.TryGetValue(nativePath, out var nativeSource)) continue;
                RegisterFile(nativePath, nativeSource, download);
                download.Path = scope + nativePath;
                download.Url = null;
            }
        }
        foreach (var plan in _libraries)
        {
            var library = JsonSerializer.Deserialize<Library>(JsonSerializer.Serialize(plan))!;
            if (library.Downloads!.Artifact is { } artifact) artifact.Path = scope + artifact.Path;
            foreach (var classifier in library.Downloads.Classifiers?.Values.AsEnumerable() ?? [])
                classifier.Path = scope + classifier.Path;
            var key = LibraryKey(library);
            if (library.Rules is null)
                profile.Libraries.RemoveAll(existing => LibraryKey(existing) == key && existing.Rules is null);
            profile.Libraries.Add(library);
        }
        await ValidateBundledFilesAsync(ct).ConfigureAwait(false);
        foreach (var patch in _patches) ApplyPatch(profile, patch);
        ApplyTweakersAndTraits(profile);
        var managed = new List<InstallManifestFile>();
        foreach (var (path, item) in _files.Where(item => item.Value.Source is not null))
        {
            ct.ThrowIfCancellationRequested();
            var sourceEntry = item.Source!;
            var destination = FileUtil.SafeChildPath(System.IO.Path.Combine(repository.GameDirectory, "libraries"), scope + path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            await using (var source = sourceEntry.Open())
            await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            managed.Add(new InstallManifestFile("libraries/" + scope + path,
                await InstallManifestStore.ComputeFileSha1Async(destination, ct), "MMC-library", ArchivePath: sourceEntry.FullName));
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(repository.GetVersionDirectory(id), id + ".json"),
            JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        return managed;
    }

    public static async Task<IReadOnlyList<InstallManifestFile>> RemoteFilesAsync(
        VersionRepository repository, string id, VersionJson profile, CancellationToken ct)
    {
        var files = new List<InstallManifestFile>();
        foreach (var library in LibraryResolver.Resolve(profile, RuleContext.CreateDefault())
                     .Where(library => library.RelativePath.StartsWith("wmcl-packs/" + id + "/", StringComparison.Ordinal) && library.Url is not null)
                     .DistinctBy(library => library.RelativePath, StringComparer.OrdinalIgnoreCase))
            files.Add(new InstallManifestFile("libraries/" + library.RelativePath,
                await FileUtil.Sha1Async(FileUtil.SafeChildPath(System.IO.Path.Combine(repository.GameDirectory, "libraries"), library.RelativePath), ct).ConfigureAwait(false),
                "MMC-library", library.Url));
        return files;
    }

    private static void ApplyPatch(VersionJson profile, JsonElement patch)
    {
        profile.MainClass = Text(patch, "mainClass") ?? profile.MainClass;
        if (patch.TryGetProperty("assetIndex", out var assets)) profile.AssetIndex = assets.Deserialize<AssetIndexInfo>();
        profile.JavaVersion = JavaVersion(patch) ?? profile.JavaVersion;
        profile.Arguments ??= new Arguments();
        if (Text(patch, "minecraftArguments") is { } legacy)
        {
            profile.Arguments.Game = SplitArguments(legacy).Select(Literal).ToList();
            profile.MinecraftArguments = null;
        }
        else if (profile.MinecraftArguments is { } inherited)
        {
            profile.Arguments.Game = SplitArguments(inherited).Select(Literal).ToList();
            profile.MinecraftArguments = null;
        }
        if (patch.TryGetProperty("arguments", out var modern))
        {
            var arguments = modern.Deserialize<Arguments>()!;
            profile.Arguments.Game.AddRange(arguments.Game);
            profile.Arguments.Jvm.AddRange(arguments.Jvm);
        }
        profile.Arguments.Jvm.AddRange(Strings(patch, "+jvmArgs").Select(Literal));
    }

    private void ApplyTweakersAndTraits(VersionJson profile)
    {
        if (profile.Arguments is null) return;
        var tweakers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < profile.Arguments.Game.Count; index++)
            if (profile.Arguments.Game[index].Values.SequenceEqual(["--tweakClass"]))
                tweakers.UnionWith(profile.Arguments.Game[index + 1].Values);
        foreach (var tweaker in _patches.SelectMany(patch => Strings(patch, "+tweakers")).Where(tweakers.Add))
        {
            profile.Arguments.Game.Add(Literal("--tweakClass"));
            profile.Arguments.Game.Add(Literal(tweaker));
        }
        if (_patches.SelectMany(patch => Strings(patch, "+traits")).Contains("FirstThreadOnMacOS"))
            profile.Arguments.Jvm.Add(new ArgumentEntry
            {
                Values = ["-XstartOnFirstThread"],
                Rules = [new Rule { Action = "allow", Os = new RuleOs { Name = "osx" } }]
            });
    }

    private static ArgumentEntry Literal(string value) => new() { IsLiteral = true, Values = [value] };

    private static string LibraryKey(Library library) => MavenName.Parse(library.Name).Key;

    private static JavaVersionInfo? JavaVersion(JsonElement patch)
    {
        if (patch.TryGetProperty("javaVersion", out var java))
        {
            var version = java.Deserialize<JavaVersionInfo>();
            if (version is null || version.MajorVersion <= 0) throw new InvalidDataException("MMC Java 主版本无效。");
            return version;
        }
        if (!patch.TryGetProperty("compatibleJavaMajors", out var majors)) return null;
        if (majors.ValueKind != JsonValueKind.Array || majors.GetArrayLength() == 0)
            throw new InvalidDataException("MMC compatibleJavaMajors 必须是非空数组。");
        var values = majors.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var major) && major > 0
            ? major : throw new InvalidDataException("MMC Java 主版本无效。")).ToArray();
        return new JavaVersionInfo { MajorVersion = values.Max() };
    }

    private static IReadOnlyList<string> SplitArguments(string text)
    {
        var values = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        var started = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\\' && index + 1 < text.Length && (text[index + 1] == quote || text[index + 1] == '"'))
            {
                token.Append(text[++index]);
                started = true;
            }
            else if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                else token.Append(character);
            }
            else if (character is '\'' or '"') { quote = character; started = true; }
            else if (char.IsWhiteSpace(character))
            {
                if (started) values.Add(token.ToString());
                token.Clear();
                started = false;
            }
            else { token.Append(character); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("MMC minecraftArguments 引号未闭合。");
        if (started) values.Add(token.ToString());
        return values;
    }

    private static JsonElement[] Array(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return [];
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray()
            : throw new InvalidDataException("MMC 字段必须是数组：" + property);
    }

    private static string[] Strings(JsonElement root, string property) => Array(root, property).Select(value =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("MMC 参数必须是字符串：" + property)).ToArray();

    private static string? Text(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("MMC 元数据必须是对象。");
        if (!root.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw new InvalidDataException("MMC 字段必须是字符串：" + property);
    }

    private static void ValidateFormat(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("formatVersion", out var format) &&
            (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version) || version != 1))
            throw new InvalidDataException("不支持的 MMC 格式版本。");
    }

    private static void ValidatePath(string path) => ModpackContentPaths.Validate(path, System.IO.Path.GetTempPath(), "MMC");

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidDataException("MMC 下载地址无效：" + url);
    }

    private static void ValidateDownload(DownloadInfo? download)
    {
        if (download?.Url is { } url) ValidateUrl(url);
        if (download?.Sha1 is { } hash && (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) || download?.Size is < 0)
            throw new InvalidDataException("MMC 库哈希或长度无效。");
    }
}
