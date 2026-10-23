using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Http;
using WMCL.Core.Logging;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

public sealed class VersionRepository
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _manifestCachePath;
    private readonly SemaphoreSlim _manifestLock = new(1, 1);
    private VersionManifest? _manifest;

    public VersionRepository(string gameDirectory)
    {
        GameDirectory = WmclPaths.Expand(gameDirectory);
        VersionsDirectory = Path.Combine(GameDirectory, "versions");
        _manifestCachePath = Path.Combine(WmclPaths.CacheDirectory, "version_manifest_v2.json");
    }

    public string GameDirectory { get; private set; }
    public string VersionsDirectory { get; private set; }

    public void SetGameDirectory(string gameDirectory)
    {
        GameDirectory = WmclPaths.Expand(gameDirectory);
        VersionsDirectory = Path.Combine(GameDirectory, "versions");
    }

    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default,
        bool allowCachedFallback = true)
    {
        if (!forceRefresh && _manifest is not null) return _manifest;

        await _manifestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _manifest is not null) return _manifest;

            var cacheFresh = File.Exists(_manifestCachePath) &&
                             DateTime.UtcNow - File.GetLastWriteTimeUtc(_manifestCachePath) < TimeSpan.FromMinutes(10);

            if (!forceRefresh && cacheFresh)
            {
                var cached = TryReadManifest(_manifestCachePath);
                if (cached is not null)
                {
                    _manifest = cached;
                    return cached;
                }
            }

            try
            {
                var text = await Http.GetStringAsync(ManifestUrl, ct).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<VersionManifest>(text, JsonOptions)
                               ?? throw new InvalidDataException("版本清单为空。");

                Directory.CreateDirectory(Path.GetDirectoryName(_manifestCachePath)!);
                await File.WriteAllTextAsync(_manifestCachePath, text, ct).ConfigureAwait(false);
                _manifest = manifest;
                return manifest;
            }
            catch (Exception ex) when (allowCachedFallback && ex is not OperationCanceledException)
            {
                var cached = TryReadManifest(_manifestCachePath);
                if (cached is not null)
                {
                    Log.Warn($"获取版本清单失败，使用缓存：{ex.Message}");
                    _manifest = cached;
                    return cached;
                }
                throw;
            }
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    public async Task<VersionJson> GetVersionAsync(string id, CancellationToken ct = default)
        => await GetVersionInternalAsync(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase), true, ct)
            .ConfigureAwait(false);

    public async Task<VersionJson> GetLocalVersionAsync(string id, CancellationToken ct = default)
        => await GetVersionInternalAsync(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, ct)
            .ConfigureAwait(false);

    private async Task<VersionJson> GetVersionInternalAsync(
        string id,
        HashSet<string> stack,
        bool allowDownload,
        CancellationToken ct)
    {
        InstanceManager.ValidateName(id);
        ct.ThrowIfCancellationRequested();
        if (!stack.Add(id))
            throw new InvalidOperationException($"版本继承出现循环：{id}");

        try
        {
            var versionDirectory = Path.Combine(VersionsDirectory, id);
            var jsonPath = Path.Combine(versionDirectory, id + ".json");

            VersionJson? json = null;
            string? profileText = null;
            if (File.Exists(jsonPath))
            {
                try
                {
                    profileText = await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false);
                    json = JsonSerializer.Deserialize<VersionJson>(profileText, JsonOptions);
                }
                catch (JsonException ex)
                {
                    if (!allowDownload) throw;
                    Log.Warn($"本地版本 JSON 损坏，将重新下载：{jsonPath}（{ex.Message}）");
                }
            }

            if (json is null)
            {
                if (!allowDownload) throw new InvalidDataException($"本地版本配置缺失或无效：{id}");
                var manifest = await GetManifestAsync(ct: ct).ConfigureAwait(false);
                var summary = manifest.Versions.FirstOrDefault(v =>
                    string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException($"版本清单中找不到 {id}。");

                var text = await Http.GetStringAsync(summary.Url, ct).ConfigureAwait(false);
                profileText = text;
                json = JsonSerializer.Deserialize<VersionJson>(text, JsonOptions)
                       ?? throw new InvalidDataException($"无法解析版本 JSON：{id}");

                Directory.CreateDirectory(versionDirectory);
                await File.WriteAllTextAsync(jsonPath, text, ct).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(json.InheritsFrom))
            {
                var parent = await GetVersionInternalAsync(json.InheritsFrom, stack, allowDownload, ct).ConfigureAwait(false);
                json = VersionMerger.Merge(parent, json);
            }
            else
            {
                using var document = JsonDocument.Parse(profileText!);
                json.GameVersionIdentity = MinecraftVersionIdentifier.FromProfile(json, document.RootElement, ReadManifestGameVersion(id))
                    ?? await ReadClientGameVersionAsync(versionDirectory, id, ct).ConfigureAwait(false)
                    ?? MinecraftVersionIdentifier.Fallback(id, json, document.RootElement);
                json.BaseGameVersion = json.GameVersionIdentity.Version;
            }

            return json;
        }
        finally
        {
            stack.Remove(id);
        }
    }

    public string GetVersionDirectory(string id)
    {
        InstanceManager.ValidateName(id);
        return Path.Combine(VersionsDirectory, id);
    }

    public string GetClientJarPath(string id) =>
        Path.Combine(GetVersionDirectory(id), id + ".jar");

    private string? ReadManifestGameVersion(string id)
    {
        try
        {
            var components = InstallManifestStore.Load(GameDirectory, id)?.Components;
            return components?.FirstOrDefault(component =>
                component.Key.Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.Value)).Value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"读取实例基座版本清单失败：{id}（{ex.Message}）");
            return null;
        }
    }

    private static async Task<MinecraftVersionIdentity?> ReadClientGameVersionAsync(
        string versionDirectory, string id, CancellationToken ct)
    {
        var jarPath = Path.Combine(versionDirectory, id + ".jar");
        if (!File.Exists(jarPath)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry("version.json");
            if (entry is null) return null;
            await using var stream = entry.Open();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { Length: > 0 and < 32 } clientName && !string.IsNullOrWhiteSpace(clientName))
                return MinecraftVersionIdentifier.Reliable(clientName, MinecraftVersionIdentitySource.ClientMetadata);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("id", out var versionId) &&
                versionId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(versionId.GetString()))
                return MinecraftVersionIdentifier.Reliable(versionId.GetString()!, MinecraftVersionIdentitySource.ClientMetadata);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Warn($"读取客户端基座版本失败：{jarPath}（{ex.Message}）");
        }
        return null;
    }

    public IReadOnlyList<InstalledVersion> ListInstalled()
    {
        if (!Directory.Exists(VersionsDirectory)) return [];

        var result = new List<InstalledVersion>();
        foreach (var directory in Directory.EnumerateDirectories(VersionsDirectory))
        {
            var id = Path.GetFileName(directory);
            var jsonPath = Path.Combine(directory, id + ".json");
            var jarPath = Path.Combine(directory, id + ".jar");
            if (!File.Exists(jsonPath)) continue;

            string? type = null;
            string? releaseTime = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (document.RootElement.TryGetProperty("type", out var t)) type = t.GetString();
                if (document.RootElement.TryGetProperty("releaseTime", out var r)) releaseTime = r.GetString();
            }
            catch
            {
                // ignore broken instance metadata
            }

            result.Add(new InstalledVersion(id, type, releaseTime, directory, File.Exists(jarPath)));
        }

        return result.OrderByDescending(v => v.ReleaseTime ?? "").ToList();
    }

    private static VersionManifest? TryReadManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<VersionManifest>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record InstalledVersion(
    string Id,
    string? Type,
    string? ReleaseTime,
    string Directory,
    bool HasClientJar);
