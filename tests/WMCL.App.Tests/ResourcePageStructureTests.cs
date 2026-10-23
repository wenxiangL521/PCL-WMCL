using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ResourcePageStructureTests
{
    private static AppServices Services(Action<AppSettings>? save = null) => new(new AppSettings
    {
        GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-page-" + Guid.NewGuid().ToString("N"))
    }, saveSettings: save ?? (_ => { }));

    [AvaloniaTheory]
    [InlineData("mod")]
    [InlineData("resourcepack")]
    [InlineData("modpack")]
    public async Task ResourceSearchOpensExplicitProjectAndReturnsWithoutLosingResults(string type)
    {
        using var http = new HttpClient(new EmptyVersions());
        var services = new AppServices(Services().Settings, new ModrinthClient(http, http), saveSettings: _ => { });
        var mods = InstallTestData.CreateMods(services);
        mods.SetMode("mods");
        var content = new InstanceContentViewModel(services) { IsDownloadPage = true };
        var pack = new ModpackViewModel(services);
        UserControl view = type switch
        {
            "mod" => new ModsView { DataContext = mods },
            "modpack" => new ModpackView { DataContext = pack },
            _ => new InstanceContentView { DataContext = content }
        };
        var window = new Window { Content = view, Width = 660, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(Visible<Button>(view), button => Equals(button.Content, "返回搜索结果"));
            Assert.DoesNotContain(Visible<TextBlock>(view), text => text.Text?.StartsWith("已安装", StringComparison.Ordinal) == true);
            if (type == "mod")
            {
                mods.SearchResults.Add(new ModrinthProject("sample", "sample", "Sample", "Description", "mod", 1, null, []));
                Assert.False(mods.HasResourceDetail);
                mods.SelectedMod = mods.SearchResults[0];
            }
            else if (type == "modpack")
            {
                pack.Results.Add(new ModpackItem("Sample", "Description", "sample", 1, "Modrinth", "sample", 0));
                Assert.False(pack.HasResourceDetail);
                pack.SelectedProject = pack.Results[0];
            }
            else
            {
                content.DownloadResults.Add(new ResourceItem("Sample", "Description", "Modrinth", "sample", 0));
                Assert.False(content.HasResourceDetail);
                content.SelectedDownloadProject = content.DownloadResults[0];
            }
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            var back = Assert.Single(Visible<Button>(view), button => Equals(button.Content, "返回搜索结果"));
            Assert.Contains(Visible<TextBlock>(view), text => text.Text == "Sample");
            if (type == "modpack")
                Assert.Contains(Visible<ListBox>(view), list => list.Classes.Contains("resourceRows"));
            else
                Assert.Single(Visible<ItemsControl>(view), control => control.Name == "VersionGroups");
            var detail = view.FindControl<StackPanel>("ResourceDetailPanel")!;
            Assert.True(detail.IsVisible);
            Assert.Contains(Visible<Button>(view), button => Equals(button.Content, "复制名称"));
            Assert.Contains(Visible<Button>(view), button => Equals(button.Content, "转到 Modrinth"));
            Assert.Equal(type == "modpack" ? 3 : 4, detail.Children.Count);
            if (type != "modpack")
                Assert.Contains(Visible<TextBlock>(view), text => text.Text == "安装目标");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(Visible<Button>(view), button => Equals(button.Content, "返回搜索结果"));
            Assert.Single(type switch { "mod" => mods.SearchResults.Cast<object>(), "modpack" => pack.Results.Cast<object>(), _ => content.DownloadResults.Cast<object>() });
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ModDetailUsesIntroTargetResultsAnatomyAndPclPageMotion()
    {
        var services = Services();
        var mods = InstallTestData.CreateMods(services);
        mods.SetMode("mods");
        mods.SearchResults.Add(new ModrinthProject("sample", "sample", "Sample", "Description", "mod", 1, null, []));
        var view = new ModsView { DataContext = mods };
        var window = new Window { Content = view, Width = 660, Height = 500 };
        window.Show();
        try
        {
            mods.SelectedMod = mods.SearchResults[0];
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            var panel = view.FindControl<StackPanel>("ResourceDetailPanel")!;
            Assert.True(panel.IsVisible);
            Assert.True(WMCL.App.Animation.ResourceDetailMotion.GetEnabled(panel));
            Assert.Equal(4, panel.Children.Count);
            Assert.Contains(Visible<Button>(view), button => Equals(button.Content, "转到 Modrinth"));
            Assert.Contains(Visible<Button>(view), button => Equals(button.Content, "复制名称"));
            Assert.Contains(Visible<TextBlock>(view), text => text.Text == "安装目标");
            Assert.Contains(Visible<TextBlock>(view), text => text.Text == "版本列表");

            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            var cards = panel.Children.OfType<Border>().ToList();
            Assert.Equal(3, cards.Count);
            Assert.All(cards, card => Assert.Equal(1, card.Opacity, 2));
            Assert.All(cards, card => Assert.Equal(0, Assert.IsType<Avalonia.Media.TranslateTransform>(card.RenderTransform).Y, 1));

            var back = Visible<Button>(view).Single(button => Equals(button.Content, "返回搜索结果"));
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(panel.IsVisible);
            Assert.NotNull(mods.SelectedMod);
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            Assert.False(panel.IsVisible);
            Assert.Null(mods.SelectedMod);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void JavaManagementNavigationAndConnectionSettingsShareRealModels()
    {
        using var main = new MainWindowViewModel(Services());
        main.Settings.SetSection("settings-launch");
        var view = new SettingsView { DataContext = main.Settings };
        var window = new Window { Content = view, Width = 850, Height = 700 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(Visible<Button>(view), button => ReferenceEquals(button.Command, main.Settings.InstallJavaCommand)
                || ReferenceEquals(button.Command, main.Settings.CleanJavaCommand));
            Assert.Single(Visible<ComboBox>(view), box => ReferenceEquals(box.ItemsSource, main.Settings.JavaRuntimes));
            Assert.Contains(Visible<TextBlock>(view), text => text.Text == "最大内存");
            view.FindControl<Button>("ManageJavaButton")!.Command!.Execute(null);
            Assert.Equal("settings-java", main.SelectedSubTab!.Key);
            Assert.True(main.Settings.ShowJavaSettings);
            Assert.False(main.Settings.ShowLaunchSettings);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(Visible<ComboBox>(view), box => ReferenceEquals(box.ItemsSource, main.Settings.JavaRuntimes));
            Assert.DoesNotContain(Visible<TextBlock>(view), text => text.Text == "最大内存");
            Assert.Contains(Visible<TextBlock>(view), text => text.Text == "安装 Java");
            Assert.Contains(Visible<Button>(view), button => ReferenceEquals(button.Command, main.Settings.InstallJavaCommand));
            Assert.Contains(Visible<Button>(view), button => ReferenceEquals(button.Command, main.Settings.CleanJavaCommand));
            main.Link.OpenSettingsCommand.Execute(null);
            Assert.Equal("settings-link", main.SelectedSubTab!.Key);
            Assert.Same(main.Link, main.LinkSettings.Connection);
            Assert.Same(main.LinkSettings, main.CurrentPage);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GroupCardHeaderClickOnlyTogglesExpansion()
    {
        var entry = new ResourceVersionEntry(new object(), "v1", "detail", true, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        var group = new ResourceVersionGroup("1.20.1", [entry], true);
        var card = new WMCL.App.Controls.ResourceVersionGroupCard { DataContext = group };
        var window = new Window { Content = card, Width = 500, Height = 300 };
        window.Show();
        try
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var header = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("versionHeader"));
            var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            Assert.InRange(point.X, 0, window.Bounds.Width);
            Assert.InRange(point.Y, 0, window.Bounds.Height);
            foreach (var method in new[] { "MouseMove", "MouseDown", "MouseUp" })
            {
                var args = method == "MouseMove"
                    ? new object?[] { point, RawInputModifiers.None }
                    : new object?[] { point, MouseButton.Left, RawInputModifiers.None };
                typeof(Avalonia.Headless.AvaloniaHeadlessPlatform).Assembly
                    .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!
                    .Invoke(window.PlatformImpl, args);
            }
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.False(group.IsExpanded);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void VersionFiltersStayRawForSmallSetsAndGroupByDropForLargeSets()
    {
        var small = ResourceVersionFilterSet.Create(["1.21.1", "1.20.6"]);
        Assert.False(small.Grouped);
        Assert.Equal(["全部", "1.21.1", "1.20.6"], small.Labels);
        Assert.True(small.Matches("1.20.6", ["1.20.6"]));
        Assert.False(small.Matches("1.20.6", ["1.21.1"]));

        var large = ResourceVersionFilterSet.Create([
            "1.21.4", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1",
            "1.19.4", "1.19.2", "1.18.2"
        ]);
        Assert.True(large.Grouped);
        Assert.Contains("1.21", large.Labels);
        Assert.Contains("1.20", large.Labels);
        Assert.True(large.Matches("1.20", ["1.20.4"]));
        Assert.False(large.Matches("1.21", ["1.20.4"]));
    }

    [Fact]
    public async Task SearchBrowsesWithoutInstalledInstanceAndWaitsForExplicitProjectSelection()
    {
        using var handler = new SearchHits();
        using var http = new HttpClient(handler);
        var services = new AppServices(Services().Settings, new ModrinthClient(http, http), saveSettings: _ => { });
        var mods = InstallTestData.CreateMods(services);
        var content = new InstanceContentViewModel(services) { IsDownloadPage = true };
        var pack = new ModpackViewModel(services) { GameVersion = "" };
        await mods.SearchModsCommand.ExecuteAsync(null);
        await content.SearchDownloadsCommand.ExecuteAsync(null);
        await pack.SearchCommand.ExecuteAsync(null);
        Assert.Single(mods.SearchResults);
        Assert.Single(content.DownloadResults);
        Assert.Single(pack.Results);
        Assert.False(mods.HasResourceDetail);
        Assert.False(content.HasResourceDetail);
        Assert.False(pack.HasResourceDetail);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            var decoded = Uri.UnescapeDataString(request.Query);
            Assert.DoesNotContain("versions:", decoded);
            Assert.DoesNotContain("categories:", decoded);
        });
    }

    [Fact]
    public async Task RoomCommandsUseEnteredCodeAndRespectCancellationAndBusyState()
    {
        var services = Services();
        var started = 0;
        string? answer = null;
        var model = new LinkViewModel(services, () => Task.FromResult(answer), () => { started++; return Task.CompletedTask; });
        await model.JoinRoomCommand.ExecuteAsync(null);
        Assert.Equal(0, started);
        answer = "  example-code  ";
        await model.JoinRoomCommand.ExecuteAsync(null);
        Assert.Equal("example-code", model.RoomCode);
        Assert.Equal("加入房间", model.TerracottaMode);
        Assert.Equal(1, started);
        model.IsRunning = true;
        await model.CreateRoomCommand.ExecuteAsync(null);
        await model.JoinRoomCommand.ExecuteAsync(null);
        Assert.Equal(1, started);
        model.IsRunning = false;
        await model.CreateRoomCommand.ExecuteAsync(null);
        Assert.Equal(2, started);
        Assert.Equal(12, model.RoomCode.Length);
        Assert.Equal("创建房间", model.TerracottaMode);
    }

    [AvaloniaFact]
    public void ConnectionHomeShowsOriginalChoicesAndSettingsPersistActualOptions()
    {
        var saves = 0;
        var services = Services(_ => saves++);
        var model = new LinkViewModel(services);
        var view = new LinkView { DataContext = model };
        var window = new Window { Content = view, Width = 660, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Button>("CreateRoomButton")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<Button>("JoinRoomButton")!.IsEffectivelyVisible);
            Assert.Empty(Visible<TextBox>(view));
            model.ConnectionMode = "优先低延迟";
            model.Peer = "tcp://localhost:11010";
            Assert.True(services.Settings.LinkLatencyFirst);
            Assert.Equal(model.Peer, services.Settings.EasyTierPeer);
            Assert.Equal(2, saves);
            var reloaded = new LinkViewModel(services);
            Assert.Equal(model.ConnectionMode, reloaded.ConnectionMode);
            Assert.Equal(model.Peer, reloaded.Peer);
            model.IsBusy = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Button>("CreateRoomButton")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<TControl> Visible<TControl>(Control view) where TControl : Control =>
        view.GetVisualDescendants().OfType<TControl>().Where(control => control.IsEffectivelyVisible);

    private sealed class EmptyVersions : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private sealed class SearchHits : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"hits":[{"project_id":"sample","slug":"sample","title":"Sample","description":"Description","project_type":"mod","downloads":1,"categories":[]}],"total_hits":1}""")
            });
        }
    }
}
