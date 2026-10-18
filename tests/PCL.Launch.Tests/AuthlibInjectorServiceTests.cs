using System.Net;
using System.Security.Cryptography;
using System.Text;
using PCL.Launch.Auth;

namespace PCL.Launch.Tests;

public class AuthlibInjectorServiceTests
{
    [Fact]
    public async Task DownloadsAndReturnsCachedJar()
    {
        var bytes = new byte[] { (byte)'P', (byte)'K', 3, 4, 1, 2, 3 };
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("latest.json"))
                return Json(
                    "{\"version\":\"1.2.3\",\"download_url\":\"https://example.com/injector.jar\"," +
                    "\"checksums\":{\"sha256\":\"" + hash + "\"}}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        var cache = Path.Combine(Path.GetTempPath(), "pcl-injector-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AuthlibInjectorService(new HttpClient(handler), cache);
            var path = await service.EnsureAsync();
            Assert.True(File.Exists(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(path, await service.EnsureAsync());
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
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
