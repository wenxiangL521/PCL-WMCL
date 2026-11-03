using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class UiFontServiceTests
{
    [AvaloniaFact]
    public void DefaultChainBundlesHarmonyAndFallsBackToSystemChinese()
    {
        var chain = UiFontService.Resolve(new AppSettings());
        Assert.Contains("avares://WMCL.App/Assets/Fonts#HarmonyOS Sans", chain);
        Assert.Contains("Noto Sans CJK SC", chain);
    }

    [AvaloniaFact]
    public void UserFontTakesPriorityButKeepsChineseFallback()
    {
        var chain = UiFontService.Resolve(new AppSettings { UiFontFamily = "DejaVu Sans" });
        Assert.StartsWith("DejaVu Sans,", chain);
        Assert.Contains("Noto Sans CJK SC", chain);
    }

    [AvaloniaFact]
    public void InstalledFontsAreListed()
    {
        Assert.NotEmpty(UiFontService.InstalledFonts());
    }
}
