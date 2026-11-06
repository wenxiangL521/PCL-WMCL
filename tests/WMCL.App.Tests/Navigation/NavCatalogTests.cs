using System.Collections.ObjectModel;
using WMCL.App.Services.Navigation;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class NavCatalogTests
{
    private static NavItem Page(string title, string key) => new("", title, () => throw new InvalidOperationException("测试不应创建页面"), key, null);

    private static NavCatalog Create(out NavItem download, out NavItem more, out NavItem settings, out NavItem link)
    {
        var launch = Page("启动", "launch");
        download = Page("下载", "download");
        link = Page("联机", "link");
        settings = Page("设置", "settings");
        more = Page("更多", "more");
        return new NavCatalog(launch, download, link, settings, more,
            [Page("原版游戏", "download"), Page("Mod", "mods")],
            [Page("帮助", "help")],
            [Page("启动", "settings-launch")]);
    }

    [Fact]
    public void SourceForReturnsSubTabsOnlyForTabsWithChildren()
    {
        var catalog = Create(out var download, out var more, out var settings, out var link);
        Assert.Equal(2, catalog.SourceFor(download)!.Count);
        Assert.Single(catalog.SourceFor(more)!);
        Assert.Single(catalog.SourceFor(settings)!);
        Assert.Null(catalog.SourceFor(link));
    }

    [Fact]
    public void VisibleSubTabsFilterByPredicate()
    {
        var catalog = Create(out var download, out _, out _, out _);
        var visible = catalog.VisibleSubTabs(download, item => item.Key != "mods");
        Assert.Single(visible);
        Assert.Equal("download", visible[0].Key);
    }

    [Fact]
    public void HidingAllChildrenHidesTheTopTab()
    {
        var catalog = Create(out var more, out _, out _, out _);
        Assert.True(catalog.HasVisibleContent(more, _ => false) is false);
    }

    [Fact]
    public void ResolveTopMapsTargets()
    {
        var catalog = Create(out var download, out var more, out var settings, out var link);
        Assert.Same(download, catalog.ResolveTop(NavTarget.Download));
        Assert.Same(more, catalog.ResolveTop(NavTarget.More));
        Assert.Same(settings, catalog.ResolveTop(NavTarget.Settings));
        Assert.Same(link, catalog.ResolveTop(NavTarget.Link));
    }
}
