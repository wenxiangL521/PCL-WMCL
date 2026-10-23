using System.Net;
using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public sealed class CleanroomServiceTests
{
    [Fact]
    public void ParsesInstallerAssetsAndSkipsDraftsWithoutInstallers()
    {
        const string json = """
            [
              {
                "tag_name": "0.7.11-alpha",
                "draft": false,
                "published_at": "2026-10-19T10:09:31Z",
                "html_url": "https://github.com/CleanroomMC/Cleanroom/releases/tag/0.7.11-alpha",
                "assets": [
                  { "name": "cleanroom-0.7.11-alpha-universal.jar", "browser_download_url": "https://example.invalid/universal.jar" },
                  { "name": "cleanroom-0.7.11-alpha-installer.jar", "browser_download_url": "https://example.invalid/installer.jar" }
                ]
              },
              {
                "tag_name": "0.7.10-alpha",
                "draft": true,
                "assets": [
                  { "name": "cleanroom-0.7.10-alpha-installer.jar", "browser_download_url": "https://example.invalid/draft.jar" }
                ]
              },
              {
                "tag_name": "no-assets",
                "draft": false,
                "assets": []
              }
            ]
            """;

        var versions = CleanroomService.Parse(json);

        var version = Assert.Single(versions);
        Assert.Equal("0.7.11-alpha", version.Tag);
        Assert.Equal("cleanroom-0.7.11-alpha-installer.jar", version.InstallerFileName);
        Assert.Equal("https://example.invalid/installer.jar", version.InstallerUrl);
        Assert.True(version.IsBeta);
        Assert.Equal("Alpha/测试版", version.Detail);
        Assert.NotNull(version.Published);
    }

    [Fact]
    public async Task RejectsNonArrayResponse()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") })));
        var service = new CleanroomService(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetVersionsAsync());
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
