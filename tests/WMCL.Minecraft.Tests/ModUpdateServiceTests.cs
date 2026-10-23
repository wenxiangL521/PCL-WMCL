using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WMCL.Core.Jobs;
using WMCL.Modding.Modrinth;

namespace WMCL.Minecraft.Tests;

public sealed class ModUpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-mod-update-service-" + Guid.NewGuid().ToString("N"));
    private readonly string _recycled = Path.Combine(Path.GetTempPath(), "wmcl-mod-update-recycled-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, ModrinthVersion> _versions = [];
    private readonly Dictionary<string, ModrinthVersion[]> _catalog = [];
    private readonly Dictionary<string, ModrinthVersion> _hashes = [];
    private readonly Dictionary<string, byte[]> _payloads = [];
    private readonly ConcurrentQueue<string> _downloads = new();
    private readonly HttpClient _http;
    private readonly ModUpdateService _service;
    private Func<string, HttpResponseMessage>? _hashResponse;
    private Func<string, HttpResponseMessage>? _catalogResponse;
    private Action<string>? _onDownload;

    public ModUpdateServiceTests()
    {
        Directory.CreateDirectory(_root);
        _http = new HttpClient(new Handler(Respond));
        _service = new ModUpdateService(new ModrinthClient(_http, _http), directory =>
        {
            Directory.CreateDirectory(_recycled);
            Directory.Move(directory, Path.Combine(_recycled, Guid.NewGuid().ToString("N")));
        });
    }

    [Theory]
    [InlineData("unknown", ModUpdateCheckState.Unknown)]
    [InlineData("incompatible", ModUpdateCheckState.NoCompatibleVersion)]
    [InlineData("latest", ModUpdateCheckState.UpToDate)]
    [InlineData("newer-local", ModUpdateCheckState.UpToDate)]
    [InlineData("error", ModUpdateCheckState.Failed)]
    [InlineData("null", ModUpdateCheckState.Failed)]
    [InlineData("malformed", ModUpdateCheckState.Failed)]
    [InlineData("missing-id", ModUpdateCheckState.Failed)]
    [InlineData("catalog-null", ModUpdateCheckState.Failed)]
    [InlineData("catalog-error", ModUpdateCheckState.Failed)]
    [InlineData("catalog-missing-id", ModUpdateCheckState.Failed)]
    [InlineData("catalog-wrong-project", ModUpdateCheckState.Failed)]
    public async Task CheckDistinguishesLatestUnknownIncompatibleAndFailed(string scenario, ModUpdateCheckState expected)
    {
        var current = Version("main", "installed", 2);
        Install(current);
        _catalog["main"] = scenario == "newer-local" ? [Version("main", "older", 1)] : [current];
        if (scenario == "unknown") _hashes.Clear();
        if (scenario == "incompatible") _catalog["main"] = [];
        if (scenario == "error") _hashResponse = _ => new(HttpStatusCode.ServiceUnavailable);
        if (scenario == "null") _hashResponse = _ => Json(null);
        if (scenario == "malformed") _hashResponse = _ => new(HttpStatusCode.OK) { Content = new StringContent("{") };
        if (scenario == "missing-id") _hashResponse = _ => Json(new { name = "missing identity" });
        if (scenario == "catalog-null") _catalogResponse = _ => Json(null);
        if (scenario == "catalog-error") _catalogResponse = _ => new(HttpStatusCode.ServiceUnavailable);
        if (scenario == "catalog-missing-id") _catalog["main"] = [current with { Id = "" }];
        if (scenario == "catalog-wrong-project") _catalog["main"] = [current with { ProjectId = "other" }];
        var result = await _service.CheckDetailedAsync(_root, "1.20.1", "fabric");
        Assert.Equal(expected, Assert.Single(result.Items).State);
        Assert.Empty(result.Updates);
        if (expected == ModUpdateCheckState.UpToDate) Assert.Contains("均为最新", result.Summary);
        else Assert.DoesNotContain("均为最新", result.Summary);
        Assert.Empty(_downloads);
    }

    [Fact]
    public async Task EmptyEnabledInventoryAndMixedResultsDoNotClaimEverythingIsLatest()
    {
        Assert.Contains("没有已启用", (await _service.CheckDetailedAsync(_root, "1.20.1", "fabric")).Summary);
        var known = Version("known", "known-old");
        Install(known);
        File.WriteAllText(Path.Combine(_root, "unknown.jar"), "unknown");
        _catalog["known"] = [known];
        var result = await _service.CheckDetailedAsync(_root, "1.20.1", "fabric");
        Assert.Equal(2, result.Items.Count);
        Assert.Contains("已最新 1", result.Summary);
        Assert.Contains("未识别 1", result.Summary);
        Assert.DoesNotContain("均为最新", result.Summary);
    }

    [Fact]
    public async Task CancellationDuringHashLookupPropagatesInsteadOfBecomingUnknown()
    {
        Install(Version("main", "old"));
        using var cancellation = new CancellationTokenSource();
        _hashResponse = _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CheckDetailedAsync(_root, "1.20.1", "fabric", ct: cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateResolvesPinnedDependenciesAndReplacesIdentifiedOldFilesTogether(bool sameFileName)
    {
        var old = Version("main", "old");
        var dependencyOld = Version("dep", "dep-old");
        Install(old);
        Install(dependencyOld);
        var dependency = Version("dep", "pinned", 2);
        var latest = Version("main", "new", 2, [new("pinned", "dep", "required")]);
        if (sameFileName)
        {
            latest = latest with { Files = [latest.Files[0] with { Filename = old.Files[0].Filename }] };
            _versions[latest.Id] = latest;
        }
        _catalog["main"] = [latest];
        _catalog["dep"] = [Version("dep", "unwanted-latest", 3)];
        File.WriteAllText(Path.Combine(_root, "user.jar"), "unidentified user mod");
        File.WriteAllText(Path.Combine(_root, "disabled.jar.disabled"), "disabled user mod");
        var check = await _service.CheckDetailedAsync(_root, "1.20.1", "fabric");
        var update = Assert.Single(check.Updates, item => item.Latest.ProjectId == "main");
        await _service.ApplyAsync([update], _root, "1.20.1", "fabric");
        Assert.Equal(_payloads["new.jar"], File.ReadAllBytes(Path.Combine(_root, latest.Files[0].Filename)));
        Assert.Equal(_payloads["pinned.jar"], File.ReadAllBytes(Path.Combine(_root, dependency.Files[0].Filename)));
        Assert.False(File.Exists(Path.Combine(_root, "dep-old.jar")));
        if (!sameFileName) Assert.False(File.Exists(Path.Combine(_root, "old.jar")));
        Assert.False(File.Exists(Path.Combine(_root, "unwanted-latest.jar")));
        Assert.Equal("unidentified user mod", File.ReadAllText(Path.Combine(_root, "user.jar")));
        Assert.Equal("disabled user mod", File.ReadAllText(Path.Combine(_root, "disabled.jar.disabled")));
        Assert.Equal(_payloads["old.jar"], File.ReadAllBytes(Assert.Single(Directory.EnumerateFiles(_recycled, "old.jar", SearchOption.AllDirectories))));
        Assert.Equal(_payloads["dep-old.jar"], File.ReadAllBytes(Assert.Single(Directory.EnumerateFiles(_recycled, "dep-old.jar", SearchOption.AllDirectories))));
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Theory]
    [InlineData("missing-dependency")]
    [InlineData("incompatible-pins")]
    [InlineData("stale-source")]
    [InlineData("target-collision")]
    [InlineData("download-error")]
    [InlineData("source-changed-during-download")]
    [InlineData("apply-error")]
    [InlineData("cancel-during-apply")]
    [InlineData("missing-source")]
    [InlineData("cancel-before-download")]
    [InlineData("reused-target-changed")]
    public async Task FailedBatchPreservesAllOriginalFilesAndDoesNotLeaveEarlierUpdates(string failure)
    {
        var first = Version("first", "first-old");
        var second = Version("second", "second-old");
        Install(first);
        Install(second);
        var firstNext = Version("first", "first-new", 2);
        var secondNext = Version("second", "second-new", 2);
        if (failure == "missing-dependency")
            firstNext = firstNext with { Dependencies = [new(null, "missing", "required")] };
        if (failure == "incompatible-pins")
        {
            Version("dep", "dep-a");
            Version("dep", "dep-b");
            firstNext = firstNext with { Dependencies = [new("dep-a", "dep", "required")] };
            secondNext = secondNext with { Dependencies = [new("dep-b", "dep", "required")] };
        }
        _catalog["first"] = [firstNext];
        _catalog["second"] = [secondNext];
        var updates = (await _service.CheckDetailedAsync(_root, "1.20.1", "fabric")).Updates;
        Assert.Equal(2, updates.Count);
        var firstPath = Path.Combine(_root, "first-old.jar");
        if (failure == "stale-source") File.WriteAllText(firstPath, "user changed source");
        if (failure == "target-collision") File.WriteAllText(Path.Combine(_root, "second-new.jar"), "user owned target");
        if (failure == "missing-source") File.Delete(firstPath);
        if (failure == "reused-target-changed") File.WriteAllBytes(Path.Combine(_root, "first-new.jar"), _payloads["first-new.jar"]);
        if (failure == "download-error") _payloads.Remove("second-new.jar");
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancel-before-download") cancellation.Cancel();
        if (failure == "source-changed-during-download") _onDownload = _ => File.WriteAllText(firstPath, "external change");
        if (failure == "reused-target-changed") _onDownload = _ => File.WriteAllText(Path.Combine(_root, "first-new.jar"), "external change");
        var progress = new InlineProgress(update =>
        {
            if (update.Stage != "应用资源文件") return;
            if (failure == "apply-error") throw new IOException("late apply failure");
            if (failure == "cancel-during-apply") cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<Exception>(() => _service.ApplyAsync(updates, _root, "1.20.1", "fabric", progress, cancellation.Token));
        if (failure == "source-changed-during-download")
            before["first-old.jar"] = Hash(Encoding.UTF8.GetBytes("external change"));
        if (failure == "reused-target-changed")
            before["first-new.jar"] = Hash(Encoding.UTF8.GetBytes("external change"));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root));
        if (failure is "missing-dependency" or "incompatible-pins" or "stale-source" or "target-collision" or "missing-source" or "cancel-before-download") Assert.Empty(_downloads);
    }

    [Fact]
    public async Task RecycleFailureKeepsTheSuccessfulUpdateAndReportsTheNamedOriginalBackup()
    {
        Install(Version("main", "old"));
        _catalog["main"] = [Version("main", "new", 2)];
        var updates = (await _service.CheckDetailedAsync(_root, "1.20.1", "fabric")).Updates;
        var service = new ModUpdateService(new ModrinthClient(_http, _http), _ => throw new IOException("trash unavailable"));
        var result = await service.ApplyAsync(updates, _root, "1.20.1", "fabric");
        Assert.Contains("trash unavailable", result.RecoveryWarning);
        Assert.NotNull(result.RecoveryDirectory);
        Assert.Equal(_payloads["old.jar"], File.ReadAllBytes(Path.Combine(result.RecoveryDirectory, "old.jar")));
        Assert.Equal(_payloads["new.jar"], File.ReadAllBytes(Path.Combine(_root, "new.jar")));
        Assert.False(File.Exists(Path.Combine(_root, "old.jar")));
        Assert.Contains(Path.Combine(_root, "new.jar"), result.Files);
    }

    [Fact]
    public async Task CancellationAfterPublicationDoesNotDiscardRecoveryFilesOrMisreportTheCommittedUpdate()
    {
        Install(Version("main", "old"));
        _catalog["main"] = [Version("main", "new", 2)];
        var updates = (await _service.CheckDetailedAsync(_root, "1.20.1", "fabric")).Updates;
        using var cancellation = new CancellationTokenSource();
        var service = new ModUpdateService(new ModrinthClient(_http, _http), backup =>
        {
            cancellation.Cancel();
            Directory.CreateDirectory(_recycled);
            Directory.Move(backup, Path.Combine(_recycled, "committed"));
        });
        var result = await service.ApplyAsync(updates, _root, "1.20.1", "fabric", ct: cancellation.Token);
        Assert.Null(result.RecoveryWarning);
        Assert.Equal(_payloads["old.jar"], File.ReadAllBytes(Path.Combine(_recycled, "committed", "old.jar")));
        Assert.Equal(_payloads["new.jar"], File.ReadAllBytes(Path.Combine(_root, "new.jar")));
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task ModifiedPublishedFileRetainsTheOriginalBackupAndReportsIncompleteRollback()
    {
        var old = Version("main", "same");
        Install(old);
        var latest = Version("main", "new", 2) with { Files = [Version("main", "new", 2).Files[0] with { Filename = "same.jar" }] };
        _catalog["main"] = [latest];
        var updates = (await _service.CheckDetailedAsync(_root, "1.20.1", "fabric")).Updates;
        var progress = new InlineProgress(update =>
        {
            if (update.Stage != "应用资源文件") return;
            File.WriteAllText(Path.Combine(_root, "same.jar"), "external edit");
            throw new IOException("notification failed");
        });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => _service.ApplyAsync(updates, _root, "1.20.1", "fabric", progress));
        Assert.Contains("备份保留", failure.Message);
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(_root, "same.jar")));
        var backup = Assert.Single(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), path => Path.GetFileName(Path.GetDirectoryName(path))!.StartsWith("WMCL Mod 更新备份 ", StringComparison.Ordinal));
        Assert.Equal(_payloads["same.jar"], File.ReadAllBytes(backup));
    }

    private ModrinthVersion Version(string project, string id, int day = 1, IReadOnlyList<ModrinthDependency>? dependencies = null)
    {
        var payload = Encoding.UTF8.GetBytes(project + ":" + id);
        _payloads[id + ".jar"] = payload;
        var version = new ModrinthVersion(id, project, project, id, "release", DateTimeOffset.UnixEpoch.AddDays(day),
            ["1.20.1"], ["fabric"], [new(id + ".jar", "https://fixture.invalid/" + id + ".jar", payload.Length, Hash(payload), true)], dependencies ?? []);
        _versions[id] = version;
        return version;
    }

    private void Install(ModrinthVersion version)
    {
        var payload = _payloads[version.Files[0].Filename];
        File.WriteAllBytes(Path.Combine(_root, version.Files[0].Filename), payload);
        _hashes[Hash(payload)] = version;
    }

    private HttpResponseMessage Respond(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/version_file/", StringComparison.Ordinal))
        {
            var hash = path.Split('/').Last();
            return _hashResponse?.Invoke(hash) ?? (_hashes.TryGetValue(hash, out var installed) ? Json(Wire(installed)) : new(HttpStatusCode.NotFound));
        }
        if (path.Contains("/project/", StringComparison.Ordinal))
        {
            var project = path.Split('/')[^2];
            return _catalogResponse?.Invoke(project) ?? (_catalog.TryGetValue(project, out var versions) ? Json(versions.Select(Wire)) : Json(Array.Empty<object>()));
        }
        if (path.Contains("/version/", StringComparison.Ordinal))
            return _versions.TryGetValue(path.Split('/').Last(), out var version) ? Json(Wire(version)) : new(HttpStatusCode.NotFound);
        var name = path.Split('/').Last();
        _downloads.Enqueue(name);
        _onDownload?.Invoke(name);
        return _payloads.TryGetValue(name, out var payload)
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) } : new(HttpStatusCode.NotFound);
    }

    private static object Wire(ModrinthVersion version) => new
    {
        id = version.Id,
        project_id = version.ProjectId,
        name = version.Name,
        version_number = version.VersionNumber,
        version_type = version.VersionType,
        date_published = version.DatePublished,
        game_versions = version.GameVersions,
        loaders = version.Loaders,
        files = version.Files.Select(file => new { filename = file.Filename, url = file.Url, size = file.Size, hashes = new { sha1 = file.Sha1 }, primary = file.Primary }),
        dependencies = version.Dependencies.Select(dependency => new { version_id = dependency.VersionId, project_id = dependency.ProjectId, dependency_type = dependency.DependencyType })
    };

    private static HttpResponseMessage Json(object? value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), path => Hash(File.ReadAllBytes(path)));
    private sealed class InlineProgress(Action<JobProgress> report) : IProgress<JobProgress> { public void Report(JobProgress value) => report(value); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request, cancellationToken));
    }
    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_root, true);
        if (Directory.Exists(_recycled)) Directory.Delete(_recycled, true);
    }
}
