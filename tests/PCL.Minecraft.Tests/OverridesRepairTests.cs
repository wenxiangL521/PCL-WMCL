using System.IO.Compression;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class OverridesRepairTests
{
    [Fact]
    public async Task ReExtractsOverridesFromSourceArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-overrides-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "versions", "Pack");
        var archivePath = Path.Combine(root, "pack.zip");
        Directory.CreateDirectory(instance);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(
                archive.CreateEntry("overrides/config/test.txt").Open());
            writer.Write("restored");
        }
        try
        {
            InstallManifestStore.Save(root, "Pack", new InstallManifest
            {
                SourceArchive = archivePath,
                OverridesPrefix = "overrides",
                OverridesRoot = instance
            });
            var count = await OverridesRepair.RepairAsync(root, "Pack");
            Assert.Equal(1, count);
            Assert.Equal("restored", File.ReadAllText(
                Path.Combine(instance, "config", "test.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
