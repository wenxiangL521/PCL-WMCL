using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WMCL.App.Tests.TestAppBuilder))]

namespace WMCL.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
