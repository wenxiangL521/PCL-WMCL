namespace PCL.Minecraft.Tests;

public class InstanceSettingsTests
{
    [Fact]
    public void AuthServerRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-instance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new InstanceSettings
            {
                AuthServer = "https://example.com/api/yggdrasil",
                AuthServerName = "Example",
                Server = "mc.example.com",
                IsolationMode = IsolationPolicy.Isolated,
                Category = "生存",
                LogoPath = "/tmp/logo.png",
                Description = "Test instance",
                IsFavorite = true,
                PreLaunchCommand = "{java} -version",
                PreLaunchWait = false
            };
            InstanceSettingsStore.Save(root, settings);
            var loaded = InstanceSettingsStore.Load(root);
            Assert.Equal(settings.AuthServer, loaded.AuthServer);
            Assert.Equal(settings.AuthServerName, loaded.AuthServerName);
            Assert.Equal(settings.Server, loaded.Server);
            Assert.Equal(IsolationPolicy.Isolated, loaded.IsolationMode);
            Assert.Equal("生存", loaded.Category);
            Assert.Equal("/tmp/logo.png", loaded.LogoPath);
            Assert.Equal("Test instance", loaded.Description);
            Assert.True(loaded.IsFavorite);
            Assert.Equal(settings.PreLaunchCommand, loaded.PreLaunchCommand);
            Assert.False(loaded.PreLaunchWait);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
