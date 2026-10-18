using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PCL.App.Services;
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
            var viewModel = new MainWindowViewModel(settings);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
