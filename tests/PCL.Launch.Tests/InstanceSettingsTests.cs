using PCL.Launch;
using PCL.Minecraft;

namespace PCL.Launch.Tests;

public class InstanceSettingsTests
{
    [Fact]
    public void SavesAndLoadsInstanceSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcl-instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            InstanceSettingsStore.Save(directory, new InstanceSettings
            {
                MemoryMb = 8192,
                JavaPath = "/opt/java/bin/java",
                ExtraJvmArgs = "-Dfoo=bar",
                ExtraGameArgs = "--fullscreen",
                Width = 1024,
                Height = 600,
                Server = "localhost",
                Port = 25565
            });

            var loaded = InstanceSettingsStore.Load(directory);
            Assert.Equal(8192, loaded.MemoryMb);
            Assert.Equal("/opt/java/bin/java", loaded.JavaPath);
            Assert.Equal("-Dfoo=bar", loaded.ExtraJvmArgs);
            Assert.Equal("--fullscreen", loaded.ExtraGameArgs);
            Assert.Equal(1024, loaded.Width);
            Assert.Equal(600, loaded.Height);
            Assert.Equal("localhost", loaded.Server);
            Assert.Equal(25565, loaded.Port);

            InstanceSettingsStore.Reset(directory);
            Assert.Null(InstanceSettingsStore.Load(directory).MemoryMb);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("-Dfoo=bar --fullscreen", new[] { "-Dfoo=bar", "--fullscreen" })]
    [InlineData("-Dname=\"hello world\" --x", new[] { "-Dname=hello world", "--x" })]
    [InlineData("  --a   --b  ", new[] { "--a", "--b" })]
    public void SplitsArguments(string input, string[] expected)
    {
        Assert.Equal(expected, CommandLineSplitter.Split(input));
    }
}
