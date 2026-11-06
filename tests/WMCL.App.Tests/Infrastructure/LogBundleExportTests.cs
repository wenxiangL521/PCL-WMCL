using System.IO.Compression;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LogBundleExportTests : IDisposable
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "log-bundle-" + Guid.NewGuid().ToString("N"));

    public LogBundleExportTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task BundleContainsDiagnosticsRedactedSettingsAndInstanceLogs()
    {
        var instance = Path.Combine(_root, "versions", "Pack");
        Directory.CreateDirectory(Path.Combine(instance, "logs"));
        Directory.CreateDirectory(Path.Combine(instance, "crash-reports"));
        File.WriteAllText(Path.Combine(instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.21.1","libraries":[],"arguments":{"game":[],"jvm":[]}}""");
        File.WriteAllText(Path.Combine(instance, "logs", "latest.log"), "[12:00] game started");
        File.WriteAllText(Path.Combine(instance, "crash-reports", "crash-1.txt"), "crash report");
        var services = new AppServices(new AppSettings
        {
            GameDirectory = _root,
            ProxyPassword = "secret-proxy-pw",
        }, saveSettings: _ => { });
        var output = Path.Combine(_root, "bundle.zip");
        var result = await LogBundleExporter.ExportAsync(services, output);
        Assert.True(File.Exists(output));
        Assert.True(result.FileCount >= 3);

        using var archive = ZipFile.OpenRead(output);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("diagnostics.txt", names);
        Assert.Contains("settings.json", names);
        Assert.Contains("instances/Pack/logs/latest.log", names);
        Assert.Contains("instances/Pack/crash-reports/crash-1.txt", names);

        using var settings = new StreamReader(archive.GetEntry("settings.json")!.Open());
        var settingsJson = await settings.ReadToEndAsync();
        Assert.DoesNotContain("secret-proxy-pw", settingsJson);
        using var diagnostics = new StreamReader(archive.GetEntry("diagnostics.txt")!.Open());
        var diagnosticsText = await diagnostics.ReadToEndAsync();
        Assert.Contains("WMCL 诊断信息", diagnosticsText);
        Assert.Contains("游戏目录", diagnosticsText);
        Assert.Contains("Pack", diagnosticsText);
    }

    [AvaloniaFact]
    public async Task ExportRefusesToOverwriteExistingFile()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var output = Path.Combine(_root, "existing.zip");
        File.WriteAllText(output, "keep");
        await Assert.ThrowsAnyAsync<IOException>(() => LogBundleExporter.ExportAsync(services, output));
        Assert.Equal("keep", File.ReadAllText(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
