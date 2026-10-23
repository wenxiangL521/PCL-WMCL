using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;
using static WMCL.App.Tests.ResourceFavoritesTests;

namespace WMCL.App.Tests;

public sealed class ResourceFavoritesInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 520)]
    public async Task MouseCanFavoriteSearchOpenAndRemoveResource(string theme, int width)
    {
        ThemeService.Apply(theme);
        var settings = new AppSettings();
        var saves = 0;
        var favorites = new ResourceFavoritesService(settings, () => saves++);
        var pickerModel = new ResourceFavoritePickerViewModel(favorites) { Resource = Resource() };
        var picker = new ResourceFavoritePicker { DataContext = pickerModel };
        FavoriteResource? opened = null;
        var model = new ResourceFavoritesViewModel(favorites, resource =>
        {
            opened = resource;
            return Task.CompletedTask;
        }, (_, _) => Task.FromResult(true));
        var view = new ResourceFavoritesView { DataContext = model };
        var host = new ContentControl { Content = picker };
        var window = new Window { Content = host, Width = width, Height = 720 };
        window.Show();
        try
        {
            await PumpAsync();
            Click(window, picker.FindControl<Button>("ToggleFavoriteButton")!);
            await PumpAsync();
            Assert.True(pickerModel.IsFavorite);
            Assert.Equal(1, saves);
            host.Content = view;
            await PumpAsync();
            var search = view.FindControl<TextBox>("FavoriteSearchBox")!;
            search.Focus();
            Input(window, "TextInput", "no-match");
            await PumpAsync();
            Assert.Empty(model.Rows);
            search.Text = "111";
            await PumpAsync();
            var row = view.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("resourceRow"));
            Assert.InRange(row.TranslatePoint(new Point(row.Bounds.Width, 0), window)!.Value.X, 0, width);
            Click(window, row);
            await PumpAsync();
            Assert.Equal(Resource(), opened);
            var check = view.GetVisualDescendants().OfType<CheckBox>().Single();
            Click(window, check);
            await PumpAsync();
            Assert.Equal(1, model.SelectedCount);
            Click(window, view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "移除收藏")));
            await model.RemoveSelectedCommand.ExecutionTask!;
            await PumpAsync();
            Assert.Empty(model.Rows);
            Assert.False(pickerModel.IsFavorite);
            Click(window, view.FindControl<Button>("ManageFoldersButton")!);
            await PumpAsync();
            var name = view.FindControl<TextBox>("FolderNameBox")!;
            name.Text = "";
            name.Focus();
            Input(window, "TextInput", "Review");
            Click(window, view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "新建")));
            await PumpAsync();
            Assert.Equal(2, model.Folders.Count);
            Assert.Equal("Review", model.SelectedFolder!.Name);
            Click(window, view.FindControl<Button>("ManageFoldersButton")!);
            await PumpAsync();
            Assert.False(model.IsManaging);
        }
        finally
        {
            window.Close();
            ThemeService.Apply("light");
        }
    }

    [AvaloniaTheory]
    [InlineData("mod", "mods")]
    [InlineData("modpack", "modpack")]
    [InlineData("resourcepack", "content-resource")]
    [InlineData("shader", "content-shader")]
    [InlineData("datapack", "content-datapack")]
    [InlineData("world", "content-world")]
    [InlineData("plugin", "content-resource")]
    public async Task FavoritesNavigateToMatchingDownloadPageAndKeepIdentity(string type, string route)
    {
        using var fixture = new Fixture();
        using var main = new MainWindowViewModel(fixture.Services);
        main.Modpack.GameVersion = "1.20.1";
        var resource = Resource("CurseForge", "111", type);
        await main.Favorites.OpenCommand.ExecuteAsync(resource);
        Assert.Equal(route, main.SelectedSubTab!.Key);
        Assert.Equal(main.SelectedSubTab.Title, main.CurrentTitle);
        var actual = type switch
        {
            "mod" => main.Mods.Favorite.Resource,
            "modpack" => main.Modpack.Favorite.Resource,
            _ => main.Content.Favorite.Resource
        };
        Assert.Equal(resource, actual);
        if (type == "world") Assert.True(main.Content.IsWorldMode);
        if (type == "plugin") Assert.True(main.Content.IsPluginDownload);
        main.SelectedSubTab = main.SubTabs.Single(tab => tab.Key == "favorites");
        Assert.Same(main.Favorites, main.CurrentPage);
    }

    [AvaloniaFact]
    public async Task ReopenedResourceUsesSelectedVersionForActualDownload()
    {
        using var fixture = new Fixture();
        var model = new InstanceContentViewModel(fixture.Services);
        await model.LoadAsync();
        await model.OpenFavoriteAsync(Resource(type: "resourcepack"));
        Assert.Equal(2, model.ResourceVersions.Count);
        var older = model.ResourceVersions.Single(version => version.Modrinth!.Id == "old");
        model.SelectedResourceVersion = older;
        await model.InstallDownloadCommand.ExecuteAsync(null);
        Assert.Contains("已安装", model.DownloadStatus);
        var path = Path.Combine(fixture.Root, "resourcepacks", "old.zip");
        Assert.Equal(Fixture.Payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "resourcepacks", "new.zip")));
        Assert.Contains(fixture.Requests, uri => uri.AbsolutePath == "/old.zip");
        Assert.DoesNotContain(fixture.Requests, uri => uri.AbsolutePath == "/new.zip");
    }

    [AvaloniaTheory]
    [InlineData("mod")]
    [InlineData("resourcepack")]
    public async Task OpeningFavoriteScrollsVersionSelectorIntoViewBeforeAndAfterAttachment(string type)
    {
        using var fixture = new Fixture();
        var content = new InstanceContentViewModel(fixture.Services) { IsDownloadPage = true };
        var mods = InstallTestData.CreateMods(fixture.Services);
        UserControl view;
        Func<Task> open;
        object groups;
        if (type == "mod")
        {
            mods.SelectedModInstance = "fabric-target";
            await PumpAsync();
            view = new ModsView { DataContext = mods };
            open = () => mods.OpenFavoriteAsync(Resource());
            groups = mods.ModVersionGroups;
        }
        else
        {
            await content.LoadAsync();
            view = new InstanceContentView { DataContext = content };
            open = () => content.OpenFavoriteAsync(Resource(type: "resourcepack"));
            groups = content.ResourceVersionGroups;
        }
        await open();
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync();
            var picker = view.GetVisualDescendants().OfType<ItemsControl>()
                .Single(box => box.Name == "VersionGroups" && ReferenceEquals(box.ItemsSource, groups));
            var scroll = picker.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Offset.Y > 0);
            AssertScrolledIntoView(window, picker);
            scroll.Offset = default;
            await open();
            await PumpAsync();
            Assert.True(scroll.Offset.Y > 0);
            AssertScrolledIntoView(window, picker);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GroupedVersionHeaderOnlyTogglesAndRowsSelectTheVersion()
    {
        using var fixture = new Fixture();
        var mods = InstallTestData.CreateMods(fixture.Services);
        mods.SelectedModInstance = "fabric-target";
        await PumpAsync();
        await mods.OpenFavoriteAsync(Resource());
        var group = Assert.Single(mods.ModVersionGroups);
        Assert.Equal("1.20.1", group.Title);
        Assert.Equal(2, group.Entries.Count);
        var selected = mods.SelectedModVersion;
        var view = new ModsView { DataContext = mods };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync();
            var card = view.GetVisualDescendants().OfType<ResourceVersionGroupCard>().Single();
            var header = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("versionHeader"));
            header.BringIntoView();
            await PumpAsync();
            Assert.True(header.IsEffectivelyEnabled, "version group header should be enabled");
            Assert.True(header.Bounds.Width > 100);
            Click(window, header);
            await PumpAsync();
            Assert.False(group.IsExpanded);
            Assert.Same(selected, mods.SelectedModVersion);

            Click(window, header);
            await PumpAsync();
            Assert.True(group.IsExpanded);
            var rows = card.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("resourceVersionRow")).ToList();
            Assert.Equal(2, rows.Count);
            Click(window, rows[1]);
            await PumpAsync();
            Assert.NotSame(selected, mods.SelectedModVersion);
            Assert.True(group.Entries.Single(entry => ReferenceEquals(entry.Source, mods.SelectedModVersion)).IsSelected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TargetSelectionAndGameFilterReloadFavoriteVersions()
    {
        using var fixture = new Fixture();
        var content = new InstanceContentViewModel(fixture.Services);
        await content.OpenFavoriteAsync(Resource(type: "shader"));
        // PCL2 detail browses versions even without an install target; installation stays disabled.
        Assert.Equal(2, content.ResourceVersions.Count);
        Assert.False(content.CanDownload);
        await content.LoadAsync();
        await PumpAsync();
        Assert.Equal(2, content.ResourceVersions.Count);
        var mods = InstallTestData.CreateMods(fixture.Services);
        await mods.OpenFavoriteAsync(Resource());
        Assert.Equal(2, mods.ModVersions.Count);
        Assert.False(mods.CanInstallMod);
        mods.SelectedModInstance = "fabric-target";
        await PumpAsync();
        Assert.Equal(2, mods.ModVersions.Count);
        var packs = new ModpackViewModel(fixture.Services) { GameVersion = "1.20.1" };
        await packs.OpenFavoriteAsync(Resource(type: "modpack"));
        Assert.Equal(2, packs.ModpackVersions.Count);
        packs.GameVersion = "1.21.1";
        await PumpAsync();
        Assert.Empty(packs.ModpackVersions);
    }

    [AvaloniaTheory]
    [InlineData("mod")]
    [InlineData("modpack")]
    [InlineData("resourcepack")]
    public async Task ChangingSourceDuringSearchCannotSaveOldResultsUnderNewSource(string type)
    {
        using var fixture = new Fixture();
        fixture.DelaySearch = true;
        var mods = InstallTestData.CreateMods(fixture.Services);
        var packs = new ModpackViewModel(fixture.Services);
        var content = new InstanceContentViewModel(fixture.Services);
        Task search;
        if (type == "mod")
        {
            mods.SelectedModInstance = "fabric-target";
            await PumpAsync();
            search = mods.SearchModsCommand.ExecuteAsync(null);
        }
        else if (type == "modpack") search = packs.SearchCommand.ExecuteAsync(null);
        else
        {
            await content.LoadAsync();
            search = content.SearchDownloadsCommand.ExecuteAsync(null);
        }
        await fixture.SearchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (type == "mod") mods.SelectedModSource = "CurseForge";
        else if (type == "modpack") packs.SelectedPlatform = "CurseForge";
        else content.SelectedPlatform = "CurseForge";
        fixture.ReleaseSearch.SetResult();
        await search.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(mods.SearchResults);
        Assert.Empty(packs.Results);
        Assert.Empty(content.DownloadResults);
        Assert.Null(mods.Favorite.Resource);
        Assert.Null(packs.Favorite.Resource);
        Assert.Null(content.Favorite.Resource);
    }

    [AvaloniaFact]
    public async Task ClearingOrLosingTargetCannotReuseItsPreviousInstallationDirectory()
    {
        using var fixture = new Fixture();
        var content = new InstanceContentViewModel(fixture.Services);
        await content.LoadAsync();
        await content.OpenFavoriteAsync(Resource(type: "resourcepack"));
        Assert.True(content.CanDownload);
        content.SelectedInstance = null;
        Assert.False(content.CanDownload);
        Assert.Empty(content.ResourceVersions);
        Assert.Equal("", content.GameDirectory);
        content.SelectedInstance = "missing";
        await PumpAsync();
        Assert.False(content.CanDownload);
        Assert.Equal("", content.GameDirectory);
        var mods = InstallTestData.CreateMods(fixture.Services);
        mods.SelectedModInstance = "fabric-target";
        await PumpAsync();
        await mods.OpenFavoriteAsync(Resource());
        Assert.True(mods.CanInstallMod);
        mods.SelectedModInstance = null;
        Assert.False(mods.CanInstallMod);
        Assert.Empty(mods.ModVersions);
        Assert.Equal("", mods.ModsDirectory);
        mods.SelectedModInstance = "missing";
        await PumpAsync();
        Assert.False(mods.CanInstallMod);
        Assert.Equal("", mods.ModsDirectory);
    }

    [AvaloniaFact]
    public async Task ResourceFileCanBeSavedWithoutAnInstallTarget()
    {
        using var fixture = new Fixture();
        Directory.Delete(Path.Combine(fixture.Root, "versions"), true);
        var content = new InstanceContentViewModel(fixture.Services);
        await content.LoadAsync();
        await content.OpenFavoriteAsync(Resource(type: "resourcepack"));
        Assert.Null(content.SelectedInstance);
        Assert.False(content.CanDownload);
        Assert.True(content.CanSaveDownload);
        var destination = Path.Combine(fixture.Root, "saved-resource.zip");
        await content.SaveDownloadAsync(destination);
        Assert.True(File.Exists(destination));
        Assert.Equal(Fixture.Payload, await File.ReadAllBytesAsync(destination));
        Assert.Contains("已保存", content.DownloadStatus);
    }

    [AvaloniaFact]
    public async Task CancellingResourceInstallAbortsTheHttpDownloadWithoutWriting()
    {
        using var fixture = new Fixture { DelayDownload = true };
        var content = new InstanceContentViewModel(fixture.Services);
        await content.LoadAsync();
        await content.OpenFavoriteAsync(Resource(type: "resourcepack"));
        var install = content.InstallDownloadCommand.ExecuteAsync(null);
        await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(content.IsDownloadBusy);
        Assert.True(content.CanCancelDownload);
        content.CancelDownloadCommand.Execute(null);
        fixture.ReleaseDownload.TrySetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(content.IsDownloadBusy);
        Assert.False(content.CanCancelDownload);
        Assert.Contains("已取消", content.DownloadStatus);
        var directory = Path.Combine(fixture.Root, "resourcepacks");
        Assert.False(Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any());
    }

    [AvaloniaFact]
    public async Task ChangingResourceTypeInvalidatesSearchAndSelectedProject()
    {
        using var fixture = new Fixture();
        fixture.DelaySearch = true;
        var content = new InstanceContentViewModel(fixture.Services);
        await content.LoadAsync();
        await content.OpenFavoriteAsync(Resource(type: "resourcepack"));
        var search = content.SearchDownloadsCommand.ExecuteAsync(null);
        await fixture.SearchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.SelectedDownloadType = "数据包";
        Assert.Null(content.Favorite.Resource);
        Assert.Null(content.SelectedDownloadProject);
        fixture.ReleaseSearch.SetResult();
        await search.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(content.DownloadResults);
        Assert.Empty(content.ResourceVersions);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("Resource file fixture");
        private readonly HttpClient _http;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wmcl-favorites-" + Guid.NewGuid().ToString("N"));
        public AppServices Services { get; }
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public bool DelaySearch { get; set; }
        public TaskCompletionSource SearchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSearch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DelayDownload { get; set; }
        public TaskCompletionSource DownloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Fixture()
        {
            foreach (var id in new[] { "1.20.1", "fabric-target" })
            {
                var directory = Path.Combine(Root, "versions", id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new
                {
                    id,
                    clientVersion = "1.20.1",
                    mainClass = id == "1.20.1" ? "net.minecraft.client.main.Main" : "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    libraries = Array.Empty<object>()
                }));
            }
            _http = new HttpClient(new Handler(RespondAsync));
            Services = new AppServices(new AppSettings { GameDirectory = Root, IsolationMode = "global" },
                new ModrinthClient(_http, _http), new CurseForgeClient("fixture-key", _http), _ => { });
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith("/mods/search", StringComparison.Ordinal))
            {
                SearchEntered.TrySetResult();
                if (DelaySearch) await ReleaseSearch.Task.WaitAsync(ct);
                return Json(new { hits = new[] { new { project_id = "111", slug = "test-project", title = "Old search", description = "old", project_type = "mod" } }, total_hits = 1 });
            }
            if (uri.AbsolutePath.EndsWith("/version", StringComparison.Ordinal))
                return Json(new[] { Version("new", "2026-01-01T00:00:00Z"), Version("old", "2025-01-01T00:00:00Z") });
            if (uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal))
                return Json(new { data = new[] { new { id = 222, modId = 111, displayName = "CF file", fileName = "cf.zip", fileDate = "2026-01-01T00:00:00Z", gameVersions = new[] { "1.20.1" } } } });
            if (uri.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                DownloadEntered.TrySetResult();
                if (DelayDownload) await ReleaseDownload.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static object Version(string id, string date) => new
        {
            id,
            project_id = "111",
            name = id,
            version_number = id,
            version_type = "release",
            date_published = date,
            game_versions = new[] { "1.20.1" },
            loaders = new[] { "fabric" },
            dependencies = Array.Empty<object>(),
            files = new[] { new { filename = id + ".zip", url = "https://example.invalid/" + id + ".zip", size = Payload.Length, primary = true,
                hashes = new { sha1 = Convert.ToHexStringLower(SHA1.HashData(Payload)) } } }
        };

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

        public void Dispose()
        {
            ReleaseSearch.TrySetResult();
            _http.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private static void AssertScrolledIntoView(Window window, Control control)
    {
        var top = control.TranslatePoint(default, window)!.Value.Y;
        var probe = control.TranslatePoint(
            new Point(0, Math.Min(40, control.Bounds.Height)), window)!.Value.Y;
        Assert.InRange(top, 0, window.Bounds.Height - 1);
        Assert.InRange(probe, 0, window.Bounds.Height);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.Y, 0, window.Bounds.Height);
        Input(window, "MouseMove", point, RawInputModifiers.None);
        Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Input(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }
}
