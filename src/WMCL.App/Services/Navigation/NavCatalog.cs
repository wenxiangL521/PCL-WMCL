namespace WMCL.App.Services.Navigation;

/// <summary>
/// 导航目录：集中保存顶部标签与各标签的子页定义，避免主导航 VM 里散落
/// 「哪个标签有哪些子页」的知识。
/// </summary>
public sealed class NavCatalog
{
    private readonly NavItem _launchTab;
    private readonly NavItem _downloadTab;
    private readonly NavItem _linkTab;
    private readonly NavItem _settingsTab;
    private readonly NavItem _moreTab;
    private readonly IReadOnlyList<NavItem> _downloadTabs;
    private readonly IReadOnlyList<NavItem> _moreTabs;
    private readonly IReadOnlyList<NavItem> _settingsTabs;

    public NavCatalog(
        NavItem launchTab,
        NavItem downloadTab,
        NavItem linkTab,
        NavItem settingsTab,
        NavItem moreTab,
        IReadOnlyList<NavItem> downloadTabs,
        IReadOnlyList<NavItem> moreTabs,
        IReadOnlyList<NavItem> settingsTabs)
    {
        _launchTab = launchTab;
        _downloadTab = downloadTab;
        _linkTab = linkTab;
        _settingsTab = settingsTab;
        _moreTab = moreTab;
        _downloadTabs = downloadTabs;
        _moreTabs = moreTabs;
        _settingsTabs = settingsTabs;
    }

    public NavItem LaunchTab => _launchTab;

    public IReadOnlyList<NavItem> AllTopTabs => [_launchTab, _downloadTab, _linkTab, _settingsTab, _moreTab];

    /// <summary>某个顶部标签的子页列表；没有子页（启动/联机）时返回 null。</summary>
    public IReadOnlyList<NavItem>? SourceFor(NavItem top) =>
        ReferenceEquals(top, _downloadTab) ? _downloadTabs
        : ReferenceEquals(top, _moreTab) ? _moreTabs
        : ReferenceEquals(top, _settingsTab) ? _settingsTabs
        : null;

    public NavItem ResolveTop(NavTarget target) => target switch
    {
        NavTarget.Download => _downloadTab,
        NavTarget.Link => _linkTab,
        NavTarget.Settings => _settingsTab,
        NavTarget.More => _moreTab,
        _ => _launchTab
    };

    /// <summary>按可见性过滤后的顶部标签；全部隐藏时保底返回启动页。</summary>
    public IReadOnlyList<NavItem> VisibleTopTabs(Func<NavItem, bool> isVisible)
    {
        var result = new List<NavItem>();
        foreach (var tab in AllTopTabs)
            if (isVisible(tab) && HasVisibleContent(tab, isVisible))
                result.Add(tab);
        if (result.Count == 0) result.Add(_launchTab);
        return result;
    }

    /// <summary>隐藏必须是真隐藏：子页全部隐藏时，整个顶栏页也不出现。</summary>
    public bool HasVisibleContent(NavItem top, Func<NavItem, bool> isVisible) =>
        SourceFor(top) is not { } source || source.Any(isVisible);

    /// <summary>按可见性过滤后的子页。</summary>
    public IReadOnlyList<NavItem> VisibleSubTabs(NavItem top, Func<NavItem, bool> isVisible) =>
        SourceFor(top) is { } source ? source.Where(isVisible).ToArray() : [];
}
