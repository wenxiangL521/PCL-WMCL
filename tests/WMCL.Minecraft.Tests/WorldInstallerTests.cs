using System.IO.Compression;
using WMCL.Modding.CurseForge;

namespace WMCL.Minecraft.Tests;

public class WorldInstallerTests
{
    [Theory]
    [InlineData("escape")]
    [InlineData("symlink")]
    [InlineData("missing")]
    [InlineData("multiple")]
    public void InvalidWorldArchivesNeverLeaveAnImportedSave(string problem)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, "world.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(problem == "missing" ? "readme.txt" : "World/level.dat").Open())) writer.Write("fixture");
            if (problem != "missing")
            {
                var entry = archive.CreateEntry(problem switch { "escape" => "../escape.txt", "multiple" => "Other/level.dat", _ => "World/link" });
                if (problem == "symlink") entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("other");
            }
        }
        try
        {
            Assert.ThrowsAny<Exception>(() => WorldInstaller.Install(zip, Path.Combine(root, "saves"), "Map"));
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "saves")));
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NestedWorldWithReadmeImportsAndExistingFileIsProtected()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, "world.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Wrapper/Map/level.dat").Open())) writer.Write("world");
            using (var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open())) writer.Write("readme");
        }
        try
        {
            var saves = Path.Combine(root, "saves");
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, "Map"), "keep");
            var destination = WorldInstaller.Install(zip, saves, "Map");
            Assert.Equal("Map (2)", Path.GetFileName(destination));
            Assert.Equal("world", File.ReadAllText(Path.Combine(destination, "level.dat")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(saves, "Map")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ExtractsWorldWithLevelDat()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-world-" + Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(root, "world.zip");
        Directory.CreateDirectory(root);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("level.dat").Open());
            writer.Write("level");
        }
        try
        {
            var destination = WorldInstaller.Install(zip, Path.Combine(root, "saves"), "My World");
            Assert.True(File.Exists(Path.Combine(destination, "level.dat")));
            Assert.EndsWith("My World", destination);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
