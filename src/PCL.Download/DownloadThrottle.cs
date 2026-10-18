namespace PCL.Download;

/// <summary>全局下载限速（令牌桶）。0 表示不限速。</summary>
public static class DownloadThrottle
{
    private static readonly object Gate = new();
    private static long _limitBytesPerSecond;
    private static long _tokens;
    private static long _lastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

    public static long LimitBytesPerSecond => Interlocked.Read(ref _limitBytesPerSecond);

    public static void SetLimit(long bytesPerSecond)
    {
        lock (Gate)
        {
            _limitBytesPerSecond = Math.Max(0, bytesPerSecond);
            _tokens = _limitBytesPerSecond;
            _lastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    public static async Task WaitAsync(int bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;
        while (true)
        {
            int delayMs;
            lock (Gate)
            {
                var limit = _limitBytesPerSecond;
                if (limit <= 0) return;
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_lastTimestamp, now);
                _lastTimestamp = now;
                _tokens = Math.Min(limit, _tokens + (long)(elapsed.TotalSeconds * limit));
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }
                var needed = bytes - _tokens;
                delayMs = (int)Math.Clamp(needed * 1000 / limit + 1, 1, 1000);
            }
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }
}
