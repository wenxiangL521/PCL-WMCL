using System.IO.Compression;
using PCL.Launch.Diagnostics;

namespace PCL.Launch.Tests;

public class DiagnosticPackageTests
{
    [Fact]
    public void CreatesZipWithReportLogSystemAndMods()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-diag-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "mods"));
        Directory.CreateDirectory(Path.Combine(game, "logs"));
        File.WriteAllText(Path.Combine(game, "logs", "latest.log"), "log line");
        File.WriteAllText(Path.Combine(game, "mods", "example.jar"), "jar");
        var output = Path.Combine(root, "out");
        try
        {
            var path = DiagnosticPackage.Export(
                output, "1.21.1", "崩溃摘要", Path.Combine(game, "logs", "latest.log"), game);
            Assert.True(File.Exists(path));
            using var archive = ZipFile.OpenRead(path);
            Assert.Contains(archive.Entries, entry => entry.FullName == "report.txt");
            Assert.Contains(archive.Entries, entry => entry.FullName == "latest.log");
            Assert.Contains(archive.Entries, entry => entry.FullName == "system.txt");
            Assert.Contains(archive.Entries, entry => entry.FullName == "mods.txt");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
