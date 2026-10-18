using System.Net;
using System.Security.Cryptography;
using WMCL.Download;

namespace WMCL.Minecraft.Tests;

public class DownloadManagerShaTests
{
    [Fact]
    public async Task VerifiesShaAfterClosingPartFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var data = "download data"u8.ToArray();
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(data));
        var destination = Path.Combine(root, "file.bin");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        });
        try
        {
            var manager = new DownloadManager(new HttpClient(handler));
            await manager.DownloadAsync(
                [DownloadItem.Create("https://example.com/file", destination, sha1, data.Length)]);
            Assert.Equal(data, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".part"));
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
