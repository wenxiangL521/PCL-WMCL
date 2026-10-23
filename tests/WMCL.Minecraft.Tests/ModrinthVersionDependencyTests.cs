using System.Net;
using System.Text;
using WMCL.Modding.Modrinth;

namespace WMCL.Minecraft.Tests;

public class ModrinthVersionDependencyTests
{
    [Fact]
    public async Task InstallsRequiredDependencyByVersionId()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/version/main"))
                return Json("""
                    {"id":"main","project_id":"project-main","name":"Main","version_number":"1.0","version_type":"release",
                     "date_published":"2024-01-01T00:00:00Z","game_versions":["1.20.1"],"loaders":["fabric"],
                     "files":[{"filename":"main.jar","url":"https://cdn.example/main.jar","size":4,"primary":true}],
                     "dependencies":[{"version_id":"dep-1","project_id":"project-dep","dependency_type":"required"}]}
                    """);
            if (path.EndsWith("/version/dep-1"))
                return Json("""
                    {"id":"dep-1","project_id":"project-dep","name":"Dep","version_number":"2.0","version_type":"release",
                     "date_published":"2024-01-01T00:00:00Z","game_versions":["1.20.1"],"loaders":["fabric"],
                     "files":[{"filename":"dep.jar","url":"https://cdn.example/dep.jar","size":3,"primary":true}],
                     "dependencies":[]}
                    """);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(path.EndsWith("/dep.jar", StringComparison.Ordinal) ? [1, 2, 3] : [1, 2, 3, 4])
            };
        });
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-modrinth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var main = await client.GetVersionAsync("main");
            var result = await client.InstallVersionAsync(main, "1.20.1", directory, "fabric");
            Assert.Contains("main.jar", result.Files);
            Assert.Contains("dep.jar", result.Files);
            Assert.True(File.Exists(Path.Combine(directory, "main.jar")));
            Assert.True(File.Exists(Path.Combine(directory, "dep.jar")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public async Task InstallsSelectedContentVersionOnly()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/version/pack-1"))
                return Json("""
                    {"id":"pack-1","project_id":"pack","name":"Pack","version_number":"1.0","version_type":"release",
                     "date_published":"2024-01-01T00:00:00Z","game_versions":["1.20.1"],"loaders":[],
                     "files":[{"filename":"pack.zip","url":"https://cdn.example/pack.zip","size":4,"primary":true}],
                     "dependencies":[{"project_id":"dep","dependency_type":"required"}]}
                    """);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
        });
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var version = await client.GetVersionAsync("pack-1");
            var result = await client.InstallContentVersionAsync(version, directory, "resourcepack");
            Assert.Equal(["pack.zip"], result.Files);
            Assert.True(File.Exists(Path.Combine(directory, "pack.zip")));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SearchPassesOffset()
    {
        Uri? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request.RequestUri;
            return Json("""{"hits":[],"total_hits":100}""");
        });
        var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
        await client.SearchAsync("q", "1.20.1", "fabric", 20, default, "mod", 40);
        Assert.NotNull(captured);
        Assert.Contains("offset=40", captured!.Query);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ExistingFilesAreReportedForManifestWithoutCountingThemAsNewDownloads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-modrinth-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "existing.jar"), [1, 2, 3]);
        try
        {
            var handler = new StubHandler(_ => throw new InvalidOperationException("No network expected"));
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var version = Version("existing", []) with
            {
                Files = [new ModrinthFile("existing.jar", "https://cdn.example/existing.jar", 3,
                    WMCL.Core.IO.FileUtil.Sha1(Path.Combine(directory, "existing.jar")), true)]
            };
            var result = await client.InstallVersionAsync(version, "1.20.1", directory, requireDependencies: true);
            Assert.Empty(result.Files);
            Assert.Equal(["existing.jar"], result.ResolvedFiles);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MissingRequiredDependencyFailsOrdinaryAndStrictInstallation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-modrinth-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "main.jar"), [1, 2, 3]);
        try
        {
            var handler = new StubHandler(_ => Json("[]"));
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var version = Version("main", [new ModrinthDependency(null, "missing", "required")]);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.InstallVersionAsync(version, "1.20.1", directory, requireDependencies: true));
            Assert.Contains("missing", failure.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.InstallVersionAsync(version, "1.20.1", directory));
            var ordinary = await client.InstallVersionAsync(version, "1.20.1", directory, requireDependencies: false);
            Assert.Empty(ordinary.Files);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RequiredDependencyCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-modrinth-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "main.jar"), [1, 2, 3]);
        try
        {
            var handler = new StubHandler(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var version = Version("main", [new ModrinthDependency("required-id", null, "required")]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.InstallVersionAsync(version, "1.20.1", directory, ct: cancellation.Token, requireDependencies: true));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-id")]
    [InlineData("wrong-project")]
    [InlineData("wrong-game")]
    [InlineData("wrong-loader")]
    public async Task StrictPinnedDependenciesDoNotChangeVersionOrInstallIncompatibleMetadata(string problem)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-modrinth-pinned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "main.jar"), [1, 2, 3]);
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (problem == "missing") return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = problem == "wrong-id" ? "another-version" : "pinned",
                project_id = problem == "wrong-project" ? "other-project" : "dependency",
                name = "Dependency",
                version_number = "1.0",
                version_type = "release",
                date_published = DateTimeOffset.UnixEpoch,
                game_versions = new[] { problem == "wrong-game" ? "1.20.4" : "1.20.1" },
                loaders = new[] { problem == "wrong-loader" ? "quilt" : "fabric" },
                files = new[] { new { filename = "dependency.jar", url = "https://cdn.example/dependency.jar", size = 3, primary = true } },
                dependencies = Array.Empty<object>()
            }));
        });
        try
        {
            var client = new ModrinthClient(new HttpClient(handler), new HttpClient(handler));
            var version = Version("main", [new ModrinthDependency("pinned", "dependency", "required")]);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.InstallVersionAsync(version, "1.20.1", directory, requireDependencies: true));
            Assert.Contains("必需", error.Message);
            Assert.DoesNotContain(requests, request => request.Contains("/project/", StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(directory, "dependency.jar")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ModrinthVersion Version(string id, IReadOnlyList<ModrinthDependency> dependencies) =>
        new(id, id, id, "1.0", "release", DateTimeOffset.UnixEpoch, ["1.20.1"], ["fabric"],
            [new ModrinthFile(id + ".jar", "https://cdn.example/" + id + ".jar", 3,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(new byte[] { 1, 2, 3 })), true)], dependencies);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
