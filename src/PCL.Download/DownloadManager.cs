using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using PCL.Core.IO;
using PCL.Core.Logging;

namespace PCL.Download;

public sealed class DownloadException : Exception
{
    public DownloadException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// 并发下载器：多 URL 回退、断点续传、SHA1 校验、聚合进度。
/// </summary>
public sealed class DownloadManager
{
    private readonly HttpClient _http;

    public DownloadManager(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 64
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<DownloadSummary> DownloadAsync(
        IEnumerable<DownloadItem> items,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new DownloadOptions();
        var list = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Destination))
            .GroupBy(i => i.Destination, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var state = new State(list.Count, progress);
        var pending = new List<DownloadItem>(list.Count);
        foreach (var item in list)
        {
            var length = FileUtil.GetLength(item.Destination);
            var matchesSize = length >= 0 && (item.Size is null || item.Size == length);
            if (matchesSize && !options.VerifyExisting)
            {
                state.AddExisting(item, length);
            }
            else if (matchesSize && options.VerifyExisting && item.Sha1 is not null)
            {
                if (string.Equals(await FileUtil.Sha1Async(item.Destination, ct), item.Sha1,
                        StringComparison.OrdinalIgnoreCase))
                    state.AddExisting(item, length);
                else
                    pending.Add(item);
            }
            else
            {
                pending.Add(item);
            }
        }

        state.SetTotalBytes(list);
        state.ResetTransferRate();
        state.Report(force: true);
        if (pending.Count == 0)
        {
            state.Report(force: true);
            return new DownloadSummary(list.Count, list.Count, state.DownloadedBytes, TimeSpan.Zero);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        var failures = new ConcurrentBag<Exception>();
        var started = Stopwatch.StartNew();

        var tasks = pending.Select(async item =>
        {
            await semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                await DownloadOneAsync(item, options, state, linked.Token).ConfigureAwait(false);
                state.MarkCompleted(item);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // 取消或其它文件失败
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                linked.Cancel();
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 由下面的 failures / ct 处理
        }

        ct.ThrowIfCancellationRequested();

        if (!failures.IsEmpty)
        {
            var first = failures.First();
            throw new DownloadException(
                $"有 {failures.Count} 个文件下载失败，例如：{first.Message}", first);
        }

        state.Report(force: true);
        return new DownloadSummary(list.Count, list.Count, state.DownloadedBytes, started.Elapsed);
    }

    private async Task DownloadOneAsync(
        DownloadItem item,
        DownloadOptions options,
        State state,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(item.Destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var part = item.Destination + ".part";
        Exception? last = null;

        foreach (var url in item.Urls)
        {
            for (var attempt = 0; attempt <= options.RetryCount; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    state.SetCurrent(item.DisplayName);
                    await DownloadFromUrlAsync(url, item, part, state, ct).ConfigureAwait(false);
                    FileUtil.MoveReplace(part, item.Destination);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Debug($"下载失败（{url}，第 {attempt + 1} 次）：{ex.Message}");
                    if (attempt < options.RetryCount)
                        await Task.Delay(options.RetryDelay * (attempt + 1), ct).ConfigureAwait(false);
                }
            }
        }

        throw new DownloadException($"下载失败：{item.Destination}", last);
    }

    private async Task DownloadFromUrlAsync(
        string url,
        DownloadItem item,
        string part,
        State state,
        CancellationToken ct)
    {
        var existing = FileUtil.GetLength(part);
        if (existing < 0) existing = 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
        {
            if (!string.IsNullOrEmpty(item.Sha1) && File.Exists(part))
            {
                var hash = await FileUtil.Sha1Async(part, ct).ConfigureAwait(false);
                if (string.Equals(hash, item.Sha1, StringComparison.OrdinalIgnoreCase))
                    return; // 临时文件其实已经完整
            }

            FileUtil.DeleteIfExists(part);
            throw new InvalidDataException(
                $"服务器拒绝续传范围（已有 {existing} 字节，目标 {item.Size} 字节），已清理临时文件，将重试。");
        }

        response.EnsureSuccessStatusCode();

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) existing = 0;

        await using var file = new FileStream(
            part,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                state.AddBytes(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await file.FlushAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(item.Sha1))
        {
            var actual = await FileUtil.Sha1Async(part, ct).ConfigureAwait(false);
            if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                FileUtil.DeleteIfExists(part);
                throw new InvalidDataException($"SHA1 不匹配：期望 {item.Sha1}，实际 {actual}");
            }
        }
    }

    private sealed class State
    {
        private readonly object _gate = new();
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _totalFiles;
        private long _totalBytes;
        private long _downloadedBytes;
        private int _completedFiles;
        private string? _currentFile;
        private long _lastReportTicks;
        private long _lastBytes;
        private double _speed;

        public State(int totalFiles, IProgress<DownloadProgress>? progress)
        {
            _totalFiles = totalFiles;
            _progress = progress;
        }

        public long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

        public void AddExisting(DownloadItem item, long length)
        {
            Interlocked.Add(ref _downloadedBytes, length);
            Interlocked.Increment(ref _completedFiles);
        }

        public void SetTotalBytes(IReadOnlyList<DownloadItem> items)
        {
            long total = 0;
            foreach (var item in items) total += item.Size ?? 0;
            _totalBytes = total;
        }

        public void SetCurrent(string file)
        {
            lock (_gate) _currentFile = file;
        }

        public void AddBytes(long count)
        {
            Interlocked.Add(ref _downloadedBytes, count);
            Report();
        }

        public void ResetTransferRate()
        {
            lock (_gate)
            {
                _lastBytes = Interlocked.Read(ref _downloadedBytes);
                _lastReportTicks = _clock.ElapsedMilliseconds;
                _speed = 0;
            }
        }

        public void MarkCompleted(DownloadItem item)
        {
            Interlocked.Increment(ref _completedFiles);
            Report(force: true);
        }

        public void Report(bool force = false)
        {
            lock (_gate)
            {
                var now = _clock.ElapsedMilliseconds;
                if (!force && now - _lastReportTicks < 100) return;

                var bytes = Interlocked.Read(ref _downloadedBytes);
                var elapsed = Math.Max(1, now - _lastReportTicks);
                var delta = bytes - _lastBytes;
                if (delta > 0) _speed = delta * 1000.0 / elapsed;
                _lastReportTicks = now;
                _lastBytes = bytes;

                _progress?.Report(new DownloadProgress(
                    _totalFiles,
                    Volatile.Read(ref _completedFiles),
                    _totalBytes,
                    bytes,
                    _speed,
                    _currentFile));
            }
        }
    }
}
