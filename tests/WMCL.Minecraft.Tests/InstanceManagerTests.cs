using System.Text.Json;
using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class InstanceManagerTests
{
    [Fact]
    public void RenamesAndDeletesInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-manage-" + Guid.NewGuid().ToString("N"));
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
    public void ClonesInstanceWithData()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-clone-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Source");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        File.WriteAllText(Path.Combine(version, "Source.json"),
            """{"id":"Source","type":"release","mainClass":"x","libraries":[]}""");
        File.WriteAllText(Path.Combine(version, "Source.jar"), "jar");
        File.WriteAllText(Path.Combine(version, "mods", "a.jar"), "mod");

        try
        {
            var clone = InstanceManager.Clone(root, "Source", "Clone");
            Assert.Equal("Clone", clone);
            Assert.True(Directory.Exists(version));
            var cloneDirectory = Path.Combine(root, "versions", "Clone");
            using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(cloneDirectory, "Clone.json"))))
                Assert.Equal("Clone", json.RootElement.GetProperty("id").GetString());
            Assert.True(File.Exists(Path.Combine(cloneDirectory, "Clone.jar")));
            Assert.True(File.Exists(Path.Combine(cloneDirectory, "mods", "a.jar")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsExistingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-manage-" + Guid.NewGuid().ToString("N"));
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
