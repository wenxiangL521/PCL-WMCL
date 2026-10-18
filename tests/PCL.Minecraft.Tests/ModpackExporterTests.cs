using System.IO.Compression;
using System.Text.Json;
using PCL.Modding.Modpack;

namespace PCL.Minecraft.Tests;

public class ModpackExporterTests
{
    [Fact]
    public async Task ExportsFabricInstanceAsMrpack()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-mrpack-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        Directory.CreateDirectory(Path.Combine(version, "config"));
        await File.WriteAllTextAsync(Path.Combine(version, "Pack.json"), """
            {"id":"Pack","type":"release","inheritsFrom":"1.21.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.19.5"}],"arguments":{"game":["--x"],"jvm":[]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(version, "Pack.jar"), "jar");
        await File.WriteAllTextAsync(Path.Combine(version, "mods", "a.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(version, "config", "c.txt"), "config");

        try
        {
            var output = Path.Combine(root, "pack.mrpack");
            var result = await ModpackExporter.ExportAsync(root, "Pack", output);
            Assert.Equal(2, result.FileCount);

            using var archive = ZipFile.OpenRead(output);
            using var indexStream = archive.GetEntry("modrinth.index.json")!.Open();
            using var document = JsonDocument.Parse(indexStream);
            var deps = document.RootElement.GetProperty("dependencies");
            Assert.Equal("1.21.1", deps.GetProperty("minecraft").GetString());
            Assert.Equal("0.19.5", deps.GetProperty("fabric-loader").GetString());
            Assert.NotNull(archive.GetEntry("overrides/mods/a.jar"));
            Assert.NotNull(archive.GetEntry("overrides/config/c.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
