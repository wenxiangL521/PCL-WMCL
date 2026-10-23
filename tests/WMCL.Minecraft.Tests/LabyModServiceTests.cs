using System.Net;
using System.Text;
using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public sealed class LabyModServiceTests
{
    [Fact]
    public void ReadsProductionAndSnapshotManifests()
    {
        var production = LabyModService.ReadManifest(
            """{"labyModVersion":"47.4","sha1":"abc","size":123,"commitReference":"e1b5f160"}""",
            "production");
        var snapshot = LabyModService.ReadManifest(
            """{"labyModVersion":"48","sha1":"def","size":456}""",
            "snapshot");

        Assert.Equal("47.4", production.Version);
        Assert.Equal("正式版", production.DisplayName);
        Assert.Equal("LabyMod4ProductionInstaller.jar", production.InstallerFileName);
        Assert.Equal("https://releases.labymod.net/api/v1/installer/production/java", production.InstallerUrl);
        Assert.Equal("快照版", snapshot.DisplayName);
        Assert.Equal("LabyMod4SnapshotInstaller.jar", snapshot.InstallerFileName);
    }

    [Fact]
    public async Task DownloadsInstallerWithoutTreatingManifestSha1AsInstallerHash()
    {
        var payload = Encoding.UTF8.GetBytes("LabyMod installer fixture");
        var version = new LabyModVersionInfo("production", "47.4",
            "https://example.invalid/installer.jar", "LabyMod4ProductionInstaller.jar");
        var destination = Path.Combine(Path.GetTempPath(), "labymod-download-" + Guid.NewGuid().ToString("N") + ".jar");
        using var http = new HttpClient(new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
        var service = new LabyModService(http);
        try
        {
            await service.DownloadInstallerAsync(version, destination);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }

    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
