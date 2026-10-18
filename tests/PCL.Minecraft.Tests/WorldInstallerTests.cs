using System.IO.Compression;
using PCL.Modding.CurseForge;

namespace PCL.Minecraft.Tests;

public class WorldInstallerTests
{
    [Fact]
    public void ExtractsWorldWithLevelDat()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-world-" + Guid.NewGuid().ToString("N"));
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
