using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class ComponentPreloadTests
{
    [AvaloniaTheory]
    [InlineData("47.1.0", "推荐版", "最新版")]
    [InlineData("47.2.0", "推荐版", null)]
    [InlineData(null, "最新版", null)]
    public void ForgePreloadsPreserveAllVersionsAndDeduplicateTheRecommendedLatest(string? recommended, string first, string? second)
    {
        var model = Model(recommended);
        var items = model.ForgeCard.DisplayItems.ToArray();
        var preloads = items.TakeWhile(item => item is not InstallComponentSeparator).Cast<InstallComponentEntry>().ToArray();
        Assert.StartsWith(first, preloads[0].Summary);
        Assert.Equal(second is null ? 1 : 2, preloads.Length);
        if (second is not null) Assert.StartsWith(second, preloads[1].Summary);
        Assert.Equal("全部版本 (2)", Assert.Single(items.OfType<InstallComponentSeparator>()).Title);
        Assert.Equal(["47.2.0", "47.1.0"], items.Skip(preloads.Length + 1).Cast<InstallComponentEntry>().Select(entry => entry.Choice.Version));
        Assert.All(preloads, entry => Assert.Contains(entry.Choice, model.ForgeCard.Entries.Select(full => full.Choice)));
    }

    [AvaloniaTheory]
    [InlineData("beta-first", "最新稳定版,最新测试版")]
    [InlineData("stable-first", "最新稳定版")]
    [InlineData("beta-only", "最新测试版")]
    public void NeoForgeOnlyPreloadsABetaNewerThanTheFirstStableVersion(string order, string expected)
    {
        var versions = order switch
        {
            "beta-first" => new[] { "20.1.94-beta", "20.1.93-beta", "20.1.92", "20.1.91" },
            "stable-first" => ["20.1.94", "20.1.93-beta", "20.1.92"],
            _ => ["20.1.94-beta", "20.1.93-beta"]
        };
        var card = Model(neoVersions: versions).NeoForgeCard;
        var preloads = card.DisplayItems.TakeWhile(item => item is not InstallComponentSeparator).Cast<InstallComponentEntry>().ToArray();
        Assert.Equal(expected.Split(','), preloads.Select(entry => entry.Summary));
        Assert.Equal(versions, card.Entries.Select(entry => entry.Choice.Version));
        Assert.Equal(versions.Length, Assert.Single(card.DisplayItems.OfType<InstallComponentSeparator>()).Count);
    }

    [AvaloniaFact]
    public async Task CompatibilityFilteringRunsBeforePreloadsAndSingleVersionHasNoSeparator()
    {
        var model = Model("47.1.0");
        var optifine = model.OptiFineCard.Entries.Single();
        await ((IAsyncRelayCommand)optifine.SelectCommand).ExecuteAsync(null);
        var entry = Assert.IsType<InstallComponentEntry>(Assert.Single(model.ForgeCard.DisplayItems));
        Assert.Equal("47.2.0", entry.Choice.Version);
        Assert.DoesNotContain("推荐版", entry.Summary);
        Assert.DoesNotContain("最新版", entry.Summary);
        Assert.Empty(model.ForgeCard.DisplayItems.OfType<InstallComponentSeparator>());
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task PreloadAndCompleteListSelectTheSameVersionAndKeyboardSkipsTheSeparator(string theme)
    {
        ThemeService.Apply(theme);
        var model = Model("47.1.0");
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 400 };
        window.Show();
        try
        {
            model.ForgeCard.IsExpanded = true;
            await PumpAsync();
            Assert.Equal(218, card.FindControl<Border>("ContentClip")!.Height);
            var separator = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "全部版本 (2)");
            Assert.Equal(new Thickness(6, 13, 0, 4), separator.Margin);
            Assert.False(separator.Focusable);
            var preloads = model.ForgeCard.DisplayItems.Take(2).Cast<InstallComponentEntry>().ToArray();
            var lastPreload = Row(card, preloads[1]);
            lastPreload.Focus(NavigationMethod.Tab);
            SendKey(window, "KeyPress", Key.Down);
            SendKey(window, "KeyRelease", Key.Down);
            await PumpAsync();
            Assert.True(Row(card, model.ForgeCard.Entries[0]).IsFocused);
            SendKey(window, "KeyPress", Key.Up);
            SendKey(window, "KeyRelease", Key.Up);
            await PumpAsync();
            Assert.True(lastPreload.IsFocused);
            Click(window, Row(card, preloads[0]));
            await ((IAsyncRelayCommand)preloads[0].SelectCommand).ExecutionTask!;
            Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
            Assert.False(model.ForgeCard.IsExpanded);
            model.ForgeCard.ClearCommand.Execute(null);
            await ((IAsyncRelayCommand)model.ForgeCard.Entries.Single(entry => entry.Choice.Version == "47.1.0").SelectCommand).ExecuteAsync(null);
            Assert.Equal("47.1.0", model.Mods.SelectedForgelikeVersion);
            Assert.Equal("1.20.1-Forge_47.1.0", model.InstanceName);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    private static Button Row(Control control, InstallComponentEntry entry) =>
        control.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.DataContext, entry));

    private static void SendKey(Window window, string method, Key key) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, [key, RawInputModifiers.None, PhysicalKey.None, null]);

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
        input.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [point, RawInputModifiers.None]);
        input.GetMethod("MouseDown")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, RawInputModifiers.None]);
        input.GetMethod("MouseUp")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, RawInputModifiers.None]);
    }

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 20; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }

    private static InstallViewModel Model(string? recommended = null, string[]? neoVersions = null)
    {
        var services = new AppServices(new AppSettings { GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-preloads-" + Guid.NewGuid().ToString("N")) },
            saveSettings: _ => { });
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (kind, _) => Task.FromResult<IReadOnlyList<string>>(kind == ForgelikeKind.Forge ? ["47.2.0", "47.1.0"] : neoVersions ?? ["20.1.92"]),
            game => Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine(game)]),
            getForgeVersionDetails: _ => Task.FromResult<IReadOnlyList<ForgeVersionInfo>>(
                [new("47.2.0", null, recommended == "47.2.0"), new("47.1.0", null, recommended == "47.1.0")]));
        var model = new InstallViewModel(download, mods, services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        model.ShowSelectedVersion();
        return model;
    }
}
