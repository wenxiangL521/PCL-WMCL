using System.IO.Compression;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Launch.Crash;
using WMCL.Launch.Diagnostics;

namespace WMCL.App.Tests;

public sealed class CrashDiagnosticInteractionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-crash-context-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public void SwitchingRootAndSelectionKeepsDiagnosticAndTextExportsOnTheFaultSnapshot()
    {
        var game = Path.Combine(_root, "first", "versions", "Fault Instance");
        var session = CrashSession.Capture(Path.Combine(_root, "first"), "Fault Instance", game);
        Directory.CreateDirectory(Path.Combine(game, "logs"));
        Directory.CreateDirectory(Path.Combine(game, "mods"));
        File.WriteAllText(Path.Combine(game, "logs", "latest.log"), "OutOfMemoryError fault-content");
        File.WriteAllText(Path.Combine(game, "mods", "fault.jar"), "fixture");
        var report = CrashAnalyzer.AnalyzeGameDirectory(game, 1, session: session);
        var snapshot = CrashDiagnosticSnapshot.Capture(session, report, 1, "fault-output");
        var services = new AppServices(new AppSettings { GameDirectory = Path.Combine(_root, "first") }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services, null, null, Path.Combine(_root, "accounts"), null, null);
        launch.ApplyCrashResult(snapshot);
        services.SetGameDirectory(Path.Combine(_root, "second"));
        launch.SelectedVersion = new VersionItem { Id = "Different Instance", IsInstalled = true };
        Directory.Delete(game, recursive: true);
        var output = Path.Combine(_root, "output");
        using var archive = ZipFile.OpenRead(launch.WriteDiagnosticPackage(output));
        var text = File.ReadAllText(launch.WriteCrashReport(output));
        Assert.True(launch.HasCrashReport);
        Assert.Contains("Fault Instance", Read(archive, "instance.txt"));
        Assert.Contains("fault-content", Read(archive, "latest.log"));
        Assert.Contains("fault.jar", Read(archive, "mods.txt"));
        Assert.Contains(game, text);
        Assert.Contains("Fault Instance", text);
        Assert.DoesNotContain("Different Instance", text);
    }

    [AvaloniaFact]
    public void ApplyingACleanSessionClearsThePreviousCrashPresentation()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services, null, null, Path.Combine(_root, "accounts"), null, null);
        var session = CrashSession.Capture(_root, "Instance", _root);
        launch.ApplyCrashResult(CrashDiagnosticSnapshot.Capture(session, CrashAnalyzer.AnalyzeText("OutOfMemoryError", 1), 1, ""));
        Assert.True(launch.HasCrashReport);
        launch.ApplyCrashResult(CrashDiagnosticSnapshot.Capture(session, CrashAnalyzer.AnalyzeText("Stopping!", 0), 0, ""));
        Assert.False(launch.HasCrashReport);
        Assert.Empty(launch.CrashTitle);
        Assert.Empty(launch.CrashDetails);
        Assert.Contains("游戏已退出", launch.Status);
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
