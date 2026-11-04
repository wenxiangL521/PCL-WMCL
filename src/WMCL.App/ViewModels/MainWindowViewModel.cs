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

    public MainWindowViewModel(AppSettings settings) : this(new AppServices(settings))
    {
    }

    internal MainWindowViewModel(AppServices services,
        Func<string, Task<IReadOnlyList<VersionItem>>>? loadVersions = null,
        Func<string, string, string, Task<string?>>? instancePrompt = null,
        Func<string, string, Task<bool>>? instanceConfirm = null,
        Func<DownloadViewModel, AppServices, InstallViewModel>? createInstall = null,
        Func<AppServices, ModsViewModel>? createMods = null)
    {
        Services = services;
        var settings = services.Settings;
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

        Launch = new LaunchViewModel(Services, loadVersions: loadVersions, newsService: Services.News);
        Launch.PropertyChanged += OnLaunchPropertyChanged;
        Launch.DownloadRequested += (_, _) => NavigateTo("download");
        Download = new DownloadViewModel(Services);
        Cleanroom = new CleanroomViewModel(Services);
        Cleanroom.VersionInstalled += (_, _) => _ = Launch.LoadAsync();
        LabyMod = new LabyModViewModel();
        Instance = new InstanceViewModel(Services, prompt: instancePrompt, confirm: instanceConfirm);
        Mods = createMods?.Invoke(Services) ?? new ModsViewModel(Services);
        Install = createInstall?.Invoke(Download, Services) ?? new InstallViewModel(Download, new ModsViewModel(Services), Services);
        Mods.DownloadRequested += (_, _) =>
        {
            if (ReferenceEquals(CurrentPage, Instance))
            {
                Install.ShowDownloadCatalog();
                PushPage(Install, "下载");
            }
            else NavigateTo("download");
        };
        Mods.VersionSelectRequested += (_, _) => OpenVersionSelect();
        Content = new InstanceContentViewModel(Services);
        Modpack = new ModpackViewModel(Services);
        Favorites = new ResourceFavoritesViewModel(Services.ResourceFavorites, OpenFavoriteAsync,
            lookup: Services.ResourceLookup.ResolveAsync,
            batch: new ResourceFavoriteBatchViewModel(Services,
                () => Launch.SelectedVersion?.Id ?? Instance.SelectedInstance?.Id ?? Mods.SelectedModInstance ?? Content.SelectedInstance));
        Tasks = new TaskManagerViewModel(Download, Mods, Modpack, Content, Instance, Install, Favorites.Batch, Launch);
        Settings = new SettingsViewModel(Services);
        GameManage = new GameManageViewModel(Services);
        Logs = new LogsViewModel(Services);
        Update = new UpdateViewModel(Services);
        About = new AboutViewModel(Services);
        Link = new LinkViewModel(Services);
        LinkSettings = new LinkSettingsViewModel(Link);
        Link.SettingsRequested += (_, _) => NavigateTo("settings-link");
        Settings.JavaManagementRequested += (_, _) => NavigateTo("settings-java");
        Help = new HelpViewModel();
        Toolbox = new ToolboxViewModel(Services);
        Feedback = new FeedbackViewModel(Services);
        Vote = new MorePageViewModel(
            "新功能投票",
            "PCL2 社区的新功能投票",
            "新功能投票沿用原版 PCL2 的 GitHub Discussions。\n\n"
            + "本项目会优先对齐原版已有功能，暂不另行维护投票列表。",
            "打开投票页面",
            "https://github.com/Meloong-Git/WMCL/discussions/categories/%E5%8A%9F%E8%83%BD%E6%8A%95%E7%A5%A8");

        _downloadTabs =
        [
            new NavItem("", "原版游戏", Install, "download", NavIcons.Boxes),
            new NavItem("", "Cleanroom", Cleanroom, "cleanroom", NavIcons.Flask),
            new NavItem("", "LabyMod", LabyMod, "labymod", NavIcons.Box),
            new NavItem("", "社区资源", Mods) { IsHeader = true },
            new NavItem("", "Mod", Mods, "mods", NavIcons.Puzzle),
            new NavItem("", "整合包", Modpack, "modpack", NavIcons.Package),
            new NavItem("", "数据包", Content, "content-datapack", NavIcons.FileArchive),
            new NavItem("", "资源包", Content, "content-resource", NavIcons.Layers),
            new NavItem("", "光影包", Content, "content-shader", NavIcons.Sparkles),
            new NavItem("", "存档", Content, "content-world", NavIcons.Globe),
            new NavItem("", "资源收藏", Favorites, "favorites", NavIcons.Heart)
        ];
        _moreTabs =
        [
            new NavItem("", "帮助", Help, "help", NavIcons.Help),
            new NavItem("", "关于与鸣谢", About, "about", NavIcons.Info),
            new NavItem("", "百宝箱", Toolbox, "toolbox", NavIcons.ToolCase),
            new NavItem("", "反馈", Feedback, "feedback", NavIcons.Feedback),
            new NavItem("", "新功能投票", Vote, "vote", NavIcons.Vote)
        ];
        _settingsTabs =
        [
            new NavItem("", "启动", Settings, "settings-launch", NavIcons.Play),
            new NavItem("", "Java", Settings, "settings-java", NavIcons.Coffee),
            new NavItem("", "游戏管理", GameManage, "settings-games", NavIcons.Monitor),
            new NavItem("", "联机", LinkSettings, "settings-link", NavIcons.Link),
            new NavItem("", "日志", Logs, "settings-log", NavIcons.ScrollText),
            new NavItem("", "更新", Update, "settings-update", NavIcons.Refresh),
            new NavItem("", "个性化", Settings, "settings-ui", NavIcons.Palette),
            new NavItem("", "其他", Settings, "settings-other", NavIcons.Ellipsis)
        ];

        _launchTab = new NavItem("", "启动", Launch, "launch", TopTabIcons.Launch);
        _downloadTab = new NavItem("", "下载", Download, "download", TopTabIcons.Download);
        _linkTab = new NavItem("", "联机", Link, "link", TopTabIcons.Link);
        _settingsTab = new NavItem("", "设置", Settings, "settings", TopTabIcons.Settings);
        _moreTab = new NavItem("", "更多", About, "more", TopTabIcons.More);
        RebuildTopTabs();
        Services.Features.Changed += (_, _) => OnFeatureVisibilityChanged();

        VersionSelect = new VersionSelectViewModel(
            Launch,
            Services,
            CloseVersionSelect,
            () => NavigateTo("download"));
        Services.GameDirectoryChanged += (_, _) => _ = ReloadGameFolderAsync();
        VersionSelect.InstanceInstalled += (_, _) =>
        {
            _ = Launch.LoadAsync();
            _ = Instance.LoadAsync();
            _ = Content.LoadAsync();
            _ = Mods.LoadAsync();
        };
        Launch.VersionSettingsRequested += async (_, id) =>
            await OpenInstanceSettingsAsync(id);
        VersionSelect.VersionSettingsRequested += async (_, id) =>
            await OpenInstanceSettingsAsync(id);
        Install.InstallCompleted += (_, result) =>
        {
            if (!result.InstanceRenamed)
            {
                _ = Launch.LoadAsync();
                _ = Instance.LoadAsync();
                _ = Mods.LoadAsync();
                _ = Content.LoadAsync();
            }
            RefreshTaskState();
        };
        Instance.PropertyChanged += OnInstanceNavigationChanged;
        Install.ExistingInstallSubmitted += (_, _) =>
        {
            if (ReferenceEquals(CurrentPage, Instance) && Instance.SelectedSectionIndex == 2)
                Instance.SelectedSectionIndex = 0;
            else if (ReferenceEquals(CurrentPage, Install)) GoBack();
        };
        Install.TaskFinished += async (_, task) =>
        {
            if (task.PercentText == "失败")
                await DialogService.ShowMessageAsync(task.Title + "失败", task.Detail, isWarning: true);
        };
        Mods.VersionInstalled += (_, _) => _ = Launch.LoadAsync();
        Instance.InstancesChanged += (_, change) => InstanceRefreshTask = RefreshInstancesAsync(change);
        Instance.InstanceSettingsSaved += (_, _) => _ = Launch.LoadAsync();
        Install.InstancesChanged += (_, change) => InstanceRefreshTask = RefreshInstancesAsync(change);
        Modpack.InstanceInstalled += (_, _) =>
        {
            _ = Launch.LoadAsync();
            _ = Instance.LoadAsync();
            _ = Content.LoadAsync();
            _ = Mods.LoadAsync();
        };

        foreach (var taskSource in new INotifyPropertyChanged[]
                 {
                     Download, Mods, Modpack, Content, Instance, Install, Launch, Favorites.Batch!
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
        Launch.PropertyChanged -= OnLaunchPropertyChanged;
        Instance.PropertyChanged -= OnInstanceNavigationChanged;
        ++_instanceSectionRequest;
        foreach (var taskSource in new INotifyPropertyChanged[]
                 {
                     Download, Mods, Modpack, Content, Instance, Install, Favorites.Batch!
                 })
            taskSource.PropertyChanged -= OnTaskPropertyChanged;
        Tasks.Dispose();
        Instance.Dispose();
        Link.Dispose();
        Settings.Dispose();
        Content.Dispose();
        About.Dispose();
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
        else if (Mods.IsBusy && Mods.CanCancel)
        {
            progress = Mods.Progress;
            text = "安装 Fabric";
        }
        else if (Mods.IsInstallingForgelike)
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
        else if (Mods.IsInstallingMod)
        {
            progress = Mods.ModProgress;
            text = "安装 Mod 与必需前置";
        }
        else if (Favorites.Batch is { IsInstalling: true } batch)
        {
            progress = batch.Progress;
            text = "批量安装收藏";
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
        else if (Launch.IsCheckingModpackUpdate || Launch.IsCheckingGameFiles || Launch.IsBusy && Launch.Progress > 0)
        {
            progress = Launch.Progress;
            text = Launch.IsCheckingModpackUpdate ? "启动前检查整合包"
                : Launch.IsCheckingGameFiles ? "启动前校验游戏文件" : "安装/启动版本";
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
        if ((Download.IsBusy && Download.CanCancel) || (Mods.IsBusy && Mods.CanCancel) ||
            Mods.IsInstallingForgelike || Mods.IsOptiFineBusy || Install.IsCombinedBusy)
            NavigateTo("download");
        else if (Mods.IsInstallingMod)
            NavigateTo("mods");
        else if (Favorites.Batch is { IsInstalling: true })
            NavigateTo("favorites");
        else if (Modpack.IsBusy)
            NavigateTo("modpack");
        else if (Content.IsDownloadBusy)
            NavigateTo("content-resource");
        else if (Instance.IsImporting)
            NavigateTo("instance");
        else if (Launch.IsCheckingModpackUpdate || Launch.IsCheckingGameFiles)
            NavigateTo("launch");
    }
    public LaunchViewModel Launch { get; }
    public DownloadViewModel Download { get; }
    public CleanroomViewModel Cleanroom { get; }
    public LabyModViewModel LabyMod { get; }
    public InstallViewModel Install { get; }
    public TaskManagerViewModel Tasks { get; }
    public InstanceViewModel Instance { get; }
    public ModsViewModel Mods { get; }
    public InstanceContentViewModel Content { get; }
    public ModpackViewModel Modpack { get; }
    public ResourceFavoritesViewModel Favorites { get; }
    public SettingsViewModel Settings { get; }
    public GameManageViewModel GameManage { get; }
    public LogsViewModel Logs { get; }
    public UpdateViewModel Update { get; }
    public AboutViewModel About { get; }
    public LinkViewModel Link { get; }
    public LinkSettingsViewModel LinkSettings { get; }
    public HelpViewModel Help { get; }
    public ToolboxViewModel Toolbox { get; }
    public FeedbackViewModel Feedback { get; }
    public MorePageViewModel Vote { get; }

    public VersionSelectViewModel VersionSelect { get; }
    public ObservableCollection<NavItem> TopTabs { get; } = [];

    private static string? FeatureKeyOf(NavItem item) => item.Key switch
    {
        "download" => FeatureCatalog.Download,
        "link" => FeatureCatalog.Link,
        "settings" => FeatureCatalog.Settings,
        "more" => FeatureCatalog.Tools,
        "settings-launch" => FeatureCatalog.SettingsLaunch,
        "settings-java" => FeatureCatalog.SettingsJava,
        "settings-games" => FeatureCatalog.SettingsGames,
        "settings-link" => FeatureCatalog.SettingsLink,
        "settings-log" => FeatureCatalog.SettingsLog,
        "settings-update" => FeatureCatalog.SettingsUpdate,
        "settings-ui" => FeatureCatalog.SettingsUi,
        "settings-other" => FeatureCatalog.SettingsOther,
        "help" => FeatureCatalog.Help,
        "about" => FeatureCatalog.About,
        "toolbox" => FeatureCatalog.Toolbox,
        "feedback" => FeatureCatalog.Feedback,
        "vote" => FeatureCatalog.Vote,
        _ => null
    };

    private bool IsNavVisible(NavItem item) =>
        item.IsHeader || FeatureKeyOf(item) is not { } key || Services.Features.IsVisible(key);

    private IReadOnlyList<NavItem>? SourceFor(NavItem top) =>
        ReferenceEquals(top, _downloadTab) ? _downloadTabs
        : ReferenceEquals(top, _moreTab) ? _moreTabs
        : ReferenceEquals(top, _settingsTab) ? _settingsTabs
        : null;

    /// <summary>隐藏必须是真隐藏：子页全部隐藏时整个顶栏页也不再出现。</summary>
    private bool HasVisibleContent(NavItem top) =>
        SourceFor(top) is not { } source || source.Any(IsNavVisible);

    private void RebuildTopTabs()
    {
        var current = SelectedTopTab;
        TopTabs.Clear();
        foreach (var tab in new[] { _launchTab, _downloadTab, _linkTab, _settingsTab, _moreTab })
            if (IsNavVisible(tab) && HasVisibleContent(tab)) TopTabs.Add(tab);
        if (TopTabs.Count == 0) TopTabs.Add(_launchTab);
        // 构造期间还没有选中项，交给原有初始化；只有当前项被隐藏时才改选第一项。
        if (current is null || TopTabs.Contains(current)) return;
        SelectedTopTab = TopTabs[0];
    }

    private void OnFeatureVisibilityChanged()
    {
        var currentTop = SelectedTopTab;
        RebuildTopTabs();
        if (currentTop is not null && TopTabs.Contains(currentTop)) ConfigureSubTabs(currentTop, preserveSelection: true);
        else if (SelectedTopTab is not null) ConfigureSubTabs(SelectedTopTab, preserveSelection: true);
    }
    public ObservableCollection<NavItem> SubTabs { get; } = [];

    // PCL2 的启动页左栏从窗口内容边缘开始；其他页面按 WmclPagePadding（20,15,20,10）留边。
    public Thickness PageMargin =>
        CurrentPage is LaunchViewModel or VersionSelectViewModel or InstanceViewModel
            ? new Thickness(0)
            : new Thickness(20, 15, 20, 10);

    [ObservableProperty]
    private NavItem? _selectedTopTab;

    [ObservableProperty]
    private NavItem? _selectedSubTab;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _hasSubTabs;

    public double SubNavWidth => HasSubTabs && CurrentPage is not InstanceViewModel ? 152 : 0;

    partial void OnHasSubTabsChanged(bool value) =>
        OnPropertyChanged(nameof(SubNavWidth));

    private bool _initialized;
    private bool _navigating;

    partial void OnCurrentPageChanged(ViewModelBase? value)
    {
        OnPropertyChanged(nameof(PageMargin));
        OnPropertyChanged(nameof(SubNavWidth));
        if (value is InstanceViewModel)
            QueueInstanceSectionPreparation();
        else
        {
            ++_instanceSectionRequest;
            Instance.EmbeddedPage = null;
            Instance.IsSectionPreparing = false;
        }
    }

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

    private bool _suppressSubTabNavigation;

    private bool _revertingSubTab;

    partial void OnSelectedSubTabChanged(NavItem? value)
    {
        // 分组标题不是页面，不能被选中；回退到上一个可导航项。
        if (value is { IsHeader: true } && !_revertingSubTab)
        {
            var fallback = _lastSelectableSubTab
                           ?? SubTabs.FirstOrDefault(item => !item.IsHeader);
            if (fallback is not null)
            {
                _revertingSubTab = true;
                SelectedSubTab = fallback;
                _revertingSubTab = false;
                return;
            }
        }
        if (value is { IsHeader: false }) _lastSelectableSubTab = value;
        HandleSubTabChanged(value);
    }

    private NavItem? _lastSelectableSubTab;

    private void HandleSubTabChanged(NavItem? value)
    {
        if (value is null || _navigating || _suppressSubTabNavigation) return;
        var previousPage = CurrentPage;
        ApplySubTab(value);
        if (ReferenceEquals(previousPage, value.Page) && value.Page is not null)
        {
            // Same page instance, different section (启动/Java/个性化/其他 share one VM).
            // TransitioningContentControl compares content instances, so clear it for one
            // frame to force the same CrossFade used by cross-page navigation.
            CurrentPage = null;
            CurrentTitle = value.Title;
            var target = value.Page;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CurrentPage = target;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            CurrentPage = value.Page;
            CurrentTitle = value.Title;
        }
        if (_initialized && value.Page is { } page) LoadPage(page);
    }

    /// <summary>循环切换顶栏项。backward=true 时反向。</summary>
    public void CycleTopTab(bool backward = false)
    {
        var tabs = TopTabs;
        if (tabs.Count == 0) return;
        var current = SelectedTopTab;
        var idx = Array.IndexOf(tabs.ToArray(), current);
        if (idx < 0) idx = backward ? tabs.Count - 1 : 0;
        else idx = (idx + (backward ? tabs.Count - 1 : 1)) % tabs.Count;
        SelectedTopTab = tabs[idx];
    }

    private void ApplySubTab(NavItem? subTab)
    {
        if (subTab is null) return;
        if (ReferenceEquals(subTab.Page, Install))
            Install.ShowDownloadCatalog();
        else if (ReferenceEquals(subTab.Page, Mods))
        {
            Mods.IsInstanceManagement = false;
            Mods.SetMode(subTab.Key);
        }
        else if (ReferenceEquals(subTab.Page, Settings))
            Settings.SetSection(subTab.Key);
        else if (ReferenceEquals(subTab.Page, Content))
        {
            Content.IsEmbedded = false;
            Content.IsDownloadPage = true;
            Content.SetMode(subTab.Key);
        }
    }

    private void ConfigureSubTabs(NavItem top, bool preserveSelection = false)
    {
        var previous = preserveSelection ? SelectedSubTab : null;
        SubTabs.Clear();
        if (SourceFor(top) is { } source)
            foreach (var tab in source)
                if (IsNavVisible(tab)) SubTabs.Add(tab);

        HasSubTabs = SubTabs.Count > 0;
        var next = previous is not null && SubTabs.Contains(previous) ? previous : SubTabs.FirstOrDefault();
        if (ReferenceEquals(next, SelectedSubTab)) return;
        // 只是隐藏/显示功能时保持当前子页，且不重建页面，避免滚动位置被重置。
        _suppressSubTabNavigation = preserveSelection;
        try { SelectedSubTab = next; }
        finally { _suppressSubTabNavigation = false; }
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
        await GameManage.LoadAsync();
    }

    public void OpenInstanceInstall() => OpenInstanceSection(2);

    public void OpenInstanceMods() => OpenInstanceSection(3);

    public void OpenInstanceContent(string key, string title) => OpenInstanceSection(key switch
    {
        "content-world" => 7,
        "content-shader" => 8,
        _ => 6
    });

    public void OpenVersionSelect()
    {
        VersionSelect.RefreshSelection();
        PushPage(VersionSelect, "版本选择");
    }

    public void OpenTaskManager() => PushPage(Tasks, "任务管理");

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

    /// <summary>供视图层（快捷键等）调用的导航入口。</summary>
    internal void NavigateTo(string key)
    {
        if (key == "instance")
        {
            PushPage(Instance, "实例设置");
            return;
        }
        if (key == "tasks")
        {
            OpenTaskManager();
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
                case "cleanroom":
                    top = _downloadTab;
                    subKey = "cleanroom";
                    break;
                case "labymod":
                    top = _downloadTab;
                    subKey = "labymod";
                    break;
                case "mods":
                case "modpack":
                case "favorites":
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
                case "settings-launch":
                case "settings-games":
                case "settings-log":
                case "settings-update":
                case "settings-java":
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
            var previousPage = CurrentPage;
            ApplySubTab(SelectedSubTab);
            var targetPage = SelectedSubTab?.Page ?? top.Page;
            CurrentTitle = SelectedSubTab?.Title ?? top.Title;
            if (ReferenceEquals(previousPage, targetPage))
            {
                CurrentPage = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => CurrentPage = targetPage,
                    Avalonia.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                CurrentPage = targetPage;
            }
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
            _ = LoadContentPageAsync();
        else if (ReferenceEquals(page, Modpack))
            _ = LoadModpackPageAsync();
        else if (ReferenceEquals(page, Mods))
            _ = LoadModsPageAsync();
        else if (ReferenceEquals(page, Cleanroom))
            _ = Cleanroom.LoadAsync();
        else if (ReferenceEquals(page, LabyMod))
            _ = LabyMod.LoadAsync();
        else if (ReferenceEquals(page, Settings))
            _ = Settings.LoadAsync();
        else if (ReferenceEquals(page, GameManage))
            _ = GameManage.LoadAsync();
        else if (ReferenceEquals(page, Logs))
            Logs.Refresh();
    }

    private async Task LoadContentPageAsync()
    {
        await Content.LoadAsync();
        await Content.EnsureResourceSearchAsync();
    }

    private async Task LoadModpackPageAsync()
    {
        await Modpack.LoadAsync();
        await Modpack.EnsureResourceSearchAsync();
    }

    private async Task LoadModsPageAsync()
    {
        await Mods.LoadAsync();
        await Mods.EnsureResourceSearchAsync();
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
        await GameManage.LoadAsync();
        Logs.Refresh();

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
                await Mods.WaitForModSelectionAsync();
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
        if (CurrentPage is not null) LoadResourceSearch(CurrentPage);
        if (ReferenceEquals(CurrentPage, Cleanroom))
            _ = Cleanroom.LoadAsync();
        if (ReferenceEquals(CurrentPage, LabyMod))
            _ = LabyMod.LoadAsync();
        if (ReferenceEquals(CurrentPage, Settings))
            await Settings.LoadAsync();
    }

    private void LoadResourceSearch(ViewModelBase page)
    {
        if (ReferenceEquals(page, Mods)) _ = Mods.EnsureResourceSearchAsync();
        else if (ReferenceEquals(page, Content)) _ = Content.EnsureResourceSearchAsync();
        else if (ReferenceEquals(page, Modpack)) _ = Modpack.EnsureResourceSearchAsync();
    }
}
