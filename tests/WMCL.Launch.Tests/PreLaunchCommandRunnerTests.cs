using WMCL.Launch.Java;

namespace WMCL.Launch.Tests;

[Trait("Kind", "Unit")]
public sealed class PreLaunchCommandRunnerTests
{
    private static readonly JavaRuntime Runtime = new("java path", new Version(21, 0, 1), 21,
        "x86_64", "java-home", "Test");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildsTheExistingShellContractAndExpandsPlaceholders(bool wait)
    {
        var start = PreLaunchCommandRunner.BuildStartInfo(
            "{JAVA}|{minecraft}|{verpath}|{name}", "game-root", "instance-dir", "Pack", Runtime, wait);
        Assert.Equal(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh", start.FileName);
        Assert.Equal(OperatingSystem.IsWindows() ? "/c" : "-c", start.ArgumentList[0]);
        Assert.Equal("java path|game-root|instance-dir|Pack", start.ArgumentList[1]);
        Assert.Equal("game-root", start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(wait, start.RedirectStandardOutput);
        Assert.Equal(wait, start.RedirectStandardError);
    }

    [Fact]
    public async Task CapturesBothStreamsAndReportsExitCodeWithoutFailingOnNonzeroExit()
    {
        var log = new List<string>();
        await PreLaunchCommandRunner.RunAsync("echo output & echo error >&2 & exit 7",
            Path.GetTempPath(), "instance-dir", "Pack", Runtime, true, log.Add);
        Assert.Contains(log, line => line.Contains("output", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("error", StringComparison.Ordinal));
        Assert.Equal("[启动前命令] 退出码 7", log[^1]);
    }
}
