using System.Net;
using System.Security.Cryptography;
using PCL.Download;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class ServerInstallerTests
{
    [Fact]
    public async Task DownloadsServerJarAndCreatesEulaProperties()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-server-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Test");
        Directory.CreateDirectory(version);
        var bytes = "server jar"u8.ToArray();
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(bytes));
        var json = "{\"id\":\"Test\",\"downloads\":{\"server\":{"
                   + "\"url\":\"https://example.com/server.jar\","
                   + "\"sha1\":\"" + sha1 + "\",\"size\":" + bytes.Length
                   + "}},\"libraries\":[]}";
        await File.WriteAllTextAsync(Path.Combine(version, "Test.json"), json);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        try
        {
            var result = await ServerInstaller.EnsureAsync(
                root, "Test", new DownloadManager(new HttpClient(handler)));
            Assert.True(result.Downloaded);
            Assert.True(File.Exists(result.ServerJar));
            Assert.Contains("eula=false", await File.ReadAllTextAsync(result.EulaFile));
            Assert.Contains("server-port=25565",
                await File.ReadAllTextAsync(result.PropertiesFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
