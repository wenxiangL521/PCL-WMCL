using System.Net;
using System.Text;
using System.Text.Json;
using WMCL.Launch.Auth;

namespace WMCL.Launch.Tests;

public class AuthlibSkinServiceTests
{
    [Fact]
    public async Task DecodesSkinTextureAndCachesPng()
    {
        var texture = Convert.ToBase64String(Encoding.UTF8.GetBytes("""
            {"textures":{"SKIN":{"url":"https://example.com/skin.png","metadata":{"model":"slim"}}}}
            """));
        var png = new byte[] { 137, 80, 78, 71 };
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/sessionserver/"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"properties\":[{\"name\":\"textures\",\"value\":\"" + texture + "\"}]}",
                        Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(png)
            };
        });
        var cache = Path.Combine(Path.GetTempPath(), "wmcl-skin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AuthlibSkinService(new HttpClient(handler), cache);
            var account = new AuthlibAccount(
                "https://example.com/api/yggdrasil", "Steve",
                "069a79f4-44e9-4726-a5be-fca90e38aaf5", "token", "client");
            var path = await service.DownloadSkinAsync(account);
            Assert.NotNull(path);
            Assert.Equal(png, await File.ReadAllBytesAsync(path!));

            var skin = await service.GetSkinAsync(account);
            Assert.NotNull(skin);
            Assert.Equal("https://example.com/skin.png", skin!.SkinUrl);
            Assert.Equal("slim", skin.Model);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
