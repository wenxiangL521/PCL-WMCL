using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public sealed class CleanroomSplashConfigTests
{
    [Fact]
    public void CreatesSplashConfigWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-splash-" + Guid.NewGuid().ToString("N"));
        try
        {
            CleanroomSplashConfig.Ensure(root);
            var text = File.ReadAllText(Path.Combine(root, "config", "splash.properties"));
            Assert.Contains("disableSplash=true", text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplacesDisableSplashAndPreservesOtherOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-splash-" + Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config");
        Directory.CreateDirectory(config);
        try
        {
            File.WriteAllText(Path.Combine(config, "splash.properties"),
                "enabled=true\ndisableSplash=false\nrotate=true\n");
            CleanroomSplashConfig.Ensure(root);
            var lines = File.ReadAllLines(Path.Combine(config, "splash.properties"));
            Assert.Contains("disableSplash=true", lines);
            Assert.Contains("enabled=true", lines);
            Assert.Contains("rotate=true", lines);
            Assert.DoesNotContain("disableSplash=false", lines);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
