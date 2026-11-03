using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class LauncherWindowSizeTests
{
    [AvaloniaFact]
    public void LauncherWindowIsResizableAndDefaultsToPclSize()
    {
        var settings = new AppSettings();
        Assert.Equal(850, settings.LauncherWidth);
        Assert.Equal(500, settings.LauncherHeight);
        // 游戏分辨率与启动器窗口尺寸是两个独立设置，改一个不会影响另一个。
        settings.Width = 1920;
        settings.Height = 1080;
        Assert.Equal(850, settings.LauncherWidth);
        Assert.Equal(500, settings.LauncherHeight);

        var window = new MainWindow { DataContext = null };
        try
        {
            Assert.True(window.CanResize);
            Assert.Equal(810, window.MinWidth);
            Assert.Equal(470, window.MinHeight);
            Assert.Equal(850, window.Width);
            Assert.Equal(500, window.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WindowSizeIsRememberedSeparatelyFromGameResolution()
    {
        var directory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "launcher-size-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { GameDirectory = directory, Width = 1280, Height = 720 };
        var saved = 0;
        var services = new AppServices(settings, saveSettings: _ => saved++);
        services.Settings.LauncherWidth = 1000;
        services.Settings.LauncherHeight = 640;
        services.SaveSettings();
        Assert.True(saved > 0);
        Assert.Equal(1280, services.Settings.Width);
        Assert.Equal(1000, services.Settings.LauncherWidth);
        Dispatcher.UIThread.RunJobs();
    }
}
