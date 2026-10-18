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
        Mods = new ModsViewModel(Services);
        Content = new InstanceContentViewModel(Services);
        Modpack = new ModpackViewModel(Services);
        Settings = new SettingsViewModel(Services);
        About = new AboutViewModel(Services);

        NavItems =
        [
            new NavItem("▶", "启动", Launch),
            new NavItem("↓", "下载", Download),
            new NavItem("▦", "实例", Instance),
            new NavItem("◈", "模组", Mods),
            new NavItem("▣", "整合包", Modpack),
            new NavItem("▤", "资源", Content),
            new NavItem("⚙", "设置", Settings),
            new NavItem("ⓘ", "关于", About)
        ];

        Mods.VersionInstalled += (_, _) => _ = Launch.LoadAsync();
        Instance.InstancesChanged += (_, _) =>
        {
            _ = Launch.LoadAsync();
            _ = Mods.LoadAsync();
            _ = Content.LoadAsync();
            _ = Modpack.LoadAsync();
        };
        Modpack.InstanceInstalled += (_, _) =>
        {
            _ = Launch.LoadAsync();
            _ = Instance.LoadAsync();
            _ = Content.LoadAsync();
            _ = Mods.LoadAsync();
        };
        SelectedNav = StartupOptions.StartPage?.ToLowerInvariant() switch
        {
            "download" => NavItems[1],
            "instance" => NavItems[2],
            "mods" => NavItems[3],
            "modpack" => NavItems[4],
            "content" => NavItems[5],
            "settings" => NavItems[6],
            "about" => NavItems[7],
            _ => NavItems[0]
        };
    }

    public AppServices Services { get; }
    public LaunchViewModel Launch { get; }
    public DownloadViewModel Download { get; }
    public InstanceViewModel Instance { get; }
    public ModsViewModel Mods { get; }
    public InstanceContentViewModel Content { get; }
    public ModpackViewModel Modpack { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    public ObservableCollection<NavItem> NavItems { get; }
    public string VersionText => $"v{PCL.Core.AppInfo.Version} · M7";

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    private bool _initialized;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null)
            CurrentPage = value.Page;

        if (!_initialized) return;
        if (ReferenceEquals(value?.Page, Launch) && !Launch.IsBusy)
            _ = Launch.LoadAsync();
        else if (ReferenceEquals(value?.Page, Instance))
            _ = Instance.LoadAsync();
        else if (ReferenceEquals(value?.Page, Content))
            _ = Content.LoadAsync();
        else if (ReferenceEquals(value?.Page, Modpack))
            _ = Modpack.LoadAsync();
        else if (ReferenceEquals(value?.Page, Settings))
            _ = Settings.LoadAsync();
    }

    public async Task InitializeAsync()
    {
        await Launch.LoadAsync();
        await Download.LoadAsync();
        await Instance.LoadAsync();
        await Mods.LoadAsync();
        await Content.LoadAsync();
        await Modpack.LoadAsync();

        if (!string.IsNullOrWhiteSpace(StartupOptions.ModInstance))
            await Mods.SelectModInstanceAsync(StartupOptions.ModInstance);

        if (StartupOptions.CheckModUpdates)
        {
            await Mods.CheckModUpdatesCommand.ExecuteAsync(null);
            if (StartupOptions.ApplyModUpdate)
                await Mods.UpdateSelectedModCommand.ExecuteAsync(null);
        }

        if (!string.IsNullOrWhiteSpace(StartupOptions.ModpackSearch))
        {
            Modpack.SearchText = StartupOptions.ModpackSearch;
            await Modpack.SearchCommand.ExecuteAsync(null);
        }

        if (!string.IsNullOrWhiteSpace(StartupOptions.ContentSearch))
        {
            Content.DownloadSearchText = StartupOptions.ContentSearch;
            await Content.SearchDownloadsCommand.ExecuteAsync(null);
        }

        if (!string.IsNullOrWhiteSpace(StartupOptions.ModSource))
            Mods.SelectedModSource = StartupOptions.ModSource;

        if (!string.IsNullOrWhiteSpace(StartupOptions.ModrinthSearch))
        {
            Mods.SearchText = StartupOptions.ModrinthSearch;
            await Mods.SearchModsCommand.ExecuteAsync(null);
        }

        if (!string.IsNullOrWhiteSpace(StartupOptions.ModrinthInstall))
        {
            var target = Mods.SearchResults.FirstOrDefault(project =>
                string.Equals(project.Slug, StartupOptions.ModrinthInstall,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Id, StartupOptions.ModrinthInstall,
                    StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                Mods.SelectedMod = target;
                await Mods.InstallModCommand.ExecuteAsync(null);
            }
        }

        if (StartupOptions.AutoInstallVersion is { Length: > 0 } autoInstall)
        {
            var target = Launch.Versions.FirstOrDefault(version =>
                string.Equals(version.Id, autoInstall, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                Launch.SelectedVersion = target;
                await Launch.InstallCommand.ExecuteAsync(null);
            }
        }

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

        _initialized = true;
        if (ReferenceEquals(SelectedNav?.Page, Settings))
            await Settings.LoadAsync();
    }
}
