using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.App.Services;

internal static class StagedAssetIndexResolver
{
    public static bool IsAliasReferenced(string gameRoot, string path)
    {
        var relative = Path.GetRelativePath(gameRoot, path).Replace('\\', '/');
        var id = Path.GetFileNameWithoutExtension(path);
        var separator = id.LastIndexOf('-');
        if (!SharedAssetComparer.IsAssetIndex(relative) || separator < 1 ||
            id.Length - separator - 1 != 40 || !id[(separator + 1)..].All(char.IsAsciiHexDigit)) return false;
        var versions = Path.Combine(gameRoot, "versions");
        if (!Directory.Exists(versions)) return false;
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var versionId = Path.GetFileName(directory);
            var profilePath = Path.Combine(directory, versionId + ".json");
            if (!File.Exists(profilePath)) continue;
            try
            {
                InstallManifestStore.ManagedFilePath(gameRoot, Path.GetRelativePath(gameRoot, profilePath), versionId);
                var profile = JsonNode.Parse(File.ReadAllText(profilePath));
                if (profile?["assetIndex"]?["id"]?.GetValue<string>() == id || profile?["assets"]?.GetValue<string>() == id)
                    return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
                JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task ResolveAsync(string gameRoot, string stageRoot, string instanceId, CancellationToken ct = default)
    {
        var profileRelative = $"versions/{instanceId}/{instanceId}.json";
        var profilePath = InstallManifestStore.ManagedFilePath(stageRoot, profileRelative, instanceId);
        var profile = JsonNode.Parse(await File.ReadAllTextAsync(profilePath, ct)) as JsonObject
            ?? throw new InvalidDataException("暂存实例配置不是对象");
        if (profile["assetIndex"] is not JsonObject reference) return;
        var info = reference.Deserialize<AssetIndexInfo>() ?? throw new InvalidDataException("暂存资源索引引用无效");
        if (string.IsNullOrEmpty(info.Id) || info.Id is "." or ".." ||
            !info.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new InvalidDataException("资源索引 ID 不是安全的文件名：" + info.Id);
        var relative = "assets/indexes/" + info.Id + ".json";
        var source = InstallManifestStore.ManagedFilePath(stageRoot, relative, instanceId);
        var destination = InstallManifestStore.ManagedFilePath(gameRoot, relative, instanceId);
        if (!File.Exists(source)) throw new InvalidDataException("当前实例引用的暂存资源索引缺失：" + relative);
        if (!File.Exists(destination)) return;
        var hash = await FileUtil.Sha1Async(source, ct);
        if (string.Equals(await FileUtil.Sha1Async(destination, ct), hash, StringComparison.OrdinalIgnoreCase)) return;
        if (info.Sha1 is not { Length: 40 } || !info.Sha1.All(char.IsAsciiHexDigit) ||
            !string.Equals(hash, info.Sha1, StringComparison.OrdinalIgnoreCase) ||
            info.Size is null or < 0 || new FileInfo(source).Length != info.Size)
            throw new InvalidDataException("冲突资源索引未通过当前实例的 SHA1/长度校验：" + relative);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(source, ct));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("冲突资源索引没有有效的 objects：" + relative);
        var index = document.Deserialize<AssetIndex>() ?? throw new InvalidDataException("冲突资源索引无效：" + relative);
        if (index.Virtual || index.MapToResources)
            throw new InvalidDataException("旧版虚拟/共享 resources 资源索引冲突暂不支持安全并存：" + relative);
        foreach (var (assetPath, asset) in index.Objects)
        {
            ct.ThrowIfCancellationRequested();
            FileUtil.SafeChildPath(Path.Combine(stageRoot, "assets", "objects"), assetPath);
            if (asset is null || asset.Hash is not { Length: 40 } || !asset.Hash.All(char.IsAsciiHexDigit) || asset.Size < 0)
                throw new InvalidDataException("冲突资源索引包含无效的对象：" + assetPath);
        }
        var alias = info.Id + "-" + hash;
        var aliasRelative = "assets/indexes/" + alias + ".json";
        var aliasSource = InstallManifestStore.ManagedFilePath(stageRoot, aliasRelative, instanceId);
        var aliasDestination = InstallManifestStore.ManagedFilePath(gameRoot, aliasRelative, instanceId);
        foreach (var path in new[] { aliasSource, aliasDestination })
            if (Directory.Exists(path) || File.Exists(path) && !string.Equals(await FileUtil.Sha1Async(path, ct), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("资源索引别名已存在且内容不同，未覆盖：" + aliasRelative);
        var manifestPath = InstallManifestStore.ManagedFilePath(stageRoot, $"versions/{instanceId}/WMCL/install.json", instanceId);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, ct)) as JsonObject
            ?? throw new InvalidDataException("暂存安装清单不是对象");
        InstallManifestStore.ValidateSchemaVersion(manifest["SchemaVersion"]?.GetValue<int>() ?? InstallManifestStore.SupportedSchemaVersion);
        var managed = manifest["ManagedFiles"]?.Deserialize<List<InstallManifestFile>>()
            ?? throw new InvalidDataException("暂存安装清单没有受管文件");
        if (!managed.Any(file => file.RelativePath.Replace('\\', '/') == profileRelative))
            throw new InvalidDataException("暂存实例配置未记录于安装清单");
        ct.ThrowIfCancellationRequested();
        if (File.Exists(aliasSource)) File.Delete(source);
        else File.Move(source, aliasSource);
        reference["id"] = alias;
        await File.WriteAllTextAsync(profilePath, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        var profileHash = await FileUtil.Sha1Async(profilePath, ct);
        for (var position = 0; position < managed.Count; position++)
        {
            var file = managed[position];
            var normalized = file.RelativePath.Replace('\\', '/');
            if (normalized == profileRelative)
                managed[position] = file with { Sha1 = profileHash, Url = null, ArchiveId = null, ArchivePath = null };
            else if (normalized == relative)
                managed[position] = file with { RelativePath = aliasRelative, Sha1 = hash };
        }
        if (!managed.Any(file => file.RelativePath == aliasRelative))
            managed.Add(new InstallManifestFile(aliasRelative, hash, "assets", info.Url));
        manifest["ManagedFiles"] = JsonSerializer.SerializeToNode(managed);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
    }
}
