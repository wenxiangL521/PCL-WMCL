using Avalonia;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions.Parse(args);
        WmclPaths.EnsureCreated();
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
