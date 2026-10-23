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
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ModDependencyInteractionTests
{
    [AvaloniaTheory]
    [InlineData("Modrinth")]
    [InlineData("CurseForge")]
    public async Task MissingRequiredDependencyIsVisibleAndDisablesInstall(string platform)
    {
        using var fixture = new Fixture { MissingDependency = true };
        using var main = new MainWindowViewModel(fixture.Services);
        await PrepareAsync(main.Mods, platform);
        Assert.True(main.Mods.HasModDependencyError);
        Assert.Contains("必需", main.Mods.ModDependencyStatus);
        Assert.False(main.Mods.CanInstallMod);
        var view = new ModsView { DataContext = main.Mods };
        var window = new Window { Content = view, Width = 850, Height = 700 };
        window.Show();
        try
        {
            await PumpAsync();
            var install = Button(view, "安装所选 Mod");
            await ScrollToAsync(install);
            Assert.False(install.IsEffectivelyEnabled);
            Click(window, install);
            Assert.DoesNotContain(fixture.Requests, path => path.EndsWith(".jar", StringComparison.Ordinal));
            Assert.True(Button(view, "重新检查前置").IsEffectivelyVisible);
            fixture.MissingDependency = false;
            var retry = Button(view, "重新检查前置");
            await ScrollToAsync(retry);
            Click(window, retry);
            await main.Mods.RetryModDependenciesCommand.ExecutionTask!;
            await PumpAsync();
            Assert.False(main.Mods.HasModDependencyError);
            Assert.True(main.Mods.CanInstallMod);
            Assert.Single(main.Mods.ModDependencies);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Modrinth", false)]
    [InlineData("Modrinth", true)]
    [InlineData("CurseForge", false)]
    [InlineData("CurseForge", true)]
    public async Task MouseInstallsOrCancelsTheFrozenPlanAndTaskCenterTracksIt(string platform, bool cancel)
    {
        using var fixture = new Fixture { DelayDownloads = true };
        using var main = new MainWindowViewModel(fixture.Services);
        var model = main.Mods;
        await PrepareAsync(model, platform);
        Assert.True(model.CanInstallMod);
        Assert.Null(model.SelectedGameVersion);
        Assert.Single(model.ModDependencies);
        var directory = model.ModsDirectory;
        var view = new ModsView { DataContext = model };
        var window = new Window { Content = view, Width = cancel ? 360 : 850, Height = 700 };
        window.Show();
        try
        {
            await PumpAsync();
            var install = Button(view, "安装所选 Mod");
            await ScrollToAsync(install);
            Click(window, install);
            await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.True(model.IsInstallingMod);
            Assert.False(model.CanInstallMod);
            Assert.True(main.IsTaskActive);
            var task = Assert.Single(main.Tasks.Tasks, task => task.Title == "安装 Mod 与必需前置");
            Assert.True(task.HasCancel);
            if (cancel)
            {
                var cancelButton = Button(view, "取消安装");
                await ScrollToAsync(cancelButton);
                Click(window, cancelButton);
            }
            else
            {
                model.SelectedModInstance = "second";
                await PumpAsync();
                fixture.ReleaseDownloads.TrySetResult();
            }
            await model.InstallModCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.False(model.IsInstallingMod);
            Assert.DoesNotContain(main.Tasks.Tasks, task => task.Title == "安装 Mod 与必需前置");
            if (cancel)
            {
                Assert.Contains("取消", model.ModStatus);
                Assert.False(Directory.Exists(directory));
            }
            else
            {
                Assert.Contains("已安装", model.ModStatus);
                Assert.Equal(Fixture.Payload, await File.ReadAllBytesAsync(Path.Combine(directory, "main.jar")));
                Assert.Equal(Fixture.Payload, await File.ReadAllBytesAsync(Path.Combine(directory, "dependency.jar")));
                Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "second", "mods")));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("version")]
    [InlineData("platform")]
    [InlineData("target")]
    public async Task LateDependencyResponseCannotReplaceANewerSelection(string change)
    {
        using var fixture = new Fixture { DelayDependency = true };
        using var main = new MainWindowViewModel(fixture.Services);
        var model = main.Mods;
        model.ModInstances.Add("first");
        model.SelectedModInstance = "first";
        await PumpAsync();
        var opening = model.OpenFavoriteAsync(Resource("Modrinth"));
        await fixture.DependencyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsResolvingModDependencies);
        Assert.False(model.CanInstallMod);
        if (change == "version") model.SelectedModVersion = model.ModVersions.Single(version => version.Modrinth!.Id == "standalone");
        else if (change == "platform") model.SelectedModSource = "CurseForge";
        else model.SelectedModInstance = null;
        fixture.ReleaseDependency.TrySetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(5));
        await PumpAsync();
        Assert.False(model.HasModDependencyError);
        Assert.False(model.IsResolvingModDependencies);
        Assert.Empty(model.ModDependencies);
        Assert.Equal(change == "version", model.CanInstallMod);
    }

    private static async Task PrepareAsync(ModsViewModel model, string platform)
    {
        model.SetMode("mods");
        model.ModInstances.Add("first");
        model.ModInstances.Add("second");
        model.SelectedModInstance = "first";
        await PumpAsync();
        await model.OpenFavoriteAsync(Resource(platform));
    }

    private static FavoriteResource Resource(string platform) => new(platform, "111", "mod", "Example Mod", "Fixture", "example");

    private sealed class Fixture : IDisposable
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("Mod dependency fixture");
        private readonly HttpClient _http;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wmcl-dependency-ui-" + Guid.NewGuid().ToString("N"));
        public AppServices Services { get; }
        public ConcurrentQueue<string> Requests { get; } = new();
        public bool MissingDependency { get; set; }
        public bool DelayDownloads { get; set; }
        public bool DelayDependency { get; set; }
        public TaskCompletionSource DownloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownloads { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DependencyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDependency { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Fixture()
        {
            foreach (var id in new[] { "first", "second" })
            {
                var directory = Path.Combine(Root, "versions", id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new
                {
                    id,
                    clientVersion = "1.20.1",
                    mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    libraries = Array.Empty<object>()
                }));
            }
            _http = new HttpClient(new Handler(RespondAsync));
            Services = new AppServices(new AppSettings { GameDirectory = Root, IsolationMode = "isolated" },
                new ModrinthClient(_http, _http), new CurseForgeClient("fixture", _http, _http), _ => { });
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(path);
            if (path.EndsWith(".jar", StringComparison.Ordinal))
            {
                DownloadEntered.TrySetResult();
                if (DelayDownloads) await ReleaseDownloads.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            }
            if (path == "/v2/project/111/version") return Json(new[] { MrVersion("main", true), MrVersion("standalone", false) });
            if (path == "/v2/version/dependency")
            {
                DependencyEntered.TrySetResult();
                if (DelayDependency) await ReleaseDependency.Task;
                return MissingDependency || DelayDependency ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(MrVersion("dependency", false));
            }
            if (path == "/v1/mods/111/files") return Json(new { data = new[] { CfFile(10, 111, true) } });
            if (path == "/v1/mods/222/files") return Json(new { data = MissingDependency ? Array.Empty<object>() : new[] { CfFile(20, 222, false) } });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static object MrVersion(string id, bool depends) => new
        {
            id,
            project_id = id == "dependency" ? "222" : "111",
            name = id == "dependency" ? "Example API" : "Example Mod",
            version_number = id,
            version_type = "release",
            date_published = depends ? DateTimeOffset.UnixEpoch.AddDays(1) : DateTimeOffset.UnixEpoch,
            game_versions = new[] { "1.20.1" },
            loaders = new[] { "fabric" },
            files = new[] { new { filename = id + ".jar", url = "https://fixture.invalid/" + id + ".jar", size = Payload.Length, primary = true,
                hashes = new { sha1 = Convert.ToHexStringLower(SHA1.HashData(Payload)) } } },
            dependencies = depends ? new[] { new { project_id = "222", version_id = "dependency", dependency_type = "required" } } : []
        };

        private static object CfFile(long id, long project, bool depends) => new
        {
            id,
            modId = project,
            displayName = depends ? "Example Mod" : "Example API",
            fileName = depends ? "main.jar" : "dependency.jar",
            fileDate = DateTimeOffset.UnixEpoch,
            fileLength = Payload.Length,
            downloadUrl = "https://fixture.invalid/" + (depends ? "main.jar" : "dependency.jar"),
            gameVersions = new[] { "1.20.1", "Fabric" },
            hashes = new[] { new { algo = 1, value = Convert.ToHexStringLower(SHA1.HashData(Payload)) } },
            dependencies = depends ? new[] { new { modId = 222, relationType = 3 } } : []
        };

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

        public void Dispose()
        {
            ReleaseDependency.TrySetResult();
            ReleaseDownloads.TrySetResult();
            _http.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private static Button Button(Control view, string content) => view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));

    private static async Task ScrollToAsync(Control control)
    {
        await PumpAsync();
        var scroll = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var position = control.TranslatePoint(default, scroll)!.Value;
        scroll.Offset = new Vector(0, scroll.Offset.Y + position.Y - scroll.Viewport.Height * 0.4);
        await PumpAsync();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width);
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
