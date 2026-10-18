using System.Text.Json;
using PCL.Core;
using PCL.Core.Http;
using PCL.Core.Logging;
using PCL.Minecraft.Models;

namespace PCL.Minecraft;

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
        GameDirectory = PclPaths.Expand(gameDirectory);
        VersionsDirectory = Path.Combine(GameDirectory, "versions");
        _manifestCachePath = Path.Combine(PclPaths.CacheDirectory, "version_manifest_v2.json");
    }

    public string GameDirectory { get; }
    public string VersionsDirectory { get; }

    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
        => await GetVersionInternalAsync(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct)
            .ConfigureAwait(false);

    private async Task<VersionJson> GetVersionInternalAsync(
        string id,
        HashSet<string> stack,
        CancellationToken ct)
    {
        if (!stack.Add(id))
            throw new InvalidOperationException($"版本继承出现循环：{id}");

        try
        {
            var versionDirectory = Path.Combine(VersionsDirectory, id);
            var jsonPath = Path.Combine(versionDirectory, id + ".json");

            VersionJson? json = null;
            if (File.Exists(jsonPath))
            {
                try
                {
                    json = JsonSerializer.Deserialize<VersionJson>(
                        await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false), JsonOptions);
                }
                catch (JsonException ex)
                {
                    Log.Warn($"本地版本 JSON 损坏，将重新下载：{jsonPath}（{ex.Message}）");
                }
            }

            if (json is null)
            {
                var manifest = await GetManifestAsync(ct: ct).ConfigureAwait(false);
                var summary = manifest.Versions.FirstOrDefault(v =>
                    string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException($"版本清单中找不到 {id}。");

                var text = await Http.GetStringAsync(summary.Url, ct).ConfigureAwait(false);
                json = JsonSerializer.Deserialize<VersionJson>(text, JsonOptions)
                       ?? throw new InvalidDataException($"无法解析版本 JSON：{id}");

                Directory.CreateDirectory(versionDirectory);
                await File.WriteAllTextAsync(jsonPath, text, ct).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(json.InheritsFrom))
            {
                var parent = await GetVersionInternalAsync(json.InheritsFrom, stack, ct).ConfigureAwait(false);
                json = VersionMerger.Merge(parent, json);
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
