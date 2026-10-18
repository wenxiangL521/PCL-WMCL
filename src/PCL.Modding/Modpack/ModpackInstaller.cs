using System.IO.Compression;
using System.Text.Json;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;
using PCL.Minecraft;
using PCL.Modding.Fabric;
using PCL.Modding.Modrinth;

namespace PCL.Modding.Modpack;

public sealed record ModpackFile(
    string Path,
    IReadOnlyList<string> Downloads,
    string? Sha1,
    string? EnvironmentClient);

public sealed record ModpackIndex(
    string Name,
    string VersionId,
    string Minecraft,
    string? FabricLoader,
    string? Forge,
    string? NeoForge,
    IReadOnlyList<ModpackFile> Files);

/// <summary>
/// Modrinth .mrpack 安装。对照 PCL 的 InstallPackModrinth：
/// 读取 modrinth.index.json → 安装依赖加载器 → 解压 overrides/client-overrides → 下载 files。
/// 当前仅支持 Fabric 整合包。
/// </summary>
public sealed class ModpackInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly FabricInstaller _fabric;
    private readonly ModrinthClient _client;

    public ModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ModrinthClient? client = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _client = client ?? new ModrinthClient();
    }

    public static ModpackIndex ParseIndex(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("dependencies", out var dependencies) ||
            !dependencies.TryGetProperty("minecraft", out var minecraft))
            throw new InvalidDataException("Modrinth 整合包未提供 Minecraft 版本信息。");

        string? GetDependency(string name) =>
            dependencies.TryGetProperty(name, out var value) ? value.GetString() : null;

        var files = new List<ModpackFile>();
        if (root.TryGetProperty("files", out var filesElement) &&
            filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in filesElement.EnumerateArray())
            {
                var path = file.TryGetProperty("path", out var pathElement)
                    ? pathElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var downloads = file.TryGetProperty("downloads", out var downloadsElement) &&
                                downloadsElement.ValueKind == JsonValueKind.Array
                    ? downloadsElement.EnumerateArray()
                        .Select(element => element.GetString())
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .Cast<string>()
                        .ToList()
                    : [];

                var sha1 = file.TryGetProperty("hashes", out var hashes) &&
                           hashes.TryGetProperty("sha1", out var sha1Element)
                    ? sha1Element.GetString()
                    : null;
                var environment = file.TryGetProperty("env", out var env) &&
                                  env.TryGetProperty("client", out var client)
                    ? client.GetString()
                    : null;

                files.Add(new ModpackFile(path, downloads, sha1, environment));
            }
        }

        return new ModpackIndex(
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "Modpack" : "Modpack",
            root.TryGetProperty("versionId", out var version) ? version.GetString() ?? "1.0" : "1.0",
            minecraft.GetString() ?? throw new InvalidDataException("缺少 Minecraft 版本。"),
            GetDependency("fabric-loader"),
            GetDependency("forge"),
            GetDependency("neoforge") ?? GetDependency("neo-forge"),
            files);
    }

    public async Task<string> InstallFromFileAsync(
        string mrpackPath,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("解析整合包", 0.02));
        using var archive = ZipFile.OpenRead(mrpackPath);
        var indexEntry = archive.GetEntry("modrinth.index.json")
                         ?? throw new InvalidDataException(
                             "不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");
        using (var stream = indexEntry.Open())
        using (var reader = new StreamReader(stream))
        {
            var index = ParseIndex(await reader.ReadToEndAsync(ct).ConfigureAwait(false));

            if (string.IsNullOrWhiteSpace(index.FabricLoader))
                throw new NotSupportedException(
                    "当前仅支持 Fabric 整合包" +
                    (index.Forge is not null ? "；该包需要 Forge。" :
                     index.NeoForge is not null ? "；该包需要 NeoForge。" : "。"));

            var id = CreateInstanceId(instanceName ?? index.Name + "-" + index.VersionId);
            progress?.Report(new JobProgress("安装整合包加载器", 0.05));

            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.05 + (update.Percent ?? 0) * 0.4, null, null, update.Detail)));
            await _fabric.InstallAsync(
                index.Minecraft, index.FabricLoader, loaderProgress, ct, id).ConfigureAwait(false);

            var instanceDirectory = _repository.GetVersionDirectory(id);
            progress?.Report(new JobProgress("解压整合包覆写", 0.5));
            ExtractOverrides(archive, instanceDirectory);
            await DownloadFilesAsync(archive, index, instanceDirectory, progress, ct)
                .ConfigureAwait(false);

            Log.Info($"Modrinth 整合包安装完成：{id}");
            return id;
        }
    }

    public async Task<string> InstallFromModrinthAsync(
        string projectId,
        string? gameVersion = null,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new JobProgress("获取整合包版本", 0.01));
        var versions = await _client.GetVersionsAsync(projectId, ct).ConfigureAwait(false);
        var version = versions
            .Where(item => gameVersion is null ||
                           item.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            .Where(item => item.Files.Count > 0)
            .OrderByDescending(item => item.DatePublished)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"找不到适用于 Minecraft {gameVersion ?? "任意版本"} 的整合包版本。");

        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files[0];
        var destination = Path.Combine(
            PclPaths.CacheDirectory, "modpacks", file.Filename);
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var adapter = new DownloadProgressAdapter(download => progress?.Report(new JobProgress(
                "下载整合包", download.Percent, (long)download.BytesPerSecond, null, file.Filename)));
            await new DownloadManager().DownloadAsync(
                [new DownloadItem
                {
                    Destination = destination,
                    Urls = [file.Url],
                    Sha1 = file.Sha1,
                    Size = file.Size
                }],
                new DownloadOptions { MaxConcurrency = 1 },
                adapter,
                ct).ConfigureAwait(false);
        }

        return await InstallFromFileAsync(destination, instanceName, progress, ct)
            .ConfigureAwait(false);
    }

    private async Task DownloadFilesAsync(
        ZipArchive archive,
        ModpackIndex index,
        string instanceDirectory,
        IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        var items = new List<DownloadItem>();
        foreach (var file in index.Files)
        {
            if (string.Equals(file.EnvironmentClient, "unsupported", StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = SafePath(instanceDirectory, file.Path);
            if (destination is null || File.Exists(destination) || file.Downloads.Count == 0)
                continue;

            items.Add(new DownloadItem
            {
                Destination = destination,
                Urls = file.Downloads,
                Sha1 = file.Sha1
            });
        }

        if (items.Count == 0)
        {
            progress?.Report(new JobProgress("整合包文件已就绪", 1));
            return;
        }

        var adapter = new DownloadProgressAdapter(download => progress?.Report(new JobProgress(
            "下载整合包文件", 0.55 + (download.Percent ?? 0) * 0.4,
            (long)download.BytesPerSecond, null, null)));
        await new DownloadManager()
            .DownloadAsync(items, new DownloadOptions { MaxConcurrency = 8 }, adapter, ct)
            .ConfigureAwait(false);
        progress?.Report(new JobProgress("整合包安装完成", 1));
    }

    private static void ExtractOverrides(ZipArchive archive, string instanceDirectory)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var relative = entry.FullName;
            if (relative.StartsWith("overrides/", StringComparison.Ordinal))
                relative = relative["overrides/".Length..];
            else if (relative.StartsWith("client-overrides/", StringComparison.Ordinal))
                relative = relative["client-overrides/".Length..];
            else
                continue;

            var destination = SafePath(instanceDirectory, relative);
            if (destination is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static string? SafePath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 || normalized.Split('/').Contains(".."))
            return null;

        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
    }

    private string CreateInstanceId(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('-', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Modpack";
        if (name.Length > 64) name = name[..64];

        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(_repository.VersionsDirectory, candidate)))
            candidate = name + "-" + suffix++;
        return candidate;
    }

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }

    private sealed class DownloadProgressAdapter(Action<DownloadProgress> action)
        : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
