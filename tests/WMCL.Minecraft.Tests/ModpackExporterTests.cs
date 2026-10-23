using System.IO.Compression;
using System.Text.Json;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public class ModpackExporterTests
{
    [Theory]
    [InlineData("net.fabricmc:fabric-loader:0.19.5", "fabric-loader", "0.19.5")]
    [InlineData("org.quiltmc:quilt-loader:0.28.0", "quilt-loader", "0.28.0")]
    [InlineData("net.minecraftforge:forge:1.21.1-52.0.0", "forge", "52.0.0")]
    public async Task ExportsFlattenedInstanceWithActualMinecraftAndLoaderDependencies(
        string library, string dependency, string loaderVersion)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-mrpack-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        Directory.CreateDirectory(Path.Combine(version, "config"));
        await File.WriteAllTextAsync(Path.Combine(version, "Pack.json"), """
            {"id":"Pack","type":"release","libraries":[{"name":"LOADER"},{"name":"optifine:OptiFine:1.21.1_HD_U_J1"}],"arguments":{"game":["--x"],"jvm":[]}}
            """.Replace("LOADER", library));
        InstallManifestStore.Save(root, "Pack", new InstallManifest { Components = { ["minecraft"] = "1.21.1" } });
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
            Assert.Equal(loaderVersion, deps.GetProperty(dependency).GetString());
            Assert.Equal(2, deps.EnumerateObject().Count());
            Assert.NotNull(archive.GetEntry("overrides/mods/a.jar"));
            Assert.NotNull(archive.GetEntry("overrides/config/c.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StandaloneOptiFineIsRejectedBeforeCreatingAnInvalidForgePack()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-mrpack-optifine-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "Pack.json"), """
            {"id":"Pack","clientVersion":"1.20.1","libraries":[{"name":"optifine:OptiFine:1.20.1_HD_U_I6"}]}
            """);
        try
        {
            var output = Path.Combine(root, "invalid.mrpack");
            var error = await Assert.ThrowsAsync<NotSupportedException>(() => ModpackExporter.ExportAsync(root, "Pack", output));
            Assert.Contains("optifine", error.Message);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
