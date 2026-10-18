using Avalonia;
using PCL.App.Services;
using PCL.Core;

namespace PCL.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions.Parse(args);
        var settings = SettingsService.Load();
        ProxyEnvironment.Apply(settings.ProxyUrl);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
