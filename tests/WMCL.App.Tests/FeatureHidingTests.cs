using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class FeatureHidingTests
{
    private static (AppServices Services, List<AppSettings> Saved) Fixture()
    {
        var saved = new List<AppSettings>();
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "feature-hiding-" + Guid.NewGuid().ToString("N"))
        };
        return (new AppServices(settings, saveSettings: saved.Add), saved);
    }

    [Fact]
    public void HidingPersistsAndRevealsOnlyTemporarily()
    {
        var (services, saved) = Fixture();
        var features = services.Features;
        Assert.True(features.IsVisible(FeatureCatalog.Toolbox));
        features.SetHidden(FeatureCatalog.Toolbox, true);
        Assert.False(features.IsVisible(FeatureCatalog.Toolbox));
        Assert.Contains(FeatureCatalog.Toolbox, services.Settings.HiddenFeatures);
        Assert.NotEmpty(saved);

        features.RevealTemporarily(true);
        Assert.True(features.IsVisible(FeatureCatalog.Toolbox));
        Assert.True(features.IsHidden(FeatureCatalog.Toolbox));
        features.RevealTemporarily(false);
        Assert.False(features.IsVisible(FeatureCatalog.Toolbox));

        features.SetHidden(FeatureCatalog.Toolbox, false);
        Assert.True(features.IsVisible(FeatureCatalog.Toolbox));
        Assert.Empty(services.Settings.HiddenFeatures);
    }

    [Fact]
    public void FilterDropsHiddenEntriesInsteadOfFallingBack()
    {
        var (services, _) = Fixture();
        var features = services.Features;
        var items = new[] { "a", "b" };
        Assert.Equal(items, features.Filter(items, _ => FeatureCatalog.Toolbox));
        features.SetHidden(FeatureCatalog.Toolbox, true);
        Assert.Empty(features.Filter(items, _ => FeatureCatalog.Toolbox));
    }

    [AvaloniaFact]
    public void HidingEveryToolSubPageAlsoRemovesTheToolTab()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        main.NavigateTo("more");
        Assert.Contains(main.TopTabs, tab => tab.Key == "more");
        foreach (var key in new[] { FeatureCatalog.Help, FeatureCatalog.About, FeatureCatalog.Toolbox,
                     FeatureCatalog.Feedback, FeatureCatalog.Vote })
            services.Features.SetHidden(key, true);
        Assert.DoesNotContain(main.TopTabs, tab => tab.Key == "more");
        Assert.NotNull(main.SelectedTopTab);
        services.Features.RevealTemporarily(true);
        Assert.Contains(main.TopTabs, tab => tab.Key == "more");
    }

    [AvaloniaFact]
    public void HidingTopTabsRemovesThemAndF12BringsThemBack()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        var before = main.TopTabs.Count;
        services.Features.SetHidden(FeatureCatalog.Download, true);
        Assert.DoesNotContain(main.TopTabs, tab => tab.Key == "download");
        Assert.Equal(before - 1, main.TopTabs.Count);
        Assert.NotNull(main.SelectedTopTab);

        services.Features.RevealTemporarily(true);
        Assert.Contains(main.TopTabs, tab => tab.Key == "download");
        services.Features.RevealTemporarily(false);
        Assert.DoesNotContain(main.TopTabs, tab => tab.Key == "download");
    }

    [AvaloniaFact]
    public void HidingSettingsSubPagesFiltersTheSubNavigation()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        main.NavigateTo("settings");
        Assert.Contains(main.SubTabs, tab => tab.Key == "settings-ui");
        services.Features.SetHidden(FeatureCatalog.SettingsUi, true);
        Assert.DoesNotContain(main.SubTabs, tab => tab.Key == "settings-ui");
        services.Features.RevealTemporarily(true);
        Assert.Contains(main.SubTabs, tab => tab.Key == "settings-ui");
    }

    [AvaloniaFact]
    public void TogglingAHiddenFeatureKeepsTheCurrentSubPage()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        main.NavigateTo("settings-ui");
        Assert.Equal("settings-ui", main.SelectedSubTab!.Key);
        var page = main.CurrentPage;
        // 勾选“下载”与当前所在页无关，不应该把用户带回“启动”。
        services.Features.SetHidden(FeatureCatalog.Download, true);
        Assert.Equal("settings-ui", main.SelectedSubTab!.Key);
        Assert.Same(page, main.CurrentPage);
        services.Features.SetHidden(FeatureCatalog.Download, false);
        Assert.Equal("settings-ui", main.SelectedSubTab!.Key);
    }

    [AvaloniaFact]
    public void GroupTitlesNameTheActualPageAndHomeNewsCanBeHidden()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        var groups = main.Settings.HiddenFeatureGroups.Select(group => group.Title).ToArray();
        Assert.Equal(["顶栏页面", "启动页", "设置页的子页", "更多页的子页", "实例设置页的子页", "特定功能"], groups);
        var tools = main.Settings.HiddenFeatureGroups[0].Items.Single(item => item.Key == FeatureCatalog.Tools);
        Assert.Equal("更多", tools.Label);
        var news = main.Settings.HiddenFeatureGroups[1].Items.Single(item => item.Key == FeatureCatalog.HomeNews);
        Assert.Equal("Minecraft 新闻", news.Label);
        Assert.True(main.Launch.IsHomeNewsVisible);
        news.IsHidden = true;
        Assert.False(main.Launch.IsHomeNewsVisible);
        services.Features.RevealTemporarily(true);
        Assert.True(main.Launch.IsHomeNewsVisible);
    }

    [AvaloniaFact]
    public void SettingsPageExposesGroupedTogglesThatWriteThrough()
    {
        var (services, _) = Fixture();
        using var main = new MainWindowViewModel(services);
        var settings = main.Settings;
        Assert.Equal(FeatureCatalog.Groups.Count, settings.HiddenFeatureGroups.Count);
        var item = settings.HiddenFeatureGroups
            .SelectMany(group => group.Items)
            .Single(entry => entry.Key == FeatureCatalog.InstanceShaders);
        Assert.False(item.IsHidden);
        item.IsHidden = true;
        Assert.True(services.Features.IsHidden(FeatureCatalog.InstanceShaders));
        Assert.True(item.IsHidden);
    }

    [AvaloniaFact]
    public void HidingAnInstanceSectionMovesSelectionToAVisibleOne()
    {
        var (services, _) = Fixture();
        using var model = new InstanceViewModel(services) { SelectedSectionIndex = 8 };
        Assert.True(model.ShowShaderSection);
        services.Features.SetHidden(FeatureCatalog.InstanceShaders, true);
        Assert.False(model.ShowShaderSection);
        Assert.NotEqual(8, model.SelectedSectionIndex);
        Assert.True(model.ShowOverviewSection);
    }

    [AvaloniaFact]
    public void HidingTheModUpdateFeatureKeepsTheToolbarButtonHidden()
    {
        var (services, _) = Fixture();
        var mods = InstallTestData.CreateMods(services);
        Assert.True(mods.IsModUpdateVisible);
        services.Features.SetHidden(FeatureCatalog.ModUpdate, true);
        Assert.False(mods.IsModUpdateVisible);
        Dispatcher.UIThread.RunJobs();
    }
}
