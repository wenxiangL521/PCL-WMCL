using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed partial class ModUpdateTargetTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDependencyRejectsBothUpdateCommandsBeforeDeletingOldFiles(bool selected)
    {
        using var fixture = new Fixture { MissingDependency = true };
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        Assert.True(model.CanUpdateAllMods);
        await (selected ? model.UpdateSelectedModCommand : model.UpdateAllModsCommand).ExecuteAsync(null);
        Assert.Contains("依赖", model.UpdateStatus);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
        Assert.Null(fixture.Services.GameOperations.GetActiveOperation(fixture.Root));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedFileAfterCheckIsPreservedByBothUpdateCommands(bool selected)
    {
        using var fixture = new Fixture();
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        File.WriteAllText(fixture.OldFile(fixture.Root, "first"), "user replaced the old file");
        await (selected ? model.UpdateSelectedModCommand : model.UpdateAllModsCommand).ExecuteAsync(null);
        Assert.Contains("重新检查", model.UpdateStatus);
        Assert.Equal("user replaced the old file", File.ReadAllText(fixture.OldFile(fixture.Root, "first")));
        Assert.False(File.Exists(Path.Combine(fixture.Directory(fixture.Root, "first"), "new.jar")));
        Assert.Equal(0, fixture.Downloads);
    }

    [AvaloniaTheory]
    [InlineData(HttpStatusCode.NotFound, "未识别")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "检查失败")]
    [InlineData(null, "无兼容版本")]
    public async Task UnknownFailedAndIncompatibleChecksDoNotClaimEverythingIsLatest(HttpStatusCode? hashStatus, string expected)
    {
        using var fixture = new Fixture { HashStatus = hashStatus, EmptyCatalog = hashStatus is null };
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        Assert.Contains(expected, model.UpdateStatus);
        Assert.DoesNotContain("均为最新", model.UpdateStatus);
        Assert.False(model.CanUpdateAllMods);
        Assert.False(model.CanUpdateSelectedMod);
        fixture.AssertOriginalFiles();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingInstanceInvalidatesCompletedUpdatesAndBothUpdateCommands(bool selected)
    {
        using var fixture = new Fixture();
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        Assert.True(model.CanUpdateAllMods);
        Assert.True(model.CanUpdateSelectedMod);
        await model.SelectModInstanceAsync("second");
        Assert.False(model.CanUpdateAllMods);
        Assert.False(model.CanUpdateSelectedMod);
        Assert.All(model.LocalMods, mod => Assert.Null(mod.UpdateVersion));
        await (selected ? model.UpdateSelectedModCommand : model.UpdateAllModsCommand).ExecuteAsync(null);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
    }

    [AvaloniaTheory]
    [InlineData("instance")]
    [InlineData("root")]
    [InlineData("empty")]
    public async Task LateCheckResponseCannotRestoreAnObsoleteTarget(string change)
    {
        using var fixture = new Fixture { DelayCheck = true };
        var model = await fixture.CreateModelAsync();
        var checking = model.CheckModUpdatesCommand.ExecuteAsync(null);
        await fixture.CheckEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "root") fixture.Services.SetGameDirectory(fixture.OtherRoot);
        else if (change == "empty") model.SelectedModInstance = null;
        else await model.SelectModInstanceAsync("second");
        fixture.ReleaseCheck.TrySetResult();
        await checking.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsCheckingUpdates);
        Assert.False(model.CanUpdateAllMods);
        Assert.False(model.CanUpdateSelectedMod);
        Assert.Equal("", model.UpdateStatus);
        Assert.All(model.LocalMods, mod => Assert.Null(mod.UpdateVersion));
        await model.UpdateAllModsCommand.ExecuteAsync(null);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingDuringDownloadPreservesTheFrozenFilesAndDoesNotPublishIntoTheNewTarget(bool selected)
    {
        using var fixture = new Fixture { DelayDownload = true };
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        var updating = (selected ? model.UpdateSelectedModCommand : model.UpdateAllModsCommand).ExecuteAsync(null);
        await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("更新 Mod", fixture.Services.GameOperations.GetActiveOperation(fixture.Root));
        fixture.Services.SetGameDirectory(fixture.OtherRoot);
        await model.SelectModInstanceAsync("second");
        fixture.ReleaseDownload.TrySetResult();
        await updating.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(fixture.OldFile(fixture.Root, "first")));
        Assert.Equal(Fixture.Payload, await File.ReadAllBytesAsync(Path.Combine(fixture.Directory(fixture.Root, "first"), "new.jar")));
        Assert.Equal("old second", await File.ReadAllTextAsync(fixture.OldFile(fixture.OtherRoot, "second")));
        Assert.False(File.Exists(Path.Combine(model.ModsDirectory, "new.jar")));
        Assert.False(model.CanUpdateAllMods);
        Assert.False(model.CanUpdateSelectedMod);
        Assert.Equal("", model.UpdateStatus);
        Assert.Equal("old.jar", Assert.Single(model.LocalMods).FileName);
        Assert.Null(fixture.Services.GameOperations.GetActiveOperation(fixture.Root));
    }

    [AvaloniaFact]
    public async Task GameDirectoryLeaseRejectsUpdatesBeforeDownloadingOrDeleting()
    {
        using var fixture = new Fixture();
        var model = await fixture.CreateModelAsync();
        await model.CheckModUpdatesCommand.ExecuteAsync(null);
        using var operation = fixture.Services.GameOperations.Acquire(fixture.Root, "运行游戏 first");
        await model.UpdateAllModsCommand.ExecuteAsync(null);
        Assert.Contains("运行游戏 first", model.UpdateStatus);
        fixture.AssertOriginalFiles();
        Assert.Equal(0, fixture.Downloads);
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("updated mod fixture");
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wmcl-mod-update-" + Guid.NewGuid().ToString("N"));
        public string OtherRoot => Path.Combine(Root, "other-root");
        public AppServices Services { get; }
        public bool DelayCheck { get; init; }
        public bool DelayDownload { get; init; }
        public bool MissingDependency { get; init; }
        public bool EmptyCatalog { get; init; }
        public HttpStatusCode? HashStatus { get; init; }
        public Func<string, string, Task<bool>>? ConfirmUpdate { get; init; }
        public bool FailRecycling { get; init; }
        public bool FailSavingSettings { get; init; }
        public int Confirmations { get; private set; }
        public int SettingsSaves { get; private set; }
        public string LastWarning { get; private set; } = "";
        public string RecycledDirectory => Path.Combine(Root, "recycled");
        public int Downloads { get; private set; }
        public TaskCompletionSource CheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DownloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HttpClient _http;

        public Fixture()
        {
            foreach (var root in new[] { Root, OtherRoot })
                foreach (var id in new[] { "first", "second" })
                {
                    System.IO.Directory.CreateDirectory(Directory(root, id));
                    File.WriteAllText(Path.Combine(root, "versions", id, id + ".json"), JsonSerializer.Serialize(new
                    {
                        id,
                        clientVersion = "1.20.1",
                        mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                        libraries = Array.Empty<object>()
                    }));
                    File.WriteAllText(OldFile(root, id), "old " + id);
                }
            _http = new HttpClient(new Handler(RespondAsync));
            Services = new AppServices(new AppSettings { GameDirectory = Root, IsolationMode = "isolated" },
                new ModrinthClient(_http, _http), saveSettings: _ =>
                {
                    SettingsSaves++;
                    if (FailSavingSettings) throw new IOException("fixture settings failure");
                }, recycleModBackup: directory =>
                {
                    if (FailRecycling) throw new IOException("fixture recycle failure");
                    System.IO.Directory.CreateDirectory(RecycledDirectory);
                    System.IO.Directory.Move(directory, Path.Combine(RecycledDirectory, Guid.NewGuid().ToString("N")));
                });
        }

        public async Task<ModsViewModel> CreateModelAsync()
        {
            var model = new ModsViewModel(Services, null, null, null, confirmModUpdate: async (title, message) =>
            {
                Confirmations++;
                LastWarning = message;
                return ConfirmUpdate is null || await ConfirmUpdate(title, message);
            });
            model.ModInstances.Add("first");
            model.ModInstances.Add("second");
            await model.SelectModInstanceAsync("first");
            Assert.Equal(Directory(Root, "first"), model.ModsDirectory);
            return model;
        }

        public string Directory(string root, string id) => Path.Combine(root, "versions", id, "mods");
        public string OldFile(string root, string id) => Path.Combine(Directory(root, id), "old.jar");

        public void AssertOriginalFiles()
        {
            foreach (var root in new[] { Root, OtherRoot })
                foreach (var id in new[] { "first", "second" })
                {
                    Assert.Equal("old " + id, File.ReadAllText(OldFile(root, id)));
                    Assert.False(File.Exists(Path.Combine(Directory(root, id), "new.jar")));
                }
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("new.jar", StringComparison.Ordinal))
            {
                Downloads++;
                DownloadEntered.TrySetResult();
                if (DelayDownload) await ReleaseDownload.Task.WaitAsync(ct);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            }
            if (path.Contains("/version_file/", StringComparison.Ordinal))
                return HashStatus is { } status ? new(status) : Json(Version("old"));
            if (path.EndsWith("/project/project/version", StringComparison.Ordinal))
            {
                CheckEntered.TrySetResult();
                if (DelayCheck) await ReleaseCheck.Task;
                return EmptyCatalog ? Json(Array.Empty<object>()) : Json(new[] { Version("new") });
            }
            return new(HttpStatusCode.NotFound);
        }

        private object Version(string id) => new
        {
            id,
            project_id = "project",
            name = "Example Mod",
            version_number = id,
            version_type = "release",
            date_published = DateTimeOffset.UnixEpoch,
            game_versions = new[] { "1.20.1" },
            loaders = new[] { "fabric" },
            files = new[] { new { filename = id + ".jar", url = "https://fixture.invalid/" + id + ".jar",
                size = Payload.Length, primary = true, hashes = new { sha1 = Convert.ToHexStringLower(SHA1.HashData(Payload)) } } },
            dependencies = MissingDependency && id == "new"
                ? new object[] { new { project_id = "missing", dependency_type = "required" } } : Array.Empty<object>()
        };

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value))
        };

        public void Dispose()
        {
            ReleaseCheck.TrySetResult();
            ReleaseDownload.TrySetResult();
            _http.Dispose();
            if (System.IO.Directory.Exists(Root)) System.IO.Directory.Delete(Root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
