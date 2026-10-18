using Avalonia;
using PCL.App.Services;

namespace PCL.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
