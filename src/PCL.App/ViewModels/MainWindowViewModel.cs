using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PCL.App.Services;

namespace PCL.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(AppSettings settings)
    {
        Services = new AppServices(settings);
        Launch = new LaunchViewModel(Services);
        Download = new DownloadViewModel(Services);
        Instance = new InstanceViewModel(Services);
        Settings = new SettingsViewModel(Services);
        About = new AboutViewModel();

        NavItems =
        [
            new NavItem("▶", "启动", Launch),
            new NavItem("↓", "下载", Download),
            new NavItem("▦", "实例", Instance),
            new NavItem("⚙", "设置", Settings),
            new NavItem("ⓘ", "关于", About)
        ];

        SelectedNav = NavItems[0];
    }

    public AppServices Services { get; }
    public LaunchViewModel Launch { get; }
    public DownloadViewModel Download { get; }
    public InstanceViewModel Instance { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    public ObservableCollection<NavItem> NavItems { get; }
    public string VersionText => "v0.1.0 · M2";

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null)
            CurrentPage = value.Page;
    }

    public async Task InitializeAsync()
    {
        await Launch.LoadAsync();
        await Download.LoadAsync();
        await Instance.LoadAsync();

        if (StartupOptions.AutoLaunchVersion is { Length: > 0 } autoLaunch)
        {
            var target = Launch.Versions.FirstOrDefault(version =>
                string.Equals(version.Id, autoLaunch, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                Launch.SelectedVersion = target;
                await Launch.LaunchCommand.ExecuteAsync(null);
            }
        }
    }
}
