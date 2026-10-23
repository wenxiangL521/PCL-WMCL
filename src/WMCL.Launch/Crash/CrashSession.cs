using System.Security.Cryptography;

namespace WMCL.Launch.Crash;

public sealed class CrashSession
{
    private readonly Dictionary<string, FileState?> _before;
    public string GameRoot { get; }
    public string InstanceId { get; }
    public string GameDirectory { get; }
    public DateTimeOffset StartedUtc { get; }

    private CrashSession(string gameRoot, string instanceId, string gameDirectory, DateTimeOffset startedUtc)
    {
        GameRoot = Path.GetFullPath(gameRoot);
        InstanceId = instanceId;
        GameDirectory = Path.GetFullPath(gameDirectory);
        StartedUtc = startedUtc;
        _before = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var paths = CrashFiles(GameDirectory).Prepend(Path.Combine(GameDirectory, "logs", "latest.log"));
        foreach (var path in paths) _before[path] = State(path);
    }

    public static CrashSession Capture(string gameRoot, string instanceId, string gameDirectory, DateTimeOffset? startedUtc = null) =>
        new(gameRoot, instanceId, gameDirectory, startedUtc ?? DateTimeOffset.UtcNow);

    internal bool Includes(string path)
    {
        var current = State(path);
        if (current is null || current.Length == 0 || current.Modified < StartedUtc.UtcDateTime.AddSeconds(-2) ||
            current.Modified > DateTime.UtcNow.AddSeconds(2)) return false;
        return !_before.TryGetValue(path, out var before) || current != before;
    }

    internal static IReadOnlyList<string> CrashFiles(string gameDirectory)
    {
        try
        {
            var directory = Path.Combine(gameDirectory, "crash-reports");
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    private static FileState? State(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[(int)Math.Min(stream.Length, 400_000)];
            stream.Seek(-bytes.Length, SeekOrigin.End);
            stream.ReadExactly(bytes);
            return new(info.LastWriteTimeUtc, info.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private sealed record FileState(DateTime Modified, long Length, string Hash);
}
