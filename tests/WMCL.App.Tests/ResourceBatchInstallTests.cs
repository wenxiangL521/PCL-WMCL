using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WMCL.App.Services;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ResourceBatchInstallTests
{
    [Fact]
    public async Task MixedPlatformsAndTypesInstallToTheChosenInstanceAndSaveWithVisiblePackSkip()
    {
        using var fixture = new ResourceBatchFixture();
        var plan = await fixture.Services.ResourceBatch.BuildAsync(fixture.MixedResources, "first", "Save Two");
        Assert.Equal(9, plan.Artifacts.Length);
        Assert.Equal(2, plan.Artifacts.Count(item => item.Entry.IsDependency));
        Assert.Contains("独立实例", Assert.Single(plan.Skipped).Reason);
        Assert.Empty(fixture.BinaryRequests);
        var result = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.All(result.Outcomes, item => Assert.Equal("installed", item.State));
        var instance = Path.Combine(fixture.Root, "versions", "first");
        foreach (var artifact in plan.Artifacts.Where(item => item.WorldName is null))
            Assert.Equal(artifact.Entry.Sha1, Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(artifact.Destination))));
        Assert.True(File.Exists(Path.Combine(instance, "mods", "main-v.jar")));
        Assert.True(File.Exists(Path.Combine(instance, "mods", "cf10.jar")));
        Assert.True(File.Exists(Path.Combine(instance, "resourcepacks", "pack-v.zip")));
        Assert.True(File.Exists(Path.Combine(instance, "shaderpacks", "shader-v.zip")));
        Assert.True(File.Exists(Path.Combine(instance, "plugins", "plugin-v.jar")));
        Assert.True(File.Exists(Path.Combine(instance, "saves", "Save Two", "datapacks", "data-v.zip")));
        var world = Assert.Single(result.Outcomes, item => item.Artifact.WorldName is not null);
        Assert.Equal("world metadata", await File.ReadAllTextAsync(Path.Combine(world.Detail, "level.dat")));
        Assert.False(Directory.Exists(Path.Combine(instance, "saves", "Save One", "datapacks")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "mods")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "second", "mods")));
        var binaryCount = fixture.BinaryRequests.Count;
        await File.WriteAllTextAsync(Path.Combine(world.Detail, "user.txt"), "keep");
        var repeated = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.Equal(8, repeated.Outcomes.Count(item => item.State == "reused"));
        Assert.Equal(binaryCount + 1, fixture.BinaryRequests.Count);
        Assert.True(Directory.Exists(Path.Combine(instance, "saves", "Test Map (2)")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(world.Detail, "user.txt")));
    }

    [Fact]
    public async Task MissingRequiredDependencySkipsItsRootAndStillPlansIndependentResources()
    {
        using var fixture = new ResourceBatchFixture();
        fixture.MissingDependency = true;
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("main"), fixture.Resource("shader", "shader")], "first", null);
        Assert.Single(plan.Artifacts);
        Assert.Contains("必需", Assert.Single(plan.Skipped).Reason);
        var result = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.Equal("installed", Assert.Single(result.Outcomes).State);
        Assert.DoesNotContain(fixture.BinaryRequests, path => path.EndsWith(".jar", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "first", "mods")));
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("isolation")]
    [InlineData("root")]
    [InlineData("save")]
    public async Task ChangedTargetInvalidatesThePreviewBeforeAnyBinaryRequest(string change)
    {
        using var fixture = new ResourceBatchFixture();
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("data", "datapack")], "first", "Save One");
        if (change == "metadata") await File.AppendAllTextAsync(Path.Combine(fixture.Root, "versions", "first", "first.json"), " ");
        else if (change == "isolation") fixture.Services.Settings.IsolationMode = "global";
        else if (change == "root") fixture.Services.SetGameDirectory(Path.Combine(fixture.Root, "other"));
        else Directory.Delete(Path.Combine(fixture.Root, "versions", "first", "saves", "Save One"), recursive: true);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Services.ResourceBatch.InstallAsync(plan));
        Assert.Empty(fixture.BinaryRequests);
    }

    [Fact]
    public async Task ADeletedSaveDuringDownloadIsNotRecreatedByFileApplication()
    {
        using var fixture = new ResourceBatchFixture();
        var save = Path.Combine(fixture.Root, "versions", "first", "saves", "Save One");
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("data", "datapack")], "first", "Save One");
        fixture.OnBinaryRequest = () => Directory.Delete(save, recursive: true);
        var result = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.Equal("failed", Assert.Single(result.Outcomes).State);
        Assert.False(Directory.Exists(save));
    }

    [Fact]
    public async Task MissingSaveBlocksDatapackPlanningAndDualModeProjectSelectsDatapackFile()
    {
        using var fixture = new ResourceBatchFixture();
        fixture.AddModrinth("data", "newer-mod", "mod");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("data", "datapack")], "first", null));
        Assert.Empty(fixture.Requests);
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("data", "datapack")], "first", "Save One");
        Assert.Equal("data-v", Assert.Single(plan.Artifacts).Entry.VersionId);
    }

    [Fact]
    public async Task ConflictingPinnedDependenciesBlockTheCombinedPlanBeforeDownloading()
    {
        using var fixture = new ResourceBatchFixture();
        fixture.AddModrinth("dependency", "dep-two", "mod");
        fixture.AddModrinth("one", "one-v", "mod", [new("dependency-v", "dependency", "required")]);
        fixture.AddModrinth("two", "two-v", "mod", [new("dep-two", "dependency", "required")]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services.ResourceBatch.BuildAsync(
            [fixture.Resource("one"), fixture.Resource("two")], "first", null));
        Assert.Contains("冲突", error.Message);
        Assert.Empty(fixture.BinaryRequests);
    }

    [Fact]
    public async Task DownloadCancellationLeavesNoMainDependencyOrWorldAndReportsEveryItem()
    {
        using var fixture = new ResourceBatchFixture { DelayDownloads = true };
        var plan = await fixture.Services.ResourceBatch.BuildAsync(fixture.MixedResources, "first", "Save One");
        using var cancellation = new CancellationTokenSource();
        var installing = fixture.Services.ResourceBatch.InstallAsync(plan, ct: cancellation.Token);
        await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var result = await installing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(plan.Artifacts.Length, result.Outcomes.Length);
        Assert.All(result.Outcomes, item => Assert.Equal("canceled", item.State));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "first", "mods")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "first", "saves", "Test Map")));
    }

    [Fact]
    public async Task BadWorldArchiveReportsPartialFailureAndKeepsSuccessfullyInstalledFiles()
    {
        using var fixture = new ResourceBatchFixture();
        fixture.AddCurseForge(444, 40, "world", Encoding.UTF8.GetBytes("not a zip"));
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("pack", "resourcepack"), fixture.Resource("444", "world", "CurseForge")], "first", null);
        var result = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.Single(result.Outcomes, item => item.State == "installed");
        Assert.Single(result.Outcomes, item => item.State == "failed");
        Assert.Contains("失败 1", result.Summary);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "first", "saves", "Test Map")));
    }
}

