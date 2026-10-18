using System.IO.Compression;
using System.Text.Json;
using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class InstanceImporterTests
{
    [Fact]
    public async Task ExportsAndImportsInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-import-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "SourcePack");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        Directory.CreateDirectory(Path.Combine(version, "config"));
        await File.WriteAllTextAsync(Path.Combine(version, "SourcePack.json"), """
            {"id":"SourcePack","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[],"arguments":{"game":["--x"],"jvm":[]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(version, "SourcePack.jar"), "jar");
        await File.WriteAllTextAsync(Path.Combine(version, "mods", "a.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(version, "config", "c.txt"), "config");

        try
        {
            var archivePath = Path.Combine(root, "pack.zip");
            await InstanceExporter.ExportAsync(root, "SourcePack", archivePath);

            var imported = await InstanceImporter.ImportAsync(
                root, archivePath, "ImportedPack", installMissing: false);

            Assert.Equal("ImportedPack", imported.Id);
            var jsonPath = Path.Combine(imported.VersionDirectory, "ImportedPack.json");
            Assert.True(File.Exists(jsonPath));
            using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath)))
                Assert.Equal("ImportedPack", json.RootElement.GetProperty("id").GetString());
            Assert.True(File.Exists(Path.Combine(imported.VersionDirectory, "ImportedPack.jar")));
            Assert.True(File.Exists(Path.Combine(imported.VersionDirectory, "mods", "a.jar")));
            Assert.True(File.Exists(Path.Combine(imported.VersionDirectory, "config", "c.txt")));
            Assert.True(imported.FileCount >= 4);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
