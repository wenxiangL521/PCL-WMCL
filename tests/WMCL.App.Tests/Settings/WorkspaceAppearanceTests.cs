using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class WorkspaceAppearanceTests
{
    [Fact]
    public void DefaultsToWmclWhenNoCustomText()
    {
        Assert.Equal("WMCL", new WorkspaceAppearance(new AppSettings()).LogoText);
        Assert.Equal("WMCL", new WorkspaceAppearance(new AppSettings { CustomLogoText = "   " }).LogoText);
    }

    [Fact]
    public void UsesTrimmedCustomText()
    {
        var appearance = new WorkspaceAppearance(new AppSettings { CustomLogoText = "  My Launcher  " });
        Assert.Equal("My Launcher", appearance.LogoText);
    }
}
