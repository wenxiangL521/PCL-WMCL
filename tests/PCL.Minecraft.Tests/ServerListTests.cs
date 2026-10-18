using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class ServerListTests
{
    [Fact]
    public void RoundTripsServersWithIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-servers-" + Guid.NewGuid().ToString("N"));
        try
        {
            var servers = new[]
            {
                new MinecraftServer("Main", "mc.example.com", "icon-data"),
                new MinecraftServer("Backup", "backup.example.com:25566")
            };
            ServerListService.Save(root, servers);
            var loaded = ServerListService.Load(root);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(servers[0], loaded[0]);
            Assert.Equal(servers[1], loaded[1]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExportsAndImportsArbitraryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-servers-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "exported.dat");
        try
        {
            ServerListService.SaveFile(file, [new MinecraftServer("One", "one.example.com")]);
            var loaded = ServerListService.LoadFile(file);
            Assert.Single(loaded);
            Assert.Equal("one.example.com", loaded[0].Address);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingFileReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-servers-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Empty(ServerListService.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
