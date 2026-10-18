using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class LegacyCompatibilityTests
{
    [Fact]
    public void ReadsLegacyPclInstanceMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-legacy-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Test");
        var legacy = Path.Combine(version, "PCL");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "instance.json"),
            """{"AuthServer":"https://example.com/api/yggdrasil","PluginDirectory":"/srv/mc/plugins"}""");
        File.WriteAllText(Path.Combine(legacy, "install.json"),
            """{"SchemaVersion":1,"Components":{"minecraft":"1.20.1"},"ManagedFiles":[]}""");
        try
        {
            var settings = InstanceSettingsStore.Load(version);
            Assert.Equal("https://example.com/api/yggdrasil", settings.AuthServer);
            Assert.Equal("/srv/mc/plugins", settings.PluginDirectory);
            var manifest = InstallManifestStore.Load(root, "Test");
            Assert.NotNull(manifest);
            Assert.Equal("1.20.1", manifest!.Components["minecraft"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
