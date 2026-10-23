using System.Text;
using WMCL.Launch.Crash;

namespace WMCL.Launch.Diagnostics;

public sealed record CrashDiagnosticSnapshot(CrashSession Session, CrashReport Report, int ExitCode,
    string Output, string ModsText, DateTimeOffset FinishedUtc)
{
    public string ReportText => $"""
        WMCL 崩溃报告
        启动时间：{Session.StartedUtc:O}
        退出时间：{FinishedUtc:O}
        游戏版本：{Session.InstanceId}
        游戏根目录：{Session.GameRoot}
        实际游戏目录：{Session.GameDirectory}
        退出码：{ExitCode}
        日志：{Report.LogPath ?? "未找到"}
        崩溃报告：{Report.CrashReportPath ?? "未找到"}

        摘要：{Report.Summary}

        {string.Join(Environment.NewLine + Environment.NewLine, Report.Findings.Select(finding => $"{finding.Title}：{finding.Detail}{Environment.NewLine}建议：{finding.Suggestion}"))}
        """;

    public static CrashDiagnosticSnapshot Capture(CrashSession session, CrashReport report, int exitCode, string output)
    {
        if (!report.HasFindings) return new(session, report, exitCode, output, "", DateTimeOffset.UtcNow);
        var mods = new StringBuilder();
        try
        {
            var directory = Path.Combine(session.GameDirectory, "mods");
            if (!Directory.Exists(directory)) mods.AppendLine("没有找到 mods 目录。");
            else
                foreach (var path in Directory.EnumerateFiles(directory, "*.jar").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    mods.AppendLine($"{info.Name}\t{info.Length} bytes");
                }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mods.AppendLine("无法完整读取 Mod 清单：" + exception.Message);
        }
        return new(session, report, exitCode, output, mods.ToString(), DateTimeOffset.UtcNow);
    }
}
