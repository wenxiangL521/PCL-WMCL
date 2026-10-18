namespace PCL.Download;

public enum MirrorMode
{
    Auto,
    Official,
    Bmclapi
}

/// <summary>一个待下载文件：目标路径 + 一组按优先级尝试的 URL。</summary>
public sealed record DownloadItem
{
    public required string Destination { get; init; }
    public required IReadOnlyList<string> Urls { get; init; }
    public string? Sha1 { get; init; }
    public long? Size { get; init; }

    public string DisplayName => Path.GetFileName(Destination);

    public static DownloadItem Create(
        string url,
        string destination,
        string? sha1 = null,
        long? size = null,
        MirrorMode mirror = MirrorMode.Auto)
        => new()
        {
            Destination = destination,
            Urls = Mirrors.GetUrls(url, mirror),
            Sha1 = sha1,
            Size = size
        };
}
