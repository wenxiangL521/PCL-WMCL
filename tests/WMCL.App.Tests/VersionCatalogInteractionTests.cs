using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Animation;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core;
using static WMCL.App.Tests.VersionCatalogTests;

namespace WMCL.App.Tests;

public sealed class VersionCatalogInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task ComponentSelectionStartsWithThePclNameCardAndOneBackArrow(string theme, int width)
    {
        ThemeService.Apply(theme);
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync(100);
            Click(window, Assert.Single(RowButtons(view)));
            await PumpAsync(350);
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var card = view.FindControl<Border>("InstallNameCard")!;
            var name = view.FindControl<TextBox>("InstallNameBox")!;
            var logo = view.FindControl<Image>("InstallLogo")!;
            Assert.False(model.IsChoosingVersion);
            Assert.True(scroll.IsVisible);
            Assert.Equal(15, card.TranslatePoint(default, scroll)!.Value.Y);
            Assert.Equal(62, card.Bounds.Height);
            Assert.Equal(new Point(52, 15), logo.TranslatePoint(default, card));
            Assert.Equal(new Size(32, 32), logo.Bounds.Size);
            Assert.NotNull(logo.Source);
            Assert.Equal(new Point(93, 16), name.TranslatePoint(default, card));
            Assert.Equal(15, name.FontSize);
            Assert.Equal(new Thickness(1), name.BorderThickness);
            Assert.Equal("1.21", name.Text);
            Assert.False(name.IsReadOnly);
            Assert.Single(card.GetVisualDescendants().OfType<Button>(), button => button.IsVisible);
            Assert.False(view.FindControl<Button>("OriginalNameRestoreButton")!.IsVisible);
            Assert.Single(scroll.GetVisualDescendants().OfType<TextBox>());
            Assert.DoesNotContain(scroll.GetVisualDescendants().OfType<Button>(), button =>
                button.Content is "选择版本" or "刷新" or "校验 / 修复");
            Click(window, name);
            Press(window, Key.A, RawInputModifiers.Control);
            SendInput(window, "TextInput", "My Minecraft");
            await PumpAsync(50);
            Assert.Equal("My Minecraft", model.InstanceName);
            Assert.False(model.IsChoosingVersion);
            Click(window, view.FindControl<Button>("BackToCatalogButton")!);
            await PumpAsync(350);
            Assert.True(model.IsChoosingVersion);
            Assert.True(view.FindControl<ScrollViewer>("CatalogScroll")!.IsVisible);
            Assert.Equal("1.21", model.Download.SelectedVersion!.Id);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task DefaultInstanceNameFollowsComponentsUntilEditedAndResetsForAnotherVersion()
    {
        var model = CreateInstallModel();
        await model.OpenVersionCatalogCommand.ExecuteAsync(null);
        model.VersionCatalog.Groups[0].Entries[0].SelectCommand.Execute(null);
        Assert.Equal("1.21", model.InstanceName);
        model.SelectedBaseLoader = "Fabric";
        Assert.Equal("1.21-Fabric 0.16.0", model.InstanceName);
        var fabricLogo = model.InstallLogo;
        model.Mods.SelectedLoader = "0.15.0+build";
        Assert.Equal("1.21-Fabric 0.15.0", model.InstanceName);
        model.InstallOptiFineWithBase = true;
        Assert.Equal("1.21-Fabric 0.15.0-OptiFine_I6", model.InstanceName);
        model.InstanceName = "My Minecraft";
        model.SelectedBaseLoader = "Forge";
        model.Mods.SelectedForgelikeVersion = "47.2.0";
        Assert.Equal("My Minecraft", model.InstanceName);
        Assert.NotSame(fabricLogo, model.InstallLogo);
        await model.OpenVersionCatalogCommand.ExecuteAsync(null);
        model.VersionCatalog.Groups.Single(group => group.Title == "正式版").Entries
            .Single(entry => entry.Version.Id == "1.20").SelectCommand.Execute(null);
        Assert.Equal("1.20", model.InstanceName);
        Assert.Equal("无", model.SelectedBaseLoader);
        model.UseVanilla();
        Assert.Equal("1.20", model.InstanceName);
    }

    [AvaloniaFact]
    public async Task InvalidInstanceNameShowsTheHintAndDisablesInstallationWithoutNavigating()
    {
        var model = CreateInstallModel();
        model.Download.SelectedVersion = Version("1.20");
        model.ShowSelectedVersion();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 650, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync(100);
            var name = view.FindControl<TextBox>("InstallNameBox")!;
            Click(window, name);
            Press(window, Key.A, RawInputModifiers.Control);
            Press(window, Key.Back);
            await PumpAsync(50);
            Assert.Equal("", model.InstanceName);
            Assert.True(view.FindControl<Border>("InstallNameHint")!.IsVisible);
            Assert.True(model.HasInstanceNameError);
            Assert.False(model.CanApplyInstall);
            Assert.False(model.IsChoosingVersion);
            SendInput(window, "TextInput", "Custom");
            await PumpAsync(50);
            Assert.False(model.HasInstanceNameError);
            Assert.False(view.FindControl<Border>("InstallNameHint")!.IsVisible);
            Assert.True(model.CanApplyInstall);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDirectoryOrFileCannotBeUsedAsTheInstanceName(bool file)
    {
        var model = CreateInstallModel();
        model.Download.SelectedVersion = Version("1.20");
        model.ShowSelectedVersion();
        var versions = Path.Combine(model.Download.GameDirectory, "versions");
        var target = Path.Combine(versions, "Custom");
        Directory.CreateDirectory(versions);
        try
        {
            if (file) File.WriteAllText(target, "keep");
            else Directory.CreateDirectory(target);
            model.InstanceName = "Custom";
            Assert.Contains("实例已存在", model.InstanceNameError);
            Assert.False(model.CanApplyInstall);
            model.InstanceName = "Available";
            Assert.False(model.HasInstanceNameError);
            Assert.True(model.CanApplyInstall);
            Assert.True(file ? File.Exists(target) : Directory.Exists(target));
        }
        finally { Directory.Delete(model.Download.GameDirectory, true); }
    }

    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task DefaultCatalogStartsWithThePclRecommendationCard(string theme, int width)
    {
        ThemeService.Apply(theme);
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync(150);
            var scroll = view.FindControl<ScrollViewer>("CatalogScroll")!;
            var cards = view.GetVisualDescendants().OfType<VersionCatalogCard>().ToArray();
            Assert.True(scroll.IsVisible);
            Assert.False(view.FindControl<Border>("CatalogTools")!.IsVisible);
            Assert.Equal(15, cards[0].TranslatePoint(default, scroll)!.Value.Y);
            Assert.Equal(100, cards[0].Bounds.Height);
            Assert.Equal(130, cards[1].TranslatePoint(default, scroll)!.Value.Y);
            Assert.All(cards, card =>
            {
                var header = card.FindControl<Button>("HeaderButton")!;
                var title = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(((VersionCatalogGroup)card.DataContext!).Title, title.Text);
                Assert.Equal(FontWeight.Bold, title.FontWeight);
            });
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "当前选择");
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task SearchShortcutAndEscapeRestoreTheCatalogBeforeCancellingSelection()
    {
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 650, Height = 450 };
        window.Show();
        try
        {
            await PumpAsync(100);
            Click(window, Assert.Single(RowButtons(view)));
            await PumpAsync(350);
            await model.OpenVersionCatalogCommand.ExecuteAsync(null);
            await PumpAsync(350);
            Assert.True(model.VersionCatalog.CanCancel);
            Assert.False(model.VersionCatalog.IsSearchVisible);
            Assert.True(model.VersionCatalog.Groups[0].Entries[0].IsCurrent);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "当前选择");
            Press(window, Key.F, RawInputModifiers.Control);
            Assert.True(view.FindControl<Border>("CatalogTools")!.IsVisible);
            Assert.True(view.FindControl<TextBox>("CatalogSearch")!.IsFocused);
            SendInput(window, "TextInput", "1.20");
            await PumpAsync(80);
            Assert.Equal("1.20", model.VersionCatalog.SearchText);
            Assert.Single(model.VersionCatalog.Groups);
            Press(window, Key.Escape);
            await PumpAsync(80);
            Assert.True(model.IsChoosingVersion);
            Assert.False(model.VersionCatalog.IsSearchVisible);
            Assert.False(view.FindControl<Border>("CatalogTools")!.IsVisible);
            Assert.Equal("", model.VersionCatalog.SearchText);
            Assert.Equal(2, model.VersionCatalog.Groups.Count);
            Assert.True(view.GetVisualDescendants().OfType<VersionCatalogCard>().First()
                .FindControl<Button>("HeaderButton")!.IsFocused);
            Press(window, Key.Escape);
            await PumpAsync(350);
            Assert.False(model.IsChoosingVersion);
            await model.OpenVersionCatalogCommand.ExecuteAsync(null);
            await PumpAsync(350);
            Assert.False(view.FindControl<Border>("CatalogTools")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CardAnimatesBothDirectionsAndRapidToggleSettles()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version("1.21"), Version("1.20")], null);
        var group = catalog.Groups.Single(item => item.Title == "正式版");
        var card = new VersionCatalogCard { DataContext = group };
        var window = new Window { Content = card, Width = 600, Height = 400 };
        window.Show();
        try
        {
            await PumpAsync(50);
            var body = card.FindControl<Border>("ContentClip")!;
            var items = card.FindControl<ItemsControl>("VersionItems")!;
            var header = card.FindControl<Button>("HeaderButton")!;
            Assert.Null(items.ItemsSource);
            Click(window, header);
            await PumpAsync(45);
            Assert.InRange(body.Height, 1, 101);
            await PumpAsync(300);
            Assert.Equal(102, body.Height);
            Assert.Equal(180, ((RotateTransform)card.FindControl<Avalonia.Controls.Shapes.Path>("Chevron")!.RenderTransform!).Angle);
            Click(window, header);
            await PumpAsync(40);
            Assert.InRange(body.Height, 1, 101);
            Click(window, header);
            await PumpAsync(20);
            Click(window, header);
            await PumpAsync(350);
            Assert.False(group.IsExpanded);
            Assert.Equal(0, body.Height);
            Assert.Null(items.ItemsSource);
            Assert.False(body.IsVisible);
            Assert.Equal(1, card.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LargeExpandedGroupVirtualizesAndScrollsToLastVersion()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open(Enumerable.Range(0, 900).Select(index => Version($"preview-{index}", "snapshot")).ToList(), null);
        var group = catalog.Groups.Single(item => item.Title == "预览版");
        group.IsExpanded = true;
        var card = new VersionCatalogCard { DataContext = group };
        var scroll = new ScrollViewer { Content = card };
        var window = new Window { Content = scroll, Width = 600, Height = 320 };
        window.Show();
        try
        {
            await PumpAsync(80);
            Assert.InRange(RowButtons(card).Count(), 1, 30);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            await PumpAsync(80);
            Assert.Contains(RowButtons(card), button => ((VersionCatalogEntry)button.DataContext!).Title == "preview-899");
            Assert.InRange(RowButtons(card).Count(), 1, 30);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LargeGroupCanReverseAndRebindDuringExpansion()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open(Enumerable.Range(0, 900).Select(index => Version($"preview-{index}", "snapshot")).ToList(), null);
        var group = catalog.Groups.Single(item => item.Title == "预览版");
        var card = new VersionCatalogCard { DataContext = group };
        var window = new Window { Content = new ScrollViewer { Content = card }, Width = 600, Height = 320 };
        window.Show();
        try
        {
            await PumpAsync(50);
            var body = card.FindControl<Border>("ContentClip")!;
            var items = card.FindControl<ItemsControl>("VersionItems")!;
            group.IsExpanded = true;
            await PumpAsync(120);
            Assert.InRange(body.Height, 1, 37817);
            Assert.InRange(RowButtons(card).Count(), 1, 30);
            group.IsExpanded = false;
            await PumpAsync(40);
            group.IsExpanded = true;
            await PumpAsync(750);
            Assert.Equal(37818, body.Height);
            Assert.InRange(RowButtons(card).Count(), 1, 30);
            group.IsExpanded = false;
            await PumpAsync(30);
            card.DataContext = null;
            await PumpAsync(300);
            Assert.Equal(0, body.Height);
            Assert.False(body.IsVisible);
            Assert.Null(items.ItemsSource);
            card.DataContext = group;
            group.IsExpanded = true;
            await PumpAsync(750);
            Assert.Equal(37818, body.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HeaderHighlightsItsTextAndChevronWithoutTintingTheSurface()
    {
        ThemeService.Apply("dark", 300, 70, 0);
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version("1.21")], null);
        var card = new VersionCatalogCard { DataContext = catalog.Groups[0] };
        var window = new Window { Content = card, Width = 500, Height = 180 };
        window.Show();
        try
        {
            await PumpAsync(80);
            var header = card.FindControl<Button>("HeaderButton")!;
            var point = header.TranslatePoint(new Point(100, 20), window)!.Value;
            SendInput(window, "MouseMove", point, RawInputModifiers.None);
            await PumpAsync(150);
            Assert.True(header.IsPointerOver);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);
            Assert.Same(Application.Current!.Resources["WmclBlue2"], header.Foreground);
            Assert.Same(header.Foreground, header.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "最新版本").Foreground);
            Assert.Same(header.Foreground, card.FindControl<Avalonia.Controls.Shapes.Path>("Chevron")!.Fill);
            header.Focus(NavigationMethod.Tab);
            await PumpAsync(150);
            Assert.Same(Application.Current.Resources["WmclBlue3"], header.BorderBrush);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task MouseSelectionAndEscapeRoundTripKeepSelectedVersion()
    {
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 650, Height = 450 };
        window.Show();
        try
        {
            await PumpAsync(400);
            var row = Assert.Single(RowButtons(view));
            Click(window, row);
            await PumpAsync(400);
            Assert.False(model.IsChoosingVersion);
            Assert.Equal("1.21", model.Download.SelectedVersion?.Id);
            Assert.True(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            Assert.False(view.FindControl<ScrollViewer>("CatalogScroll")!.IsVisible);
            Press(window, Key.Escape);
            await PumpAsync(400);
            Assert.True(model.IsChoosingVersion);
            Assert.True(model.VersionCatalog.CanCancel);
            Assert.True(model.VersionCatalog.Groups[0].Entries[0].IsCurrent);
            Press(window, Key.Escape);
            await PumpAsync(400);
            Assert.False(model.IsChoosingVersion);
            Assert.Equal("1.21", model.Download.SelectedVersion?.Id);
            Assert.Equal(1, view.FindControl<ScrollViewer>("PageScroll")!.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardSearchClearAndEnterSelectWithoutConfirmation()
    {
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            await PumpAsync(400);
            Press(window, Key.F, RawInputModifiers.Control);
            SendInput(window, "TextInput", "1.20");
            await PumpAsync(80);
            Assert.Equal("1.20", model.VersionCatalog.SearchText);
            var row = Assert.Single(RowButtons(view));
            Assert.Equal("1.20", ((VersionCatalogEntry)row.DataContext!).Title);
            row.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            await PumpAsync(400);
            Assert.False(model.IsChoosingVersion);
            Assert.Equal("1.20", model.Download.SelectedVersion?.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task StageSwitchSlidesHorizontallyInThePclDirection()
    {
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 650, Height = 450 };
        window.Show();
        try
        {
            await PumpAsync(100);
            var catalog = view.FindControl<ScrollViewer>("CatalogScroll")!;
            var selection = view.FindControl<ScrollViewer>("PageScroll")!;
            model.VersionCatalog.Groups[0].Entries[0].SelectCommand.Execute(null);
            await PumpAsync(60);
            Assert.True(catalog.IsVisible);
            Assert.False(catalog.IsHitTestVisible);
            Assert.InRange(((TranslateTransform)catalog.RenderTransform!).X, -50, -0.01);
            Assert.Equal(0, ((TranslateTransform)catalog.RenderTransform!).Y);
            await PumpAsync(250);
            Assert.True(selection.IsVisible);
            Assert.False(catalog.IsVisible);
            Assert.Equal(0, ((TranslateTransform)selection.RenderTransform!).X);
            await model.OpenVersionCatalogCommand.ExecuteAsync(null);
            await PumpAsync(60);
            Assert.True(selection.IsVisible);
            Assert.False(selection.IsHitTestVisible);
            Assert.InRange(((TranslateTransform)selection.RenderTransform!).X, 0.01, 50);
            Assert.Equal(0, ((TranslateTransform)selection.RenderTransform!).Y);
            await PumpAsync(250);
            Assert.True(catalog.IsVisible);
            Assert.True(catalog.IsHitTestVisible);
            Assert.False(selection.IsVisible);
            Assert.Equal(1, catalog.Opacity);
            Assert.Equal(0, ((TranslateTransform)catalog.RenderTransform!).X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RepeatedTransitionsAndDetachDoNotLeaveInvisibleOrDisabledStage()
    {
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            await PumpAsync(350);
            model.VersionCatalog.Groups[0].Entries[0].SelectCommand.Execute(null);
            await PumpAsync(30);
            await model.OpenVersionCatalogCommand.ExecuteAsync(null);
            await PumpAsync(25);
            model.VersionCatalog.CancelCommand.Execute(null);
            await PumpAsync(350);
            window.Content = null;
            window.Content = view;
            await PumpAsync(350);
            var stage = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.True(stage.IsVisible);
            Assert.True(stage.IsHitTestVisible);
            Assert.Equal(1, stage.Opacity);
            Assert.Equal(0, ((TranslateTransform)stage.RenderTransform!).X);
            Assert.Equal(0, ((TranslateTransform)stage.RenderTransform!).Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DarkThemeAndNarrowWidthKeepRowsInsideCard()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version(new string('a', 100), "snapshot")], null);
        var card = new VersionCatalogCard { DataContext = catalog.Groups[0] };
        var window = new Window { Content = card, Width = 320, Height = 200, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            await PumpAsync(80);
            var row = Assert.Single(RowButtons(card));
            Assert.InRange(row.Bounds.Width, 1, 320);
            Assert.All(row.GetVisualDescendants().OfType<Image>(), image => Assert.NotNull(image.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ArrowKeysNavigateBeyondRealizedRowsAndCollapseRestoresHeaderFocus()
    {
        var catalog = new VersionCatalogViewModel();
        catalog.Open(Enumerable.Range(0, 60).Select(index => Version($"preview-{index}", "snapshot")).ToList(), null);
        var group = catalog.Groups.Single(item => item.Title == "预览版");
        group.IsExpanded = true;
        var card = new VersionCatalogCard { DataContext = group };
        var scroll = new ScrollViewer { Content = card };
        var window = new Window { Content = scroll, Width = 600, Height = 260 };
        window.Show();
        try
        {
            await PumpAsync(80);
            var header = card.FindControl<Button>("HeaderButton")!;
            header.Focus(NavigationMethod.Tab);
            for (var index = 0; index < 25; index++)
            {
                Press(window, Key.Down);
                await PumpAsync(20);
            }
            var focused = RowButtons(card).Single(button => button.IsFocused);
            Assert.Equal("preview-24", ((VersionCatalogEntry)focused.DataContext!).Title);
            Assert.True(scroll.Offset.Y > 0);
            group.IsExpanded = false;
            await PumpAsync(300);
            Assert.True(header.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionSwitchesImmediatelyAndCanStillSelect()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var model = CreateInstallModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        try
        {
            window.Show();
            await PumpAsync(50);
            Assert.Equal(TimeSpan.Zero, MotionDurations.Interactive);
            Click(window, Assert.Single(RowButtons(view)));
            Assert.False(model.IsChoosingVersion);
            Assert.True(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            Assert.True(view.FindControl<ScrollViewer>("PageScroll")!.IsEnabled);
            Assert.Equal(1, view.FindControl<ScrollViewer>("PageScroll")!.Opacity);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous);
        }
    }

    [AvaloniaFact]
    public async Task HoverPressAndKeyboardFocusUseCustomThemeBrushes()
    {
        ThemeService.Apply("dark", 300, 70, 0);
        var catalog = new VersionCatalogViewModel();
        catalog.Open([Version("1.21")], null);
        var card = new VersionCatalogCard { DataContext = catalog.Groups[0] };
        var window = new Window { Content = card, Width = 500, Height = 180 };
        window.Show();
        try
        {
            await PumpAsync(80);
            var row = Assert.Single(RowButtons(card));
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            SendInput(window, "MouseMove", point, RawInputModifiers.None);
            await PumpAsync(150);
            Assert.True(row.IsPointerOver);
            Assert.Same(Application.Current!.Resources["WmclEntryHover"], row.Background);
            var hover = Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color;
            var baseColor = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current.Resources["WmclBlue7"]).Color;
            Assert.Equal(Color.FromArgb(190, baseColor.R, baseColor.G, baseColor.B), hover);
            Assert.Same(Application.Current.Resources["WmclBlue1"], row.Foreground);
            SendInput(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync(150);
            Assert.True(row.IsPressed);
            Assert.Same(Application.Current.Resources["WmclBlue6"], row.Background);
            Assert.Same(Application.Current.Resources["WmclBlue1"], row.Foreground);
            SendInput(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
            card.FindControl<Button>("HeaderButton")!.Focus(NavigationMethod.Tab);
            row.Focus(NavigationMethod.Tab);
            await PumpAsync(150);
            Assert.True(row.IsFocused);
            Assert.Same(Application.Current.Resources["WmclBlue3"], row.BorderBrush);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    private static InstallViewModel CreateInstallModel()
    {
        var services = new AppServices(new AppSettings { GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-catalog-tests", Guid.NewGuid().ToString("N")) });
        var download = new DownloadViewModel(services);
        download.Versions.Add(Version("1.21"));
        download.Versions.Add(Version("1.20"));
        return new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
    }

    private static IEnumerable<Button> RowButtons(Control control) =>
        control.GetVisualDescendants().OfType<Button>().Where(button => button.DataContext is VersionCatalogEntry);

    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task OptiFabricRowsSelectTheRequestedFileAndIndependentClearUpdatesTheHint(string theme, int width)
    {
        ThemeService.Apply(theme);
        var model = CreateInstallModel();
        model.Download.SelectedVersion = Version("1.20.1");
        model.ShowSelectedVersion();
        model.SelectComponent("Fabric");
        model.InstallOptiFineWithBase = true;
        model.InstallFabricApi = true;
        var older = InstallTestData.OptiFabric(version: "1.13.25", id: 4642832);
        model.OptiFabricSelection.Versions.Add(older);
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 900 };
        window.Show();
        try
        {
            await PumpAsync(250);
            var card = view.FindControl<InstallComponentCard>("OptiFabricComponentCard")!;
            var header = card.FindControl<Button>("HeaderButton")!;
            header.BringIntoView();
            await PumpAsync(80);
            Assert.True(card.IsVisible);
            Assert.Empty(card.GetVisualDescendants().OfType<ComboBox>());
            Click(window, header);
            await PumpAsync(300);
            var row = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is InstallComponentEntry entry && entry.Choice.OptiFabric == older);
            row.BringIntoView();
            await PumpAsync(60);
            Assert.InRange(row.TranslatePoint(new Point(row.Bounds.Width, 0), view)!.Value.X, 0, width);
            Click(window, row);
            await PumpAsync(300);
            Assert.Same(older, model.OptiFabricSelection.SelectedVersion);
            Assert.True(model.InstallOptiFabric);
            Assert.False(model.OptiFabricCard.IsExpanded);
            var clear = card.FindControl<Button>("ClearButton")!;
            clear.BringIntoView();
            await PumpAsync(60);
            Click(window, clear);
            await PumpAsync(80);
            Assert.False(model.InstallOptiFabric);
            Assert.True(header.IsFocused);
            var hint = view.FindControl<TextBlock>("InstallHintText")!;
            Assert.Contains("OptiFine 无法工作", hint.Text);
            Assert.True(Assert.IsType<Border>(hint.Parent).IsVisible);
            Assert.Equal(Color.Parse(theme == "dark" ? "#FFC78F" : "#A85000"), Assert.IsAssignableFrom<ISolidColorBrush>(hint.Foreground).Color);
            Click(window, header);
            await PumpAsync(300);
            var sameRow = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is InstallComponentEntry entry && entry.Choice.OptiFabric == older);
            sameRow.BringIntoView();
            await PumpAsync(60);
            Click(window, sameRow);
            await PumpAsync(300);
            Assert.True(model.InstallOptiFabric);
            Assert.Same(older, model.OptiFabricSelection.SelectedVersion);
            Assert.DoesNotContain("无法工作", hint.Text);
            Assert.Contains("OptiFine 与一部分 Mod 的兼容性不佳", hint.Text);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task ApiCardSupportsKeyboardVersionRowsAndIndependentRemoval(string theme, int width)
    {
        ThemeService.Apply(theme);
        var model = CreateInstallModel();
        model.Download.SelectedVersion = Version("1.20.1");
        model.ShowSelectedVersion();
        model.SelectComponent("Fabric");
        var older = InstallTestData.Api(version: "0.91.0", id: "older");
        model.ApiSelection.Versions.Add(older);
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 900 };
        window.Show();
        try
        {
            await PumpAsync(250);
            var card = view.FindControl<InstallComponentCard>("ApiComponentCard")!;
            var header = card.FindControl<Button>("HeaderButton")!;
            header.BringIntoView();
            await PumpAsync(80);
            Assert.True(card.IsVisible);
            Assert.Empty(card.GetVisualDescendants().OfType<ComboBox>());
            header.Focus(NavigationMethod.Tab);
            Press(window, Key.Right);
            await PumpAsync(300);
            Assert.Equal(2, model.ApiCard.Entries.Count);
            Press(window, Key.Down);
            await PumpAsync(60);
            Press(window, Key.Down);
            await PumpAsync(60);
            Press(window, Key.Enter);
            await PumpAsync(300);
            Assert.Same(older, model.ApiSelection.SelectedVersion);
            Assert.Equal(older.VersionNumber, model.ApiSummary);
            Assert.False(model.ApiCard.IsExpanded);
            Assert.True(header.IsFocused);
            var clear = card.FindControl<Button>("ClearButton")!;
            clear.BringIntoView();
            await PumpAsync(60);
            Click(window, clear);
            await PumpAsync(80);
            Assert.False(model.InstallFabricApi);
            Assert.False(model.ApiCard.IsExpanded);
            Assert.True(header.IsFocused);
            Assert.Contains("部分 Mod 可能无法运行", view.FindControl<TextBlock>("InstallHintText")!.Text);
            header.Focus(NavigationMethod.Tab);
            Press(window, Key.Right);
            await PumpAsync(300);
            Press(window, Key.Down);
            await PumpAsync(60);
            Press(window, Key.Enter);
            await PumpAsync(300);
            Assert.True(model.InstallFabricApi);
            Assert.Equal("Fabric", model.SelectedBaseLoader);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        SendInput(window, "MouseMove", point, RawInputModifiers.None);
        SendInput(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        SendInput(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        SendInput(window, "KeyPress", key, modifiers, PhysicalKey.None, null);
        SendInput(window, "KeyRelease", key, modifiers, PhysicalKey.None, null);
    }

    private static void SendInput(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        } while (DateTime.UtcNow < deadline);
    }
}
