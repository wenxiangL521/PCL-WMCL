using System.IO.Compression;
using WMCL.Modding.Local;

namespace WMCL.Minecraft.Tests;

public class LocalModServiceTests
{
    [Fact]
    public void ReadsFabricForgeAndNeoForgeMetadataAndFallsBackOnBrokenJars()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-mod-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var archive = ZipFile.Open(Path.Combine(directory, "fabric.jar"), ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("fabric.mod.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""{"id":"sodium","name":"Sodium","version":"0.5.8"}""");
            }
            using (var archive = ZipFile.Open(Path.Combine(directory, "forge.jar"), ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("META-INF/mods.toml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""
                    [[mods]]
                    modId="jei"
                    displayName="Just Enough Items"
                    version="15.2.0"
                    """);
            }
            using (var archive = ZipFile.Open(Path.Combine(directory, "neoforge.jar"), ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("META-INF/neoforge.mods.toml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""
                    [[mods]]
                    modId="neoforge-test"
                    displayName="Neo Test"
                    version="1.0.0"
                    """);
            }
            File.WriteAllText(Path.Combine(directory, "broken.jar"), "not a zip");

            var mods = LocalModService.List(directory);
            var fabric = mods.Single(mod => mod.FileName == "fabric.jar");
            Assert.Equal("sodium", fabric.ModId);
            Assert.Equal("Sodium", fabric.DisplayName);
            Assert.Equal("0.5.8", fabric.Version);
            Assert.Equal("jei", mods.Single(mod => mod.FileName == "forge.jar").ModId);
            Assert.Equal("Just Enough Items", mods.Single(mod => mod.FileName == "forge.jar").DisplayName);
            Assert.Equal("neoforge-test", mods.Single(mod => mod.FileName == "neoforge.jar").ModId);
            Assert.Equal("broken.jar", mods.Single(mod => mod.FileName == "broken.jar").DisplayName);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ListsAndTogglesMods()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "a.jar"), "a");
            File.WriteAllText(Path.Combine(directory, "b.jar.disabled"), "b");
            File.WriteAllText(Path.Combine(directory, "c.jar.old"), "c");
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "ignore");

            var mods = LocalModService.List(directory);
            Assert.Equal(3, mods.Count);
            Assert.True(mods.Single(m => m.DisplayName == "a.jar").IsEnabled);
            Assert.False(mods.Single(m => m.DisplayName == "b.jar").IsEnabled);
            Assert.False(mods.Single(m => m.DisplayName == "c.jar").IsEnabled);

            var a = mods.Single(m => m.DisplayName == "a.jar");
            LocalModService.SetEnabled(directory, a, false);
            Assert.True(File.Exists(Path.Combine(directory, "a.jar.disabled")));

            var b = LocalModService.List(directory).Single(m => m.DisplayName == "b.jar");
            LocalModService.SetEnabled(directory, b, true);
            Assert.True(File.Exists(Path.Combine(directory, "b.jar")));

            var c = LocalModService.List(directory).Single(m => m.DisplayName == "c.jar");
            LocalModService.Delete(c);
            Assert.False(File.Exists(Path.Combine(directory, "c.jar.old")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
