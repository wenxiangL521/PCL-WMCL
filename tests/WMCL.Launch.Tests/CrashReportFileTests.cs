using WMCL.Launch.Crash;

namespace WMCL.Launch.Tests;

public class CrashReportFileTests
{
    [Fact]
    public void ParsesNewestCrashReportDescription()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(Path.Combine(root, "crash-reports"));
        File.WriteAllText(Path.Combine(root, "logs", "latest.log"), "Stopping!");
        var report = Path.Combine(root, "crash-reports", "crash-1.txt");
        File.WriteAllText(report, """
            ---- Minecraft Crash Report ----
            Description: Ticking entity
            java.lang.NullPointerException: test
            """);
        try
        {
            var result = CrashAnalyzer.AnalyzeGameDirectory(root, 1, "Stopping!");
            Assert.Equal(report, result.CrashReportPath);
            Assert.Contains(result.Findings, f => f.Title == "崩溃报告摘要");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
