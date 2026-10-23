using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;
using WMCL.Modding.CurseForge;

namespace WMCL.Modding.Modpack;

internal sealed record McbbsManifestFile(
    string Type,
    bool Force,
    string? Path,
    string? Hash,
    long ProjectId,
    long FileId,
    string? FileName,
    string? Url,
    string? ResolvedHash = null,
    long? ResolvedSize = null)
{
    public string Identity => Type switch
    {
        "addon" => "addon:" + Path,
        "curse" => $"curse:{ProjectId}:{FileId}",
        _ => throw new NotSupportedException("不支持的 MCBBS 文件类型：" + Type)
    };

    public string? TargetPath => Type switch
    {
        "addon" => Path,
        "curse" when !string.IsNullOrWhiteSpace(FileName) => "mods/" + FileName,
        _ => null
    };
}

internal sealed record McbbsLaunchConstraints(
    int? MinMemoryMb,
    IReadOnlyList<int> SupportJava,
    IReadOnlyList<string> LaunchArguments,
    IReadOnlyList<string> JavaArguments,
    string? AuthlibInjectorServer)
{
    public static McbbsLaunchConstraints Empty { get; } = new(null, [], [], [], null);
}

internal sealed record McbbsManifestDocument(
    string RawJson,
    string? ManifestType,
    int? ManifestVersion,
    string? Name,
    string? Version,
    string? FileApi,
    bool ForceUpdate,
    IReadOnlyList<McbbsManifestFile> Files,
    McbbsLaunchConstraints LaunchInfo,
    IReadOnlyList<Library> Libraries,
    JsonElement Root)
{
    public static McbbsManifestDocument Parse(string json, string directory, string id)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, json, directory, id);
    }

    public static McbbsManifestDocument Parse(JsonElement root, string rawJson, string directory, string id)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("MCBBS manifest 必须是 JSON 对象。");

        var manifestType = Text(root, "manifestType");
        if (root.TryGetProperty("manifestType", out _) && manifestType != "minecraftModpack")
            throw new InvalidDataException("不支持的 MCBBS 整合包格式。");

        int? manifestVersion = null;
        if (root.TryGetProperty("manifestVersion", out var version))
        {
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var parsedVersion))
                throw new InvalidDataException("MCBBS manifestVersion 无效。");
            manifestVersion = parsedVersion;
            if (parsedVersion != 1)
                throw new InvalidDataException("不支持的 MCBBS 整合包格式版本。");
        }

        var fileApi = Text(root, "fileApi");
        if (!string.IsNullOrWhiteSpace(fileApi))
        {
            var endpoint = DownloadUri(fileApi);
            if (endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
                throw new InvalidDataException("MCBBS fileApi 必须是下载根地址。");
        }

        var forceUpdate = false;
        if (root.TryGetProperty("forceUpdate", out var force))
        {
            if (force.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("MCBBS forceUpdate 必须是布尔值。");
            forceUpdate = force.GetBoolean();
        }

        var files = ReadFiles(root, directory, id);
        var launchInfo = ReadLaunchInfo(root);
        var libraries = ReadLibraries(root);
        return new McbbsManifestDocument(
            rawJson,
            manifestType,
            manifestVersion,
            Text(root, "name"),
            Text(root, "version"),
            fileApi,
            forceUpdate,
            files,
            launchInfo,
            libraries,
            root.Clone());
    }

    private static IReadOnlyList<Library> ReadLibraries(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries)) return [];
        if (libraries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MCBBS libraries 必须是数组。");
        var result = new List<Library>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var paths = new Dictionary<string, DownloadInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in libraries.EnumerateArray())
        {
            Library library;
            try
            {
                library = item.Deserialize<Library>()
                    ?? throw new InvalidDataException("MCBBS library 条目无效。");
            }
            catch (JsonException ex)
            { throw new InvalidDataException("MCBBS library 条目无效。", ex); }
            MavenName maven;
            try { maven = MavenName.Parse(library.Name); }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            { throw new InvalidDataException("MCBBS library 名称无效：" + library.Name, ex); }
            foreach (var segment in new[] { maven.Group, maven.Artifact, maven.Version, maven.Classifier ?? "_", maven.Extension })
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                    segment.Any(character => char.IsControl(character) || "/\\:@<>\"|?*".Contains(character)))
                    throw new InvalidDataException("MCBBS library 名称无效：" + library.Name);
            ModpackContentPaths.ValidateLibraryPath(maven.Path);
            var key = maven.Key + "|" + JsonSerializer.Serialize(library.Rules ?? []);
            if (!keys.Add(key)) throw new InvalidDataException("MCBBS library 重复：" + library.Name);
            var repository = library.Url ?? "https://libraries.minecraft.net/";
            DownloadUri(repository);
            library.Downloads ??= new LibraryDownloads();
            if (library.Downloads.Artifact is not null || library.Downloads.Classifiers is null && library.Natives is null)
            {
                library.Downloads.Artifact ??= new DownloadInfo();
                PrepareLibraryDownload(library.Downloads.Artifact, maven.Path, repository, paths);
            }
            foreach (var (classifier, download) in library.Downloads?.Classifiers ?? [])
            {
                if (string.IsNullOrWhiteSpace(classifier) || classifier is "." or ".." ||
                    classifier.Any(character => char.IsControl(character) || "/\\:@<>\"|?*".Contains(character)) || download is null)
                    throw new InvalidDataException("MCBBS library classifier 无效：" + classifier);
                PrepareLibraryDownload(download, (maven with { Classifier = classifier }).Path, repository, paths);
            }
            library.Url = null;
            result.Add(library);
        }
        return result;
    }

    private static void PrepareLibraryDownload(DownloadInfo download, string fallbackPath,
        string repository, Dictionary<string, DownloadInfo> paths)
    {
        var path = (download.Path ?? fallbackPath).Replace('\\', '/');
        ModpackContentPaths.ValidateLibraryPath(path);
        if (download.Sha1 is not null && (download.Sha1.Length != 40 || !download.Sha1.All(Uri.IsHexDigit)) ||
            download.Size is < 0)
            throw new InvalidDataException("MCBBS library SHA-1 或大小无效：" + path);
        download.Path = path;
        download.Url = string.IsNullOrWhiteSpace(download.Url)
            ? repository.TrimEnd('/') + "/" + fallbackPath
            : download.Url;
        DownloadUri(download.Url);
        foreach (var (existing, metadata) in paths)
        {
            if (existing.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                if (existing != path || metadata.Url != download.Url ||
                    !string.Equals(metadata.Sha1, download.Sha1, StringComparison.OrdinalIgnoreCase) || metadata.Size != download.Size)
                    throw new InvalidDataException("MCBBS library 文件声明冲突：" + path);
            }
            else if (existing.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MCBBS library 文件/目录冲突：" + path);
        }
        paths[path] = download;
    }

    internal static McbbsLaunchConstraints ReadLaunchInfo(JsonElement root)
    {
        var minMemory = (int?)null;
        var supportJava = new List<int>();
        var launchArguments = new List<string>();
        var javaArguments = new List<string>();
        if (root.TryGetProperty("launchInfo", out var info))
        {
            if (info.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("MCBBS launchInfo 必须是对象。");
            if (info.TryGetProperty("minMemory", out var memory))
            {
                if (memory.ValueKind != JsonValueKind.Number || !memory.TryGetInt32(out var value) || value < 0)
                    throw new InvalidDataException("MCBBS launchInfo.minMemory 无效。");
                minMemory = value;
            }
            ReadJavaVersions(info, supportJava);
            ReadStrings(info, "launchArgument", launchArguments);
            ReadStrings(info, "javaArgument", javaArguments);
        }

        string? authServer = null;
        if (root.TryGetProperty("serverInfo", out var serverInfo))
        {
            if (serverInfo.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("MCBBS serverInfo 必须是对象。");
            if (serverInfo.TryGetProperty("authlibInjectorServer", out var auth))
            {
                if (auth.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("MCBBS serverInfo.authlibInjectorServer 必须是字符串。");
                authServer = auth.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(authServer)) DownloadUri(authServer);
            }
        }
        return new McbbsLaunchConstraints(minMemory, supportJava, launchArguments, javaArguments, authServer);
    }

    private static void ReadJavaVersions(JsonElement info, List<int> target)
    {
        if (!info.TryGetProperty("supportJava", out var java)) return;
        if (java.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MCBBS launchInfo.supportJava 必须是数组。");
        foreach (var item in java.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var value) || value < 1)
                throw new InvalidDataException("MCBBS launchInfo.supportJava 包含无效版本。");
            if (!target.Contains(value)) target.Add(value);
        }
    }

    private static void ReadStrings(JsonElement info, string name, List<string> target)
    {
        if (!info.TryGetProperty(name, out var values)) return;
        if (values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"MCBBS launchInfo.{name} 必须是数组。");
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"MCBBS launchInfo.{name} 包含非字符串参数。");
            target.Add(item.GetString() ?? "");
        }
    }

    public McbbsManifestDocument WithFiles(IEnumerable<McbbsManifestFile> files, string directory, string id, string? fileApi = null)
    {
        var root = JsonNode.Parse(RawJson)!.AsObject();
        if (fileApi is not null) root["fileApi"] = fileApi;
        root["files"] = new JsonArray(files.Select(file => file.Type == "addon"
            ? (JsonNode)new JsonObject
            {
                ["type"] = file.Type,
                ["force"] = file.Force,
                ["path"] = file.Path,
                ["hash"] = file.Hash
            }
            : new JsonObject
            {
                ["type"] = file.Type,
                ["force"] = file.Force,
                ["projectID"] = file.ProjectId,
                ["fileID"] = file.FileId,
                ["fileName"] = file.FileName,
                ["url"] = file.Url
            }).ToArray());
        return Parse(root.ToJsonString(), directory, id);
    }

    internal static async Task<McbbsManifestFile> ResolveCurseAsync(
        McbbsManifestFile file, CurseForgeClient client, CancellationToken ct)
    {
        var metadata = string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.Url)
            ? await client.GetFileByIdAsync(file.ProjectId, file.FileId, ct).ConfigureAwait(false)
            : new CurseForgeFile(file.FileId, file.FileName, file.FileName, DateTimeOffset.MinValue,
                0, file.Url, null, [], [], ProjectId: file.ProjectId);
        metadata = metadata with
        {
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? metadata.FileName : file.FileName,
            DownloadUrl = string.IsNullOrWhiteSpace(file.Url) ? metadata.DownloadUrl : file.Url
        };
        CurseForgeClient.ValidateDownloadMetadata(metadata);
        return file with
        {
            FileName = metadata.FileName,
            Url = metadata.DownloadUrl,
            ResolvedHash = metadata.Sha1,
            ResolvedSize = metadata.Length > 0 ? metadata.Length : null
        };
    }

    private static List<McbbsManifestFile> ReadFiles(JsonElement root, string directory, string id)
    {
        if (!root.TryGetProperty("files", out var files)) return [];
        if (files.ValueKind != JsonValueKind.Array) throw new InvalidDataException("MCBBS files 必须是数组。");
        var result = new List<McbbsManifestFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object) throw new InvalidDataException("MCBBS 文件条目无效。");
            var type = Text(file, "type") ?? throw new InvalidDataException("MCBBS 文件缺少 type。");
            var force = false;
            if (file.TryGetProperty("force", out var forceElement))
            {
                if (forceElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("MCBBS 文件 force 必须是布尔值。");
                force = forceElement.GetBoolean();
            }

            McbbsManifestFile parsed;
            if (type == "addon")
            {
                var path = Text(file, "path")?.Replace('\\', '/');
                var hash = Text(file, "hash");
                if (string.IsNullOrWhiteSpace(path) || !IsSha1(hash))
                    throw new InvalidDataException("MCBBS addon 缺少有效路径或 SHA-1。");
                ModpackContentPaths.Validate(path, directory, id);
                parsed = new McbbsManifestFile(type, force, path, hash, 0, 0, null, null);
            }
            else if (type == "curse")
            {
                var project = PositiveId(file, "projectID");
                var fileId = PositiveId(file, "fileID");
                var name = Text(file, "fileName");
                var url = Text(file, "url");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (name != FileUtil.SafeFileName(name))
                        throw new InvalidDataException("MCBBS CurseForge 文件名必须是单段名称。");
                    ModpackContentPaths.Validate("mods/" + name, directory, id);
                }
                if (!string.IsNullOrWhiteSpace(url)) DownloadUri(url);
                parsed = new McbbsManifestFile(type, force, null, null, project, fileId, name, url);
            }
            else
            {
                throw new NotSupportedException("不支持的 MCBBS 文件类型：" + type);
            }

            if (!paths.Add(parsed.TargetPath ?? parsed.Identity))
                throw new InvalidDataException("MCBBS 文件目标路径重复：" + (parsed.TargetPath ?? parsed.Identity));
            if (!identities.Add(parsed.Identity))
                throw new InvalidDataException("MCBBS 文件标识重复：" + parsed.Identity);
            result.Add(parsed);
        }

        var targets = result.Select(file => file.TargetPath).Where(path => path is not null)
            .Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var separator = target.IndexOf('/');
            while (separator >= 0)
            {
                if (targets.Contains(target[..separator]))
                    throw new InvalidDataException("MCBBS 文件与目录路径冲突：" + target);
                separator = target.IndexOf('/', separator + 1);
            }
        }
        return result;
    }

    private static bool IsSha1(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static long PositiveId(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var id) && id > 0
            ? id
            : throw new InvalidDataException("MCBBS 文件缺少有效标识：" + name);

    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("MCBBS 字段必须是字符串：" + name);
        return value.GetString();
    }

    internal static Uri DownloadUri(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new InvalidDataException("MCBBS 下载地址无效：" + url);
}
