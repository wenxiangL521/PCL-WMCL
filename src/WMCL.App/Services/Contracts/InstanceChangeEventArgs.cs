namespace WMCL.App.Services;

public enum InstanceChangeKind { Renamed, Cloned, Deleted }

public sealed class InstanceChangeEventArgs(string gameDirectory, string sourceId, string? targetId, InstanceChangeKind kind) : EventArgs
{
    public string GameDirectory { get; } = gameDirectory;
    public string SourceId { get; } = sourceId;
    public string? TargetId { get; } = targetId;
    public InstanceChangeKind Kind { get; } = kind;

    public string? Remap(string? id) => Kind != InstanceChangeKind.Cloned &&
        string.Equals(id, SourceId, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? TargetId : id;
}
