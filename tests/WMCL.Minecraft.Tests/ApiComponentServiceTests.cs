using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modrinth;

namespace WMCL.Minecraft.Tests;

public sealed class ApiComponentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-api-service-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Fabric", "P7dR8mSH")]
    [InlineData("Quilt", "qvIfYCYJ")]
    [InlineData("LegacyFabric", "9CJED7xi")]
    public async Task CatalogChecksProjectGameFamilyAndFilesAndSortsByPublicationInstant(string kind, string projectId)
    {
        var expected = Version([1], kind) with { DatePublished = DateTimeOffset.Parse("2024-01-01T09:00:00-08:00") };
        var older = expected with { Id = "older", DatePublished = DateTimeOffset.Parse("2024-01-01T10:00:00Z") };
        var wrongGame = expected with { Id = "wrong-game", GameVersions = ["1.20.4"] };
        var wrongFamily = expected with { Id = "wrong-family", Loaders = [kind == "Fabric" ? "quilt" : "fabric"] };
        var wrongProject = expected with { Id = "wrong-project", ProjectId = "other-project" };
        var ornithe = expected with { Id = "ornithe", Loaders = ["ornithe"] };
        var empty = expected with { Id = "empty", Files = [] };
        var handler = new Handler(request =>
        {
            Assert.Contains(projectId, request.RequestUri!.AbsolutePath);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("game_versions=[\"1.20.1\"]", query);
            Assert.Contains("loaders=[\"" + ApiComponentService.Loader(kind) + "\"]", query);
            return Json(new[] { older, wrongGame, wrongFamily, wrongProject, ornithe, empty, expected }.Select(Response));
        });
        var versions = await new ApiComponentService(Client(handler)).GetVersionsAsync("1.20.1", kind);
        Assert.Equal(new[] { "chosen", "older" }, versions.Select(version => version.Id));
        Assert.Equal(projectId, versions[0].ProjectId);
    }

    [Fact]
    public async Task InstallsTheCapturedVersionWithoutQueryingLatestAndReusesOnlyIdenticalFiles()
    {
        var bytes = ApiArchiveTestData.Jar("""{"id":"fabric-api","depends":{"fabricloader":">=0.16.10"}}""");
        var version = Version(bytes);
        var requests = new ConcurrentBag<string>();
        var handler = new Handler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            Assert.EndsWith("chosen.jar", request.RequestUri.AbsolutePath);
            return Bytes(bytes);
        });
        var service = new ApiComponentService(Client(handler));
        var result = await service.InstallAsync(version, "1.20.1", "Fabric", "0.16.10", _root);
        Assert.Equal(["chosen.jar"], result);
        var path = Path.Combine(_root, "chosen.jar");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await service.InstallAsync(version, "1.20.1", "Fabric", "0.16.10", _root);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, requests.Count);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ReproducedLoaderConflictDoesNotWriteApiIntoTheInstance()
    {
        var bytes = ApiArchiveTestData.Jar("""{"id":"fabric-api","depends":{"fabricloader":">=0.16.10"}}""");
        var service = new ApiComponentService(Client(new Handler(_ => Bytes(bytes))));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(Version(bytes), "1.20.1", "Fabric", "0.15.11", _root));
        Assert.Contains("0.16.10", error.Message);
        Assert.Contains("0.15.11", error.Message);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task DifferentExistingFilesArePreservedAndAreNotReportedAsInstalled()
    {
        var bytes = ApiArchiveTestData.Jar("""{"depends":{"fabricloader":">=0.16.10"}}""");
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "chosen.jar");
        await File.WriteAllTextAsync(path, "user mod");
        var service = new ApiComponentService(Client(new Handler(_ => Bytes(bytes))));
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Version(bytes), "1.20.1", "Fabric", "0.16.10", _root));
        Assert.Equal("user mod", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task EveryRequiredDependencyIsCheckedBeforeAnyApiFileIsMoved()
    {
        var mainBytes = ApiArchiveTestData.Jar("{}");
        var dependencyBytes = ApiArchiveTestData.Jar("""{"depends":{"fabricloader":">=0.17"}}""");
        var dependency = Version(dependencyBytes) with
        {
            Id = "pinned-dependency", ProjectId = "dependency-project",
            Files = [VersionFile(dependencyBytes, "dependency.jar")]
        };
        var main = Version(mainBytes) with { Dependencies = [new ModrinthDependency(dependency.Id, dependency.ProjectId, "required")] };
        var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/chosen.jar" => Bytes(mainBytes),
            "/dependency.jar" => Bytes(dependencyBytes),
            "/v2/version/pinned-dependency" => Json(Response(dependency)),
            _ => throw new InvalidOperationException("Unexpected request " + request.RequestUri)
        });
        var service = new ApiComponentService(Client(handler));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(main, "1.20.1", "Fabric", "0.16.10", _root));
        Assert.Contains("dependency.jar", error.Message);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task RenamedOlderApiIsPreservedAndDuplicateVersionsAreNotAdded()
    {
        var bytes = ApiArchiveTestData.Jar("""{"id":"fabric-api","version":"new","depends":{"fabricloader":">=0.16.10"}}""");
        var old = ApiArchiveTestData.Jar("""{"id":"fabric-api","version":"old"}""");
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "renamed-by-user.jar");
        await File.WriteAllBytesAsync(existing, old);
        var service = new ApiComponentService(Client(new Handler(_ => Bytes(bytes))));
        var error = await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Version(bytes), "1.20.1", "Fabric", "0.16.10", _root));
        Assert.Contains("renamed-by-user.jar", error.Message);
        Assert.Equal(old, await File.ReadAllBytesAsync(existing));
        Assert.False(File.Exists(Path.Combine(_root, "chosen.jar")));
        File.Move(existing, existing + ".disabled");
        await service.InstallAsync(Version(bytes), "1.20.1", "Fabric", "0.16.10", _root);
        Assert.Equal(old, await File.ReadAllBytesAsync(existing + ".disabled"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "chosen.jar")));
    }

    [Fact]
    public async Task WrongSelectionsFailBeforeNetworkAndCancellationPropagates()
    {
        var bytes = ApiArchiveTestData.Jar("{}");
        var version = Version(bytes);
        var service = new ApiComponentService(Client(new Handler(_ => throw new InvalidOperationException("No network expected"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(version, "1.20.4", "Fabric", "0.16.10", _root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(version, "1.20.1", "Quilt", "0.25.0", _root));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(version, "1.20.1", "Fabric", "0.16.10", _root, ct: cancellation.Token));
        Assert.False(Directory.Exists(_root));
    }

    private static ModrinthVersion Version(byte[] bytes, string kind = "Fabric") => new(
        "chosen", ApiComponentService.ProjectId(kind), "API", "1.0.0", "release", DateTimeOffset.UnixEpoch,
        ["1.20.1"], [ApiComponentService.Loader(kind)], [VersionFile(bytes, "chosen.jar")], []);

    private static ModrinthFile VersionFile(byte[] bytes, string filename) =>
        new(filename, "https://example.invalid/" + filename, bytes.Length, Convert.ToHexStringLower(SHA1.HashData(bytes)), true);

    private static object Response(ModrinthVersion version) => new
    {
        id = version.Id, project_id = version.ProjectId, name = version.Name, version_number = version.VersionNumber,
        version_type = version.VersionType, date_published = version.DatePublished, game_versions = version.GameVersions,
        loaders = version.Loaders, files = version.Files.Select(file => new
        {
            filename = file.Filename, url = file.Url, size = file.Size, primary = file.Primary, hashes = new { sha1 = file.Sha1 }
        }), dependencies = version.Dependencies.Select(dependency => new
        {
            version_id = dependency.VersionId, project_id = dependency.ProjectId, dependency_type = dependency.DependencyType
        })
    };

    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static ModrinthClient Client(Handler handler) => new(new HttpClient(handler), new HttpClient(handler));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
