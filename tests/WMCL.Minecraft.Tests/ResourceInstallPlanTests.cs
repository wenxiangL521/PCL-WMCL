using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;
using WMCL.Modding.Resources;

namespace WMCL.Minecraft.Tests;

public sealed class ResourceInstallPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-resource-plan-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, ModrinthVersion> _modrinth = new(StringComparer.Ordinal);
    private readonly Dictionary<long, CurseForgeFile> _curseForge = [];
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly HttpClient _http;
    private readonly ModrinthClient _mr;
    private readonly CurseForgeClient _cf;
    private readonly ResourceInstallPlanner _planner;

    public ResourceInstallPlanTests()
    {
        _http = new HttpClient(new Handler(RespondAsync));
        _mr = new ModrinthClient(_http, _http);
        _cf = new CurseForgeClient("fixture", _http, _http);
        _planner = new ResourceInstallPlanner(_mr, _cf);
    }

    [Theory]
    [InlineData("Modrinth", true)]
    [InlineData("Modrinth", false)]
    [InlineData("CurseForge", true)]
    [InlineData("CurseForge", false)]
    public async Task MissingRequiredDependencyBlocksBothEntryPointsBeforeAnyFileDownload(string platform, bool selected)
    {
        var main = Mr("main", "selected", [new(null, "missing", "required")]);
        var cf = Cf(1, 10, [new(2, 3)]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (platform == "Modrinth")
            {
                if (selected) await _mr.InstallVersionAsync(main, "1.20.1", _root);
                else await _mr.InstallAsync(main.ProjectId, "1.20.1", _root);
            }
            else if (selected) await _cf.InstallSelectedAsync(1, cf.Id, "1.20.1", "fabric", _root);
            else await _cf.InstallAsync(1, "1.20.1", "fabric", _root);
        });
        Assert.DoesNotContain(_requests, path => path.EndsWith(".jar", StringComparison.Ordinal));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task ProjectEntryPointHonorsVersionOnlyDependencyWithoutReplacingItWithLatest()
    {
        var pinned = Mr("dependency", "pinned");
        Mr("dependency", "latest");
        var main = Mr("main", "main-version", [new("pinned", null, "required")]);
        var result = await _mr.InstallAsync(main.ProjectId, "1.20.1", _root);
        Assert.Equal([pinned.Files[0].Filename, main.Files[0].Filename], result.Files);
        Assert.Contains("/v2/version/pinned", _requests);
        Assert.DoesNotContain("/v2/project/dependency/version", _requests);
        Assert.False(File.Exists(Path.Combine(_root, "latest.jar")));
    }

    [Theory]
    [InlineData("Modrinth")]
    [InlineData("CurseForge")]
    public async Task LongDependencyChainsAndProjectCyclesResolveOnceBeforeApplyingFiles(string platform)
    {
        const int count = 9;
        for (var index = 1; index <= count; index++)
        {
            Mr("project" + index, "version" + index, [new(null, "project" + (index == count ? 1 : index + 1), "required")]);
            Cf(index, index * 10, [new(index == count ? 1 : index + 1, 3)]);
        }
        if (platform == "Modrinth")
        {
            var result = await _mr.InstallAsync("project1", "1.20.1", _root);
            Assert.Equal(count, result.Files.Count);
        }
        else
        {
            var result = await _cf.InstallAsync(1, "1.20.1", "fabric", _root);
            Assert.Equal(count, result.Files.Count);
        }
        Assert.Equal(count, Directory.GetFiles(_root).Length);
        Assert.Equal(count, _requests.Count(path => path.EndsWith(".jar", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CyclicProjectDependencyReusesTheExplicitlySelectedOlderFile()
    {
        Cf(1, 11);
        Cf(1, 10, [new(2, 3)]);
        Cf(2, 20, [new(1, 3)]);
        var result = await _cf.InstallSelectedAsync(1, 10, "1.20.1", "fabric", _root);
        Assert.Equal(["cf20.jar", "cf10.jar"], result.Files);
        Assert.False(File.Exists(Path.Combine(_root, "cf11.jar")));
    }

    [Fact]
    public async Task ConflictingPinnedVersionsAreRejectedBeforeDownloadingTheirParent()
    {
        Mr("shared", "one");
        Mr("shared", "two");
        var first = Mr("first", "first-version", [new("one", "shared", "required")]);
        var second = Mr("second", "second-version", [new("two", "shared", "required")]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _planner.ResolveAsync(
            [Request(first), Request(second)], "1.20.1", "fabric"));
        Assert.Contains("冲突", error.Message);
        Assert.DoesNotContain(_requests, path => path.EndsWith(".jar", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Modrinth")]
    [InlineData("CurseForge")]
    public async Task LaterExplicitRootSatisfiesEarlierProjectDependencyWithoutSelectingLatest(string platform)
    {
        ResourceInstallRequest main;
        ResourceInstallRequest dependency;
        if (platform == "Modrinth")
        {
            dependency = Request(Mr("dependency", "older"));
            Mr("dependency", "latest");
            main = Request(Mr("main", "main-version", [new(null, "dependency", "required")]));
        }
        else
        {
            dependency = Request(Cf(2, 20));
            Cf(2, 21);
            main = Request(Cf(1, 10, [new(2, 3)]));
        }
        var plan = await _planner.ResolveAsync([main, dependency], "1.20.1", "fabric");
        Assert.Equal(2, plan.Entries.Length);
        Assert.Equal(platform == "Modrinth" ? "older" : "20", plan.Entries[0].VersionId);
        Assert.All(plan.Entries, entry => Assert.False(entry.IsDependency));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task VersionOnlyRootIsRecognizedWhenItAlsoSatisfiesAProjectDependency()
    {
        Mr("dependency", "older");
        Mr("dependency", "latest");
        var main = Mr("main", "main-version", [new(null, "dependency", "required")]);
        var plan = await _planner.ResolveAsync(
            [Request(main), new("Modrinth", "", "Selected dependency", VersionId: "older")], "1.20.1", "fabric");
        Assert.Equal(2, plan.Entries.Length);
        Assert.All(plan.Entries, entry => Assert.False(entry.IsDependency));
        Assert.Equal("older", plan.Entries[0].VersionId);
        Assert.Equal(["/v2/version/older"], _requests);
    }

    [Fact]
    public async Task InconsistentMetadataForTheSameSelectedVersionIsRejected()
    {
        var version = Mr("main", "one");
        var changed = version with { Files = [version.Files[0] with { Size = version.Files[0].Size + 1 }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _planner.ResolveAsync(
            [Request(version), Request(changed)], "1.20.1", "fabric"));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("game")]
    [InlineData("loader")]
    public async Task CurseForgeSelectedMetadataIsValidatedBeforeDownloading(string problem)
    {
        var file = Cf(1, 10);
        file = file with
        {
            ProjectId = problem == "project" ? 2 : 1,
            GameVersions = problem == "game" ? ["1.21", "Fabric"] : problem == "loader" ? ["1.20.1", "Forge"] : file.GameVersions
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _planner.ResolveAsync(
            [new("CurseForge", "1", "Selected", CurseForge: file)], "1.20.1", "fabric"));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("Modrinth")]
    [InlineData("CurseForge")]
    public async Task DeclaredIncompatibleProjectsCannotShareOnePlan(string platform)
    {
        ResourceInstallRequest first;
        ResourceInstallRequest second;
        if (platform == "Modrinth")
        {
            first = Request(Mr("first", "first-version", [new(null, "second", "incompatible")]));
            second = Request(Mr("second", "second-version"));
        }
        else
        {
            first = Request(Cf(1, 10, [new(2, 5)]));
            second = Request(Cf(2, 20));
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _planner.ResolveAsync([first, second], "1.20.1", "fabric"));
        Assert.Contains("不兼容", error.Message);
    }

    [Fact]
    public async Task SharedDependenciesDeduplicateButModrinthProjectCaseRemainsSignificant()
    {
        Mr("Dep", "upper");
        Mr("dep", "lower");
        var first = Mr("first", "first-version", [new(null, "Dep", "required"), new(null, "dep", "required")]);
        var second = Mr("second", "second-version", [new(null, "Dep", "required")]);
        var plan = await _planner.ResolveAsync([Request(first), Request(second)], "1.20.1", "fabric");
        Assert.Equal(4, plan.Entries.Length);
        Assert.Equal(2, plan.Entries.Count(entry => entry.IsDependency));
        Assert.Equal(1, _requests.Count(path => path == "/v2/project/Dep/version"));
    }

    [Theory]
    [InlineData("wrong-project")]
    [InlineData("wrong-game")]
    [InlineData("wrong-loader")]
    [InlineData("path")]
    public async Task PinnedMetadataIsValidatedBeforeAnyDownload(string problem)
    {
        var dependency = Mr("dependency", "pinned");
        _modrinth["pinned"] = dependency with
        {
            ProjectId = problem == "wrong-project" ? "different" : dependency.ProjectId,
            GameVersions = problem == "wrong-game" ? ["1.21"] : dependency.GameVersions,
            Loaders = problem == "wrong-loader" ? ["forge"] : dependency.Loaders,
            Files = problem == "path" ? [dependency.Files[0] with { Filename = "../escape.jar" }] : dependency.Files
        };
        var main = Mr("main", "main-version", [new("pinned", "dependency", "required")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _mr.InstallVersionAsync(main, "1.20.1", _root));
        Assert.False(Directory.Exists(_root));
        Assert.DoesNotContain(_requests, path => path.EndsWith(".jar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DifferentProjectsWithOneFilenameFailBeforeDownload()
    {
        var first = Mr("first", "one");
        var second = Mr("second", "two");
        second = second with { Files = [second.Files[0] with { Filename = first.Files[0].Filename }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _planner.ResolveAsync([Request(first), Request(second)], "1.20.1", "fabric"));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task FailedDownloadDoesNotLeaveAnyParentOrDependencyInstalled()
    {
        var dependency = Mr("dependency", "dependency-version");
        var main = Mr("main", "main-version", [new(dependency.Id, dependency.ProjectId, "required")]);
        _files["/" + dependency.Files[0].Filename] = Encoding.UTF8.GetBytes("bad checksum");
        await Assert.ThrowsAnyAsync<Exception>(() => _mr.InstallVersionAsync(main, "1.20.1", _root));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("collision")]
    public async Task FailureAfterFirstFileApplicationRollsBackOnlyNewUnchangedFiles(string outcome)
    {
        var dependency = Mr("dependency", "dependency-version");
        var main = Mr("main", "main-version", [new(dependency.Id, dependency.ProjectId, "required")]);
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "user.txt"), "keep");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(update =>
        {
            if (update.Stage != "应用资源文件") return;
            if (outcome == "cancel") cancellation.Cancel();
            else File.WriteAllText(Path.Combine(_root, main.Files[0].Filename), "user collision");
        });
        await Assert.ThrowsAnyAsync<Exception>(() => _mr.InstallVersionAsync(main, "1.20.1", _root, progress: progress, ct: cancellation.Token));
        Assert.False(File.Exists(Path.Combine(_root, dependency.Files[0].Filename)));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, "user.txt")));
        Assert.Equal(outcome == "collision", File.Exists(Path.Combine(_root, main.Files[0].Filename)));
    }

    [Fact]
    public async Task HashVerifiedExistingFilesAreReusedAndDifferentContentIsProtected()
    {
        var main = Mr("main", "main-version");
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, main.Files[0].Filename);
        await File.WriteAllBytesAsync(path, _files["/" + main.Files[0].Filename]);
        var result = await _mr.InstallVersionAsync(main, "1.20.1", _root);
        Assert.Empty(result.Files);
        Assert.Single(result.ResolvedFiles);
        Assert.Empty(_requests);
        await File.WriteAllBytesAsync(path, new byte[main.Files[0].Size]);
        await Assert.ThrowsAsync<IOException>(() => _mr.InstallVersionAsync(main, "1.20.1", _root));
        Assert.Equal(new byte[main.Files[0].Size], await File.ReadAllBytesAsync(path));
        Assert.Empty(_requests);
    }

    private ModrinthVersion Mr(string project, string id, IReadOnlyList<ModrinthDependency>? dependencies = null)
    {
        var name = id + ".jar";
        var payload = Encoding.UTF8.GetBytes(project + "/" + id);
        _files["/" + name] = payload;
        var version = new ModrinthVersion(id, project, project, id, "release", DateTimeOffset.UnixEpoch,
            ["1.20.1"], ["fabric"], [new(name, "https://fixture.invalid/" + name, payload.Length, Hash(payload), true)], dependencies ?? []);
        _modrinth[id] = version;
        return version;
    }

    private CurseForgeFile Cf(long project, long id, IReadOnlyList<CurseForgeDependency>? dependencies = null)
    {
        var name = "cf" + id + ".jar";
        var payload = Encoding.UTF8.GetBytes(project + "/" + id);
        _files["/" + name] = payload;
        var file = new CurseForgeFile(id, "CF " + project, name, DateTimeOffset.UnixEpoch, payload.Length,
            "https://fixture.invalid/" + name, Hash(payload), ["1.20.1", "Fabric"], dependencies ?? [], ProjectId: project);
        _curseForge[id] = file;
        return file;
    }

    private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        _requests.Enqueue(path);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (_files.TryGetValue(path, out var bytes)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        if (parts.Length == 3 && parts[0] == "v2" && parts[1] == "version" && _modrinth.TryGetValue(parts[2], out var version))
            return Task.FromResult(Json(MrJson(version)));
        if (parts.Length == 4 && parts[0] == "v2" && parts[1] == "project")
            return Task.FromResult(Json(_modrinth.Values.Where(item => item.ProjectId == parts[2]).Reverse().Select(MrJson).ToArray()));
        if (parts.Length >= 4 && parts[0] == "v1" && parts[1] == "mods")
        {
            var project = long.Parse(parts[2]);
            if (parts.Length == 5 && _curseForge.TryGetValue(long.Parse(parts[4]), out var file))
                return Task.FromResult(Json(new { data = CfJson(file) }));
            return Task.FromResult(Json(new { data = _curseForge.Values.Where(item => item.ProjectId == project).Select(CfJson).ToArray() }));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static object MrJson(ModrinthVersion version) => new
    {
        id = version.Id,
        project_id = version.ProjectId,
        name = version.Name,
        version_number = version.VersionNumber,
        version_type = "release",
        date_published = version.DatePublished,
        game_versions = version.GameVersions,
        loaders = version.Loaders,
        files = version.Files.Select(file => new { filename = file.Filename, url = file.Url, size = file.Size, primary = true, hashes = new { sha1 = file.Sha1 } }),
        dependencies = version.Dependencies.Select(item => new { project_id = item.ProjectId, version_id = item.VersionId, dependency_type = item.DependencyType })
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
        hashes = new[] { new { value = file.Sha1, algo = 1 } },
        dependencies = file.Dependencies.Select(item => new { modId = item.ModId, relationType = item.RelationType })
    };

    private static ResourceInstallRequest Request(ModrinthVersion version) => new("Modrinth", version.ProjectId, version.Name, Modrinth: version);
    private static ResourceInstallRequest Request(CurseForgeFile file) => new("CurseForge", file.ProjectId.ToString(), file.DisplayName, CurseForge: file);
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class InlineProgress(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
