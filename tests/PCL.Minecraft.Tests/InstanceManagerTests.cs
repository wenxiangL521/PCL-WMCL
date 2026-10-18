using System.Text.Json;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class InstanceManagerTests
{
    [Fact]
    public void RenamesAndDeletesInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-manage-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Old");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "Old.json"), """
            {"id":"Old","type":"release","mainClass":"x","libraries":[]}
            """);
        File.WriteAllText(Path.Combine(version, "Old.jar"), "jar");

        try
        {
            InstanceManager.Rename(root, "Old", "New");
            var newDirectory = Path.Combine(root, "versions", "New");
            Assert.False(Directory.Exists(version));
            Assert.True(Directory.Exists(newDirectory));
            Assert.True(File.Exists(Path.Combine(newDirectory, "New.jar")));
            using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(newDirectory, "New.json"))))
                Assert.Equal("New", json.RootElement.GetProperty("id").GetString());

            InstanceManager.Delete(root, "New");
            Assert.False(Directory.Exists(newDirectory));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsExistingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-manage-" + Guid.NewGuid().ToString("N"));
        foreach (var name in new[] { "A", "B" })
        {
            var directory = Path.Combine(root, "versions", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".json"),
                $$"""{"id":"{{name}}","type":"release","mainClass":"x","libraries":[]}""");
        }

        try
        {
            Assert.Throws<IOException>(() => InstanceManager.Rename(root, "A", "B"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
