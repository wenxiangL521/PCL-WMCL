using System.Net;
using System.Text;
using PCL.Modding.Modrinth;

namespace PCL.Minecraft.Tests;

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
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
        });
        var directory = Path.Combine(Path.GetTempPath(), "pcl-modrinth-" + Guid.NewGuid().ToString("N"));
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
        var directory = Path.Combine(Path.GetTempPath(), "pcl-content-" + Guid.NewGuid().ToString("N"));
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

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
