using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PCL.App.Services;
using PCL.Download;
using PCL.App.ViewModels;
using PCL.App.Views;

namespace PCL.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsService.Load();
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
            viewModel.StartMusic();
            desktop.MainWindow.Closing += (_, _) => viewModel.StopMusic();
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
                    var loginWindow = new MicrosoftLoginWindow { DataContext = loginViewModel };
                    await loginWindow.ShowDialog(desktop.MainWindow);
                    if (loginViewModel.Account is not null)
                        viewModel.Launch.SetMicrosoftAccount(loginViewModel.Account);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
