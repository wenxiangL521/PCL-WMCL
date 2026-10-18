using System.IO.Compression;
using PCL.Modding.Local;

namespace PCL.Minecraft.Tests;

public class InstanceContentServiceTests
{
    [Fact]
    public void ListsResourcePacksShadersAndWorlds()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(root, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(root, "saves", "world1"));
        File.WriteAllText(Path.Combine(root, "resourcepacks", "pack.zip"), "pack");
        File.WriteAllText(Path.Combine(root, "shaderpacks", "shader.zip"), "shader");
        File.WriteAllText(Path.Combine(root, "saves", "world1", "level.dat"), "level");

        try
        {
            var packs = InstanceContentService.List(root, InstanceContentKind.ResourcePack);
            var shaders = InstanceContentService.List(root, InstanceContentKind.ShaderPack);
            var worlds = InstanceContentService.List(root, InstanceContentKind.World);

            Assert.Single(packs);
            Assert.Equal("pack.zip", packs[0].Name);
            Assert.Single(shaders);
            Assert.Single(worlds);
            Assert.Equal("world1", worlds[0].Name);
            Assert.True(worlds[0].IsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportsResourcePackAndWorldZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pack = Path.Combine(root, "pack.zip");
        File.WriteAllText(pack, "zip");
        var worldZip = Path.Combine(root, "WorldImport.zip");
        using (var archive = ZipFile.Open(worldZip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("TestWorld/level.dat").Open()))
            writer.Write("level");

        try
        {
            var importedPack = InstanceContentService.Import(
                root, InstanceContentKind.ResourcePack, pack);
            Assert.Equal("pack.zip", importedPack.Name);
            Assert.True(File.Exists(Path.Combine(root, "resourcepacks", "pack.zip")));

            var importedWorld = InstanceContentService.Import(
                root, InstanceContentKind.World, worldZip);
            Assert.Equal("TestWorld", importedWorld.Name);
            Assert.True(File.Exists(Path.Combine(root, "saves", "TestWorld", "level.dat")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BacksUpAndDeletesWorld()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-content-" + Guid.NewGuid().ToString("N"));
        var worldDir = Path.Combine(root, "saves", "world1");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "level");
        var backupDir = Path.Combine(root, "backups");

        try
        {
            var world = InstanceContentService.List(root, InstanceContentKind.World).Single();
            var backup = InstanceContentService.BackupWorld(world, backupDir);
            Assert.True(File.Exists(backup));
            using (var archive = ZipFile.OpenRead(backup))
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("level.dat"));

            InstanceContentService.Delete(world);
            Assert.False(Directory.Exists(worldDir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
