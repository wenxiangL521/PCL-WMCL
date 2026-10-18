using System.IO.Compression;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class InstanceExporterTests
{
    [Fact]
    public async Task ExportsInstanceWithoutLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-export-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "TestPack");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        Directory.CreateDirectory(Path.Combine(version, "config"));
        Directory.CreateDirectory(Path.Combine(version, "logs"));
        Directory.CreateDirectory(Path.Combine(version, "PCL"));
        await File.WriteAllTextAsync(Path.Combine(version, "TestPack.json"), """
            {"id":"TestPack","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[],"arguments":{"game":["--x"],"jvm":[]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(version, "TestPack.jar"), "jar");
        await File.WriteAllTextAsync(Path.Combine(version, "mods", "a.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(version, "config", "c.txt"), "config");
        await File.WriteAllTextAsync(Path.Combine(version, "logs", "latest.log"), "log");
        await File.WriteAllTextAsync(Path.Combine(version, "options.txt"), "options");
        await File.WriteAllTextAsync(Path.Combine(version, "PCL", "instance.json"), "{}");

        try
        {
            var output = Path.Combine(root, "out.zip");
            var result = await InstanceExporter.ExportAsync(root, "TestPack", output);

            Assert.True(File.Exists(result.ArchivePath));
            Assert.True(result.FileCount >= 5);
            using var archive = ZipFile.OpenRead(output);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("pcl-export.json", names);
            Assert.Contains("instance/TestPack.json", names);
            Assert.Contains("instance/TestPack.jar", names);
            Assert.Contains("instance/mods/a.jar", names);
            Assert.Contains("instance/config/c.txt", names);
            Assert.Contains("instance/options.txt", names);
            Assert.DoesNotContain(names, name => name.Contains("logs"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
