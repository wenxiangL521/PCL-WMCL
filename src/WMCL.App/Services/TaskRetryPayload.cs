namespace WMCL.App.Services;

/// <summary>可重试任务类型。属性名冻结：历史持久化 v2 以此枚举名字符串落盘。</summary>
public enum TaskRetryKind
{
    VersionInstall,
    VersionVerify,
    BatchInstall,
    ModInstall,
    ModpackInstall,
    ResourceInstall,
    ResourceSave,
    CombinedInstall
}

/// <summary>失败任务的重试快照：按原参数重新执行。字段可 JSON 序列化（属性名冻结）。</summary>
public sealed record TaskRetryPayload(
    TaskRetryKind Kind,
    string GameRoot,
    string? VersionId,
    string? ResourceProjectId,
    IReadOnlyDictionary<string, string> Options)
{
    public static TaskRetryPayload Create(
        TaskRetryKind kind,
        string gameRoot,
        string? versionId = null,
        string? resourceProjectId = null,
        IReadOnlyDictionary<string, string>? options = null) =>
        new(kind, gameRoot, versionId, resourceProjectId, options ?? new Dictionary<string, string>());
}
