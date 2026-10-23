using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Forgelike;

namespace WMCL.App.Tests;

public sealed class ComponentSelectionInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task HeaderOnlyExpandsAndARealVersionClickSelectsCollapsesAndCanBeCleared(string theme, int width)
    {
        ThemeService.Apply(theme);
        var model = CreateModel();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync(100);
            var cards = view.FindControl<ScrollViewer>("PageScroll")!.GetVisualDescendants().OfType<InstallComponentCard>().Where(card => card.IsVisible).ToArray();
            Assert.Equal(["Forge", "NeoForge", "Fabric", "OptiFine"], cards.Select(card => ((InstallComponentCardViewModel)card.DataContext!).Kind));
            var card = cards[0];
            var header = card.FindControl<Button>("HeaderButton")!;
            var body = card.FindControl<Border>("ContentClip")!;
            var clear = card.FindControl<Button>("ClearButton")!;
            Assert.Equal(40, card.Bounds.Height);
            Assert.Equal("可以添加", card.FindControl<TextBlock>("SummaryText")!.Text);
            Assert.False(clear.IsVisible);
            Assert.Empty(Rows(card));
            Click(window, header);
            await PumpAsync(45);
            Assert.InRange(body.Height, 1, 175);
            Assert.Equal("无", model.SelectedBaseLoader);
            Assert.Equal("1.20.1", model.InstanceName);
            await PumpAsync(300);
            Assert.Equal(176, body.Height);
            Assert.Equal(0, card.FindControl<Grid>("SummaryPanel")!.Opacity);
            Assert.Equal(3, Rows(card).Length);
            Assert.Empty(card.GetVisualDescendants().OfType<ComboBox>());
            Click(window, Rows(card).Single(row => ((InstallComponentEntry)row.DataContext!).Choice.Version == "47.1.0"));
            await PumpAsync(350);
            Assert.Equal("Forge", model.SelectedBaseLoader);
            Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
            Assert.Equal("1.20.1-Forge_47.1.0", model.InstanceName);
            Assert.False(model.ForgeCard.IsExpanded);
            Assert.False(body.IsVisible);
            Assert.Empty(Rows(card));
            Assert.True(clear.IsVisible);
            Assert.Equal("47.1.0", card.FindControl<TextBlock>("SummaryText")!.Text);
            Click(window, header);
            await PumpAsync(300);
            Assert.True(model.ForgeCard.IsExpanded);
            Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
            Click(window, clear);
            await PumpAsync(300);
            Assert.Equal("无", model.SelectedBaseLoader);
            Assert.Equal("1.20.1", model.InstanceName);
            Assert.False(clear.IsVisible);
            Assert.True(header.IsFocused);
            Assert.DoesNotContain(view.FindControl<ScrollViewer>("PageScroll")!.GetVisualDescendants().OfType<Button>(),
                button => button.Content is "仅安装原版" or "☆ 收藏");
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task IncompatibleHeaderDoesNotReplaceTheSelectedLoaderAndClearingRestoresAvailability()
    {
        var model = CreateModel();
        await ChooseAsync(model.NeoForgeCard, "20.1.91");
        Assert.Equal("NeoForge", model.SelectedBaseLoader);
        Assert.Equal("20.1.91", model.Mods.SelectedForgelikeVersion);
        Assert.Equal("与 NeoForge 不兼容", model.ForgeCard.Summary);
        Assert.False(model.ForgeCard.CanExpand);
        model.ForgeCard.ToggleCommand.Execute(null);
        Assert.False(model.ForgeCard.IsExpanded);
        Assert.Equal("NeoForge", model.SelectedBaseLoader);
        model.NeoForgeCard.ClearCommand.Execute(null);
        Assert.Equal("无", model.SelectedBaseLoader);
        Assert.True(model.ForgeCard.CanExpand);
        model.ForgeCard.ToggleCommand.Execute(null);
        Assert.True(model.ForgeCard.IsExpanded);
        Assert.Equal("无", model.SelectedBaseLoader);
        await ChooseAsync(model.ForgeCard, "47.1.0");
        Assert.Equal("Forge", model.SelectedBaseLoader);
        Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
    }

    [AvaloniaFact]
    public async Task OptiFineCombinesWithFabricOrCompatibleForgeAndClearKeepsTheOtherComponent()
    {
        var model = CreateModel();
        await ChooseAsync(model.FabricCard, "0.15.0");
        await ChooseAsync(model.OptiFineCard, "I6");
        Assert.Equal("Fabric", model.SelectedBaseLoader);
        Assert.True(model.InstallOptiFineWithBase);
        Assert.True(model.OptiFineCard.IsSelected);
        Assert.False(model.NeoForgeCard.CanExpand);
        model.FabricCard.ClearCommand.Execute(null);
        Assert.Equal("OptiFine", model.SelectedBaseLoader);
        Assert.False(model.InstallOptiFineWithBase);
        Assert.False(model.InstallFabricApi);
        Assert.Equal(["47.2.0"], model.ForgeCard.Entries.Select(entry => entry.Choice.Version));
        await ChooseAsync(model.ForgeCard, "47.2.0");
        Assert.Equal("Forge", model.SelectedBaseLoader);
        Assert.True(model.InstallOptiFineWithBase);
        Assert.Equal("1.20.1-Forge_47.2.0-OptiFine_I6", model.InstanceName);
        model.OptiFineCard.ClearCommand.Execute(null);
        Assert.Equal("Forge", model.SelectedBaseLoader);
        Assert.False(model.InstallOptiFineWithBase);
        Assert.Equal(2, model.ForgeCard.Entries.Count);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedMetadataKeepsTheClickedVersionAndClearingDoesNotReselectTheLoader(bool clear)
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var neoRequests = 0;
        var model = CreateModel((kind, _) => kind == ForgelikeKind.NeoForge && ++neoRequests == 2
            ? gate.Task : Task.FromResult<IReadOnlyList<string>>(kind == ForgelikeKind.Forge ? ["47.2.0", "47.1.0"] : ["20.1.92", "20.1.91"]));
        var entry = model.NeoForgeCard.Entries.Single(item => item.Choice.Version == "20.1.91");
        var selecting = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)entry.SelectCommand).ExecuteAsync(null);
        Assert.Equal("20.1.91", model.NeoForgeCard.Summary);
        Assert.Equal("20.1.91", model.Mods.SelectedForgelikeVersion);
        if (clear) model.NeoForgeCard.ClearCommand.Execute(null);
        gate.SetResult(["20.1.92", "20.1.91"]);
        await selecting;
        await PumpAsync(60);
        Assert.Equal(clear ? "无" : "NeoForge", model.SelectedBaseLoader);
        if (!clear)
        {
            Assert.Equal("20.1.91", model.Mods.SelectedForgelikeVersion);
            Assert.Equal("1.20.1-NeoForge_20.1.91", model.InstanceName);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewMinecraftVersionRejectsTheOldCatalogResponseOrFailure(bool fail)
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var model = CreateModel((kind, game) => kind == ForgelikeKind.NeoForge && game == "1.20.1"
            ? gate.Task : Task.FromResult<IReadOnlyList<string>>(kind == ForgelikeKind.NeoForge ? ["21.1.92"] : ["47.2.0"]));
        Assert.True(model.NeoForgeCard.IsLoading);
        model.Download.SelectedVersion = new VersionItem { Id = "1.21.1" };
        model.ShowSelectedVersion();
        if (fail) gate.SetException(new IOException("old failure"));
        else gate.SetResult(["20.1.92"]);
        await PumpAsync(60);
        Assert.False(model.NeoForgeCard.IsLoading);
        Assert.Equal("", model.NeoForgeCard.Error);
        Assert.Equal(["21.1.92"], model.NeoForgeCard.Entries.Select(entry => entry.Choice.Version));
        Assert.Single(model.NeoForgeCard.DisplayItems);
        Assert.Empty(model.NeoForgeCard.DisplayItems.OfType<InstallComponentSeparator>());
        Assert.Equal("无", model.SelectedBaseLoader);
        Assert.Equal("1.21.1", model.InstanceName);
    }

    [AvaloniaFact]
    public async Task ComponentCatalogRetriesFailureAndRetainsAllVersionsInsteadOfTheOldSixtyItemLimit()
    {
        var requests = 0;
        var model = CreateModel((kind, _) => kind == ForgelikeKind.NeoForge && ++requests == 1
            ? Task.FromException<IReadOnlyList<string>>(new IOException("temporary failure"))
            : Task.FromResult<IReadOnlyList<string>>(Enumerable.Range(0, 89).Select(index => $"20.1.{index}").ToArray()));
        Assert.Equal("获取失败：temporary failure", model.NeoForgeCard.Summary);
        Assert.False(model.NeoForgeCard.CanExpand);
        await model.NeoForgeCard.RetryCommand.ExecuteAsync(null);
        Assert.Equal(89, model.NeoForgeCard.Entries.Count);
        Assert.True(model.NeoForgeCard.CanExpand);
        await ChooseAsync(model.NeoForgeCard, "20.1.88");
        Assert.Equal("20.1.88", model.Mods.SelectedForgelikeVersion);
    }

    [AvaloniaTheory]
    [InlineData("无", "无")]
    [InlineData("metadata failed", "获取失败：metadata failed")]
    public async Task FailedHeaderShowsItsReasonAndNeitherPointerNorKeyboardStartsARetry(string error, string summary)
    {
        var requests = 0;
        var model = CreateModel((kind, _) => kind == ForgelikeKind.NeoForge && ++requests == 1
            ? Task.FromException<IReadOnlyList<string>>(new IOException(error))
            : Task.FromResult<IReadOnlyList<string>>(["20.1.92"]));
        var card = new InstallComponentCard { DataContext = model.NeoForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 250 };
        window.Show();
        try
        {
            await PumpAsync(60);
            var header = card.FindControl<Button>("HeaderButton")!;
            Assert.Equal(summary, card.FindControl<TextBlock>("SummaryText")!.Text);
            Click(window, header);
            header.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            Press(window, Key.Space);
            await PumpAsync(60);
            Assert.Equal(1, requests);
            Assert.False(model.NeoForgeCard.IsExpanded);
            Assert.Empty(Rows(card));
            Assert.Equal("无", model.SelectedBaseLoader);
            await model.NeoForgeCard.RetryCommand.ExecuteAsync(null);
            Assert.Equal(2, requests);
            Assert.True(model.NeoForgeCard.CanExpand);
            Assert.False(model.NeoForgeCard.IsExpanded);
            Assert.Equal("可以添加", card.FindControl<TextBlock>("SummaryText")!.Text);
            Click(window, header);
            await PumpAsync(300);
            Assert.True(model.NeoForgeCard.IsExpanded);
            Assert.Single(Rows(card));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardExpansionAndVersionConfirmationPreserveTheHeaderFocus()
    {
        var model = CreateModel();
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 250 };
        window.Show();
        try
        {
            await PumpAsync(60);
            var header = card.FindControl<Button>("HeaderButton")!;
            header.Focus(NavigationMethod.Tab);
            Press(window, Key.Right);
            await PumpAsync(300);
            Assert.Equal("无", model.SelectedBaseLoader);
            Press(window, Key.Down);
            await PumpAsync(60);
            Press(window, Key.Down);
            await PumpAsync(60);
            Press(window, Key.Down);
            await PumpAsync(60);
            Assert.True(Rows(card)[2].IsFocused);
            Press(window, Key.Enter);
            await PumpAsync(300);
            Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
            Assert.False(model.ForgeCard.IsExpanded);
            Assert.True(header.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RapidExpansionReversalAndDetachReleaseRowsAndRestoreSummaryAndArrow()
    {
        var model = CreateModel();
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 250 };
        window.Show();
        try
        {
            await PumpAsync(60);
            model.ForgeCard.IsExpanded = true;
            await PumpAsync(40);
            model.ForgeCard.IsExpanded = false;
            await PumpAsync(30);
            model.ForgeCard.IsExpanded = true;
            await PumpAsync(30);
            model.ForgeCard.IsExpanded = false;
            await PumpAsync(300);
            Assert.Equal(0, card.FindControl<Border>("ContentClip")!.Height);
            Assert.Equal(1, card.FindControl<Grid>("SummaryPanel")!.Opacity);
            Assert.Equal(0, ((Avalonia.Media.TranslateTransform)card.FindControl<Grid>("SummaryPanel")!.RenderTransform!).Y);
            Assert.Equal(0, ((Avalonia.Media.RotateTransform)card.FindControl<Avalonia.Controls.Shapes.Path>("Chevron")!.RenderTransform!).Angle);
            Assert.Empty(Rows(card));
            model.ForgeCard.IsExpanded = true;
            await PumpAsync(35);
            card.DataContext = null;
            await PumpAsync(300);
            Assert.Empty(Rows(card));
            Assert.Equal(0, card.FindControl<Border>("ContentClip")!.Height);
            card.DataContext = model.ForgeCard;
            await PumpAsync(60);
            Assert.Equal(176, card.FindControl<Border>("ContentClip")!.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LongComponentListVirtualizesAndTheLastVersionCanBeClicked()
    {
        var model = CreateModel((_, _) => Task.FromResult<IReadOnlyList<string>>(
            Enumerable.Range(0, 89).Select(index => $"20.1.{index}").ToArray()));
        var card = new InstallComponentCard { DataContext = model.NeoForgeCard };
        var scroll = new ScrollViewer { Content = card };
        var window = new Window { Content = scroll, Width = 600, Height = 300 };
        window.Show();
        try
        {
            await PumpAsync(60);
            model.NeoForgeCard.ToggleCommand.Execute(null);
            await PumpAsync(850);
            Assert.Equal(89, model.NeoForgeCard.Entries.Count);
            Assert.InRange(Rows(card).Length, 1, 30);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            await PumpAsync(80);
            var last = Rows(card).Single(button => ((InstallComponentEntry)button.DataContext!).Choice.Version == "20.1.88");
            Click(window, last);
            await PumpAsync(350);
            Assert.Equal("20.1.88", model.Mods.SelectedForgelikeVersion);
            Assert.False(model.NeoForgeCard.IsExpanded);
            Assert.Empty(Rows(card));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("1.12.2", "2017-09-18T08:39:46Z", true, false, false)]
    [InlineData("1.13.2", "2018-10-22T11:41:07Z", true, false, false)]
    [InlineData("1.14-pre1", "2019-04-10T12:20:00Z", true, false, true)]
    [InlineData("1.20", "2023-06-02T08:00:00Z", true, false, true)]
    [InlineData("1.20.1", "2023-06-12T13:25:51Z", true, true, true)]
    [InlineData("23w13a_or_b", "2023-04-01T12:00:00Z", false, false, true)]
    [InlineData("15w14a", "2015-04-01T12:00:00Z", false, false, false)]
    [InlineData("3D Shareware v1.34", "2019-04-01T12:00:00Z", false, false, false)]
    [InlineData("2.0_blue", "2013-04-01T12:00:00Z", false, false, false)]
    [InlineData("26.1-snapshot-1", "2025-12-16T12:00:00Z", true, true, true)]
    public async Task ComponentVisibilityMatchesTheOriginalVersionAndReleaseDateRules(
        string minecraft, string released, bool forge, bool neoforge, bool fabric)
    {
        var model = CreateModel();
        model.Download.SelectedVersion = new VersionItem { Id = minecraft, ReleaseTime = released };
        model.ShowSelectedVersion();
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync(60);
            Assert.Equal(forge, view.FindControl<InstallComponentCard>("ForgeComponentCard")!.IsVisible);
            Assert.Equal(neoforge, view.FindControl<InstallComponentCard>("NeoForgeComponentCard")!.IsVisible);
            Assert.Equal(fabric, view.FindControl<InstallComponentCard>("FabricComponentCard")!.IsVisible);
            Assert.False(model.ApiCard.IsVisible);
            Assert.False(model.OptiFabricCard.IsVisible);
            if (!forge) Assert.False(model.ForgeCard.CanExpand);
            if (!neoforge) Assert.False(model.NeoForgeCard.CanExpand);
            if (!fabric) Assert.False(model.FabricCard.CanExpand);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HeaderContextMenuKeepsFavoriteAndFabricFamilyCommandsBoundWithoutSelectingALoader()
    {
        var model = CreateModel();
        var card = new InstallComponentCard { DataContext = model.FabricCard };
        var window = new Window { Content = card, Width = 600, Height = 250 };
        window.Show();
        var menu = card.FindControl<Button>("HeaderButton")!.ContextMenu!;
        try
        {
            await PumpAsync(60);
            menu.Open(card.FindControl<Button>("HeaderButton")!);
            await PumpAsync(60);
            var favorite = (MenuItem)menu.Items[1]!;
            Assert.NotNull(favorite.Command);
            favorite.Command.Execute(favorite.CommandParameter);
            await PumpAsync(30);
            Assert.True(favorite.IsChecked);
            Assert.True(model.FabricCard.IsFavorite);
            favorite.Command.Execute(favorite.CommandParameter);
            Assert.False(model.FabricCard.IsFavorite);
            var families = (MenuItem)menu.Items[2]!;
            Assert.True(families.IsVisible);
            var quilt = (MenuItem)families.Items[1]!;
            quilt.Command!.Execute(quilt.CommandParameter);
            await PumpAsync(30);
            Assert.Equal("Quilt", model.FabricCard.Kind);
            Assert.Equal("无", model.SelectedBaseLoader);
            Assert.Equal("1.20.1", model.InstanceName);
            menu.Close();
            await ChooseAsync(model.FabricCard, "0.15.0");
            menu.Open(card.FindControl<Button>("HeaderButton")!);
            await PumpAsync(30);
            Assert.False(families.IsEnabled);
            model.FabricCard.ChangeKindCommand.Execute("Fabric");
            Assert.Equal("Quilt", model.FabricCard.Kind);
            model.FabricCard.ClearCommand.Execute(null);
            Assert.True(families.IsEnabled);
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }

    private static InstallViewModel CreateModel(Func<ForgelikeKind, string, Task<IReadOnlyList<string>>>? load = null)
    {
        var services = new AppServices(new AppSettings { GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-component-cards-" + Guid.NewGuid().ToString("N")) },
            saveSettings: _ => { });
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<WMCL.Modding.Fabric.FabricLoaderInfo>>([new("0.16.0", true), new("0.15.0", true)]),
            load ?? ((kind, _) => Task.FromResult<IReadOnlyList<string>>(kind == ForgelikeKind.Forge ? ["47.2.0", "47.1.0"] : ["20.1.92", "20.1.91"])),
            game => Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>([InstallTestData.OptiFine(game)]));
        var model = new InstallViewModel(download, mods, services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        model.ShowSelectedVersion();
        return model;
    }

    private static Task ChooseAsync(InstallComponentCardViewModel card, string version) =>
        ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)card.Entries.Single(entry => entry.Choice.Version == version).SelectCommand).ExecuteAsync(null);

    private static Button[] Rows(Control card) => card.GetVisualDescendants().OfType<Button>()
        .Where(button => button.DataContext is InstallComponentEntry).ToArray();

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        SendInput(window, "MouseMove", point, RawInputModifiers.None);
        SendInput(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        SendInput(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void SendInput(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static void Press(Window window, Key key)
    {
        SendInput(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        SendInput(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }

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
