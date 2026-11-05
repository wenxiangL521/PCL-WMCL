using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void PositionDefaultsToUnset()
    {
        var settings = new AppSettings();
        Assert.Null(settings.LauncherLeft);
        Assert.Null(settings.LauncherTop);
    }

    [Fact]
    public void PositionRoundTripsThroughJson()
    {
        var file = Path.Combine(WMCL.Tests.TestEnvironment.Root, "cfg-" + Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var settings = new AppSettings { LauncherLeft = 120, LauncherTop = 340 };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        File.WriteAllText(file, json);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file))!;
        Assert.Equal(120, loaded.LauncherLeft);
        Assert.Equal(340, loaded.LauncherTop);
    }
}
