using Avalonia;
using Avalonia.Styling;

namespace PCL.App.Services;

public static class ThemeService
{
    public static string Normalize(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "dark" => "dark",
        "light" => "light",
        _ => "system"
    };

    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = Normalize(theme) switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
