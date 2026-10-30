namespace WMCL.App.Services;

public enum TaskLogLevel
{
    Info,
    Warn,
    Error
}

/// <summary>任务日志的结构化条目：时间 / 级别 / 阶段 / 消息四元组。</summary>
public sealed record TaskLogEntry(DateTimeOffset Timestamp, TaskLogLevel Level, string Stage, string Message);
