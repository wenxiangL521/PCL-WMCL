using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Modding.Modrinth;
using WMCL.Modding.Resources;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class ResourceArchiveUpdateFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-resource-update-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _requests = [];
    private readonly byte[] _old = Archive("old");
    private readonly byte[] _next = Archive("new resource contents");
    private readonly HttpClient _http;
    private readonly ResourceArchiveUpdateService _service;
    private ModrinthVersion? _current;
    private ModrinthVersion[] _catalog = [];
    private Action? _onDownload;

    public ResourceArchiveUpdateFlowTests()
    {
        Directory.CreateDirectory(_root);
        _http = new HttpClient(new Handler(Respond));
        _service = new ResourceArchiveUpdateService(new ModrinthClient(_http, _http), backup =>
            Directory.Move(backup, Path.Combine(_root, "recycled-" + Guid.NewGuid().ToString("N"))));
    }

    [Theory]
    [InlineData("resourcepack", false)]
    [InlineData("shader", true)]
    public async Task SuccessfulUpdateUsesCompatibleVersionAndKeepsNameOptionsAndDisabledState(string type, bool disabled)
    {
        var path = Prepare(disabled);
        _catalog = [Version("wrong-game", 4) with { GameVersions = ["1.21"] }, Version("new", 2)];
        var options = Path.Combine(_root, "options.txt");
        File.WriteAllText(options, "resourcePacks:[\"file/user-name.zip\"]\nuser preference");
        var update = Assert.Single((await _service.CheckAsync(_root, "1.20.1", type)).Updates);
        Assert.Equal("new", update.Latest.Id);
        Assert.Contains(_requests, request => request.Contains("/version_file/" + Hash(_old), StringComparison.Ordinal));
        await _service.ApplyAsync(update, _root, "1.20.1", type);
        var actual = File.ReadAllBytes(path);
        Assert.Equal(_next.Length, actual.Length);
        Assert.Equal(Hash(_next), Hash(actual));
        Assert.Equal("resourcePacks:[\"file/user-name.zip\"]\nuser preference", File.ReadAllText(options));
        Assert.False(File.Exists(Path.Combine(_root, "new.zip")));
        Assert.Equal(disabled ? "user-name.zip.disabled" : "user-name.zip", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(_root, "*.zip*"))));
    }

    [Theory]
    [InlineData("unknown", ModUpdateCheckState.Unknown)]
    [InlineData("incompatible", ModUpdateCheckState.NoCompatibleVersion)]
    [InlineData("wrong-project", ModUpdateCheckState.Failed)]
    public async Task CheckDoesNotOfferUnsafeOrUnrecognizedUpdates(string scenario, ModUpdateCheckState state)
    {
        Prepare();
        if (scenario == "unknown") _current = null;
        if (scenario == "incompatible") _catalog = [Version("new", 2) with { GameVersions = ["1.21"] }];
        if (scenario == "wrong-project") _catalog = [Version("new", 2) with { ProjectId = "other" }];
        var result = await _service.CheckAsync(_root, "1.20.1", "resourcepack");
        Assert.Equal(state, Assert.Single(result.Items).State);
        Assert.Empty(result.Updates);
        Assert.DoesNotContain(_requests, request => request.EndsWith("/new.zip", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("cancelled")]
    [InlineData("cancel-download")]
    [InlineData("wrong-project")]
    public async Task FailedApplyDoesNotPublishOrOverwriteExistingBytes(string scenario)
    {
        var path = Prepare();
        var update = Assert.Single((await _service.CheckAsync(_root, "1.20.1", "resourcepack")).Updates);
        using var cancellation = new CancellationTokenSource();
        if (scenario == "changed") File.WriteAllText(path, "user changed archive");
        if (scenario == "cancelled") cancellation.Cancel();
        if (scenario == "cancel-download") _onDownload = cancellation.Cancel;
        if (scenario == "wrong-project") update = update with { Latest = update.Latest with { ProjectId = "other" } };
        var before = File.ReadAllBytes(path);
        var requestsBefore = _requests.Count;
        await Assert.ThrowsAnyAsync<Exception>(() => _service.ApplyAsync(update, _root, "1.20.1", "resourcepack", cancellation.Token));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(_root, "new.zip")));
        Assert.Empty(Directory.EnumerateDirectories(_root));
        if (scenario != "cancel-download") Assert.Equal(requestsBefore, _requests.Count);
    }

    [Fact]
    public async Task WorldTypeIsExplicitlyRejectedWithoutRequests()
    {
        Prepare();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CheckAsync(_root, "1.20.1", "world"));
        Assert.Empty(_requests);
    }

    private string Prepare(bool disabled = false)
    {
        var path = Path.Combine(_root, "user-name.zip" + (disabled ? ".disabled" : ""));
        File.WriteAllBytes(path, _old);
        _current = Version("old", 1);
        _catalog = [Version("new", 2)];
        return path;
    }
    private ModrinthVersion Version(string id, int day) => new(id, "project", "Resource", id, "release",
        DateTimeOffset.UnixEpoch.AddDays(day), ["1.20.1"], ["iris"],
        [new(id + ".zip", "https://fixture.invalid/" + id + ".zip", (id == "old" ? _old : _next).Length,
            Hash(id == "old" ? _old : _next), true)], []);
    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        _requests.Add(path);
        if (path.Contains("/version_file/", StringComparison.Ordinal))
            return _current is null ? new(HttpStatusCode.NotFound) : Json(Wire(_current));
        if (path.Contains("/project/", StringComparison.Ordinal)) return Json(_catalog.Select(Wire));
        if (path.EndsWith("/new.zip", StringComparison.Ordinal))
        {
            _onDownload?.Invoke();
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(_next) };
        }
        return new(HttpStatusCode.NotFound);
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
        dependencies = Array.Empty<object>()
    };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));
    private static byte[] Archive(string text)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("pack.mcmeta").Open())) writer.Write(text);
        return stream.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_root, true);
    }
}
