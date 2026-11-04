using WMCL.App.ViewModels.Instances;

namespace WMCL.App.Tests;

public sealed class InstanceServerSettingsTests
{
    [Fact]
    public void DefaultsWhenNothingConfigured()
    {
        var settings = InstanceServerSettings.From(null, null, null, null, null, null, false);
        Assert.Equal("", settings.Server);
        Assert.Equal(InstanceServerSettings.DefaultPort, settings.Port);
        Assert.Null(settings.EffectivePort);
        Assert.Equal("", settings.TrimmedServer);
    }

    [Fact]
    public void PortOnlyPersistsWithServer()
    {
        var withServer = InstanceServerSettings.From("mc.example.com", 25566, null, null, null, null, false);
        Assert.Equal(25566, withServer.EffectivePort);

        var withoutServer = InstanceServerSettings.From("", 25566, null, null, null, null, false);
        Assert.Null(withoutServer.EffectivePort);
    }

    [Fact]
    public void TrimsAllTextFields()
    {
        var settings = InstanceServerSettings.From(
            "  host  ", null, "  auth  ", "  name  ", "  plugins  ", "  java -jar x  ", true);
        Assert.Equal("host", settings.TrimmedServer);
        Assert.Equal("auth", settings.TrimmedAuthServer);
        Assert.Equal("name", settings.TrimmedAuthServerName);
        Assert.Equal("plugins", settings.TrimmedPluginDirectory);
        Assert.Equal("java -jar x", settings.TrimmedPreLaunchCommand);
        Assert.True(settings.PreLaunchWait);
    }

    [Fact]
    public void RoundTripsThroughSettingsShape()
    {
        var original = InstanceServerSettings.From("host", 25570, "auth", "name", "plugins", "cmd", true);
        var restored = InstanceServerSettings.From(
            original.TrimmedServer, original.EffectivePort, original.TrimmedAuthServer,
            original.TrimmedAuthServerName, original.TrimmedPluginDirectory,
            original.TrimmedPreLaunchCommand, original.PreLaunchWait);
        Assert.Equal(original, restored);
    }
}
