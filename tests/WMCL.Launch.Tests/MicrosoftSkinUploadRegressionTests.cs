using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WMCL.Launch.Auth;

namespace WMCL.Launch.Tests;

public sealed class MicrosoftSkinUploadRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-skin-service-" + Guid.NewGuid().ToString("N"));
    private static MicrosoftAccount Account => new("Player", "uuid", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public MicrosoftSkinUploadRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("classic")]
    [InlineData("slim")]
    public async Task UploadSendsTheChosenVariantAndUnchangedLocalFileInMultipart(string variant)
    {
        var bytes = PngHeader();
        var file = Path.Combine(_root, "chosen.png");
        await File.WriteAllBytesAsync(file, bytes);
        var requests = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.minecraftservices.com/minecraft/profile/skins", request.RequestUri!.AbsoluteUri);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "token"), request.Headers.Authorization);
            var content = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = content.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal(variant, await parts["variant"].ReadAsStringAsync());
            Assert.Equal("chosen.png", parts["file"].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("image/png", parts["file"].Headers.ContentType!.MediaType);
            Assert.Equal(bytes, await parts["file"].ReadAsByteArrayAsync());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await new MicrosoftSkinService(http, _root).UploadSkinAsync(Account, file, variant);
        Assert.Equal(1, requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
    }

    [Theory]
    [InlineData("invalid-png")]
    [InlineData("wrong-size")]
    [InlineData("expired")]
    [InlineData("missing-token")]
    [InlineData("wrong-variant")]
    public async Task InvalidFileOrAccountIsRejectedBeforeTheNetwork(string problem)
    {
        var file = Path.Combine(_root, "skin.png");
        await File.WriteAllBytesAsync(file, problem == "invalid-png" ? "not a PNG"u8.ToArray() : PngHeader(problem == "wrong-size" ? 32 : 64));
        var account = problem == "expired" ? Account with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
            : problem == "missing-token" ? Account with { AccessToken = "" } : Account;
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            throw new InvalidOperationException("network must not be used");
        }));
        await Assert.ThrowsAnyAsync<Exception>(() => new MicrosoftSkinService(http, _root)
            .UploadSkinAsync(account, file, problem == "wrong-variant" ? "unknown" : "slim"));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ActiveSkinIsSelectedInsteadOfTheFirstInactiveHistoryEntry()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"skins":[{"state":"INACTIVE","url":"https://example.invalid/old.png","variant":"CLASSIC"},{"state":"ACTIVE","url":"https://example.invalid/new.png","variant":"SLIM"}]}""")
        })));
        var info = await new MicrosoftSkinService(http, _root).GetSkinAsync(Account);
        Assert.Equal("https://example.invalid/new.png", info!.SkinUrl);
        Assert.Equal("SLIM", info.Variant);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshDownloadsChangedSkinAndKeepsThePreviousPreviewFile(bool sameUrl)
    {
        var generation = 1;
        var images = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.minecraftservices.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"skins":[{"state":"ACTIVE","url":"https://textures.example.invalid/{{(sameUrl ? "skin" : generation.ToString())}}.png","variant":"SLIM"}]}""")
                });
            images++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("image generation " + generation))
            });
        }));
        var service = new MicrosoftSkinService(http, _root);
        var first = await service.DownloadSkinAsync(Account);
        var firstBytes = await File.ReadAllBytesAsync(first!);
        generation = 2;
        var second = await service.DownloadSkinAsync(Account);
        Assert.NotEqual(first, second);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first!));
        Assert.Equal("image generation 2", await File.ReadAllTextAsync(second!));
        Assert.Equal(2, images);
        Assert.DoesNotContain(Directory.GetFiles(_root), path => path.EndsWith(".tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedUploadAndProfileReturnAnExplicitReloginError(bool upload)
    {
        var file = Path.Combine(_root, "skin.png");
        await File.WriteAllBytesAsync(file, PngHeader());
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var service = new MicrosoftSkinService(http, _root);
        var error = upload
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadSkinAsync(Account, file))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSkinAsync(Account));
        Assert.Contains("重新登录", error.Message);
    }

    private static byte[] PngHeader(int width = 64)
    {
        var result = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(result, 0);
        new byte[] { 0, 0, 0, 13 }.CopyTo(result, 8);
        "IHDR"u8.CopyTo(result.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(20), 64);
        return result;
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
