using System.IO.Compression;
using System.Net;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class OptiFineIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-optifine-identity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProductionCombinedBackendDownloadsAndReusesExactlyTheCapturedInstallerWithoutCatalogLookup()
    {
        var selected = new OptiFineVersion("1.20.1", "HD_U", "I6", "OptiFine_1.20.1_HD_U_I6.jar", "47.2.18");
        using var jar = new MemoryStream();
        using (var archive = new ZipArchive(jar, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("fixture.txt").Open());
            writer.Write("captured installer fixture");
        }
        var bytes = jar.ToArray();
        var requests = new List<Uri>();
        using var client = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            Assert.Equal("?f=" + selected.FileName, uri.Query.Split('&')[0]);
            return uri.AbsolutePath switch
            {
                "/adloadx" => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent($"<a href=\"downloadx?f={selected.FileName}&amp;x=fixture\">Download</a>") },
                "/downloadx" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
                _ => throw new InvalidOperationException("Unexpected catalog lookup: " + uri)
            };
        }));
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var optifine = new OptiFineService(new VersionRepository(_root), http: client,
            installerCacheDirectory: Path.Combine(_root, "cache"));
        var backend = new CombinedInstallBackend(_root, MirrorMode.Official, 1, services.JavaProvisioner,
            optifine, services.Modrinth, services.Optifabric);
        var plan = CombinedInstallPlan.Create(_root, "1.20.1", false, "Forge", "47.2.18", selected,
            false, false, "captured");
        var path = await backend.DownloadOptiFineAsync(plan, new Progress<JobProgress>(), CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "cache", selected.FileName), path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(path, await backend.DownloadOptiFineAsync(plan, new Progress<JobProgress>(), CancellationToken.None));
        Assert.Equal(2, requests.Count);
        Assert.All(requests, uri => Assert.Equal("optifine.net", uri.Host));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions")));
    }

    [Fact]
    public async Task StandaloneInstallRejectsMissingSelectionMetadataBeforeCreatingAPlanOrStagingFiles()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var service = new StandaloneLoaderInstallService(services,
            _ => throw new InvalidOperationException("Must not start installation"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(
            new StandaloneLoaderInstallRequest(_root, "1.20.1", "OptiFine", "I6"),
            new Progress<JobProgress>(), null, CancellationToken.None));
        Assert.False(Directory.Exists(_root));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
