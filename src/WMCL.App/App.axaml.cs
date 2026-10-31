using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Localization;
using WMCL.Download;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            PclInteractionMotion.Initialize();
            var settings = SettingsService.Load();
            // 安装并加载本地化资源
            LocaleInstaller.EnsureInstalled();
            var localeDir = Path.Combine(WmclPaths.DataDirectory, "locales");
            Localizer.LoadFromDirectory(localeDir);
            Localizer.SetLocale(settings.Locale);
            if (!string.IsNullOrWhiteSpace(StartupOptions.GameDirectory))
                settings.GameDirectory = StartupOptions.GameDirectory;
            if (!string.IsNullOrWhiteSpace(StartupOptions.Theme))
                settings.Theme = StartupOptions.Theme;
            ThemeService.Apply(settings.Theme, settings.AccentHue, settings.AccentSaturation, settings.AccentLightAdjust);
            DownloadThrottle.SetLimit((long)(Math.Max(0, settings.DownloadSpeedLimitMbps) * 1024 * 1024));
            var viewModel = new MainWindowViewModel(settings);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                Width = Math.Max(810, settings.Width),
                Height = Math.Max(470, settings.Height)
            };
            ApplyFontScale(desktop.MainWindow, settings.FontScale);
            viewModel.StartMusic();
            desktop.MainWindow.Closing += (_, _) => viewModel.Dispose();
            _ = viewModel.InitializeAsync();


            if (!string.IsNullOrWhiteSpace(StartupOptions.AutoUpdateUrl))
            {
                desktop.MainWindow.Opened += async (_, _) =>
                    await viewModel.About.AutoUpdateAsync(StartupOptions.AutoUpdateUrl!);
            }

            if (StartupOptions.MicrosoftLogin)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    var loginViewModel = new MicrosoftLoginViewModel(viewModel.Services);
                    try
                    {
                        var loginWindow = new MicrosoftLoginWindow { DataContext = loginViewModel };
                        await loginWindow.ShowDialog(desktop.MainWindow);
                        if (loginViewModel.Account is not null)
                            viewModel.Launch.SetMicrosoftAccount(loginViewModel.Account);
                    }
                    finally
                    {
                        loginViewModel.Dispose();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyFontScale(Window window, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.01) return;
        var clamped = Math.Clamp(scale, 0.8, 1.5);
        window.FontSize = 13 * clamped;
    }
}
