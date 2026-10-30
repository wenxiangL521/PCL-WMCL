using WMCL.Core.Jobs;

namespace WMCL.App.Services;

/// <summary>
/// 任务进度的统一展示格式（提取自 DownloadViewModel.FormatProgress 与
/// TaskManagerViewModel.FormatSpeed，输出逐字节等价——既有测试断言为回归锚点）。
/// </summary>
public static class ProgressFormatter
{
    /// <summary>阶段 + 百分比 + 速度 + 详情（与 DownloadViewModel.FormatProgress 逐字节等价）。</summary>
    public static string Format(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0.0}%";
        var speed = progress.BytesPerSecond is > 0
            ? $" · {progress.BytesPerSecond / 1048576.0:0.0} MB/s"
            : "";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : $" · {progress.Detail}";
        return $"{progress.Stage} {percent}{speed}{detail}".Trim();
    }

    /// <summary>速度分档（B/KB/MB，与 TaskManagerViewModel.FormatSpeed 逐字节等价）。</summary>
    public static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond switch
        {
            >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:0.0} KB/s",
            > 0 => $"{bytesPerSecond:0} B/s",
            _ => "0 B/s"
        };
}
