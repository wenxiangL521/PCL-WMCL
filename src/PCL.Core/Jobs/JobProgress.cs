namespace PCL.Core.Jobs;

/// <summary>
/// 与 UI 无关的任务进度。业务层只发这个对象，UI 层自行节流渲染。
/// </summary>
public readonly record struct JobProgress(
    string Stage,
    double? Percent = null,
    long? BytesPerSecond = null,
    TimeSpan? Eta = null,
    string? Detail = null);
