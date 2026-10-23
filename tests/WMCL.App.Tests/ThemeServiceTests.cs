using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;
using Xunit;

namespace WMCL.App.Tests;

public sealed class ThemeServiceTests
{
    [AvaloniaFact]
    public void WindowsForcesBlueAccent()
    {
        ThemeService.SetWindowsOverrideForTests(true);
        try
        {
            ThemeService.Apply("dark", 130, 65, 12);
            var brush = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["WmclBlue3"]);
            Assert.Equal(ThemeService.Hsl(210, 85, 55), brush.Color);
        }
        finally
        {
            ThemeService.SetWindowsOverrideForTests(null);
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public void WindowsSettingsRejectNonBlueAccent()
    {
        ThemeService.SetWindowsOverrideForTests(true);
        try
        {
            var services = new AppServices(new AppSettings { GameDirectory = Path.GetTempPath() }, saveSettings: _ => { });
            var settings = new SettingsViewModel(services);
            Assert.False(settings.CanCustomizeAccent);
            Assert.True(settings.IsWindowsThemeRestricted);
            Assert.Equal(210, settings.AccentHue);
            Assert.Equal(85, settings.AccentSaturation);
            Assert.Equal(0, settings.AccentLightAdjust);

            settings.AccentHue = 130;
            settings.AccentSaturation = 20;
            settings.AccentLightAdjust = 12;
            Assert.Equal(210, settings.AccentHue);
            Assert.Equal(85, settings.AccentSaturation);
            Assert.Equal(0, settings.AccentLightAdjust);
        }
        finally
        {
            ThemeService.SetWindowsOverrideForTests(null);
        }
    }

    [AvaloniaFact]
    public void NonWindowsKeepsRequestedAccent()
    {
        ThemeService.SetWindowsOverrideForTests(false);
        try
        {
            ThemeService.Apply("light", 130, 65, 0);
            var brush = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["WmclBlue3"]);
            Assert.Equal(ThemeService.Hsl(130, 65, 55), brush.Color);
        }
        finally
        {
            ThemeService.SetWindowsOverrideForTests(null);
            ThemeService.Apply("light");
        }
    }
}
