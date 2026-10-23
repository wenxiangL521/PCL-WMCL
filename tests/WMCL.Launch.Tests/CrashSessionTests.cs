using System.IO.Compression;
using WMCL.Launch.Crash;
using WMCL.Launch.Diagnostics;

namespace WMCL.Launch.Tests;

public sealed class CrashSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-crash-session-" + Guid.NewGuid().ToString("N"));
    private string Game => Path.Combine(_root, "versions", "Original");
    private string Log => Path.Combine(Game, "logs", "latest.log");
    private string Crash => Path.Combine(Game, "crash-reports", "crash.txt");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedExistingFilesCannotTurnNormalExitIntoACrash(bool futureTimestamp)
    {
        Write(Log, "Exception in thread \"main\" OutOfMemoryError");
        Write(Crash, "Description: old crash\nException in thread \"main\"");
        var stamp = DateTime.UtcNow.AddMinutes(futureTimestamp ? 10 : -10);
        File.SetLastWriteTimeUtc(Log, stamp);
        File.SetLastWriteTimeUtc(Crash, stamp);
        var session = CrashSession.Capture(_root, "Original", Game);
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 0, "Stopping!", session);
        Assert.False(report.HasFindings);
        Assert.Null(report.LogPath);
        Assert.Null(report.CrashReportPath);
    }

    [Fact]
    public void CurrentFailureUsesOutputWithoutImportingAnOldCrashDiagnosis()
    {
        Write(Crash, "Description: OLD\nOutOfMemoryError");
        var session = CrashSession.Capture(_root, "Original", Game);
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, "failed to create window", session);
        Assert.Contains(report.Findings, finding => finding.Title == "图形/窗口初始化失败");
        Assert.DoesNotContain(report.Findings, finding => finding.Title == "内存不足");
        Assert.Null(report.CrashReportPath);
    }

    [Fact]
    public void SameLengthAndTimestampRewriteIsDetectedByContentFingerprint()
    {
        Write(Log, "normal" + new string(' ', 100));
        var modified = File.GetLastWriteTimeUtc(Log);
        var session = CrashSession.Capture(_root, "Original", Game);
        Write(Log, "OutOfMemoryError".PadRight(106));
        File.SetLastWriteTimeUtc(Log, modified);
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, session: session);
        Assert.Contains(report.Findings, finding => finding.Title == "内存不足");
        Assert.Equal(Log, report.LogPath);
    }

    [Fact]
    public void LongRunningSessionAcceptsItsEarlyLogWithoutUsingAnExitTimeWindow()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var session = CrashSession.Capture(_root, "Original", Game, started);
        Write(Log, "OutOfMemoryError");
        File.SetLastWriteTimeUtc(Log, started.AddMinutes(10).UtcDateTime);
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, session: session);
        Assert.Equal(Log, report.LogPath);
        Assert.Contains(report.Findings, finding => finding.Title == "内存不足");
    }

    [Fact]
    public void CurrentCrashIsSelectedInsteadOfUnchangedNewerHistoricalFile()
    {
        Write(Crash, "Description: unchanged future crash");
        File.SetLastWriteTimeUtc(Crash, DateTime.UtcNow.AddDays(1));
        var session = CrashSession.Capture(_root, "Original", Game);
        var current = Path.Combine(Game, "crash-reports", "current.txt");
        Write(current, "Description: current crash");
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, session: session);
        Assert.Equal(current, report.CrashReportPath);
        Assert.Contains("current crash", report.CrashContent);
    }

    [Fact]
    public void EmptyAndDeletedFilesAreIgnoredAndCapturedDirectoryWinsOverCallerSelection()
    {
        Write(Log, "before");
        var session = CrashSession.Capture(_root, "Original", Game);
        File.Delete(Log);
        Write(Crash, "");
        var other = Path.Combine(_root, "other");
        Write(Path.Combine(other, "logs", "latest.log"), "OutOfMemoryError");
        var report = CrashAnalyzer.AnalyzeGameDirectory(other, 0, session: session);
        Assert.False(report.HasFindings);
        Assert.Null(report.LogPath);
        Assert.Null(report.CrashReportPath);
    }

    [Fact]
    public void DiagnosticSnapshotKeepsIsolatedModsAndLogContentAfterFilesChangeOrDisappear()
    {
        var session = CrashSession.Capture(_root, "Original", Game);
        Write(Log, "OutOfMemoryError session-log");
        Write(Crash, "Description: captured crash");
        Write(Path.Combine(Game, "mods", "isolated.jar"), "original");
        Write(Path.Combine(_root, "mods", "wrong-root.jar"), "other");
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, "captured output", session);
        var snapshot = CrashDiagnosticSnapshot.Capture(session, report, 1, "captured output");
        Directory.Delete(Game, true);
        Write(Log, "replacement run");
        Write(Path.Combine(Game, "mods", "replacement.jar"), "replacement");
        var path = DiagnosticPackage.Export(Path.Combine(_root, "out"), snapshot);
        using var archive = ZipFile.OpenRead(path);
        Assert.Contains("session-log", Read(archive, "latest.log"));
        Assert.Contains("captured crash", Read(archive, "crash-report.txt"));
        Assert.Contains("isolated.jar", Read(archive, "mods.txt"));
        Assert.DoesNotContain("replacement.jar", Read(archive, "mods.txt"));
        Assert.DoesNotContain("wrong-root.jar", Read(archive, "mods.txt"));
        Assert.Contains("Original", Read(archive, "instance.txt"));
        Assert.Equal("captured output", Read(archive, "launcher-output.txt"));
        Assert.Contains(Game, Read(archive, "report.txt"));
    }

    [Fact]
    public void RepeatedExportsProduceSeparateArchives()
    {
        var session = CrashSession.Capture(_root, "Original", Game);
        var snapshot = CrashDiagnosticSnapshot.Capture(session, CrashAnalyzer.AnalyzeText("", 1), 1, "");
        var first = DiagnosticPackage.Export(Path.Combine(_root, "out"), snapshot);
        var second = DiagnosticPackage.Export(Path.Combine(_root, "out"), snapshot);
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void ExportRetainsTheFullLogEvenThoughRuleAnalysisUsesATail()
    {
        var session = CrashSession.Capture(_root, "Original", Game);
        var content = "session-begin\n" + new string('x', 500_000) + "\nOutOfMemoryError";
        Write(Log, content);
        var report = CrashAnalyzer.AnalyzeGameDirectory(Game, 1, session: session);
        var snapshot = CrashDiagnosticSnapshot.Capture(session, report, 1, "");
        File.Delete(Log);
        using var archive = ZipFile.OpenRead(DiagnosticPackage.Export(Path.Combine(_root, "out"), snapshot));
        Assert.Equal(content, Read(archive, "latest.log"));
        Assert.Contains(report.Findings, finding => finding.Title == "内存不足");
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
