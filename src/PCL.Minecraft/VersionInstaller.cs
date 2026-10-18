using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using PCL.Core.IO;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;
using PCL.Minecraft.Models;

namespace PCL.Minecraft;

public sealed class InstallOptions
{
    public MirrorMode Mirror { get; init; } = MirrorMode.Auto;
    public int MaxConcurrency { get; init; } = 16;
    public bool VerifyExisting { get; init; }
    public bool Force { get; init; }
}

public sealed record InstallResult(
    string VersionId,
    int Files,
    long Bytes,
    TimeSpan Elapsed,
    string VersionDirectory);

public sealed class VersionInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly VersionRepository _repository;
    private readonly DownloadManager _downloads;

    public VersionInstaller(VersionRepository repository, DownloadManager? downloads = null)
    {
        _repository = repository;
        _downloads = downloads ?? new DownloadManager();
    }

    public async Task<InstallResult> InstallAsync(
        string versionId,
        InstallOptions? options = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new InstallOptions();
        InstanceManager.ValidateName(versionId);
        var started = Stopwatch.StartNew();
        var gameDirectory = _repository.GameDirectory;
        var versionsDirectory = _repository.VersionsDirectory;

        progress?.Report(new JobProgress("获取版本信息", 0.01));
        var version = await _repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);

        progress?.Report(new JobProgress("解析版本与依赖", 0.03));
        var context = RuleContext.CreateDefault();
        var libraries = LibraryResolver.Resolve(version, context);

        var items = new List<DownloadItem>();

        if (version.Downloads is not null &&
            version.Downloads.TryGetValue("client", out var client) &&
            !string.IsNullOrEmpty(client.Url))
        {
            var fileName = string.IsNullOrEmpty(client.Path) ? version.Id + ".jar" : client.Path;
            items.Add(DownloadItem.Create(
                client.Url,
                FileUtil.SafeChildPath(
                    Path.Combine(versionsDirectory, versionId), fileName),
                client.Sha1, client.Size, options.Mirror));
        }

        foreach (var library in libraries)
        {
            if (string.IsNullOrEmpty(library.Url)) continue;
            var destination = FileUtil.SafeChildPath(
                Path.Combine(gameDirectory, "libraries"), library.RelativePath);
            items.Add(new DownloadItem
            {
                Destination = destination,
                Urls = BuildLibraryUrls(library),
                Sha1 = library.Sha1,
                Size = library.Size
            });
        }

        string? assetIndexPath = null;
        if (version.AssetIndex is not null && !string.IsNullOrEmpty(version.AssetIndex.Url))
        {
            assetIndexPath = FileUtil.SafeChildPath(
                Path.Combine(gameDirectory, "assets", "indexes"),
                version.AssetIndex.Id + ".json");
            items.Add(DownloadItem.Create(
                version.AssetIndex.Url, assetIndexPath,
                version.AssetIndex.Sha1, version.AssetIndex.Size, options.Mirror));
        }

        if (version.Logging?.Client?.File is { } logFile && !string.IsNullOrEmpty(logFile.Url))
        {
            var destination = FileUtil.SafeChildPath(
                Path.Combine(gameDirectory, "assets", "log_configs"),
                logFile.Id ?? "client.xml");
            items.Add(DownloadItem.Create(
                logFile.Url, destination, logFile.Sha1, logFile.Size, options.Mirror));
        }

        var adapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
            "下载游戏文件",
            download.Percent,
            (long)download.BytesPerSecond,
            Estimate(download),
            $"{download.CompletedFiles}/{download.TotalFiles}  {download.CurrentFile}")));

        var downloadOptions = new DownloadOptions
        {
            MaxConcurrency = options.MaxConcurrency,
            Mirror = options.Mirror,
            VerifyExisting = options.VerifyExisting
        };

        var summary = await _downloads.DownloadAsync(items, downloadOptions, adapter, ct)
            .ConfigureAwait(false);

        var assetFiles = 0;
        var assetBytes = 0L;
        if (assetIndexPath is not null && File.Exists(assetIndexPath))
        {
            progress?.Report(new JobProgress("读取资源索引", 0.6));
            AssetIndex? index;
            try
            {
                index = JsonSerializer.Deserialize<AssetIndex>(
                    await File.ReadAllTextAsync(assetIndexPath, ct).ConfigureAwait(false), JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"资源索引解析失败：{assetIndexPath}", ex);
            }

            if (index is not null)
            {
                var assetItems = AssetResolver.BuildItems(
                    index, Path.Combine(gameDirectory, "assets"), options.Mirror);
                if (assetItems.Count > 0)
                {
                    var assetAdapter = new ProgressAdapter(download => progress?.Report(new JobProgress(
                        "下载资源文件",
                        download.Percent,
                        (long)download.BytesPerSecond,
                        Estimate(download),
                        $"{download.CompletedFiles}/{download.TotalFiles}  {download.CurrentFile}")));
                    var assetSummary = await _downloads
                        .DownloadAsync(assetItems, downloadOptions, assetAdapter, ct)
                        .ConfigureAwait(false);
                    assetFiles = assetItems.Count;
                    assetBytes = assetSummary.TotalBytes;
                }

                if (index.Virtual || index.MapToResources)
                {
                    progress?.Report(new JobProgress("映射旧版资源", 0.9));
                    LegacyAssetExtractor.Extract(
                        index,
                        Path.Combine(gameDirectory, "assets"),
                        gameDirectory,
                        version.AssetIndex?.Id ?? "legacy");
                }
            }
        }

        progress?.Report(new JobProgress("解压运行库", 0.95));
        var nativesDirectory = Path.Combine(versionsDirectory, versionId, "natives");
        ExtractNatives(libraries.Where(l => l.IsNative), gameDirectory, nativesDirectory);

        progress?.Report(new JobProgress("安装完成", 1.0));
        return new InstallResult(
            versionId,
            items.Count + assetFiles,
            summary.TotalBytes + assetBytes,
            started.Elapsed,
            Path.Combine(versionsDirectory, versionId));
    }

    private static IReadOnlyList<string> BuildLibraryUrls(ResolvedLibrary library)
    {
        var primary = library.Url!;

        // Fabric 的 Maven 在国内可能超时；ASM/SLF4J 等公共依赖可从 Maven Central 取。
        if (primary.Contains("maven.fabricmc.net", StringComparison.OrdinalIgnoreCase))
        {
            // 与原版 PCL 一致：BMCLAPI 优先；公共依赖（ASM 等）可走 Maven Central。
            var urls = new List<string>();
            if (!library.Name.Group.StartsWith("net.fabricmc", StringComparison.OrdinalIgnoreCase))
                urls.Add("https://repo1.maven.org/maven2/" + library.RelativePath);
            urls.Add("https://bmclapi2.bangbang93.com/maven/" + library.RelativePath);
            urls.Add(primary);
            return urls.Distinct(StringComparer.Ordinal).ToList();
        }

        return Mirrors.GetUrls(primary, MirrorMode.Auto);
    }

    private static TimeSpan? Estimate(DownloadProgress progress)
    {
        if (progress.BytesPerSecond <= 0 || progress.TotalBytes <= 0) return null;
        var remaining = progress.TotalBytes - progress.DownloadedBytes;
        if (remaining <= 0) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(remaining / progress.BytesPerSecond);
    }

    private static void ExtractNatives(
        IEnumerable<ResolvedLibrary> natives,
        string gameDirectory,
        string nativesDirectory)
    {
        if (Directory.Exists(nativesDirectory))
            Directory.Delete(nativesDirectory, recursive: true);
        Directory.CreateDirectory(nativesDirectory);

        var root = Path.GetFullPath(nativesDirectory) + Path.DirectorySeparatorChar;

        foreach (var library in natives)
        {
            var zipPath = FileUtil.SafeChildPath(
                Path.Combine(gameDirectory, "libraries"), library.RelativePath);
            if (!File.Exists(zipPath))
            {
                Log.Warn($"缺少运行库文件，跳过解压：{zipPath}");
                continue;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                if (library.Exclude is not null &&
                    library.Exclude.Any(prefix =>
                        entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(nativesDirectory, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.Ordinal))
                {
                    Log.Warn($"跳过可疑的压缩包路径：{entry.FullName}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
    }

    private sealed class ProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