internal sealed class ResourceBatchFixture : IDisposable
{
    private readonly Dictionary<string, ModrinthVersion> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<long, CurseForgeFile> _curseForge = [];
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HttpClient _http;
    private int _binaryCallbackCalled;
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "wmcl-batch-tests-" + Guid.NewGuid().ToString("N"));
    public AppServices Services { get; }
    public ConcurrentQueue<string> Requests { get; } = new();
    public IReadOnlyList<string> BinaryRequests => Requests.Where(path => path.EndsWith(".jar", StringComparison.Ordinal) || path.EndsWith(".zip", StringComparison.Ordinal)).ToArray();
    public bool MissingDependency { get; set; }
    public bool DelayDownloads { get; set; }
    public bool DelayMetadata { get; set; }
    public Action? OnBinaryRequest { get; set; }
    public TaskCompletionSource DownloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseDownloads { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource MetadataEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseMetadata { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public FavoriteResource[] MixedResources => [Resource("main"), Resource("222", "mod", "CurseForge"), Resource("pack", "resourcepack"),
        Resource("shader", "shader"), Resource("data", "datapack"), Resource("plugin", "plugin"), Resource("444", "world", "CurseForge"), Resource("pack-project", "modpack")];

    public ResourceBatchFixture()
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
            foreach (var save in new[] { "Save One", "Save Two" })
            {
                Directory.CreateDirectory(Path.Combine(directory, "saves", save));
                File.WriteAllText(Path.Combine(directory, "saves", save, "level.dat"), "existing save");
            }
        }
        AddModrinth("dependency", "dependency-v", "mod");
        AddModrinth("main", "main-v", "mod", [new("dependency-v", "dependency", "required")]);
        foreach (var (project, type) in new[] { ("pack", "resourcepack"), ("shader", "shader"), ("data", "datapack"), ("plugin", "plugin") })
            AddModrinth(project, project + "-v", type);
        AddCurseForge(222, 10, "mod", dependencies: [new(223, 3)]);
        AddCurseForge(223, 20, "mod");
        AddCurseForge(444, 40, "world", WorldZip());
        _http = new HttpClient(new Handler(RespondAsync));
        Services = new AppServices(new AppSettings { GameDirectory = Root, IsolationMode = "isolated" },
            new ModrinthClient(_http, _http), new CurseForgeClient("fixture", _http, _http), _ => { }, _http);
    }

    public FavoriteResource Resource(string project, string type = "mod", string platform = "Modrinth") =>
        new(platform, project, type, type == "world" ? "Test Map" : project, "Fixture", project);

    public void AddModrinth(string project, string id, string type, IReadOnlyList<ModrinthDependency>? dependencies = null)
    {
        var name = id + (type is "mod" or "plugin" ? ".jar" : ".zip");
        var bytes = Encoding.UTF8.GetBytes(project + "/" + id);
        _files["/" + name] = bytes;
        _versions[id] = new(id, project, project, id, "release", DateTimeOffset.UnixEpoch.AddSeconds(_versions.Count), ["1.20.1"],
            [type switch { "resourcepack" => "minecraft", "shader" => "iris", "plugin" => "paper", "mod" => "fabric", _ => type }],
            [new(name, "https://fixture.invalid/" + name, bytes.Length, Hash(bytes), true)], dependencies ?? []);
    }

    public void AddCurseForge(long project, long id, string type, byte[]? bytes = null, IReadOnlyList<CurseForgeDependency>? dependencies = null)
    {
        bytes ??= Encoding.UTF8.GetBytes(project + "/" + id);
        var name = "cf" + id + (type == "mod" ? ".jar" : ".zip");
        _files["/" + name] = bytes;
        _curseForge[id] = new(id, type, name, DateTimeOffset.UnixEpoch, bytes.Length, "https://fixture.invalid/" + name,
            Hash(bytes), ["1.20.1", "Fabric"], dependencies ?? [], ProjectId: project);
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue(path);
        if (_files.TryGetValue(path, out var bytes))
        {
            if (Interlocked.Exchange(ref _binaryCallbackCalled, 1) == 0) OnBinaryRequest?.Invoke();
            DownloadEntered.TrySetResult();
            if (DelayDownloads) await ReleaseDownloads.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
        if (DelayMetadata)
        {
            MetadataEntered.TrySetResult();
            await ReleaseMetadata.Task;
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 && parts[0] == "v2" && parts[1] == "project")
            return Json(_versions.Values.Where(item => item.ProjectId == parts[2]).Select(MrJson).ToArray());
        if (parts.Length == 3 && parts[0] == "v2" && parts[1] == "version" && _versions.TryGetValue(parts[2], out var version))
            return MissingDependency && parts[2] == "dependency-v" ? new(HttpStatusCode.NotFound) : Json(MrJson(version));
        if (parts.Length >= 4 && parts[0] == "v1" && parts[1] == "mods")
        {
            if (parts.Length == 5 && _curseForge.TryGetValue(long.Parse(parts[4]), out var file)) return Json(new { data = CfJson(file) });
            return Json(new { data = _curseForge.Values.Where(item => item.ProjectId == long.Parse(parts[2])).Select(CfJson).ToArray() });
        }
        return new(HttpStatusCode.NotFound);
    }

    private static object MrJson(ModrinthVersion version) => new
    {
        id = version.Id,
        project_id = version.ProjectId,
        name = version.Name,
        version_number = version.VersionNumber,
        version_type = version.VersionType,
        date_published = version.DatePublished,
        game_versions = version.GameVersions,
        loaders = version.Loaders,
        files = version.Files.Select(item => new { filename = item.Filename, url = item.Url, size = item.Size, primary = true, hashes = new { sha1 = item.Sha1 } }),
        dependencies = version.Dependencies.Select(item => new { version_id = item.VersionId, project_id = item.ProjectId, dependency_type = item.DependencyType })
    };

    private static object CfJson(CurseForgeFile file) => new
    {
        id = file.Id,
        modId = file.ProjectId,
        displayName = file.DisplayName,
        fileName = file.FileName,
        fileDate = file.FileDate,
        fileLength = file.Length,
        downloadUrl = file.DownloadUrl,
        gameVersions = file.GameVersions,
        hashes = new[] { new { algo = 1, value = file.Sha1 } },
        dependencies = file.Dependencies.Select(item => new { modId = item.ModId, relationType = item.RelationType })
    };

    private static byte[] WorldZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("Wrapper/level.dat").Open())) writer.Write("world metadata");
        return stream.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
