using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class VersionCatalogTests
{
    [Fact]
    public void RecommendationsRemainInTheirCategoriesAndOnlyLatestStartsExpanded()
    {
        var release = Version("1.21.1", "release", "2024-08-08T12:00:00Z");
        var preview = Version("24w33a", "snapshot", "2024-08-15T12:00:00Z");
        var catalog = new VersionCatalogViewModel();
        catalog.Open([preview, release, Version("a1.0", "old_alpha")], release);

        Assert.Equal(["最新版本", "正式版", "预览版", "远古版"], catalog.Groups.Select(group => group.Title));
        Assert.Equal([release, preview], catalog.Groups[0].Entries.Select(entry => entry.Version));
        Assert.Same(release, Assert.Single(catalog.Groups[1].Entries).Version);
        Assert.Same(preview, Assert.Single(catalog.Groups[2].Entries).Version);
        Assert.True(catalog.Groups[0].IsExpanded);
        Assert.All(catalog.Groups.Skip(1), group => Assert.False(group.IsExpanded));
        Assert.StartsWith("最新正式版，发布于 ", catalog.Groups[0].Entries[0].Summary);
        Assert.StartsWith("最新预览版，发布于 ", catalog.Groups[0].Entries[1].Summary);
        Assert.Equal("共 3 个官方版本", catalog.ResultText);
    }

    [Fact]
    public void RecommendationsCompareInstantsRatherThanTimestampStrings()
    {
        var newest = Version("1.21.1", "release", "2024-08-08T09:00:00-07:00");
        var older = Version("1.21", "release", "2024-08-08T15:30:00Z");
        var preview = Version("24w33a", "snapshot", "2024-08-08T17:00:00+02:00");
        var catalog = new VersionCatalogViewModel();
        catalog.Open([older, preview, newest], null);

        Assert.Same(newest, Assert.Single(catalog.Groups[0].Entries).Version);
        Assert.Equal([newest, older], catalog.Groups[1].Entries.Select(entry => entry.Version));
    }

    [Theory]
    [InlineData("1.5", "snapshot", "2013-03-13T00:00:00Z", "正式版", "Grass")]
    [InlineData("1.99", "snapshot", "2030-03-31T23:00:00Z", "愚人节版", "GoldBlock")]
    [InlineData("1.99", "release", "2030-04-01T12:00:00Z", "正式版", "Grass")]
    [InlineData("1.16_combat-3", "snapshot", "2020-08-01T00:00:00Z", "预览版", "CommandBlock")]
    [InlineData("1.21-rc1", "snapshot", "2024-06-01T00:00:00Z", "预览版", "CommandBlock")]
    [InlineData("1.21-pre1", "snapshot", "2024-06-01T00:00:00Z", "预览版", "CommandBlock")]
    [InlineData("1.18_experimental-snapshot-1", "snapshot", "2021-07-13T00:00:00Z", "预览版", "CommandBlock")]
    [InlineData("26w14a", "snapshot", "2026-04-02T00:00:00Z", "愚人节版", "GoldBlock")]
    [InlineData("b1.7.3", "old_beta", "2011-07-08T00:00:00Z", "远古版", "CobbleStone")]
    public void CategoriesAndIconsFollowPclRules(string id, string type, string date, string category, string icon)
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version(id, type, date)], null);
        var entry = Assert.Single(catalog.Groups.Single(group => group.Title == category).Entries);
        Assert.EndsWith($"/{icon}.png", entry.Icon);
    }

    [Fact]
    public void FoolAliasKeepsDownloadIdAndPclLore()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version("20w14infinite", "snapshot")], null);
        var entry = Assert.Single(Assert.Single(catalog.Groups).Entries);
        Assert.Equal("20w14∞", entry.Title);
        Assert.Equal("20w14infinite", entry.Version.Id);
        Assert.StartsWith("2020 | ", entry.Summary);
        catalog.SearchText = "∞";
        Assert.Single(catalog.Groups);
        Assert.Equal("20w14infinite", Assert.Single(catalog.Groups[0].Entries).Version.Id);
    }

    [Fact]
    public void SearchExpandsMatchesAndRestoresExpansionWhenCleared()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version("1.21"), Version("24w14potato", "snapshot")], null);
        catalog.Groups.Single(group => group.Title == "正式版").ToggleCommand.Execute(null);
        catalog.SearchText = "毒马铃薯";
        Assert.True(Assert.Single(catalog.Groups).IsExpanded);
        Assert.Equal("24w14potato", Assert.Single(catalog.Groups[0].Entries).Version.Id);
        catalog.SearchText = "不存在";
        Assert.Empty(catalog.Groups);
        Assert.True(catalog.IsEmpty);
        catalog.ClearSearchCommand.Execute(null);
        Assert.True(catalog.Groups.Single(group => group.Title == "正式版").IsExpanded);
        Assert.False(catalog.Groups.Single(group => group.Title == "愚人节版").IsExpanded);
        catalog.Open([Version("1.21")], null);
        Assert.False(catalog.Groups.Single(group => group.Title == "正式版").IsExpanded);
    }

    [Fact]
    public void SingleSelectionFinishesAndCancelPreservesExistingSelection()
    {
        var version = Version("1.21");
        var catalog = new VersionCatalogViewModel();
        var results = new List<VersionItem?>();
        catalog.Finished += (_, selected) => results.Add(selected);
        catalog.Open([version], null);
        catalog.CancelCommand.Execute(null);
        Assert.Empty(results);
        catalog.Groups[0].Entries[0].SelectCommand.Execute(null);
        Assert.Same(version, Assert.Single(results));
        catalog.Open([version], version);
        Assert.True(catalog.Groups[0].Entries[0].IsCurrent);
        catalog.CancelCommand.Execute(null);
        Assert.Null(results[1]);
    }

    [Fact]
    public void EmptyLoadingAndFailureAreDistinctStates()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([], null);
        Assert.True(catalog.IsEmpty);
        Assert.False(catalog.HasSearch);
        Assert.StartsWith("暂无可下载", catalog.EmptyMessage);
        catalog.IsLoading = true;
        Assert.False(catalog.IsEmpty);
        Assert.Equal("正在获取官方版本…", catalog.StatusText);
        catalog.ErrorMessage = "连接失败";
        catalog.IsLoading = false;
        Assert.True(catalog.HasError);
        Assert.False(catalog.IsEmpty);
        catalog.Open([], null);
        Assert.False(catalog.HasError);
        Assert.True(catalog.IsEmpty);
    }

    internal static VersionItem Version(string id, string type = "release", string? date = null) =>
        new() { Id = id, Type = type, ReleaseTime = date ?? "2024-03-01T12:00:00Z" };
}
