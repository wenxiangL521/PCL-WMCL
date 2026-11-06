using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>
/// "Follow system" used to read ActualThemeVariant before it settled, so surfaces were painted
/// for one variant while text came from the other (white text on light cards).
/// </summary>
[Trait("Kind", "Headless")]
public sealed class SystemThemeConsistencyTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    public void SurfacesAndTextAlwaysComeFromTheSameVariant(string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            ThemeService.Apply(theme);
            AssertSameVariant(theme);
            // Re-applying must be idempotent, including after the window was closed/reopened.
            ThemeService.Apply(theme);
            AssertSameVariant(theme);
        }
        finally { Application.Current.RequestedThemeVariant = previous; }
    }

    [AvaloniaFact]
    public void SystemThemeUsesThePlatformVariantRatherThanAStaleDefault()
    {
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            // Simulate the bug's precondition: a different variant was requested before.
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            ThemeService.Apply("system");
            var expected = ThemeService.ResolveActualVariant(Application.Current, "system");
            AssertSameVariant("system");
            var page = Assert.IsAssignableFrom<ILinearGradientBrush>(Application.Current.FindResource("WmclPage"));
            var text = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.Resources["WmclText"]);
            var darkSurface = page.GradientStops.Average(stop => Luminance(stop.Color)) < 0.45;
            var darkText = Luminance(text.Color) > 0.45;
            Assert.Equal(expected == ThemeVariant.Dark, darkSurface == !darkText);
        }
        finally { Application.Current.RequestedThemeVariant = previous; }
    }

    [AvaloniaFact]
    public void SystemThemeFollowsThePlatformAndExplicitThemesRepaintImmediately()
    {
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            ThemeService.Apply("system");
            var platform = Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(
                Application.Current!.Resources["WmclSidebar"]).Color);
            // Following the system must stay on the platform variant even if the requested
            // variant changes underneath (activity restart ordering).
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(platform, Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(
                Application.Current!.Resources["WmclSidebar"]).Color), 6);

            ThemeService.Apply("dark");
            var dark = Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(
                Application.Current!.Resources["WmclSidebar"]).Color);
            Assert.True(dark < 0.5, $"dark theme sidebar should be dark: {dark}");
            ThemeService.Apply("light");
            var light = Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(
                Application.Current!.Resources["WmclSidebar"]).Color);
            Assert.True(light > 0.5, $"light theme sidebar should be light: {light}");
        }
        finally { Application.Current.RequestedThemeVariant = previous; }
    }

    [AvaloniaFact]
    public void LaunchButtonTextContrastsWithItsSurfaceInBothVariants()
    {
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                ThemeService.Apply(theme);
                var accent = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.Resources["WmclBlue3"]).Color;
                var buttonText = Colors.White;
                Assert.True(Contrast(buttonText, accent) >= 2.8,
                    $"{theme}: white button text on accent {accent} contrast {Contrast(buttonText, accent):0.00}");
            }
        }
        finally { Application.Current.RequestedThemeVariant = previous; }
    }

    private static void AssertSameVariant(string theme)
    {
        var page = Assert.IsAssignableFrom<ILinearGradientBrush>(Application.Current!.Resources["WmclPage"]);
        var sidebar = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.Resources["WmclSidebar"]);
        var text = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.Resources["WmclText"]);
        var darkSurface = page.GradientStops.Average(stop => Luminance(stop.Color)) < 0.45 &&
                          Luminance(sidebar.Color) < 0.45;
        var lightSurface = page.GradientStops.Average(stop => Luminance(stop.Color)) >= 0.45 &&
                           Luminance(sidebar.Color) >= 0.45;
        var lightText = Luminance(text.Color) > 0.45;
        if (darkSurface) Assert.True(lightText, $"{theme}: dark surface must use light text");
        else if (lightSurface) Assert.False(lightText, $"{theme}: light surface must use dark text");
        else Assert.Fail($"{theme}: page and sidebar disagree on variant");
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;

    private static double Contrast(Color a, Color b)
    {
        static double Relative(Color color)
        {
            static double Channel(byte value)
            {
                var c = value / 255d;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var first = Relative(a);
        var second = Relative(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
