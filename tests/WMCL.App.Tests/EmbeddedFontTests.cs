using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class EmbeddedFontTests
{
    private const string Uri = "avares://WMCL.UI/Assets/Fonts#HarmonyOS Sans";

    [AvaloniaFact]
    public void HarmonyOsSansIsEmbeddedAndResolvable()
    {
        Assert.True(AssetLoader.Exists(new Uri("avares://WMCL.UI/Assets/Fonts/HarmonyOS-Sans-Regular.ttf")));
        Assert.True(AssetLoader.Exists(new Uri("avares://WMCL.UI/Assets/Fonts/HarmonyOS-Sans-Bold.ttf")));
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(Uri), out var typeface));
        Assert.Contains("HarmonyOS", typeface.FamilyName, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void RegularAndBoldResolveToDifferentWeights()
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(
            new Typeface(Uri, FontStyle.Normal, FontWeight.Normal), out var regular));
        Assert.True(FontManager.Current.TryGetGlyphTypeface(
            new Typeface(Uri, FontStyle.Normal, FontWeight.Bold), out var bold));
        Assert.NotEqual(regular.Weight, bold.Weight);
    }
}
