using System.Diagnostics;
using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

/// <summary>
/// Verifies client and platform library/native archives, asset index/objects and
/// logging configuration against all known SHA1/size metadata. Unknown metadata
/// guarantees existence only. Local profiles and user data are never rewritten;
/// native extraction remains the launcher's responsibility.
/// </summary>
public sealed class GameFileVerificationService
{
    private readonly VersionRepository _repository;
    private readonly DownloadManager _downloads;
    private readonly RuleContext _ruleContext;

    public GameFileVerificationService(
        VersionRepository repository,
        DownloadManager? downloads = null,
        RuleContext? ruleContext = null)
    {
        _repository = repository;
        _downloads = downloads ?? new DownloadManager();
        _ruleContext = ruleContext ?? RuleContext.CreateDefault(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = true,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        });
    }

    public Task<GameFileVerificationResult> EnsureAsync(
        string versionId,
        InstallOptions? options = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.Run(() => EnsureCoreAsync(versionId, options ?? new InstallOptions(), progress, ct), ct);

    private async Task<GameFileVerificationResult> EnsureCoreAsync(
        string versionId, InstallOptions options, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var reporter = new Reporter(progress);
        reporter.Report("读取本地版本配置", 0, force: true);
        VersionJson version;
        try
        {
            version = await _repository.GetLocalVersionAsync(versionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"无法校验启动文件：本地版本配置或继承链不可用（{versionId}）。{ex.Message}", ex);
        }

        ct.ThrowIfCancellationRequested();
        var root = _repository.GameDirectory;
        var clientPath = ClientJarPathResolver.Resolve(root, versionId, version);
        DownloadInfo? client = null;
        version.Downloads?.TryGetValue("client", out client);
        var items = new List<DownloadItem> { Item(clientPath, client?.Url, client?.Sha1, client?.Size, options.Mirror) };
        foreach (var library in LibraryResolver.Resolve(version, _ruleContext))
        {
            ct.ThrowIfCancellationRequested();
            var path = LibraryFileLocator.Resolve(FileUtil.SafeChildPath(Path.Combine(root, "libraries"), library.RelativePath));
            items.Add(new DownloadItem
            {
                Destination = path,
                Urls = string.IsNullOrWhiteSpace(library.Url) ? [] : VersionInstaller.BuildLibraryUrls(library, options.Mirror),
                Sha1 = library.Sha1,
                Size = library.Size
            });
        }

        DownloadItem? indexItem = null;
        var indexId = version.AssetIndex?.Id ?? version.Assets;
        if (!string.IsNullOrWhiteSpace(indexId))
        {
            indexItem = Item(FileUtil.SafeChildPath(Path.Combine(root, "assets", "indexes"), indexId + ".json"),
                version.AssetIndex?.Url, version.AssetIndex?.Sha1, version.AssetIndex?.Size, options.Mirror);
            items.Add(indexItem);
        }
        if (version.Logging?.Client?.File is { } logging)
        {
            items.Add(Item(FileUtil.SafeChildPath(Path.Combine(root, "assets", "log_configs"), logging.Id ?? "client.xml"),
                logging.Url, logging.Sha1, logging.Size, options.Mirror));
        }

        items = items.DistinctBy(item => item.Destination, StringComparer.Ordinal).ToList();
        var pending = await CheckAsync(items, reporter, "校验客户端与运行库", 0.03, 0.25, ct).ConfigureAwait(false);
        await RepairAsync(pending, options, reporter, "补全客户端与运行库", 0.25, 0.45, ct).ConfigureAwait(false);
        var repaired = pending.Count;
        var checkedFiles = items.Count;

        if (indexItem is not null)
        {
            reporter.Report("读取资源索引", 0.45, force: true);
            AssetIndex index;
            try
            {
                index = await ReadIndexAsync(indexItem.Destination, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                if (!string.IsNullOrWhiteSpace(indexItem.Sha1) || pending.Contains(indexItem))
                    throw new InvalidDataException($"资源索引内容无效：{indexItem.Destination}。{ex.Message}", ex);
                await RepairAsync([indexItem], options, reporter, "修复资源索引", 0.45, 0.48, ct).ConfigureAwait(false);
                repaired++;
                index = await ReadIndexAsync(indexItem.Destination, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            var assets = AssetResolver.BuildItems(index, Path.Combine(root, "assets"), options.Mirror);
            var missingAssets = await CheckAsync(assets, reporter, "校验资源文件", 0.48, 0.73, ct).ConfigureAwait(false);
            await RepairAsync(missingAssets, options, reporter, "补全资源文件", 0.73, 0.96, ct).ConfigureAwait(false);
            checkedFiles += assets.Count;
            repaired += missingAssets.Count;
            if (index.Virtual || index.MapToResources)
            {
                reporter.Report("映射旧版资源", 0.97, force: true);
                checkedFiles += index.Objects.Count;
                repaired += await LegacyAssetExtractor.EnsureAsync(index, Path.Combine(root, "assets"), root, indexId!, ct)
                    .ConfigureAwait(false);
            }
        }

        ct.ThrowIfCancellationRequested();
        reporter.Report("启动文件校验完成", 1, force: true);
        return new GameFileVerificationResult(version, clientPath, checkedFiles, repaired);
    }

    private static DownloadItem Item(string path, string? url, string? sha1, long? size, MirrorMode mirror) => new()
    {
        Destination = path,
        Urls = string.IsNullOrWhiteSpace(url) ? [] : Mirrors.GetUrls(url, mirror),
        Sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1,
        Size = size
    };

    private static async Task<List<DownloadItem>> CheckAsync(
        IReadOnlyList<DownloadItem> items, Reporter reporter, string stage, double start, double end, CancellationToken ct)
    {
        reporter.Report(stage, start, force: true);
        var pending = new List<DownloadItem>();
        for (var index = 0; index < items.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[index];
            if (!await MatchesAsync(item, ct).ConfigureAwait(false))
            {
                if (item.Urls.Count == 0)
                    throw new InvalidDataException($"启动必需文件缺失或损坏，且本地配置未提供下载来源：{item.Destination}。请重新安装对应组件或恢复原始文件。");
                pending.Add(item);
            }
            reporter.Report(stage, start + (end - start) * (index + 1) / Math.Max(1, items.Count),
                $"{index + 1}/{items.Count}  {item.DisplayName}");
        }
        reporter.Report(stage, end, force: true);
        return pending;
    }

    private static async Task<bool> MatchesAsync(DownloadItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = FileUtil.GetLength(item.Destination);
        if (length < 0 || item.Size is not null && item.Size != length) return false;
        return string.IsNullOrWhiteSpace(item.Sha1) || string.Equals(
            await FileUtil.Sha1Async(item.Destination, ct).ConfigureAwait(false), item.Sha1, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RepairAsync(
        IReadOnlyList<DownloadItem> items, InstallOptions options, Reporter reporter,
        string stage, double start, double end, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        reporter.Report(stage, start, force: true);
        foreach (var item in items)
        {
            if (item.Urls.Count == 0)
                throw new InvalidDataException($"启动必需文件缺失或损坏，且没有下载来源：{item.Destination}");
            EnsureRepairSafe(_repository.GameDirectory, item.Destination);
        }
        if (items.Count > 0)
        {
            var adapter = new DownloadAdapter(download => reporter.Report(stage,
                start + (end - start) * (download.Percent ?? 0),
                $"{download.CompletedFiles}/{download.TotalFiles}  {download.CurrentFile}",
                (long)download.BytesPerSecond));
            try
            {
                await _downloads.DownloadAsync(items, new DownloadOptions
                {
                    MaxConcurrency = options.MaxConcurrency,
                    Mirror = options.Mirror,
                    VerifyExisting = true
                }, adapter, ct).ConfigureAwait(false);
            }
            catch (DownloadException ex)
            {
                throw new DownloadException($"启动文件补全失败，不能继续启动。请检查网络或重新安装对应组件。{ex.Message}", ex);
            }
            foreach (var item in items)
            {
                if (!await MatchesAsync(item, ct).ConfigureAwait(false))
                    throw new InvalidDataException($"下载后的启动文件校验失败：{item.Destination}");
            }
        }
        reporter.Report(stage, end, force: true);
    }

    internal static void EnsureRepairSafe(string minecraftDirectory, string destination)
    {
        var root = Path.GetFullPath(minecraftDirectory);
        var versionRoot = Path.Combine(root, "versions") + Path.DirectorySeparatorChar;
        if (destination.StartsWith(versionRoot, StringComparison.Ordinal))
        {
            var relative = destination[versionRoot.Length..].Replace('\\', '/').Split('/');
            if (relative.Skip(1).Any(segment =>
                    segment.Equals("mods", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("saves", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"拒绝使用启动补全覆盖用户数据：{destination}");
        }
        try
        {
            ExportSafety.ValidatePath(destination);
            ExportSafety.ValidatePath(destination + ".part");
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"拒绝通过符号链接修复启动文件：{destination}", ex);
        }
    }

    private static async Task<AssetIndex> ReadIndexAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"资源索引没有有效的 objects：{path}");
        var index = document.Deserialize<AssetIndex>() ?? throw new InvalidDataException($"资源索引为空：{path}");
        foreach (var asset in index.Objects.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (asset is null || asset.Hash is null || asset.Hash.Length != 40 ||
                !asset.Hash.All(char.IsAsciiHexDigit) || asset.Size < 0)
                throw new InvalidDataException($"资源索引包含无效的 SHA1 或长度：{path}");
        }
        return index;
    }

    private sealed class DownloadAdapter(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class Reporter(IProgress<JobProgress>? progress)
    {
        private readonly object _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastReport;
        private double _percent;

        public void Report(string stage, double percent, string? detail = null, long? speed = null, bool force = false)
        {
            if (progress is null) return;
            lock (_gate)
            {
                _percent = Math.Clamp(Math.Max(_percent, percent), 0, 1);
                var now = _clock.ElapsedMilliseconds;
                if (!force && now - _lastReport < 100) return;
                _lastReport = now;
                progress.Report(new JobProgress(stage, _percent, speed, Detail: detail));
            }
        }
    }
}

public sealed record GameFileVerificationResult(
    VersionJson Version,
    string ClientJarPath,
    int CheckedFiles,
    int RepairedFiles);
