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
    private readonly WMCL.App.Services.Navigation.NavigationHistory _history = new();

    public bool CanGoBack => _history.CanGoBack;

    private WMCL.App.Services.Navigation.NavCatalog _navCatalog = null!;

    /// <summary>按需创建的非首屏页面。</summary>
    public WMCL.App.Services.Navigation.PageRegistry Pages { get; } = new();

    private WMCL.App.Services.Navigation.RefreshCoordinator? Refresh { get; }
    private WMCL.App.Services.Navigation.NavigationCoordinator? Navigation { get; }

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
        Appearance = new WMCL.App.Services.WorkspaceAppearance(settings);

        Pages.Register("launch", new LaunchViewModel(Services, loadVersions: loadVersions, newsService: Services.News));
        Launch.PropertyChanged += OnLaunchPropertyChanged;
        Pages.Register("download", new DownloadViewModel(Services));
        Pages.Register("cleanroom", new CleanroomViewModel(Services));
        Cleanroom.VersionInstalled += (_, _) => _ = Launch.LoadAsync();
        Pages.Register("labymod", new LabyModViewModel());
        Pages.Register("instance", new InstanceViewModel(Services, prompt: instancePrompt, confirm: instanceConfirm));
        Pages.Register("mods", createMods?.Invoke(Services) ?? new ModsViewModel(Services));
        // 与「社区资源 → Mod」共用同一个 ModsViewModel：组件选择状态只有一份，避免两套事件订阅
        Pages.Register("install", createInstall?.Invoke(Download, Services) ?? new InstallViewModel(Download, Mods, Services));
        Pages.Register("content", new InstanceContentViewModel(Services));
        _ = WarmupNetworkAsync();
        Content.DownloadRequested += (_, kind) =>
        {
            Content.IsEmbedded = false;
            Content.IsDownloadPage = true;
            NavigateTo(kind);
        };
        Pages.Register("modpack", new ModpackViewModel(Services));
        Pages.Register("favorites", new ResourceFavoritesViewModel(Services.ResourceFavorites, OpenFavoriteAsync,
            lookup: Services.ResourceLookup.ResolveAsync,
            batch: new ResourceFavoriteBatchViewModel(Services,
                () => Launch.SelectedVersion?.Id ?? Instance.SelectedInstance?.Id ?? Mods.SelectedModInstance ?? Content.SelectedInstance)));
        Pages.Register("tasks", new TaskManagerViewModel(Download, Mods, Modpack, Content, Instance, Install, Favorites.Batch, Launch));
        Pages.Register("settings", new SettingsViewModel(Services));
        // 设置页下的重页面同样按需创建
        Pages.Register("settings-games", () => new GameManageViewModel(Services));
        Pages.Register("settings-log", () => new LogsViewModel(Services));
        Pages.Register("settings-update", () => new UpdateViewModel(Services));
        Pages.Register("about", new AboutViewModel(Services));
        Pages.Register("link", new LinkViewModel(Services));
        Pages.Register("link-settings", new LinkSettingsViewModel(Link));
        Pages.Register("astral", new AstralViewModel(Link, Launch));
        Link.AstralPage = Astral;
        Link.Astral = Astral;

        // 「更多」下的页面按需创建：首次导航到该页时才构造
        Pages.Register("help", () => new HelpViewModel());
        Pages.Register("toolbox", () => new ToolboxViewModel(Services));
        Pages.Register("feedback", () => new FeedbackViewModel(Services));
        Pages.Register("vote", () => new MorePageViewModel(
            "新功能投票",
            "PCL2 社区的新功能投票",
            "新功能投票沿用原版 PCL2 的 GitHub Discussions。\n\n"
            + "本项目会优先对齐原版已有功能，暂不另行维护投票列表。",
            "打开投票页面",
            "https://github.com/Meloong-Git/WMCL/discussions/categories/%E5%8A%9F%E8%83%BD%E6%8A%95%E7%A5%A8"));

        _downloadTabs =
        [
            new NavItem("", "原版游戏", () => Install, "download", NavIcons.Boxes),
            new NavItem("", "Cleanroom", () => Cleanroom, "cleanroom", NavIcons.Flask),
            new NavItem("", "LabyMod", () => LabyMod, "labymod", NavIcons.Box),
            new NavItem("", "社区资源", () => Mods) { IsHeader = true },
            new NavItem("", "Mod", () => Mods, "mods", NavIcons.Puzzle),
            new NavItem("", "整合包", () => Modpack, "modpack", NavIcons.Package),
            new NavItem("", "数据包", () => Content, "content-datapack", NavIcons.FileArchive),
            new NavItem("", "资源包", () => Content, "content-resource", NavIcons.Layers),
            new NavItem("", "光影包", () => Content, "content-shader", NavIcons.Sparkles),
            new NavItem("", "存档", () => Content, "content-world", NavIcons.Globe),
            new NavItem("", "资源收藏", () => Favorites, "favorites", NavIcons.Heart)
        ];
        _moreTabs =
        [
            new NavItem("", "帮助", () => Help, "help", NavIcons.Help),
            new NavItem("", "关于与鸣谢", () => About, "about", NavIcons.Info),
            new NavItem("", "百宝箱", () => Toolbox, "toolbox", NavIcons.ToolCase),
            new NavItem("", "反馈", () => Feedback, "feedback", NavIcons.Feedback),
            new NavItem("", "新功能投票", () => Vote, "vote", NavIcons.Vote)
        ];
        _settingsTabs =
        [
            new NavItem("", "启动", () => Settings, "settings-launch", NavIcons.Play),
            new NavItem("", "Java", () => Settings, "settings-java", NavIcons.Coffee),
            new NavItem("", "游戏管理", () => GameManage, "settings-games", NavIcons.Monitor),
            new NavItem("", "联机", () => LinkSettings, "settings-link", NavIcons.Link),
            new NavItem("", "日志", () => Logs, "settings-log", NavIcons.ScrollText),
            new NavItem("", "更新", () => Update, "settings-update", NavIcons.Refresh),
            new NavItem("", "个性化", () => Settings, "settings-ui", NavIcons.Palette),
            new NavItem("", "其他", () => Settings, "settings-other", NavIcons.Ellipsis)
        ];

        _launchTab = new NavItem("", "启动", () => Launch, "launch", TopTabIcons.Launch);
        // 顶部/子页目录集中到 NavCatalog，主 VM 只负责选中状态
        _downloadTab = new NavItem("", "下载", () => Download, "download", TopTabIcons.Download);
        _linkTab = new NavItem("", "联机", () => Link, "link", TopTabIcons.Link);
        _settingsTab = new NavItem("", "设置", () => Settings, "settings", TopTabIcons.Settings);
        _moreTab = new NavItem("", "更多", () => About, "more", TopTabIcons.More);
        _navCatalog = new WMCL.App.Services.Navigation.NavCatalog(
            _launchTab, _downloadTab, _linkTab, _settingsTab, _moreTab,
            _downloadTabs, _moreTabs, _settingsTabs);
        RebuildTopTabs();
        Services.Features.Changed += (_, _) => OnFeatureVisibilityChanged();

        Pages.Register("version-select", new VersionSelectViewModel(
            Launch,
            Services,
            CloseVersionSelect,
            () => NavigateTo("download")));
        Refresh = new WMCL.App.Services.Navigation.RefreshCoordinator(
            Services, Launch, Instance, Content, Mods, Modpack, Install, VersionSelect, Download, Cleanroom, Favorites.Batch,
            reloadGameFolder: ReloadGameFolderAsync,
            openInstanceSettings: OpenInstanceSettingsAsync,
            refreshTaskState: RefreshTaskState,
            instanceChanged: change => InstanceRefreshTask = RefreshInstancesAsync(change),
            installCompleted: result =>
            {
                if (!result.InstanceRenamed)
                {
                    _ = Launch.LoadAsync();
                    _ = Instance.LoadAsync();
                    _ = Mods.LoadAsync();
                    _ = Content.LoadAsync();
                }
                RefreshTaskState();
            },
            existingInstallSubmitted: () =>
            {
                if (ReferenceEquals(CurrentPage, Instance) && Instance.SelectedSectionIndex == 2)
                    Instance.SelectedSectionIndex = 0;
                else if (ReferenceEquals(CurrentPage, Install)) GoBack();
            },
            installTaskFinished: async task =>
            {
                if (task.PercentText == "失败")
                    await DialogService.ShowMessageAsync(task.Title + "失败", task.Detail, isWarning: true);
            });
        Navigation = new WMCL.App.Services.Navigation.NavigationCoordinator(
            Launch, Mods, Link, Settings, NavigateTo, OpenVersionSelect);
        Instance.PropertyChanged += OnInstanceNavigationChanged;

        var startPage = StartupOptions.StartPage?.ToLowerInvariant() ?? "launch";
        NavigateTo("launch");
        if (startPage == "version-select") OpenVersionSelect();
        else if (startPage != "launch") NavigateTo(startPage);
    }

    public AppServices Services { get; }
    private WMCL.App.Services.WorkspaceAppearance Appearance { get; }

    public Bitmap? BackgroundImage => Appearance.BackgroundImage;
    public bool HasBackgroundImage => BackgroundImage is not null;
    public Bitmap? LogoImage => Appearance.LogoImage;
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
        Refresh?.Dispose();
        Tasks.Dispose();
        Instance.Dispose();
        Link.Dispose();
        Settings.Dispose();
        Content.Dispose();
        About.Dispose();
    }


    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LaunchViewModel.IsGameRunning)) return;
        Music.SyncGameRunning(Launch.IsGameRunning, Services.Settings);
    }
    public double BackgroundImageOpacity => Appearance.Opacity;
    public double BackgroundImageBlur => Appearance.Blur;
    public bool BackgroundColorOverlay => Appearance.ColorOverlay;

    [ObservableProperty]
    private bool _isTaskActive;

    [ObservableProperty]
    private double _taskProgress;

    [ObservableProperty]
    private string _taskPercentText = "";

    [ObservableProperty]
    private string _taskText = "";

    private void RefreshTaskState()
    {
        var batch = Favorites.Batch;
        var snapshot = WMCL.App.Services.TaskBarState.From(
            Download.IsBusy, Download.CanCancel, Download.Progress,
            Mods.IsBusy, Mods.CanCancel, Mods.Progress,
            Mods.IsInstallingForgelike, Mods.ForgelikeProgress,
            Mods.IsOptiFineBusy, Mods.OptiFineProgress,
            Install.IsCombinedBusy, Install.CombinedProgress,
            Mods.IsInstallingMod, Mods.ModProgress,
            batch?.IsInstalling == true, batch?.Progress ?? 0,
            Modpack.IsBusy, Modpack.Progress,
            Content.IsDownloadBusy, Content.DownloadProgress,
            Instance.IsImporting,
            Launch.IsCheckingModpackUpdate, Launch.IsCheckingGameFiles, Launch.IsBusy, Launch.Progress);
        IsTaskActive = snapshot.Active;
        TaskProgress = Math.Clamp(snapshot.Progress, 0, 100);
        TaskPercentText = snapshot.PercentText;
        TaskText = snapshot.Text;
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
    public LaunchViewModel Launch => Pages.Get<LaunchViewModel>("launch");
    public DownloadViewModel Download => Pages.Get<DownloadViewModel>("download");
    public CleanroomViewModel Cleanroom => Pages.Get<CleanroomViewModel>("cleanroom");
    public LabyModViewModel LabyMod => Pages.Get<LabyModViewModel>("labymod");
    public InstallViewModel Install => Pages.Get<InstallViewModel>("install");
    public TaskManagerViewModel Tasks => Pages.Get<TaskManagerViewModel>("tasks");
    public InstanceViewModel Instance => Pages.Get<InstanceViewModel>("instance");
    public ModsViewModel Mods => Pages.Get<ModsViewModel>("mods");
    public InstanceContentViewModel Content => Pages.Get<InstanceContentViewModel>("content");
    public ModpackViewModel Modpack => Pages.Get<ModpackViewModel>("modpack");
    public ResourceFavoritesViewModel Favorites => Pages.Get<ResourceFavoritesViewModel>("favorites");
    public SettingsViewModel Settings => Pages.Get<SettingsViewModel>("settings");
    public GameManageViewModel GameManage => Pages.Get<GameManageViewModel>("settings-games");
    public LogsViewModel Logs => Pages.Get<LogsViewModel>("settings-log");
    public UpdateViewModel Update => Pages.Get<UpdateViewModel>("settings-update");
    public AboutViewModel About => Pages.Get<AboutViewModel>("about");
    public LinkViewModel Link => Pages.Get<LinkViewModel>("link");
    public LinkSettingsViewModel LinkSettings => Pages.Get<LinkSettingsViewModel>("link-settings");

    public AstralViewModel Astral => Pages.Get<AstralViewModel>("astral");
    public HelpViewModel Help => Pages.Get<HelpViewModel>("help");
    public ToolboxViewModel Toolbox => Pages.Get<ToolboxViewModel>("toolbox");
    public FeedbackViewModel Feedback => Pages.Get<FeedbackViewModel>("feedback");
    public MorePageViewModel Vote => Pages.Get<MorePageViewModel>("vote");

    public VersionSelectViewModel VersionSelect => Pages.Get<VersionSelectViewModel>("version-select");
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

    private IReadOnlyList<NavItem>? SourceFor(NavItem top) => _navCatalog.SourceFor(top);

    /// <summary>隐藏必须是真隐藏：子页全部隐藏时整个顶栏页也不再出现。</summary>
    private bool HasVisibleContent(NavItem top) => _navCatalog.HasVisibleContent(top, IsNavVisible);

    private void RebuildTopTabs()
    {
        var current = SelectedTopTab;
        TopTabs.Clear();
        foreach (var tab in _navCatalog.VisibleTopTabs(IsNavVisible)) TopTabs.Add(tab);
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
            UiDispatcher.Post(() => CurrentPage = target, Avalonia.Threading.DispatcherPriority.Loaded);
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
        foreach (var tab in _navCatalog.VisibleSubTabs(top, IsNavVisible)) SubTabs.Add(tab);

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
            _history.Push(new(CurrentPage, CurrentTitle, SelectedTopTab, SelectedSubTab?.Key));
        CurrentPage = page;
        CurrentTitle = title;
        OnPropertyChanged(nameof(CanGoBack));
        if (_initialized) LoadPage(page);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!_history.TryPop(out var entry) || entry is null) return;
        var (page, title, topTab, subKey) = (entry.Page, entry.Title, entry.TopTab, entry.SubKey);
        if (topTab is not null)
        {
            _navigating = true;
            try
            {
                SelectedTopTab = topTab;
                ConfigureSubTabs(topTab);
                SelectedSubTab = subKey is null
                    ? SubTabs.FirstOrDefault()
                    : SubTabs.FirstOrDefault(tab => tab.Key == subKey) ?? SubTabs.FirstOrDefault();
            }
            finally
            {
                _navigating = false;
            }
        }
        CurrentPage = page;
        CurrentTitle = title;
        OnPropertyChanged(nameof(CanGoBack));
        if (_initialized) LoadPage(page);
    }

    private void ClearBackStack()
    {
        if (!_history.CanGoBack) return;
        _history.Clear();
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
        // 停留在推入页（如实例设置）时，先把它压栈，返回即可回到它
        if (CurrentPage is InstanceViewModel or InstallViewModel)
        {
            _history.Push(new(CurrentPage, CurrentTitle, SelectedTopTab, SelectedSubTab?.Key));
            OnPropertyChanged(nameof(CanGoBack));
        }
        _navigating = true;
        try
        {
            var (target, subKey) = WMCL.App.Services.Navigation.NavigationKeyMap.Resolve(key);
            var top = _navCatalog.ResolveTop(target);
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
                UiDispatcher.Post(() => CurrentPage = targetPage, Avalonia.Threading.DispatcherPriority.Loaded);
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

    /// <summary>启动后台预热：版本清单与默认搜索，减少首次打开页面时的等待。</summary>
    private async Task WarmupNetworkAsync()
    {
        try
        {
            await Services.Repository.GetManifestAsync().ConfigureAwait(false);
        }
        catch
        {
            // 离线时忽略
        }
        try
        {
            await Mods.WarmupSearchAsync().ConfigureAwait(false);
        }
        catch
        {
            // 忽略预热失败
        }
    }

    private int _pageLoadGeneration;

    /// <summary>页面切换后统一加载入口：先让过渡动画播完，再在后台加载数据。</summary>
    private void LoadPage(ViewModelBase page)
    {
        var generation = ++_pageLoadGeneration;
        UiDispatcher.Post(() => _ = LoadPageDeferredAsync(page, generation),
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private async Task LoadPageDeferredAsync(ViewModelBase page, int generation)
    {
        // 页面切换的淡入先播，避免加载抢占首帧
        await Task.Delay(Animation.MotionDurations.PageTransition).ConfigureAwait(true);
        if (generation != _pageLoadGeneration) return;   // 期间又切页，丢弃过期的加载
        try
        {
            await LoadPageCoreAsync(page).ConfigureAwait(true);
        }
        catch
        {
            // 单页加载失败不影响其它页面
        }
    }

    private Task LoadPageCoreAsync(ViewModelBase page)
    {
        if (ReferenceEquals(page, Launch) && !Launch.IsBusy)
            return Launch.LoadAsync();
        if (ReferenceEquals(page, Instance))
            return Instance.LoadAsync();
        if (ReferenceEquals(page, Content))
            return LoadContentPageAsync();
        if (ReferenceEquals(page, Modpack))
            return LoadModpackPageAsync();
        if (ReferenceEquals(page, Mods))
            return LoadModsPageAsync();
        if (ReferenceEquals(page, Cleanroom))
            return Cleanroom.LoadAsync();
        if (ReferenceEquals(page, LabyMod))
            return LabyMod.LoadAsync();
        if (ReferenceEquals(page, Settings))
            return Settings.LoadAsync();
        if (ReferenceEquals(page, GameManage))
            return GameManage.LoadAsync();
        if (ReferenceEquals(page, Logs))
            Logs.Refresh();
        return Task.CompletedTask;
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
