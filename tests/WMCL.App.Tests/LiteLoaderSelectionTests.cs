using System.Text.Json;
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
using WMCL.App.Views;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;
using WMCL.Modding.Fabric;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class LiteLoaderSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-lite-selection-" + Guid.NewGuid().ToString("N"));
    private const string Minecraft = "1.12.2";
    private const string TweakClass = "com.mumfrey.liteloader.launch.LiteLoaderTweaker";

    private static LiteLoaderVersion Version(string version = "1.12.2-SNAPSHOT") => new(Minecraft, version, TweakClass, true,
        DateTimeOffset.FromUnixTimeSeconds(1500000000), null,
        JsonSerializer.SerializeToElement(new[] { new { name = "net.minecraft:launchwrapper:1.12" } }));

    private (InstallViewModel Model, AppServices Services) Model(string minecraft = Minecraft,
        Func<string, Task<IReadOnlyList<LiteLoaderVersion>>>? load = null)
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = Game(minecraft) };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>(["14.23.5.2860"]),
            game => Task.FromResult<IReadOnlyList<OptiFineVersion>>([new(game, "HD_U", "G5", "OptiFine.jar", "14.23.5.2860")]),
            getLiteLoaderVersions: load ?? (_ => Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version()])));
        var model = new InstallViewModel(download, mods, services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        model.ShowSelectedVersion();
        return (model, services);
    }

    private static VersionItem Game(string minecraft) => new()
    {
        Id = minecraft,
        ReleaseTime = minecraft == Minecraft ? "2017-09-18T08:39:46Z" : null
    };

    private static Task Select(InstallComponentCardViewModel card) =>
        ((IAsyncRelayCommand)Assert.Single(card.Entries).SelectCommand).ExecuteAsync(null);

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task OriginalCardOrderHeaderRowAndIndependentClearWorkInBothThemes(string theme)
    {
        ThemeService.Apply(theme);
        var model = Model().Model;
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await Pump();
            var visible = view.FindControl<ScrollViewer>("PageScroll")!.GetVisualDescendants()
                .OfType<InstallComponentCard>().Where(card => card.IsVisible).ToArray();
            Assert.Equal(["Forge", "OptiFine", "LiteLoader"], visible.Select(card => ((InstallComponentCardViewModel)card.DataContext!).Kind));
            var control = view.FindControl<InstallComponentCard>("LiteLoaderComponentCard")!;
            Click(window, control.FindControl<Button>("HeaderButton")!);
            await Pump();
            Assert.True(model.LiteLoaderCard.IsExpanded);
            Assert.Null(model.SelectedLiteLoader);
            Assert.Equal(57, control.FindControl<Border>("ContentClip")!.Height);
            var entry = Assert.Single(model.LiteLoaderCard.Entries);
            Assert.Equal(Minecraft, entry.Title);
            Assert.StartsWith("测试版，发布于 ", entry.Summary);
            var row = control.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.DataContext, entry));
            if (theme == "light") Click(window, row);
            else
            {
                row.Focus(NavigationMethod.Tab);
                SendKey(window, "KeyPress", Key.Enter);
                SendKey(window, "KeyRelease", Key.Enter);
            }
            await Pump();
            Assert.NotNull(model.SelectedLiteLoader);
            Assert.False(model.LiteLoaderCard.IsExpanded);
            Assert.Equal(Minecraft, model.LiteLoaderCard.Summary);
            Assert.Equal("1.12.2-LiteLoader", model.InstanceName);
            Assert.Equal(20, model.LiteLoaderCard.SummaryIconSize);
            Assert.Same(model.LiteLoaderCard.IconImage, model.InstallLogo);
            Click(window, control.FindControl<Button>("ClearButton")!);
            await Pump();
            Assert.Null(model.SelectedLiteLoader);
            Assert.Equal(Minecraft, model.InstanceName);
            Assert.Equal("无", model.SelectedBaseLoader);
            Assert.DoesNotContain(view.FindControl<ScrollViewer>("PageScroll")!.GetVisualDescendants().OfType<Button>(), button =>
                button.Content is string text && text is "选择版本" or "刷新" or "校验修复");
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task ForgeOptiFineAndLiteLoaderKeepIndependentSelectionsAndOriginalNameOrder()
    {
        var model = Model().Model;
        await Select(model.ForgeCard);
        await Select(model.OptiFineCard);
        await Select(model.LiteLoaderCard);
        Assert.Equal("1.12.2-Forge_14.23.5.2860-LiteLoader-OptiFine_G5", model.InstanceName);
        Assert.Equal("Forge", model.SelectedBaseLoader);
        model.LiteLoaderCard.ClearCommand.Execute(null);
        Assert.Equal("Forge", model.SelectedBaseLoader);
        Assert.True(model.InstallOptiFineWithBase);
        await Select(model.LiteLoaderCard);
        model.ForgeCard.ClearCommand.Execute(null);
        Assert.Equal("OptiFine", model.SelectedBaseLoader);
        Assert.NotNull(model.SelectedLiteLoader);
        Assert.Same(model.LiteLoaderCard.IconImage, model.InstallLogo);
        model.InstanceName = "my custom name";
        model.LiteLoaderCard.ClearCommand.Execute(null);
        Assert.Equal("my custom name", model.InstanceName);
        await Select(model.LiteLoaderCard);
        model.UseVanilla();
        Assert.Null(model.SelectedLiteLoader);
        Assert.Equal("无", model.SelectedBaseLoader);
    }

    [AvaloniaFact]
    public async Task VersionAndRootChangesClearSelectionsAndIgnoreOldRows()
    {
        var (model, services) = Model();
        var old = Assert.Single(model.LiteLoaderCard.Entries);
        await Select(model.LiteLoaderCard);
        model.Download.SelectedVersion = Game("1.20.1");
        Assert.Null(model.SelectedLiteLoader);
        await ((IAsyncRelayCommand)old.SelectCommand).ExecuteAsync(null);
        Assert.Null(model.SelectedLiteLoader);
        model.Download.SelectedVersion = Game(Minecraft);
        await Select(model.LiteLoaderCard);
        services.SetGameDirectory(Path.Combine(_root, "other-root"));
        Assert.Null(model.SelectedLiteLoader);
        await ((IAsyncRelayCommand)old.SelectCommand).ExecuteAsync(null);
        Assert.Null(model.SelectedLiteLoader);
    }

    [AvaloniaFact]
    public async Task IncompatibleCardsCannotReplaceLiteLoaderAndBusyCommandsCannotClearIt()
    {
        var model = Model().Model;
        await Select(model.LiteLoaderCard);
        Assert.Equal("与 LiteLoader 不兼容", model.NeoForgeCard.Incompatibility);
        Assert.Empty(model.NeoForgeCard.Entries);
        model.NeoForgeCard.ToggleCommand.Execute(null);
        Assert.False(model.NeoForgeCard.IsExpanded);
        Assert.NotNull(model.SelectedLiteLoader);
        model.IsCombinedBusy = true;
        model.LiteLoaderCard.ClearCommand.Execute(null);
        Assert.NotNull(model.SelectedLiteLoader);
        model.IsCombinedBusy = false;
        model.LiteLoaderCard.ClearCommand.Execute(null);
        Assert.Null(model.SelectedLiteLoader);
    }

    [AvaloniaTheory]
    [InlineData("1.6.4", true)]
    [InlineData("1.12.2", true)]
    [InlineData("1.13", false)]
    [InlineData("1.20.1", false)]
    [InlineData("26.1", false)]
    [InlineData("15w14a", true)]
    [InlineData("2.0", true)]
    [InlineData("1.RV-Pre1", true)]
    [InlineData("16w20a", false)]
    public void CardVisibilityMatchesOriginalOldVersionAndSpecialVersionRules(string minecraft, bool visible)
    {
        var calls = 0;
        var model = Model(minecraft, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([]);
        }).Model;
        Assert.Equal(visible, model.LiteLoaderCard.IsVisible);
        if (!LiteLoaderService.SupportsMinecraft(minecraft)) Assert.Equal(0, calls);
    }

    [AvaloniaFact]
    public async Task LateCatalogCannotSelectOldMinecraftAndFailureCanRetry()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<LiteLoaderVersion>>();
        var calls = 0;
        var model = Model(load: _ => ++calls == 1 ? gate.Task
            : Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version()])).Model;
        Assert.True(model.LiteLoaderCard.IsLoading);
        model.Download.SelectedVersion = Game("1.20.1");
        gate.SetResult([Version()]);
        await Pump();
        Assert.Empty(model.LiteLoaderCard.Entries);
        Assert.Null(model.SelectedLiteLoader);
        model.Download.SelectedVersion = Game(Minecraft);
        await Select(model.LiteLoaderCard);
        Assert.NotNull(model.SelectedLiteLoader);
        var fail = true;
        var failed = Model(load: _ => fail ? Task.FromException<IReadOnlyList<LiteLoaderVersion>>(new IOException("metadata failed"))
            : Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version()])).Model;
        Assert.Equal("获取失败：metadata failed", failed.LiteLoaderCard.Summary);
        Assert.False(failed.LiteLoaderCard.CanExpand);
        fail = false;
        await failed.LiteLoaderCard.RetryCommand.ExecuteAsync(null);
        Assert.True(failed.LiteLoaderCard.CanExpand);
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ExistingInstanceRestoresActualMetadataAndNeverSubstitutesLatest(bool saved, bool matchingCatalog)
    {
        await CreateInstalled(saved);
        var model = Model(load: _ => matchingCatalog ? Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version()])
            : saved ? Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([])
            : Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version("other-version")])).Model;
        await model.OpenInstanceAsync(_root, "installed");
        if (saved || matchingCatalog)
        {
            Assert.True(model.IsExistingInstance);
            Assert.Equal(Version().Version, model.SelectedLiteLoader!.Version);
            Assert.Equal("开始修改", model.InstallActionText);
            Assert.Equal("installed", model.InstanceName);
            var originalLibraries = model.SelectedLiteLoader.Libraries.GetRawText();
            model.LiteLoaderCard.ClearCommand.Execute(null);
            Assert.Equal("开始修改", model.InstallActionText);
            Assert.Equal("installed", model.InstanceName);
            if (matchingCatalog)
            {
                Assert.True(model.LiteLoaderCard.HasComparison);
                Assert.True(model.LiteLoaderCard.CanRestore);
                await model.LiteLoaderCard.RestoreCommand.ExecuteAsync(null);
                Assert.Equal(Version().Version, model.SelectedLiteLoader!.Version);
                Assert.Equal(originalLibraries, model.SelectedLiteLoader.Libraries.GetRawText());
                Assert.Equal("开始修改", model.InstallActionText);
            }
            else Assert.False(model.LiteLoaderCard.CanRestore);
        }
        else
        {
            Assert.False(model.IsExistingInstance);
            Assert.Null(model.SelectedLiteLoader);
            Assert.Contains("无法安全恢复", model.CombinedStatus);
        }
        Assert.True(File.Exists(Path.Combine(_root, "versions", "installed", "installed.json")));
    }

    [AvaloniaFact]
    public async Task MismatchedSavedMetadataCannotSilentlyChangeAnInstalledComponent()
    {
        await CreateInstalled(true);
        var manifest = InstallManifestStore.Load(_root, "installed")!;
        manifest.ComponentMetadata["liteloader"] = JsonSerializer.SerializeToElement(Version("wrong-version"));
        InstallManifestStore.Save(_root, "installed", manifest);
        var path = Path.Combine(_root, "versions", "installed", "installed.json");
        var original = await File.ReadAllTextAsync(path);
        var model = Model().Model;
        await model.OpenInstanceAsync(_root, "installed");
        Assert.False(model.IsExistingInstance);
        Assert.Null(model.SelectedLiteLoader);
        Assert.Contains("与当前实例不一致", model.CombinedStatus);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [AvaloniaFact]
    public async Task AStoredLiteLoaderSnapshotDoesNotTurnACatalogNetworkFailureIntoSuccessfulInitialization()
    {
        await CreateInstalled(true);
        var manifestPath = InstallManifestStore.GetPath(_root, "installed");
        var original = File.ReadAllBytes(manifestPath);
        var model = Model(load: _ => Task.FromException<IReadOnlyList<LiteLoaderVersion>>(new IOException("offline"))).Model;
        await model.OpenInstanceAsync(_root, "installed");
        Assert.True(model.HasInstanceInitializationError);
        Assert.Contains("LiteLoader", model.InstanceInitializationError);
        Assert.Contains("offline", model.InstanceInitializationError);
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
    }

    [AvaloniaFact]
    public async Task SwitchingRootWhileRestoringLegacyMetadataIgnoresTheOldCompletion()
    {
        await CreateInstalled(false);
        var gate = new TaskCompletionSource<IReadOnlyList<LiteLoaderVersion>>();
        var (model, services) = Model(load: _ => gate.Task);
        var opening = model.OpenInstanceAsync(_root, "installed");
        Assert.True(model.IsPreparingInstance);
        services.SetGameDirectory(Path.Combine(_root, "new-root"));
        gate.SetResult([Version()]);
        await opening;
        Assert.False(model.IsExistingInstance);
        Assert.Null(model.SelectedLiteLoader);
        Assert.False(model.IsPreparingInstance);
    }

    [AvaloniaFact]
    public async Task AddedLiteLoaderCanRestoreAbsenceWhileKeepingTheOriginalOptiFine()
    {
        var directory = Path.Combine(_root, "versions", "installed");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "installed.json"),
            """{"id":"installed","clientVersion":"1.12.2","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        await File.WriteAllBytesAsync(Path.Combine(directory, "installed.jar"), [1]);
        InstallManifestStore.Save(_root, "installed", new InstallManifest
        {
            Components = new() { ["minecraft"] = Minecraft, ["optifine"] = "G5" }
        });
        var (model, _) = Model();
        await model.OpenInstanceAsync(_root, "installed");
        await Select(model.LiteLoaderCard);
        model.LiteLoaderCard.IsExpanded = true;
        Assert.True(model.LiteLoaderCard.HasComparison);
        Assert.True(model.LiteLoaderCard.CanRestore);
        await model.LiteLoaderCard.RestoreCommand.ExecuteAsync(null);
        Assert.Null(model.SelectedLiteLoader);
        Assert.False(model.LiteLoaderCard.HasComparison);
        Assert.False(model.LiteLoaderCard.IsExpanded);
        Assert.Equal("OptiFine", model.SelectedBaseLoader);
        Assert.Equal("G5", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task MinecraftRestoreReturnsTheCapturedLiteLoaderMetadataAfterChangingToAnUnsupportedGame()
    {
        await CreateInstalled(true);
        var (model, _) = Model();
        await model.OpenInstanceAsync(_root, "installed");
        var original = model.SelectedLiteLoader!;
        var game = Game("1.20.1");
        model.VersionCatalog.Open([game], model.Download.SelectedVersion);
        model.VersionCatalog.Groups.SelectMany(group => group.Entries).First().SelectCommand.Execute(null);
        Assert.Null(model.SelectedLiteLoader);
        Assert.True(model.HasMinecraftComparison);
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal(original.Version, model.SelectedLiteLoader!.Version);
        Assert.Equal(original.TweakClass, model.SelectedLiteLoader.TweakClass);
        Assert.Equal(original.Libraries.GetRawText(), model.SelectedLiteLoader.Libraries.GetRawText());
        Assert.Equal(Minecraft, model.MinecraftVersionText);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaTheory]
    [InlineData("1.12.1", true)]
    [InlineData("1.20.1", false)]
    public async Task ChangingMinecraftRechoosesOnlyATargetCompatibleLiteLoader(string minecraft, bool supported)
    {
        await CreateInstalled(true);
        var (model, _) = Model(load: game => Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([
            Version() with { Minecraft = game, Version = game + "-SNAPSHOT" }
        ]));
        await model.OpenInstanceAsync(_root, "installed");
        var game = Game(minecraft);
        model.VersionCatalog.Open([game], model.Download.SelectedVersion);
        model.VersionCatalog.Groups.SelectMany(group => group.Entries).First().SelectCommand.Execute(null);
        await model.PendingComponentSelectionTask;
        if (supported)
        {
            Assert.Equal(minecraft, model.SelectedLiteLoader!.Minecraft);
            Assert.Equal(minecraft + "-SNAPSHOT", model.SelectedLiteLoader.Version);
        }
        else Assert.Null(model.SelectedLiteLoader);
        Assert.Equal(Minecraft, model.OriginalMinecraftVersionText);
        Assert.True(model.CanRestoreMinecraft);
    }

    private async Task CreateInstalled(bool saved)
    {
        var directory = Path.Combine(_root, "versions", "installed");
        Directory.CreateDirectory(directory);
        var profile = JsonSerializer.Deserialize<VersionJson>(LiteLoaderService.CreateProfile(Version()))!;
        profile.Id = "installed";
        profile.InheritsFrom = null;
        profile.BaseGameVersion = Minecraft;
        await File.WriteAllTextAsync(Path.Combine(directory, "installed.json"), JsonSerializer.Serialize(profile));
        await File.WriteAllBytesAsync(Path.Combine(directory, "installed.jar"), [1]);
        var manifest = new InstallManifest();
        manifest.Components["minecraft"] = Minecraft;
        manifest.Components["liteloader"] = Version().Version;
        if (saved) manifest.ComponentMetadata["liteloader"] = JsonSerializer.SerializeToElement(Version());
        InstallManifestStore.Save(_root, "installed", manifest);
    }

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

    private static async Task Pump()
    {
        for (var frame = 0; frame < 20; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
