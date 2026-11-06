using System.Text;

namespace WMCL.App.Services;

/// <summary>失败任务的可展示与可复制载荷：异常类型、消息、拼接后的完整堆栈与捕获时间。</summary>
public sealed record TaskFailureInfo(string ExceptionType, string Message, string StackTrace, DateTimeOffset CapturedAt)
{
    /// <summary>堆栈拼接结果的最大长度（字符）；超出时保留头尾并标注截断字符数。</summary>
    public const int MaxStackTraceLength = 32 * 1024;

    /// <summary>
    /// 从异常构建失败信息。递归拼接 InnerException 链（前缀 "---> "）；
    /// 对任意异常都不抛出（内部全量兜底）。taskTitle 供调用方日志语境，不入载荷。
    /// </summary>
    public static TaskFailureInfo From(Exception ex, string taskTitle)
    {
        try
        {
            var builder = new StringBuilder();
            AppendChain(builder, ex);
            var stack = TruncateStackTrace(builder.ToString());
            return new TaskFailureInfo(
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message ?? "",
                stack,
                DateTimeOffset.Now);
        }
        catch
        {
            try
            {
                return new TaskFailureInfo(ex.GetType().FullName ?? "Exception", "", "", DateTimeOffset.Now);
            }
            catch
            {
                return new TaskFailureInfo("Exception", "", "", DateTimeOffset.Now);
            }
        }
    }

    /// <summary>返回脱敏副本：消息与堆栈中的敏感模式替换为 ***，其余逐字节不变。</summary>
    public TaskFailureInfo Redacted() => this with
    {
        Message = SensitiveTextRedactor.Redact(Message),
        StackTrace = SensitiveTextRedactor.Redact(StackTrace)
    };

    internal static string TruncateStackTrace(string text)
    {
        if (text.Length <= MaxStackTraceLength) return text;
        var head = MaxStackTraceLength / 2;
        var tail = MaxStackTraceLength / 4;
        var omitted = text.Length - head - tail;
        return text[..head] + $"\n…[已截断 {omitted} 字符]…\n" + text[^tail..];
    }

    private static void AppendChain(StringBuilder builder, Exception ex)
    {
        builder.Append(ex.GetType().FullName ?? ex.GetType().Name);
        builder.Append(": ");
        builder.AppendLine(ex.Message ?? "");
        if (!string.IsNullOrEmpty(ex.StackTrace))
            builder.AppendLine(ex.StackTrace);
        if (ex.InnerException is { } inner)
        {
            builder.Append(" ---> ");
            AppendChain(builder, inner);
        }
    }
}
