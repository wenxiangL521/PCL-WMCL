using System.Net;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinPreviewLoaderTests
{
    [AvaloniaFact]
    public async Task OfficialTextureIsLoadedWithoutCredentialsAndHttpIsUpgraded()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var png = new MemoryStream();
        bitmap.Save(png);
        var requests = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests++;
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("textures.minecraft.net", request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png.ToArray()) });
        }));
        using var cape = await new SkinPreviewLoader(http).LoadCapeAsync(new MicrosoftCape("cape", "Cape", "http://textures.minecraft.net/texture/test", false), default);
        Assert.NotNull(cape);
        Assert.Equal(new PixelSize(64, 32), cape.PixelSize);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://textures.minecraft.net.evil.invalid/texture/test")]
    [InlineData("https://user@textures.minecraft.net/texture/test")]
    [InlineData("https://textures.minecraft.net:8443/texture/test")]
    [InlineData("https://example.invalid/texture/test")]
    public async Task UntrustedAddressesAreRejectedBeforeNetwork(string url)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("must not request")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SkinPreviewLoader(http)
            .LoadCapeAsync(new MicrosoftCape("cape", "Cape", url, false), default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrOversizedPngIsRejectedBeforeDecoding(bool hugeDimensions)
    {
        var bytes = hugeDimensions
            ? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 127, 255, 255, 255, 127, 255, 255, 255 }
            : "not png"u8.ToArray();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(bytes) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SkinPreviewLoader(http)
            .LoadCapeAsync(new MicrosoftCape("cape", "Cape", "https://textures.minecraft.net/texture/test", false), default));
    }

    [Fact]
    public async Task NoCapeDoesNotRequestAnyTexture()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("must not request")));
        Assert.Null(await new SkinPreviewLoader(http).LoadCapeAsync(new MicrosoftCape("", "无披风", null, false), default));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
