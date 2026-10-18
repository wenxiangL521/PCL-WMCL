using PCL.Launch.Crash;

namespace PCL.Launch.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void DetectsOutOfMemory()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "java.lang.OutOfMemoryError: Java heap space\nCrash report saved to: crash-reports/crash.txt",
            1);

        Assert.True(report.HasFindings);
        Assert.Contains(report.Findings, finding => finding.Title == "内存不足");
    }

    [Fact]
    public void DetectsJavaVersionAndModDependency()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "java.lang.UnsupportedClassVersionError: class file version 65.0\n" +
            "Missing or unsupported mandatory dependencies:\n" +
            "Mod ID: 'foo', Requested by: 'bar'",
            1);

        Assert.Contains(report.Findings, finding => finding.Title == "Java 版本过低");
        Assert.Contains(report.Findings, finding => finding.Title == "Mod 依赖或兼容性问题");
    }

    [Fact]
    public void DetectsMixinAndLoginFailures()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "Mixin apply failed: mixin.example.json\nInvalid session\n" +
            "Address already in use",
            1);
        Assert.Contains(report.Findings, finding => finding.Title == "Mixin 注入失败");
        Assert.Contains(report.Findings, finding => finding.Title == "登录会话失效");
        Assert.Contains(report.Findings, finding => finding.Title == "端口被占用");
    }

    [Fact]
    public void DetectsJvmAndFileFailures()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "Unrecognized VM option 'UseG1GC'\njava.io.FileNotFoundException: missing.jar",
            1);
        Assert.Contains(report.Findings, finding => finding.Title == "JVM 参数/Java 启动失败");
        Assert.Contains(report.Findings, finding => finding.Title == "游戏文件缺失");
    }

    [Fact]
    public void IgnoresNormalExit()
    {
        var report = CrashAnalyzer.AnalyzeText("Sound engine started\nStopping!", 0);
        Assert.False(report.HasFindings);
    }

    [Fact]
    public void DetectsGlfwFailure()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "GLFW error 65542: Failed to create window\nFailed to create window",
            1);

        Assert.Contains(report.Findings, finding => finding.Title == "图形/窗口初始化失败");
    }
}
