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
            PopupReveal.Initialize();
            var settings = SettingsService.Load();
            var settingsWarning = SettingsService.LastLoadWarning;
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
                Width = Math.Max(810, settings.LauncherWidth),
                Height = Math.Max(470, settings.LauncherHeight)
            };
            viewModel.StartMusic();
            desktop.MainWindow.Closing += (_, _) =>
            {
                // 自由缩放：关闭时记住启动器窗口尺寸（最大化/最小化不覆盖）。
                if (desktop.MainWindow is { WindowState: WindowState.Normal } window)
                {
                    settings.LauncherWidth = Math.Max(810, (int)window.Width);
                    settings.LauncherHeight = Math.Max(470, (int)window.Height);
                    viewModel.Services.SaveSettings();
                }
                viewModel.Dispose();
            };
            _ = viewModel.InitializeAsync();


            if (!string.IsNullOrWhiteSpace(settingsWarning))
            {
                viewModel.Settings.ConfigStatus = settingsWarning;
                desktop.MainWindow.Opened += async (_, _) =>
                    await DialogService.ShowMessageAsync("配置恢复提示", settingsWarning, isWarning: true);
            }

            // 启动后延迟检查更新：避免拖慢启动，也不打扰刚打开的用户。
            if (settings.UpdateMode != "off" || !string.IsNullOrWhiteSpace(StartupOptions.AutoUpdateUrl))
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(25));
                    await viewModel.About.AutoUpdateAsync();
                };
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

}
