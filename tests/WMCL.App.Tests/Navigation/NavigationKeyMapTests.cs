using WMCL.App.Services.Navigation;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class NavigationKeyMapTests
{
    [Theory]
    [InlineData("start", NavTarget.Launch, null)]
    [InlineData("download", NavTarget.Download, "download")]
    [InlineData("loaders", NavTarget.Download, "download")]
    [InlineData("mods", NavTarget.Download, "mods")]
    [InlineData("content-shader", NavTarget.Download, "content-shader")]
    [InlineData("content", NavTarget.Download, "content-resource")]
    [InlineData("settings", NavTarget.Settings, "settings-launch")]
    [InlineData("settings-ui", NavTarget.Settings, "settings-ui")]
    [InlineData("about", NavTarget.More, "about")]
    [InlineData("more", NavTarget.More, "help")]
    [InlineData("link", NavTarget.Link, null)]
    public void ResolvesKeyToTabAndSubKey(string key, NavTarget target, string? subKey)
    {
        var (actualTarget, actualSubKey) = NavigationKeyMap.Resolve(key);
        Assert.Equal(target, actualTarget);
        Assert.Equal(subKey, actualSubKey);
    }
}
