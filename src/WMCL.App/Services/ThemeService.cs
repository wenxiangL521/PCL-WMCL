using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace WMCL.App.Services;

public static class ThemeService
{
    public static string Normalize(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "dark" => "dark",
        "light" => "light",
        _ => "system"
    };

    public static void Apply(
        string? theme,
        int hue = 210,
        int saturation = 85,
        int lightAdjust = 0)
    {
        if (Application.Current is not { } app) return;
        var normalized = Normalize(theme);
        app.RequestedThemeVariant = normalized switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        var dark = normalized == "dark" ||
                   (normalized == "system" && app.ActualThemeVariant == ThemeVariant.Dark);
        ApplyAccent(app, hue, saturation, lightAdjust, dark);
    }

    private static void ApplyAccent(
        Application app, int hue, int saturation, int lightAdjust, bool dark)
    {
        hue = Math.Clamp(hue, 0, 360);
        saturation = Math.Clamp(saturation, 0, 100);
        lightAdjust = Math.Clamp(lightAdjust, -30, 30);

        var s = saturation;
        var l = lightAdjust;
        Set(app, "WmclBlue1", Hsl(hue, s * 0.2, dark ? 80 : 25 + l * 0.3));
        Set(app, "WmclBlue2", Hsl(hue, s, 45 + l));
        Set(app, "WmclBlue3", Hsl(hue, s, 55 + l));
        Set(app, "WmclBlue4", Hsl(hue, s, 65 + l * 0.8));
        Set(app, "WmclBlue5", Hsl(hue, s, 80 + l * 0.4));
        Set(app, "WmclBlue6", Hsl(hue, s * 0.9, dark ? 22 : 91 + l * 0.1));
        Set(app, "WmclBlue7", Hsl(hue, s * 0.8, dark ? 18 : 95));
        Set(app, "WmclBlue8", Hsl(hue, s * 0.7, dark ? 15 : 97));

        app.Resources["WmclTitleGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Hsl(hue, s, dark ? 45 : 65 + l * 0.8), 0),
                new GradientStop(Hsl(hue, s, dark ? 38 : 55 + l), 1)
            }
        };
        app.Resources["WmclPage"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.9, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Hsl(hue, s * 0.55, dark ? 11 : 78 + l * 0.2), 0),
                new GradientStop(Hsl(hue, s * 0.3, dark ? 13 : 92), 0.4),
                new GradientStop(Hsl(hue, s * 0.25, dark ? 15 : 90), 1)
            }
        };
        app.Resources["WmclBorder"] = new SolidColorBrush(
            Hsl(hue, s * 0.35, dark ? 24 : 90));
        app.Resources["WmclSidebar"] = new SolidColorBrush(
            Hsl(hue, s * 0.2, dark ? 11 : 99));
        app.Resources["WmclInput"] = new SolidColorBrush(dark ? Color.Parse("#2A2D33") : Colors.White);
        app.Resources["WmclCard"] = new SolidColorBrush(
            dark ? Color.Parse("#202226") : Color.FromArgb(245, 255, 255, 255));
        app.Resources["WmclText"] = new SolidColorBrush(
            dark ? Color.Parse("#E7E9EE") : Hsl(hue, s * 0.2, 25));
        app.Resources["WmclTextMuted"] = new SolidColorBrush(
            dark ? Color.Parse("#A9ADB8") : Color.Parse("#737373"));
        app.Resources["WmclTextFaint"] = new SolidColorBrush(
            dark ? Color.Parse("#818692") : Color.Parse("#8C8C8C"));

        // Fluent 控件（Slider、Focus 边框等）使用系统 accent 资源。
        var accent = Hsl(hue, s, 55 + l);
        foreach (var key in new[]
                 {
                     "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2",
                     "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2",
                     "SystemAccentColorDark3"
                 })
            app.Resources[key] = accent;
    }

    private static void Set(Application app, string key, Color color) =>
        app.Resources[key] = new SolidColorBrush(color);

    public static Color Hsl(int hue, double saturation, double lightness)
    {
        var h = ((hue % 360) + 360) % 360 / 360d;
        var s = Math.Clamp(saturation, 0, 100) / 100d;
        var l = Math.Clamp(lightness, 0, 100) / 100d;
        if (s <= 0.0001)
        {
            var gray = (byte)Math.Round(l * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return Color.FromRgb(
            (byte)Math.Round(HueToRgb(p, q, h + 1d / 3) * 255),
            (byte)Math.Round(HueToRgb(p, q, h) * 255),
            (byte)Math.Round(HueToRgb(p, q, h - 1d / 3) * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1d / 6) return p + (q - p) * 6 * t;
        if (t < 1d / 2) return q;
        if (t < 2d / 3) return p + (q - p) * (2d / 3 - t) * 6;
        return p;
    }
}
