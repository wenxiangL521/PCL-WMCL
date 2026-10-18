namespace PCL.Download;

public sealed class DownloadOptions
{
    public int MaxConcurrency { get; init; } = 16;
    public int RetryCount { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(600);
    public MirrorMode Mirror { get; init; } = MirrorMode.Auto;
    public bool VerifyExisting { get; init; }
}

public readonly record struct DownloadProgress(
    int TotalFiles,
    int CompletedFiles,
    long TotalBytes,
    long DownloadedBytes,
    double BytesPerSecond,
    string? CurrentFile)
{
    public double? Percent => TotalBytes > 0
        ? Math.Clamp((double)DownloadedBytes / TotalBytes, 0, 1)
        : TotalFiles > 0
            ? (double)CompletedFiles / TotalFiles
            : null;
}

public sealed record DownloadSummary(
    int TotalFiles,
    int CompletedFiles,
    long TotalBytes,
    TimeSpan Elapsed);
