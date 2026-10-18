using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly NavItem _launchTab;
    private readonly NavItem _downloadTab;
    private readonly NavItem _linkTab;
    private readonly NavItem _settingsTab;
    private readonly NavItem _moreTab;
    private readonly IReadOnlyList<NavItem> _downloadTabs;
    private readonly IReadOnlyList<NavItem> _moreTabs;
    private readonly IReadOnlyList<NavItem> _settingsTabs;
    private readonly Stack<(ViewModelBase Page, string Title)> _backStack = new();

    public bool CanGoBack => _backStack.Count > 0;

    [ObservableProperty]
    private string _currentTitle = "启动";

    public MainWindowViewModel(AppSettings settings)
    {
        Services = new AppServices(settings);
        var logoPath = settings.CustomLogoPath?.Trim();
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            try { LogoImage = new Bitmap(logoPath); }
            catch { LogoImage = null; }
        }
        var backgroundPath = ResolveBackground(settings);
        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
        {
            try { BackgroundImage = new Bitmap(backgroundPath); }
            catch { BackgroundImage = null; }
        }
        BackgroundImageOpacity = Math.Clamp(settings.BackgroundImageOpacity, 0, 100) / 100d;
        BackgroundImageBlur = Math.Clamp(settings.BackgroundImageBlur, 0, 30);
        BackgroundColorOverlay = settings.BackgroundColorOverlay;

        Launch = new LaunchViewModel(Services);
        Launch.PropertyChanged += OnLaunchPropertyChanged;
        Download = new DownloadViewModel(Services);
        Instance = new InstanceViewModel(Services);
        Mods = new ModsViewModel(Services);
        Install = new InstallViewModel(Download, Mods, Services);
        Content = new InstanceContentViewModel(Services);
        Modpack = new ModpackViewModel(Services);
        Tasks = new TaskManagerViewModel(Download, Mods, Modpack, Content, Instance, Install);
        Settings = new SettingsViewModel(Services);
        About = new AboutViewModel(Services);
        Link = new LinkViewModel(Services);
        LinkSettings = new MorePageViewModel(
            "联机设置",
            "EasyTier 手动联机参数",
            "联机页使用手动 EasyTier 网络：同一房间码 + 公共中继。\n\n"
            + "PCL2 官方房间服务依赖闭源配置，开源版无法获取，因此这里提供替代方案；"
            + "官方房间码/自动房间列表不可用。");
        Help = new HelpViewModel();
        Toolbox = new ToolboxViewModel(Services);
        Feedback = new MorePageViewModel(
            "反馈",
            "问题反馈与诊断信息",
            "当前项目为非官方重写，反馈渠道尚未配置。\n\n"
            + "提交问题前请附上启动页日志，以及“关于 → 打开日志目录”中的最新日志与崩溃报告。",
            "打开原版仓库",
            "https://github.com/Hex-Dragon/PCL2");
        Vote = new MorePageViewModel(
            "新功能投票",
            "PCL2 社区的新功能投票",
            "新功能投票沿用原版 PCL2 的 GitHub Discussions。\n\n"
            + "本项目会优先对齐原版已有功能，暂不另行维护投票列表。",
            "打开投票页面",
            "https://github.com/Meloong-Git/WMCL/discussions/categories/%E5%8A%9F%E8%83%BD%E6%8A%95%E7%A5%A8");

        _downloadTabs =
        [
            new NavItem("▣", "原版游戏", Install, "download"),
            new NavItem("◈", "Mod", Mods, "mods"),
            new NavItem("▤", "整合包", Modpack, "modpack"),
            new NavItem("▥", "数据包", Content, "content-datapack"),
            new NavItem("▥", "资源包", Content, "content-resource"),
            new NavItem("✦", "光影包", Content, "content-shader"),
            new NavItem("▧", "存档", Content, "content-world")
        ];
        _moreTabs =
        [
            new NavItem("?", "帮助", Help, "help"),
            new NavItem("ⓘ", "关于与鸣谢", About, "about"),
            new NavItem("▣", "百宝箱", Toolbox, "toolbox"),
            new NavItem("⇧", "反馈", Feedback, "feedback"),
            new NavItem("◔", "新功能投票", Vote, "vote")
        ];
        _settingsTabs =
        [
            new NavItem("▶", "启动", Settings, "settings-launch"),
            new NavItem("◎", "联机", LinkSettings, "settings-link"),
            new NavItem("◑", "个性化", Settings, "settings-ui"),
            new NavItem("…", "其他", Settings, "settings-other")
        ];

        _launchTab = new NavItem("▶", "启动", Launch, "launch", TopTabIcons.Launch);
        _downloadTab = new NavItem("↓", "下载", Download, "download", TopTabIcons.Download);
        _linkTab = new NavItem("◎", "联机", Link, "link", TopTabIcons.Link);
        _settingsTab = new NavItem("⚙", "设置", Settings, "settings", TopTabIcons.Settings);
        _moreTab = new NavItem("…", "更多", About, "more", TopTabIcons.More);
        TopTabs = [_launchTab, _downloadTab, _linkTab, _settingsTab, _moreTab];

        VersionSelect = new VersionSelectViewModel(
            Launch,
            Services,
            CloseVersionSelect,
            () => NavigateTo("download"));
        Services.GameDirectoryChanged += (_, _) => _ = ReloadGameFolderAsync();
        Launch.VersionSettingsRequested += async (_, id) =>
            await OpenInstanceSettingsAsync(id);
        Install.InstallCompleted += (_, _) =>
        {
            _ = Launch.LoadAsync();
            _ = Instance.LoadAsync();
            _ = Mods.LoadAsync();
            _ = Content.LoadAsync();
            RefreshTaskState();
        };
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

        foreach (var taskSource in new INotifyPropertyChanged[]
                 {
                     Download, Mods, Modpack, Content, Instance, Install, Launch
                 })
            taskSource.PropertyChanged += OnTaskPropertyChanged;

        var startPage = StartupOptions.StartPage?.ToLowerInvariant() ?? "launch";
        NavigateTo("launch");
        if (startPage == "version-select") OpenVersionSelect();
        else if (startPage != "launch") NavigateTo(startPage);
    }

    public AppServices Services { get; }
    public Bitmap? BackgroundImage { get; }
    public bool HasBackgroundImage => BackgroundImage is not null;
    public Bitmap? LogoImage { get; }
    public bool HasCustomLogo => LogoImage is not null;
    public MusicService Music { get; } = new();

    public void StartMusic()
    {
        if (Services.Settings.MusicStart)
            Music.Start(Services.Settings.MusicFolder, Services.Settings.MusicVolume);
    }

    public void StopMusic() => Music.Stop();

    public void Dispose()
    {
        Music.Stop();
        Instance.Dispose();
    }

    private bool _musicPausedForGame;

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LaunchViewModel.IsGameRunning)) return;
        if (Launch.IsGameRunning)
        {
            if (Services.Settings.MusicPauseOnGame && Music.IsPlaying)
            {
                Music.Stop();
                _musicPausedForGame = true;
            }
        }
        else if (_musicPausedForGame)
        {
            _musicPausedForGame = false;
            Music.Start(Services.Settings.MusicFolder, Services.Settings.MusicVolume);
        }
    }
    public double BackgroundImageOpacity { get; }
    public double BackgroundImageBlur { get; }
    public bool BackgroundColorOverlay { get; }

    private static string? ResolveBackground(AppSettings settings)
    {
        var folder = settings.BackgroundImageFolder?.Trim();
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
            var images = Directory.EnumerateFiles(folder)
                .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();
            if (images.Count > 0)
                return images[Random.Shared.Next(images.Count)];
        }
        return settings.BackgroundImagePath?.Trim();
    }

    [ObservableProperty]
    private bool _isTaskActive;

    [ObservableProperty]
    private double _taskProgress;

    [ObservableProperty]
    private string _taskPercentText = "";

    [ObservableProperty]
    private string _taskText = "";

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshTaskState();

    private void RefreshTaskState()
    {
        var active = true;
        double progress;
        string text;
        if (Download.IsBusy && Download.CanCancel)
        {
            progress = Download.Progress;
            text = "下载/安装 Minecraft";
        }
        else if (Mods.IsBusy)
        {
            progress = Mods.Progress;
            text = "安装 Fabric";
        }
        else if (Mods.IsForgelikeBusy)
        {
            progress = Mods.ForgelikeProgress;
            text = "安装 Forge/NeoForge";
        }
        else if (Mods.IsOptiFineBusy)
        {
            progress = Mods.OptiFineProgress;
            text = "安装 OptiFine";
        }
        else if (Install.IsCombinedBusy)
        {
            progress = Install.CombinedProgress;
            text = "组合安装";
        }
        else if (Modpack.IsBusy)
        {
            progress = Modpack.Progress;
            text = "安装整合包";
        }
        else if (Content.IsDownloadBusy)
        {
            progress = Content.DownloadProgress;
            text = "下载资源";
        }
        else if (Instance.IsImporting)
        {
            progress = 0;
            text = "导入实例";
        }
        else if (Launch.IsBusy && Launch.Progress > 0)
        {
            progress = Launch.Progress;
            text = "安装/启动版本";
        }
        else
        {
            active = false;
            progress = 0;
            text = "";
        }

        IsTaskActive = active;
        TaskProgress = Math.Clamp(progress, 0, 100);
        TaskPercentText = active
            ? progress > 0 ? $"{TaskProgress:0}%" : "…"
            : "";
        TaskText = text;
    }

    public double TaskProgressHeight => 40 * TaskProgress / 100;
    public string TaskIcon => TopTabIcons.Download;

    partial void OnTaskProgressChanged(double value) =>
        OnPropertyChanged(nameof(TaskProgressHeight));

    [RelayCommand]
    private void OpenTask()
    {
        if (Download.IsBusy || Mods.IsBusy || Mods.IsForgelikeBusy || Mods.IsOptiFineBusy)
            NavigateTo("download");
        else if (Modpack.IsBusy)
            NavigateTo("modpack");
        else if (Content.IsDownloadBusy)
            NavigateTo("content-resource");
        else if (Instance.IsImporting)
            NavigateTo("instance");
    }
    public LaunchViewModel Launch { get; }
    public DownloadViewModel Download { get; }
    public InstallViewModel Install { get; }
    public TaskManagerViewModel Tasks { get; }
    public InstanceViewModel Instance { get; }
    public ModsViewModel Mods { get; }
    public InstanceContentViewModel Content { get; }
    public ModpackViewModel Modpack { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }
    public LinkViewModel Link { get; }
    public MorePageViewModel LinkSettings { get; }
    public HelpViewModel Help { get; }
    public ToolboxViewModel Toolbox { get; }
    public MorePageViewModel Feedback { get; }
    public MorePageViewModel Vote { get; }

    public VersionSelectViewModel VersionSelect { get; }
    public IReadOnlyList<NavItem> TopTabs { get; }
    public ObservableCollection<NavItem> SubTabs { get; } = [];

    // PCL2 的启动页左栏从窗口内容边缘开始；其他页面右侧留 25px。
    public Thickness PageMargin =>
        CurrentPage is LaunchViewModel or VersionSelectViewModel
            ? new Thickness(0)
            : new Thickness(25, 10, 25, 25);

    [ObservableProperty]
    private NavItem? _selectedTopTab;

    [ObservableProperty]
    private NavItem? _selectedSubTab;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _hasSubTabs;

    public double SubNavWidth => HasSubTabs ? 152 : 0;

    partial void OnHasSubTabsChanged(bool value) =>
        OnPropertyChanged(nameof(SubNavWidth));

    private bool _initialized;
    private bool _navigating;

    partial void OnCurrentPageChanged(ViewModelBase? value) =>
        OnPropertyChanged(nameof(PageMargin));

    partial void OnSelectedTopTabChanged(NavItem? value)
    {
        OnPropertyChanged(nameof(PageMargin));
        if (value is null || _navigating) return;
        ClearBackStack();
        _navigating = true;
        try
        {
            ConfigureSubTabs(value);
            ApplySubTab(SelectedSubTab);
            CurrentPage = SelectedSubTab?.Page ?? value.Page;
        }
        finally
        {
            _navigating = false;
        }

        CurrentTitle = SelectedSubTab?.Title ?? value.Title;
        if (_initialized) LoadPage(CurrentPage);
    }

    partial void OnSelectedSubTabChanged(NavItem? value)
    {
        if (value is null || _navigating) return;
        ApplySubTab(value);
        CurrentPage = value.Page;
        CurrentTitle = value.Title;
        if (_initialized) LoadPage(value.Page);
    }

    private void ApplySubTab(NavItem? subTab)
    {
        if (subTab is null) return;
        if (ReferenceEquals(subTab.Page, Mods))
            Mods.SetMode(subTab.Key);
        else if (ReferenceEquals(subTab.Page, Settings))
            Settings.SetSection(subTab.Key);
        else if (ReferenceEquals(subTab.Page, Content))
            Content.SetMode(subTab.Key);
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

    private async Task ReloadGameFolderAsync()
    {
        VersionSelect.ReloadFolders();
        Settings.RefreshGameDirectory();
        await Launch.LoadAsync();
        await Download.LoadAsync();
        await Instance.LoadAsync();
        await Mods.LoadAsync();
        await Content.LoadAsync();
        await Modpack.LoadAsync();
    }

    public void OpenInstanceInstall()
    {
        if (Instance.SelectedInstance is { } instance)
            Download.SelectedVersion = instance;
        PushPage(Install, "安装");
    }

    public void OpenInstanceMods()
    {
        if (Instance.SelectedInstance is { } instance)
            Mods.SelectedModInstance = instance.Id;
        PushPage(Mods, "Mod");
    }

    public void OpenInstanceContent(string key, string title)
    {
        if (Instance.SelectedInstance is { } instance)
            Content.SelectedInstance = instance.Id;
        Content.SetMode(key);
        PushPage(Content, title);
    }

    public void OpenVersionSelect()
    {
        VersionSelect.RefreshSelection();
        PushPage(VersionSelect, "版本选择");
    }

    private void CloseVersionSelect() => GoBack();

    private void PushPage(ViewModelBase page, string title)
    {
        if (ReferenceEquals(CurrentPage, page)) return;
        if (CurrentPage is not null)
            _backStack.Push((CurrentPage, CurrentTitle));
        CurrentPage = page;
        CurrentTitle = title;
        OnPropertyChanged(nameof(CanGoBack));
        if (_initialized) LoadPage(page);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        var (page, title) = _backStack.Pop();
        CurrentPage = page;
        CurrentTitle = title;
        OnPropertyChanged(nameof(CanGoBack));
        if (_initialized) LoadPage(page);
    }

    private void ClearBackStack()
    {
        if (_backStack.Count == 0) return;
        _backStack.Clear();
        OnPropertyChanged(nameof(CanGoBack));
    }

    private void NavigateTo(string key)
    {
        if (key == "instance")
        {
            PushPage(Instance, "实例设置");
            return;
        }
        _navigating = true;
        try
        {
            NavItem top;
            string? subKey = null;
            switch (key)
            {
                case "download":
                case "loaders":
                    top = _downloadTab;
                    subKey = "download";
                    break;
                case "mods":
                case "modpack":
                case "content-resource":
                case "content-shader":
                case "content-world":
                case "content-datapack":
                    top = _downloadTab;
                    subKey = key;
                    break;
                case "instance":
                    top = _moreTab;
                    subKey = "instance";
                    break;
                case "content":
                    top = _downloadTab;
                    subKey = "content-resource";
                    break;
                case "settings":
                    top = _settingsTab;
                    subKey = "settings-launch";
                    break;
                case "settings-link":
                case "settings-ui":
                case "settings-other":
                    top = _settingsTab;
                    subKey = key;
                    break;
                case "about":
                case "help":
                case "toolbox":
                case "feedback":
                case "vote":
                    top = _moreTab;
                    subKey = key;
                    break;
                case "more":
                    top = _moreTab;
                    subKey = "help";
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
            ApplySubTab(SelectedSubTab);
            CurrentPage = SelectedSubTab?.Page ?? top.Page;
        }
        finally
        {
            _navigating = false;
        }
    }

    private async Task OpenInstanceSettingsAsync(string id)
    {
        await Instance.LoadAsync();
        var target = Instance.Instances.FirstOrDefault(instance =>
            string.Equals(instance.Id, id, StringComparison.OrdinalIgnoreCase));
        if (target is not null) Instance.SelectedInstance = target;
        PushPage(Instance, "实例设置");
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
