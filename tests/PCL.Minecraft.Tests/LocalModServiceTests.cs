using PCL.Modding.Local;

namespace PCL.Minecraft.Tests;

public class LocalModServiceTests
{
    [Fact]
    public void ListsAndTogglesMods()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcl-mods-" + Guid.NewGuid().ToString("N"));
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
