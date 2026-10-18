using System.Net;
using System.Text;
using PCL.Launch.Auth;

namespace PCL.Launch.Tests;

public class MicrosoftSkinServiceTests
{
    [Fact]
    public async Task ParsesSkinAndCachesPng()
    {
        var png = new byte[] { 137, 80, 78, 71 };
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("minecraft/profile"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"skins":[{"url":"https://example.com/skin.png","variant":"SLIM"}],"capes":[]}
                        """, Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(png)
            };
        });
        var cache = Path.Combine(Path.GetTempPath(), "pcl-ms-skin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MicrosoftSkinService(new HttpClient(handler), cache);
            var account = new MicrosoftAccount(
                "Steve", "uuid", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
            var skin = await service.GetSkinAsync(account);
            Assert.NotNull(skin);
            Assert.Equal("SLIM", skin!.Variant);
            var path = await service.DownloadSkinAsync(account);
            Assert.NotNull(path);
            Assert.Equal(png, await File.ReadAllBytesAsync(path!));
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
