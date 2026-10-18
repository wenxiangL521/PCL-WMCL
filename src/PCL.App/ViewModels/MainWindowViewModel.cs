using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PCL.App.Services;

namespace PCL.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavItem _launchTab;
    private readonly NavItem _downloadTab;
    private readonly NavItem _linkTab;
    private readonly NavItem _settingsTab;
    private readonly NavItem _moreTab;
    private readonly IReadOnlyList<NavItem> _downloadTabs;
    private readonly IReadOnlyList<NavItem> _moreTabs;
    private readonly IReadOnlyList<NavItem> _settingsTabs;

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
        Link = new LinkViewModel();

        _downloadTabs =
        [
            new NavItem("▣", "原版游戏", Download, "download"),
            new NavItem("◇", "加载器", Mods, "loaders"),
            new NavItem("◈", "Mod", Mods, "mods"),
            new NavItem("▤", "整合包", Modpack, "modpack"),
            new NavItem("▥", "资源包", Content, "content-resource"),
            new NavItem("✦", "光影包", Content, "content-shader"),
            new NavItem("▧", "存档", Content, "content-world"),
            new NavItem("▦", "实例管理", Instance, "instance")
        ];
        _moreTabs =
        [
            new NavItem("ⓘ", "关于", About, "about"),
            new NavItem("▦", "实例管理", Instance, "instance")
        ];
        _settingsTabs =
        [
            new NavItem("▶", "启动", Settings, "settings-launch"),
            new NavItem("◑", "个性化", Settings, "settings-ui"),
            new NavItem("…", "其他", Settings, "settings-other")
        ];

        _launchTab = new NavItem("▶", "启动", Launch, "launch");
        _downloadTab = new NavItem("↓", "下载", Download, "download");
        _linkTab = new NavItem("◎", "联机", Link, "link");
        _settingsTab = new NavItem("⚙", "设置", Settings, "settings");
        _moreTab = new NavItem("…", "更多", About, "more");
        TopTabs = [_launchTab, _downloadTab, _linkTab, _settingsTab, _moreTab];

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

        NavigateTo(StartupOptions.StartPage?.ToLowerInvariant() ?? "launch");
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
    public LinkViewModel Link { get; }

    public IReadOnlyList<NavItem> TopTabs { get; }
    public ObservableCollection<NavItem> SubTabs { get; } = [];

    public string VersionText => $"v{PCL.Core.AppInfo.Version} · M7";

    [ObservableProperty]
    private NavItem? _selectedTopTab;

    [ObservableProperty]
    private NavItem? _selectedSubTab;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _hasSubTabs;

    private bool _initialized;
    private bool _navigating;

    partial void OnSelectedTopTabChanged(NavItem? value)
    {
        if (value is null || _navigating) return;
        _navigating = true;
        try
        {
            ConfigureSubTabs(value);
            if (!HasSubTabs)
            {
                CurrentPage = value.Page;
                if (_initialized) LoadPage(value.Page);
            }
        }
        finally
        {
            _navigating = false;
        }
    }

    partial void OnSelectedSubTabChanged(NavItem? value)
    {
        if (value is null || _navigating) return;
        if (ReferenceEquals(value.Page, Mods))
            Mods.SetMode(value.Key);
        else if (ReferenceEquals(value.Page, Settings))
            Settings.SetSection(value.Key);
        else if (ReferenceEquals(value.Page, Content))
            Content.SetMode(value.Key);
        CurrentPage = value.Page;
        if (_initialized) LoadPage(value.Page);
    }

    private void ConfigureSubTabs(NavItem top)
    {
        SubTabs.Clear();
        if (ReferenceEquals(top, _downloadTab))
            foreach (var tab in _downloadTabs) SubTabs.Add(tab);
        else if (ReferenceEquals(top, _moreTab))
            foreach (var tab in _moreTabs) SubTabs.Add(tab);
        else if (ReferenceEquals(top, _settingsTab))
            foreach (var tab in _settingsTabs) SubTabs.Add(tab);

        HasSubTabs = SubTabs.Count > 0;
        SelectedSubTab = SubTabs.FirstOrDefault();
    }

    private void NavigateTo(string key)
    {
        _navigating = true;
        try
        {
            NavItem top;
            string? subKey = null;
            switch (key)
            {
                case "download":
                    top = _downloadTab;
                    subKey = "download";
                    break;
                case "loaders":
                case "mods":
                case "modpack":
                case "content-resource":
                case "content-shader":
                case "content-world":
                case "instance":
                    top = _downloadTab;
                    subKey = key;
                    break;
                case "content":
                    top = _downloadTab;
                    subKey = "content-resource";
                    break;
                case "settings":
                    top = _settingsTab;
                    subKey = "settings-launch";
                    break;
                case "about":
                    top = _moreTab;
                    subKey = "about";
                    break;
                case "link":
                    top = _linkTab;
                    break;
                default:
                    top = _launchTab;
                    break;
            }

            SelectedTopTab = top;
            ConfigureSubTabs(top);
            SelectedSubTab = subKey is null
                ? SubTabs.FirstOrDefault()
                : SubTabs.FirstOrDefault(tab => tab.Key == subKey) ?? SubTabs.FirstOrDefault();
            if (SelectedSubTab?.Page is { } page && ReferenceEquals(page, Mods))
                Mods.SetMode(SelectedSubTab.Key);
            if (SelectedSubTab?.Page is { } settingsPage && ReferenceEquals(settingsPage, Settings))
                Settings.SetSection(SelectedSubTab.Key);
            if (SelectedSubTab?.Page is { } contentPage && ReferenceEquals(contentPage, Content))
                Content.SetMode(SelectedSubTab.Key);
            CurrentPage = SelectedSubTab?.Page ?? top.Page;
        }
        finally
        {
            _navigating = false;
        }
    }

    private void LoadPage(ViewModelBase page)
    {
        if (ReferenceEquals(page, Launch) && !Launch.IsBusy)
            _ = Launch.LoadAsync();
        else if (ReferenceEquals(page, Instance))
            _ = Instance.LoadAsync();
        else if (ReferenceEquals(page, Content))
            _ = Content.LoadAsync();
        else if (ReferenceEquals(page, Modpack))
            _ = Modpack.LoadAsync();
        else if (ReferenceEquals(page, Settings))
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
        await Settings.LoadAsync();

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
        if (ReferenceEquals(CurrentPage, Settings))
            await Settings.LoadAsync();
    }
}
