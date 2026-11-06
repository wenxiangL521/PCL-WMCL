using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ResourceBrowserReuseTests
{
    [AvaloniaTheory]
    [InlineData("mods", false, 420)]
    [InlineData("content-resource", false, 420)]
    [InlineData("content-resource", false, 660)]
    [InlineData("content-shader", false, 420)]
    [InlineData("content-world", false, 420)]
    public async Task BothEntriesUseOneCompleteBrowserAndRetainResultsOnDetailReturn(string kind, bool management, double width)
    {
        using var fixture = new Fixture();
        var mods = InstallTestData.CreateMods(fixture.Services);
        using var content = new InstanceContentViewModel(fixture.Services);
        mods.ModInstances.Add("Source");
        mods.ModInstances.Add("Other");
        content.Instances.Add("Source");
        content.Instances.Add("Other");
        if (kind == "mods")
        {
            await mods.OpenManagementAsync("Source");
            mods.IsInstanceManagement = management;
            mods.SearchText = "sample";
            mods.SearchGameVersion = "1.20.1";
            mods.SearchLoader = "fabric";
            await mods.SearchModsCommand.ExecuteAsync(null);
        }
        else
        {
            await content.OpenManagementAsync("Source", kind);
            content.IsDownloadPage = !management;
            content.DownloadSearchText = "sample";
            content.SearchGameVersion = "1.20.1";
            await content.SearchDownloadsCommand.ExecuteAsync(null);
        }
        UserControl view = kind == "mods" ? new ModsView { DataContext = mods } : new InstanceContentView { DataContext = content };
        var window = new Window { Content = view, Width = width, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var browser = Assert.Single(view.GetVisualDescendants().OfType<ResourceBrowserView>());
            Assert.Equal(kind == "mods" ? typeof(ModResourceBrowser) : typeof(InstanceResourceBrowser), browser.GetType());
            Assert.Equal(management, browser.IsTargetLocked);
            Assert.Same(kind == "mods" ? (object)mods : content, browser.DataContext);
            Assert.DoesNotContain(browser.GetVisualDescendants(), control => control is ModsView or InstanceContentView);
            var name = Named<TextBox>(browser, "NameSearchTextBox");
            var version = Named<WMCL.App.Controls.VersionPicker>(browser, "VersionSearchTextBox");
            var source = Named<ComboBox>(browser, "SourceSearchComboBox");
            var conditions = Named<WrapPanel>(browser, "SearchConditions");
            foreach (var field in new Control[] { name, version, source })
            {
                Assert.True(field.Bounds.Width >= 170);
                Assert.True(field.TranslatePoint(new Point(field.Bounds.Width, 0), view)!.Value.X <= view.Bounds.Width + .01);
            }
            if (width < 500)
                Assert.True(source.TranslatePoint(default, conditions)!.Value.Y > name.TranslatePoint(default, conditions)!.Value.Y);
            Assert.Equal("sample", name.Text);
            Assert.Equal("1.20.1", version.SelectedItem);
            var results = Named<ListBox>(browser, "SearchResultList");
            Assert.Equal(20, results.ItemCount);
            var originalResults = results.Items.Cast<object>().ToArray();
            Assert.Contains(browser.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "加载更多") && button.IsEffectivelyVisible);
            var originalStatus = kind == "mods" ? mods.ModStatus : content.DownloadStatus;
            var scroll = view.FindControl<ScrollViewer>("ResourceBrowserScroll")!;
            // 本用例验证滚动位置/结果保留；关闭无限滚动，避免测试里自动加载下一页改变结果集。
            WMCL.App.Animation.InfiniteScroll.SetCanLoadMore(scroll, false);
            scroll.Offset = new Vector(0, 180);
            await PumpAsync();
            var originalOffset = scroll.Offset;
            Capture(window, $"{kind}-{management}-{width}-results");
            results.SelectedIndex = 3;
            await PumpAsync();
            Assert.True(Named<StackPanel>(browser, "ResourceDetailPanel").IsVisible);
            Assert.True(Named<ItemsControl>(browser, "VersionGroups").IsEffectivelyVisible);
            Assert.Contains(browser.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "复制名称") && button.IsEffectivelyVisible);
            var target = Named<ComboBox>(browser, "TargetInstanceComboBox");
            Assert.Equal(!management, target.IsEffectivelyVisible);
            if (management)
            {
                target.SetCurrentValue(ComboBox.SelectedItemProperty, "Other");
                await PumpAsync();
                Assert.Equal("Source", kind == "mods" ? mods.SelectedModInstance : content.SelectedInstance);
                Assert.Equal("Source", Named<TextBlock>(browser, "LockedTargetText").Text);
            }
            if (kind != "mods")
                Assert.Equal(kind == "content-world" ? "世界" : kind == "content-shader" ? "光影" : "资源包", content.SelectedDownloadType);
            Capture(window, $"{kind}-{management}-{width}-detail");
            Named<Button>(browser, "ReturnToResultsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(250);
            await PumpAsync();
            Assert.False(Named<StackPanel>(browser, "ResourceDetailPanel").IsVisible);
            Assert.Same(results, Named<ListBox>(browser, "SearchResultList"));
            Assert.Equal(originalResults, results.Items.Cast<object>().ToArray());
            Assert.Equal(originalOffset.Y, scroll.Offset.Y, 1);
            Assert.Equal(originalStatus, kind == "mods" ? mods.ModStatus : content.DownloadStatus);
            Assert.Equal("sample", name.Text);
            Assert.Equal("1.20.1", version.SelectedItem);
            Assert.Equal("Source", kind == "mods" ? mods.SelectedModInstance : content.SelectedInstance);
            Assert.Equal(1, fixture.Handler.SearchRequests);
            if (kind == "mods") mods.IsInstanceManagement = !management;
            else content.IsDownloadPage = management;
            await PumpAsync();
            Assert.Same(browser, Assert.Single(view.GetVisualDescendants().OfType<ResourceBrowserView>()));
            Assert.Equal(!management, browser.IsTargetLocked);
            Assert.Equal(originalResults, results.Items.Cast<object>().ToArray());
            Assert.Equal(1, fixture.Handler.SearchRequests);
            Assert.All(fixture.Handler.Requests, request => Assert.True(request.Method == HttpMethod.Get));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadEntryCanChangeTargetButManagementEntryRejectsIt(bool isMod)
    {
        using var fixture = new Fixture();
        var mods = InstallTestData.CreateMods(fixture.Services);
        using var content = new InstanceContentViewModel(fixture.Services);
        mods.ModInstances.Add("Source");
        mods.ModInstances.Add("Other");
        content.Instances.Add("Source");
        content.Instances.Add("Other");
        if (isMod)
        {
            await mods.OpenManagementAsync("Source");
            mods.IsInstanceManagement = false;
        }
        else
        {
            await content.OpenManagementAsync("Source", "content-resource");
            content.IsDownloadPage = true;
        }
        UserControl view = isMod ? new ModsView { DataContext = mods } : new InstanceContentView { DataContext = content };
        var window = new Window { Content = view, Width = 420, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var browser = Assert.Single(view.GetVisualDescendants().OfType<ResourceBrowserView>());
            var target = Named<ComboBox>(browser, "TargetInstanceComboBox");
            target.SetCurrentValue(ComboBox.SelectedItemProperty, "Other");
            await PumpAsync();
            Assert.Equal("Other", isMod ? mods.SelectedModInstance : content.SelectedInstance);
            if (isMod) await mods.OpenManagementAsync("Source");
            else await content.OpenManagementAsync("Source", "content-resource");
            await PumpAsync();
            Assert.True(browser.IsTargetLocked);
            target.SetCurrentValue(ComboBox.SelectedItemProperty, "Other");
            await PumpAsync();
            Assert.Equal("Source", isMod ? mods.SelectedModInstance : content.SelectedInstance);
        }
        finally { window.Close(); }
    }

    private static TControl Named<TControl>(Control parent, string name) where TControl : Control =>
        Assert.Single(parent.GetVisualDescendants().OfType<TControl>(), control => control.Name == name);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
        }
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_RESOURCE_BROWSER_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(WMCL.Tests.TestEnvironment.Root, "resource-browser-" + Guid.NewGuid().ToString("N"));
        public FakeCatalog Handler { get; } = new();
        private HttpClient Http { get; }
        public AppServices Services { get; }

        public Fixture()
        {
            foreach (var id in new[] { "Source", "Other" })
            {
                var directory = Path.Combine(Root, "versions", id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"),
                    $$"""{"id":"{{id}}","clientVersion":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
                InstanceSettingsStore.Save(directory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });
            }
            Http = new HttpClient(Handler);
            Services = new AppServices(new AppSettings { GameDirectory = Root }, new ModrinthClient(Http, Http),
                new CurseForgeClient("fake-test-key", Http, Http), saveSettings: _ => { }, resourceDownloadHttp: Http);
        }

        public void Dispose()
        {
            Http.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class FakeCatalog : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public int SearchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            string response;
            if (path.EndsWith("/search", StringComparison.Ordinal))
            {
                SearchRequests++;
                response = path.StartsWith("/v1/", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new
                    {
                        data = Enumerable.Range(1, 20).Select(index => new { id = index, name = $"Sample {index}", slug = $"sample-{index}", summary = "Fake catalog resource", downloadCount = index }),
                        pagination = new { totalCount = 40 }
                    })
                    : JsonSerializer.Serialize(new
                    {
                        hits = Enumerable.Range(1, 20).Select(index => new { project_id = $"sample-{index}", slug = $"sample-{index}", title = $"Sample {index}", description = "Fake catalog resource", project_type = "mod", downloads = index, categories = new[] { "fabric" } }),
                        total_hits = 40
                    });
            }
            else if (path.EndsWith("/version", StringComparison.Ordinal)) response = "[]";
            else if (path.EndsWith("/files", StringComparison.Ordinal)) response = "{\"data\":[]}";
            else throw new InvalidOperationException("Unexpected browser request: " + request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
