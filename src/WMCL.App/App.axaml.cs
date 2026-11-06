using WMCL.Link;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Download;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // 上次被强杀可能留下 easytier-core 孤儿，占用 TUN 与默认网段，先清掉
        _ = Task.Run(() =>
        {
            var killed = EasyTierService.CleanupOrphans();
            if (killed > 0)
                System.Diagnostics.Trace.WriteLine($"清理残留 EasyTier 进程：{killed} 个");
        });
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
            if (settings.LauncherLeft is { } left && settings.LauncherTop is { } top)
                desktop.MainWindow.Position = new PixelPoint(left, top);
            viewModel.StartMusic();
            desktop.MainWindow.Closing += (_, e) =>
            {
                // 关闭按钮行为：hide = 隐藏到托盘，exit = 真正退出
                if (desktop.MainWindow is MainWindow { ForceClose: false } window &&
                    !string.Equals(settings.CloseBehavior, "exit", StringComparison.OrdinalIgnoreCase) &&
                    window.TryHideToTray())
                {
                    e.Cancel = true;
                    return;
                }

                // 自由缩放：关闭时记住启动器窗口尺寸（最大化/最小化不覆盖）。
                if (desktop.MainWindow is { WindowState: WindowState.Normal } saved)
                {
                    settings.LauncherWidth = Math.Max(810, (int)saved.Width);
                    settings.LauncherHeight = Math.Max(470, (int)saved.Height);
                    settings.LauncherLeft = saved.Position.X;
                    settings.LauncherTop = saved.Position.Y;
                }
                // 最大化/最小化时也要保存，否则最后一段时间的修改会丢失
                viewModel.Services.SaveSettings();
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
