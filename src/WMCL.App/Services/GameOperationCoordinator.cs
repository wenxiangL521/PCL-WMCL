using WMCL.Core;

namespace WMCL.App.Services;

public sealed class GameOperationCoordinator
{
    public static GameOperationCoordinator Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, Operation> _active = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private Operation? _exclusive;

    public IDisposable AcquireExclusive(string description)
    {
        lock (_sync)
        {
            var current = _exclusive ?? _active.Values.FirstOrDefault();
            if (current is not null)
                throw new InvalidOperationException($"正在{current.Description}，请等待结束后再{description}。");
            _exclusive = new Operation(this, null, description);
            return _exclusive;
        }
    }

    public IDisposable Acquire(string gameRoot, string description)
    {
        var root = Normalize(gameRoot);
        lock (_sync)
        {
            var current = Find(root);
            if (current is not null)
                throw new InvalidOperationException($"此游戏目录正在{current.Description}，请等待结束后再{description}。");
            var operation = new Operation(this, root, description);
            _active.Add(root, operation);
            return operation;
        }
    }

    public string? GetActiveOperation(string gameRoot)
    {
        lock (_sync)
            return Find(Normalize(gameRoot))?.Description;
    }

    private Operation? Find(string root) => _exclusive ?? _active.FirstOrDefault(pair =>
        IsWithin(root, pair.Key) || IsWithin(pair.Key, root)).Value;

    private static bool IsWithin(string path, string parent) => path.Equals(parent, PathComparison) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Normalize(string gameRoot)
    {
        var root = Path.GetFullPath(WmclPaths.Expand(gameRoot));
        var parts = new Stack<string>();
        for (var directory = new DirectoryInfo(root); directory.Parent is not null; directory = directory.Parent)
            parts.Push(directory.Name);
        var canonical = Path.GetPathRoot(root)!;
        while (parts.TryPop(out var part))
        {
            canonical = Path.Combine(canonical, part);
            if (Directory.Exists(canonical))
                canonical = new DirectoryInfo(canonical).ResolveLinkTarget(true)?.FullName ?? canonical;
        }
        return Path.TrimEndingDirectorySeparator(canonical);
    }

    private sealed class Operation(GameOperationCoordinator owner, string? root, string description) : IDisposable
    {
        private bool _disposed;
        public string Description { get; } = description;

        public void Dispose()
        {
            lock (owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (root is null) owner._exclusive = null;
                else owner._active.Remove(root);
            }
        }
    }
}
