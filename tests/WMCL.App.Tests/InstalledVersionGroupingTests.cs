using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class InstalledVersionGroupingTests
{
    [AvaloniaFact]
    public void CardHeaderStretchesAndKeyboardTogglesWithoutSelectingAVersion()
    {
        var model = Create(Item("1.21"), Item("b1.7.3", type: "old_beta"));
        var group = model.Groups.Single(item => item.Title == "不常用版本");
        var selection = model.SelectedVersion;
        var view = new VersionSelectView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var header = view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("versionHeader") && ReferenceEquals(button.DataContext, group));
            Assert.True(header.Bounds.Width > 300);
            Assert.Equal(new Thickness(0), header.BorderThickness);
            var arrow = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Assert.True(arrow.TranslatePoint(default, header)!.Value.X > header.Bounds.Width - 40);
            Assert.False(group.IsExpanded);
            header.Focus();
            var headless = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            object?[] arguments = [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null];
            headless.GetMethod("KeyPress")!.Invoke(window.PlatformImpl, arguments);
            headless.GetMethod("KeyRelease")!.Invoke(window.PlatformImpl, arguments);
            Dispatcher.UIThread.RunJobs();
            Assert.True(group.IsExpanded);
            Assert.Same(selection, model.SelectedVersion);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void GroupsUsePclOrderAndDoNotDuplicateFoolsOrBrokenProfiles()
    {
        var model = Create(
            Item("star", favorite: true),
            Item("fabric", category: "fabric"), Item("forge", category: "forge"),
            Item("1.21.1"), Item("b1.7.3", type: "old_beta"),
            Item("24w14potato", type: "snapshot"),
            Item("Broken", category: "error", date: "2024-04-01T12:00:00Z", type: "snapshot"));

        Assert.Equal(["收藏夹", "可安装 Mod", "常规版本", "不常用版本", "愚人节版本", "错误的版本"],
            model.Groups.Select(group => group.Title));
        Assert.Equal("收藏夹", model.Groups[0].DisplayTitle);
        Assert.Equal("可安装 Mod (2)", model.Groups[1].DisplayTitle);
        Assert.Equal(7, model.Groups.SelectMany(group => group.Versions).Distinct().Count());
        Assert.Equal(7, model.Groups.Sum(group => group.Versions.Count));
        Assert.All(model.Groups.Take(3), group => Assert.True(group.IsExpanded));
        Assert.All(model.Groups.Skip(3), group => Assert.False(group.IsExpanded));
    }

    [AvaloniaTheory]
    [InlineData("fabric", "Fabric 版本")]
    [InlineData("forge", "Forge 版本")]
    [InlineData("neoforge", "NeoForge 版本")]
    [InlineData("liteloader", "LiteLoader 版本")]
    [InlineData("quilt", "Fabric 版本")]
    public void SingleLoaderUsesItsOwnTitle(string loader, string title)
    {
        var model = Create(Item("modded", category: loader));
        Assert.Equal(title, Assert.Single(model.Groups).Title);
    }

    [AvaloniaTheory]
    [InlineData("error", "release", "broken")]
    [InlineData("vanilla", "old_beta", "b1.7.3")]
    [InlineData("vanilla", "snapshot", "24w14potato")]
    public void SingleGroupIsForcedOpenAndCanStillBeCollapsed(string category, string type, string id)
    {
        var group = Assert.Single(Create(Item(id, category, type: type)).Groups);
        Assert.True(group.IsExpanded);
        group.ToggleCommand.Execute(null);
        Assert.False(group.IsExpanded);
    }

    [AvaloniaFact]
    public void OnlySnapshotNewerThanTheNewestReleaseRemainsCommon()
    {
        var older = Item("24w32a", type: "snapshot", date: "2024-08-08T15:00:00Z");
        var release = Item("1.21.1", date: "2024-08-08T09:00:00-07:00");
        var model = Create(older, release);
        Assert.Same(release, Assert.Single(model.Groups.Single(group => group.Title == "常规版本").Versions));
        Assert.Same(older, Assert.Single(model.Groups.Single(group => group.Title == "不常用版本").Versions));
        var newest = Item("24w33a", type: "snapshot", date: "2024-08-15T12:00:00Z");
        model.Versions.Add(newest);
        Assert.Contains(newest, model.Groups.Single(group => group.Title == "常规版本").Versions);
        Assert.DoesNotContain(older, model.Groups.Single(group => group.Title == "常规版本").Versions);
    }

    [AvaloniaFact]
    public void ReleaseOnAprilFirstIsNotAFoolAndNamedFoolsUseBaseIdentity()
    {
        var release = Item("1.99", date: "2030-04-01T12:00:00Z");
        var snapshot = Item("99w13a", type: "snapshot", date: "2030-03-31T23:00:00Z");
        var renamed = new VersionItem { Id = "Potato", IsInstalled = true, BaseGameVersion = "24w14potato" };
        var model = Create(release, snapshot, renamed);
        Assert.Same(release, Assert.Single(model.Groups.Single(group => group.Title == "常规版本").Versions));
        Assert.Equal(2, model.Groups.Single(group => group.Title == "愚人节版本").Versions.Count);
    }

    [AvaloniaFact]
    public void OlderReleasesInTheSameDropBecomeUncommon()
    {
        var older = Item("1.20.1", date: "2023-06-12T12:00:00Z");
        var newest = Item("1.20.4", date: "2023-12-07T12:00:00Z");
        var anotherDrop = Item("1.21", date: "2024-06-13T12:00:00Z");
        var model = Create(older, newest, anotherDrop);
        Assert.Equal([anotherDrop, newest], model.Groups.Single(group => group.Title == "常规版本").Versions);
        Assert.Same(older, Assert.Single(model.Groups.Single(group => group.Title == "不常用版本").Versions));
    }

    [AvaloniaFact]
    public void OptiFineBuildOrderWinsWithinADropAndQuiltSharesFabricGroup()
    {
        var preview = new VersionItem
        {
            Id = "preview",
            BaseGameVersion = "1.20.1",
            Category = "optifine",
            IsInstalled = true,
            OptiFineVersion = "1.20.1_HD_U_I6_pre6",
            ReleaseTime = "2024-01-01T00:00:00Z"
        };
        var stable = new VersionItem
        {
            Id = "stable",
            BaseGameVersion = "1.20.1",
            Category = "optifine",
            IsInstalled = true,
            OptiFineVersion = "1.20.1_HD_U_I6",
            ReleaseTime = "2023-01-01T00:00:00Z"
        };
        var model = Create(preview, stable, Item("1.20.4"), Item("quilt", "quilt"), Item("fabric", "fabric"));
        Assert.Equal("Fabric 版本", model.Groups[0].Title);
        Assert.Equal(2, model.Groups[0].Versions.Count);
        Assert.Same(stable, Assert.Single(model.Groups.Single(group => group.Title == "常规版本").Versions));
        Assert.Equal(2, model.Groups.Single(group => group.Title == "不常用版本").Versions.Count);
    }

    [AvaloniaFact]
    public void HiddenFavoritesAppearOnlyInHiddenMode()
    {
        var hidden = Item("hidden", favorite: true, hidden: true);
        var model = Create(Item("1.21"), hidden);
        Assert.DoesNotContain(hidden, model.Groups.SelectMany(group => group.Versions));
        model.ShowHidden = true;
        Assert.Equal("隐藏的版本", Assert.Single(model.Groups).Title);
        Assert.Same(hidden, Assert.Single(model.Groups[0].Versions));
        Assert.True(model.Groups[0].IsExpanded);
    }

    [AvaloniaFact]
    public void EqualDateSortUsesPclLoaderFlagsThenComponentVersion()
    {
        var mixed = Item("Mixed", category: "fabric", date: "2024-01-01T00:00:00Z",
            loaders: VersionLoaderKind.Fabric | VersionLoaderKind.Forge);
        var fabric = Item("Fabric", category: "fabric", date: "2024-01-01T00:00:00Z");
        var neoforge = Item("NeoForge", category: "neoforge", date: "2024-01-01T00:00:00Z");
        var forgeOld = Item("ForgeOld", category: "forge", date: "2024-01-01T00:00:00Z",
            forgeVersion: "47.1.0");
        var forgeNew = Item("ForgeNew", category: "forge", date: "2024-01-01T00:00:00Z",
            forgeVersion: "47.2.0");
        var lite = Item("Lite", category: "liteloader", date: "2024-01-01T00:00:00Z");
        var model = Create(mixed, fabric, neoforge, forgeOld, forgeNew, lite);

        var api = model.Groups.Single(group => group.Title == "可安装 Mod").Versions;
        Assert.Equal(["Mixed", "Fabric", "NeoForge", "ForgeNew", "ForgeOld", "Lite"],
            api.Select(item => item.Id));
    }

    [AvaloniaTheory]
    [InlineData(VersionCardPlacement.Api, "Fabric 版本")]
    [InlineData(VersionCardPlacement.Regular, "常规版本")]
    [InlineData(VersionCardPlacement.Uncommon, "不常用版本")]
    [InlineData(VersionCardPlacement.Fool, "愚人节版本")]
    public void DisplayTypeOverrideMovesUnfavoritedVersion(int cardType, string expectedCard)
    {
        var model = Create(Item("Moved", cardType: cardType));
        Assert.Equal(expectedCard, Assert.Single(model.Groups).Title);
    }

    [AvaloniaFact]
    public void ShowingDisplayTypeHiddenVersionWritesAutoBackToInstanceSettings()
    {
        var model = Create(Item("TypeHidden", cardType: VersionCardPlacement.Hidden));
        model.ShowHidden = true;
        var item = Assert.Single(model.Groups.Single(group => group.Title == "隐藏的版本").Versions);

        model.ToggleHidden(item);

        Assert.Equal(VersionCardPlacement.Auto, item.CardTypeOverride);
        Assert.False(item.IsHidden);
        Assert.Equal(VersionCardPlacement.Auto,
            InstanceSettingsStore.Load(Path.Combine(model.GameDirectory, "versions", "TypeHidden")).VersionCardType);
        Assert.Empty(model.Groups);
    }

    [AvaloniaFact]
    public void DisplayTypeHiddenJoinsF11AndFavoriteStillWinsOverOverride()
    {
        var hidden = Item("HiddenByType", cardType: VersionCardPlacement.Hidden);
        var favorite = Item("Favorite", favorite: true, cardType: VersionCardPlacement.Uncommon);
        var model = Create(Item("1.21"), hidden, favorite);

        Assert.DoesNotContain(hidden, model.Groups.SelectMany(group => group.Versions));
        Assert.Same(favorite, Assert.Single(model.Groups.Single(group => group.Title == "收藏夹").Versions));
        model.ShowHidden = true;
        Assert.Same(hidden, Assert.Single(model.Groups.Single(group => group.Title == "隐藏的版本").Versions));
    }

    [AvaloniaFact]
    public void CardRebuildRestoresPclDefaultCollapseLikeForceRunRefresh()
    {
        var model = Create(Item("1.21"), Item("b1.7.3", type: "old_beta"));
        var rubbish = model.Groups.Single(group => group.Title == "不常用版本");
        Assert.False(rubbish.IsExpanded);
        rubbish.IsExpanded = true;

        model.Versions.Add(Item("1.20"));

        var rebuilt = model.Groups.Single(group => group.Title == "不常用版本");
        Assert.NotSame(rubbish, rebuilt);
        Assert.False(rebuilt.IsExpanded);
    }

    private static VersionSelectViewModel Create(params VersionItem[] versions)
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-grouping-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services);
        foreach (var version in versions) launch.InstalledVersions.Add(version);
        return new VersionSelectViewModel(launch, services, () => { }, () => { });
    }

    private static VersionItem Item(string id, string category = "vanilla", string type = "release",
        string? date = null, bool favorite = false, bool hidden = false,
        VersionLoaderKind loaders = VersionLoaderKind.None, string? forgeVersion = null,
        int cardType = 0) => new()
        {
            Id = id,
            Category = category,
            Type = type,
            ReleaseTime = date,
            IsInstalled = true,
            IsFavorite = favorite,
            IsHidden = hidden,
            Loaders = loaders,
            ForgeVersion = forgeVersion,
            CardTypeOverride = cardType
        };
}
