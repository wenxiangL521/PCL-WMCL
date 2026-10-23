using System.IO.Compression;
using System.Text.Json;
using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class InstanceExporterTests
{
    [Fact]
    public async Task ExportsInstanceWithoutLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-export-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "TestPack");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        Directory.CreateDirectory(Path.Combine(version, "config"));
        Directory.CreateDirectory(Path.Combine(version, "logs"));
        Directory.CreateDirectory(Path.Combine(version, "WMCL"));
        await File.WriteAllTextAsync(Path.Combine(version, "TestPack.json"), """
            {"id":"TestPack","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[],"arguments":{"game":["--x"],"jvm":[]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(version, "TestPack.jar"), "jar");
        await File.WriteAllTextAsync(Path.Combine(version, "mods", "a.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(version, "config", "c.txt"), "config");
        await File.WriteAllTextAsync(Path.Combine(version, "logs", "latest.log"), "log");
        await File.WriteAllTextAsync(Path.Combine(version, "options.txt"), "options");
        await File.WriteAllTextAsync(Path.Combine(version, "WMCL", "instance.json"), "{}");
        InstallManifestStore.Save(root, "TestPack", new InstallManifest { Components = { ["minecraft"] = "1.20.1" } });

        try
        {
            var output = Path.Combine(root, "out.zip");
            var result = await InstanceExporter.ExportAsync(root, "TestPack", output);

            Assert.True(File.Exists(result.ArchivePath));
            Assert.True(result.FileCount >= 5);
            using var archive = ZipFile.OpenRead(output);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("wmcl-export.json", names);
            Assert.Contains("instance/TestPack.json", names);
            Assert.Contains("instance/TestPack.jar", names);
            Assert.Contains("instance/mods/a.jar", names);
            Assert.Contains("instance/config/c.txt", names);
            Assert.Contains("instance/options.txt", names);
            Assert.DoesNotContain(names, name => name.Contains("logs"));
            using var metadataStream = archive.GetEntry("wmcl-export.json")!.Open();
            using var metadata = JsonDocument.Parse(metadataStream);
            Assert.Equal("TestPack", metadata.RootElement.GetProperty("id").GetString());
            Assert.Equal("1.20.1", metadata.RootElement.GetProperty("minecraft").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class InstanceExporterOptionsTests
{
    [Fact]
    public async Task ExcludesUnselectedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-export-opt-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Pack");
        foreach (var directory in new[] { "mods", "config", "resourcepacks", "shaderpacks", "saves", "WMCL" })
            Directory.CreateDirectory(Path.Combine(version, directory));
        await File.WriteAllTextAsync(Path.Combine(version, "Pack.json"),
            """{"id":"Pack","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[],"arguments":{"game":[],"jvm":[]}}""");
        await File.WriteAllTextAsync(Path.Combine(version, "mods", "a.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(version, "config", "c.txt"), "config");
        Directory.CreateDirectory(Path.Combine(version, "saves", "world"));
        await File.WriteAllTextAsync(Path.Combine(version, "saves", "world", "level.dat"), "world");
        await File.WriteAllTextAsync(Path.Combine(version, "options.txt"), "options");

        try
        {
            var output = Path.Combine(root, "out.zip");
            await InstanceExporter.ExportAsync(root, "Pack",
                new InstanceExportOptions(
                    IncludeMods: false, IncludeConfigs: true, IncludeResourcePacks: false,
                    IncludeShaderPacks: false, IncludeSaves: false, IncludeWmcl: false,
                    IncludeOptions: false), output);
            using var archive = ZipFile.OpenRead(output);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.DoesNotContain("instance/mods/a.jar", names);
            Assert.DoesNotContain("instance/saves/world/level.dat", names);
            Assert.DoesNotContain("instance/options.txt", names);
            Assert.Contains("instance/config/c.txt", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class InstanceExporterIsolationTests
{
    [Fact]
    public async Task CanExportVanillaWhenForcedIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-export-iso-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Vanilla");
        Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "Vanilla.json"),
            """{"id":"Vanilla","type":"release","mainClass":"net.minecraft.client.main.Main","libraries":[],"arguments":{"game":[],"jvm":[]}}""");
        await File.WriteAllTextAsync(Path.Combine(version, "options.txt"), "options");
        try
        {
            var output = Path.Combine(root, "out.zip");
            var result = await InstanceExporter.ExportAsync(
                root, "Vanilla", new InstanceExportOptions(),
                output, isolationMode: IsolationPolicy.Isolated);
            Assert.True(File.Exists(result.ArchivePath));
            using var archive = ZipFile.OpenRead(output);
            Assert.Contains("instance/Vanilla.json",
                archive.Entries.Select(entry => entry.FullName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
