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

public sealed class LaunchVersionSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-launch-selection-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task LaunchCatalogReadsLocalInheritanceAndKeepsBrokenProfilesWithoutDownloadingOrRepairingThem()
    {
        await WriteProfileAsync("Local", """{"id":"Local","mainClass":"net.minecraft.client.main.Main","releaseTime":"2024-01-01T00:00:00Z"}""");
        await WriteProfileAsync("Fabric", """{"id":"Fabric","inheritsFrom":"Local","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient"}""");
        var broken = await WriteProfileAsync("1.20.1", "{ broken profile");
        await WriteProfileAsync("MissingParent", """{"id":"MissingParent","inheritsFrom":"1.21.1"}""");
        var original = await File.ReadAllBytesAsync(broken);
        var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services);

        await launch.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, launch.InstalledVersions.Count);
        Assert.Equal(4, launch.Versions.Count);
        Assert.Equal("Local", launch.SelectedVersion?.Id);
        Assert.Equal("fabric", launch.InstalledVersions.Single(item => item.Id == "Fabric").Category);
        Assert.Equal("error", launch.InstalledVersions.Single(item => item.Id == "1.20.1").Category);
        Assert.Equal("error", launch.InstalledVersions.Single(item => item.Id == "MissingParent").Category);
        Assert.Equal(original, await File.ReadAllBytesAsync(broken));
        Assert.Equal(files, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.True(launch.CanLaunch);
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task VersionRowsDistinguishPrimarySecondaryMiddleAndDragWithoutSelectingFromActionButtons(string theme)
    {
        ThemeService.Apply(theme);
        var harness = new Harness(_root, [Version("Current"), Version("Other")]);
        await harness.Launch.LoadAsync();
        var view = new VersionSelectView { DataContext = harness.Selection };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync();
            var current = Row(view, "Current");
            var other = Row(view, "Other");
            Click(window, other, MouseButton.Right);
            Assert.Equal(["Other"], harness.SettingsRequests);
            Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
            Assert.Equal(0, harness.CloseCount);

            Click(window, other, MouseButton.Middle);
            Assert.Single(harness.SettingsRequests);
            Assert.Equal(0, harness.CloseCount);
            Pointer(window, "MouseDown", current, MouseButton.Left);
            Pointer(window, "MouseMove", other);
            Pointer(window, "MouseUp", other, MouseButton.Left);
            Assert.Equal(0, harness.CloseCount);

            var favorite = other.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("favorite"));
            Click(window, favorite);
            await PumpAsync();
            Assert.True(harness.Launch.InstalledVersions.Single(item => item.Id == "Other").IsFavorite);
            Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
            Assert.Equal(0, harness.CloseCount);

            Click(window, Row(view, "Other"));
            Assert.Equal("Other", harness.Launch.SelectedVersion?.Id);
            Assert.Equal(1, harness.CloseCount);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public async Task KeyboardArrowsAndEnterChooseTheFocusedRow()
    {
        var harness = new Harness(_root, [Version("Current"), Version("Other")]);
        await harness.Launch.LoadAsync();
        var view = new VersionSelectView { DataContext = harness.Selection };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync();
            Row(view, "Current").Focus(NavigationMethod.Tab);
            // PCL2 sorts equal-date versions by name descending, so Other is above Current.
            Press(window, Key.Up);
            await PumpAsync();
            Assert.True(Row(view, "Other").IsFocused);
            Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
            Press(window, Key.Enter);
            Assert.Equal("Other", harness.Launch.SelectedVersion?.Id);
            Assert.Equal(1, harness.CloseCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task F11ShowsOnlyHiddenVersionsAndUsesTheOriginalHiddenEmptyState(bool hasHidden)
    {
        var harness = new Harness(_root, hasHidden ? [Version("Current"), Version("Hidden")] : [Version("Current")]);
        harness.Services.GetHiddenVersions(_root).Add("Hidden");
        await harness.Launch.LoadAsync();
        var view = new VersionSelectView { DataContext = harness.Selection };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync();
            Assert.DoesNotContain(harness.Selection.Groups.SelectMany(group => group.Versions), item => item.Id == "Hidden");
            view.Focus();
            Press(window, Key.F11);
            await PumpAsync();
            Assert.True(harness.Selection.ShowHidden);
            Assert.DoesNotContain(harness.Selection.Groups.SelectMany(group => group.Versions), item => item.Id == "Current");
            if (hasHidden)
            {
                Assert.Equal("隐藏的版本", Assert.Single(harness.Selection.Groups).Title);
                Assert.Equal("Hidden", Assert.Single(harness.Selection.Groups[0].Versions).Id);
            }
            else
            {
                Assert.Empty(harness.Selection.Groups);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "无隐藏版本" && text.IsEffectivelyVisible);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "下载游戏") && button.IsEffectivelyVisible);
            }
            Press(window, Key.F11);
            await PumpAsync();
            Assert.False(harness.Selection.ShowHidden);
            Assert.Equal("Current", Assert.Single(harness.Selection.Groups.SelectMany(group => group.Versions)).Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BrokenVersionClicksOpenItsFolderAndNeverReplaceTheUsableLaunchSelection()
    {
        var harness = new Harness(_root, [Version("Current"), Version("Broken", "error")]);
        await harness.Launch.LoadAsync();
        var errorGroup = harness.Selection.Groups.Single(group => group.Title == "错误的版本");
        Assert.False(errorGroup.IsExpanded);
        errorGroup.ToggleCommand.Execute(null);
        var view = new VersionSelectView { DataContext = harness.Selection };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync();
            Click(window, Row(view, "Broken"));
            Click(window, Row(view, "Broken"), MouseButton.Right);
            Assert.Equal([Path.Combine(_root, "versions", "Broken"), Path.Combine(_root, "versions", "Broken")], harness.OpenedPaths);
            Assert.Empty(harness.SettingsRequests);
            Assert.Equal(0, harness.CloseCount);
            Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
            harness.Launch.SelectedVersion = harness.Launch.InstalledVersions.Single(item => item.Id == "Broken");
            Assert.False(harness.Launch.CanLaunch);
            await harness.Launch.LaunchCommand.ExecuteAsync(null);
            Assert.False(harness.Launch.IsBusy);
            Assert.Contains("请先选择一个已安装版本", harness.Launch.Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("busy")]
    [InlineData("stale")]
    public async Task BusyOrStaleRowsCannotConfirmOrOpenSettings(string state)
    {
        var harness = new Harness(_root, [Version("Current"), Version("Other")]);
        await harness.Launch.LoadAsync();
        var item = harness.Launch.InstalledVersions.Single(version => version.Id == "Other");
        harness.Selection.SelectedVersion = item;
        if (state == "busy") harness.Launch.IsBusy = true;
        else harness.Launch.InstalledVersions.Remove(item);
        harness.Selection.ActivateVersion(item);
        harness.Selection.OpenVersionSettings(item);
        harness.Selection.SelectedVersion = item;
        harness.Selection.ConfirmCommand.Execute(null);
        Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
        Assert.Equal(0, harness.CloseCount);
        Assert.Empty(harness.SettingsRequests);
        Assert.Empty(harness.OpenedPaths);
    }

    [AvaloniaFact]
    public async Task ChangingRootsImmediatelyInvalidatesTheOldSelectionEvenWhenTheNewCatalogFails()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services, loadVersions: root => root == _root
            ? Task.FromResult<IReadOnlyList<VersionItem>>([Version("Old")])
            : Task.FromException<IReadOnlyList<VersionItem>>(new IOException("new folder unavailable")));
        var closed = 0;
        var selection = new VersionSelectViewModel(launch, services, () => closed++, () => { });
        await launch.LoadAsync();
        var old = launch.SelectedVersion!;
        services.SetGameDirectory(Path.Combine(_root, "new root"));
        Assert.Null(launch.SelectedVersion);
        Assert.False(launch.CanLaunch);
        Assert.Empty(launch.InstalledVersions);
        await launch.LoadAsync();
        selection.SelectedVersion = old;
        selection.ConfirmCommand.Execute(null);
        Assert.Equal(0, closed);
        Assert.Null(launch.SelectedVersion);
        Assert.Contains("new folder unavailable", launch.Status);
    }

    [AvaloniaFact]
    public async Task AutomaticSelectionSkipsHiddenAndBrokenVersionsButKeepsAnExplicitHiddenSelectionOnRefresh()
    {
        var harness = new Harness(_root, [Version("Hidden"), Version("Broken", "error"), Version("Current")]);
        harness.Services.GetHiddenVersions(_root).Add("Hidden");
        await harness.Launch.LoadAsync();
        Assert.Equal("Current", harness.Launch.SelectedVersion?.Id);
        harness.Selection.ShowHidden = true;
        harness.Selection.ActivateVersion(harness.Launch.InstalledVersions.Single(item => item.Id == "Hidden"));
        Assert.Equal("Hidden", harness.Launch.SelectedVersion?.Id);
        await harness.Launch.LoadAsync();
        Assert.Equal("Hidden", harness.Launch.SelectedVersion?.Id);
    }

    [AvaloniaFact]
    public void CancelWithoutAnActiveLaunchKeepsTheFinalStatus()
    {
        var harness = new Harness(_root, []);
        harness.Launch.Status = "启动失败：test failure";
        harness.Launch.CancelCommand.Execute(null);
        Assert.Equal("启动失败：test failure", harness.Launch.Status);
    }

    [Fact]
    public async Task CancelledLocalCatalogReadPropagatesCancellation()
    {
        await WriteProfileAsync("Local", """{"id":"Local"}""");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VersionCatalog(new VersionRepository(_root)).LoadInstalledAsync(cancellation.Token));
    }

    private static VersionItem Version(string id, string category = "vanilla") => new() { Id = id, IsInstalled = true, Category = category };

    private async Task<string> WriteProfileAsync(string id, string content)
    {
        var directory = Path.Combine(_root, "versions", id);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static ListBoxItem Row(Control view, string id) => view.GetVisualDescendants().OfType<ListBoxItem>()
        .Single(item => item.DataContext is VersionItem version && version.Id == id);

    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        Pointer(window, "MouseMove", control);
        Pointer(window, "MouseDown", control, button);
        Pointer(window, "MouseUp", control, button);
    }

    private static void Pointer(Window window, string method, Control control, MouseButton? button = null)
    {
        var point = control.TranslatePoint(
            new Point(Math.Max(1, control.Bounds.Width / 2), control.Bounds.Height / 2), window)!.Value;
        if (button is { } mouseButton) SendInput(window, method, point, mouseButton, RawInputModifiers.None);
        else SendInput(window, method, point, RawInputModifiers.None);
    }

    private static void Press(Window window, Key key)
    {
        SendInput(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        SendInput(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void SendInput(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 8; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Harness
    {
        public Harness(string root, IReadOnlyList<VersionItem> versions)
        {
            Services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
            Launch = new LaunchViewModel(Services, loadVersions: _ => Task.FromResult(versions));
            Selection = new VersionSelectViewModel(Launch, Services, () => CloseCount++, () => { }, OpenedPaths.Add);
            Selection.VersionSettingsRequested += (_, id) => SettingsRequests.Add(id);
        }

        public AppServices Services { get; }
        public LaunchViewModel Launch { get; }
        public VersionSelectViewModel Selection { get; }
        public int CloseCount { get; private set; }
        public List<string> OpenedPaths { get; } = [];
        public List<string> SettingsRequests { get; } = [];
    }
}
