using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using WMCL.App.Services;
using WMCL.Minecraft;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class ComponentRowActionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-row-action-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(_root, "chosen.jar");
    private readonly byte[] _jar = Jar();

    public ComponentRowActionServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerCancellationOrStaleSelectionNeverStartsDownloading(bool stale)
    {
        var current = true;
        using var http = Client(_ => throw new InvalidOperationException("Unexpected network"));
        var service = Service(http, (_, _) =>
        {
            current = !stale;
            return Task.FromResult<string?>(stale ? Target : null);
        });
        Assert.False(await service.ExecuteAsync(Request(), () => current, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task InvalidMirrorFallsBackAndReplacesExistingFileOnlyAfterSuccessfulValidation()
    {
        File.WriteAllText(Target, "original");
        var requests = 0;
        using var http = Client(_ =>
        {
            Assert.Equal("original", File.ReadAllText(Target));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(++requests == 1 ? "<html>error</html>"u8.ToArray() : _jar) };
        });
        Assert.True(await Service(http).ExecuteAsync(Request(), () => true, CancellationToken.None));
        Assert.Equal(_jar, File.ReadAllBytes(Target));
        Assert.Equal(2, requests);
        AssertNoPartFiles();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("too-small")]
    [InlineData("hash")]
    public async Task InvalidPayloadSizeOrHashNeverReplacesExistingFile(string failure)
    {
        File.WriteAllText(Target, "original");
        using var http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(failure == "invalid" ? "bad"u8.ToArray() : _jar) });
        var request = Request() with
        {
            Artifact = new InstallerArtifact("selected.jar", ["https://fixture.test/installer.jar"],
                failure == "too-small" ? _jar.Length + 1 : 1, failure == "hash" ? new string('0', 40) : null)
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(http).ExecuteAsync(request, () => true, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(Target));
        AssertNoPartFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidMd5OrSha1FromMetadataIsCheckedBeforePublishing(bool md5)
    {
        using var http = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_jar) });
        var request = Request() with
        {
            Artifact = new InstallerArtifact("selected.jar", ["https://fixture.test/installer.jar"], Hash:
                Convert.ToHexString(md5 ? MD5.HashData(_jar) : SHA1.HashData(_jar)))
        };
        Assert.True(await Service(http).ExecuteAsync(request, () => true, CancellationToken.None));
        Assert.Equal(_jar, File.ReadAllBytes(Target));
        AssertNoPartFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrSelectionChangeDuringDownloadPreservesExistingFile(bool cancel)
    {
        File.WriteAllText(Target, "original");
        using var cancellation = new CancellationTokenSource();
        var current = true;
        using var http = Client(_ =>
        {
            if (cancel) cancellation.Cancel();
            else current = false;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_jar) };
        });
        var task = Service(http).ExecuteAsync(Request(), () => current, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else Assert.False(await task);
        Assert.Equal("original", File.ReadAllText(Target));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task ExternalTargetModificationWhileDownloadingIsNotOverwritten()
    {
        File.WriteAllText(Target, "original");
        using var http = Client(_ =>
        {
            File.WriteAllText(Target, "external edit");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_jar) };
        });
        await Assert.ThrowsAsync<IOException>(() => Service(http).ExecuteAsync(Request(), () => true, CancellationToken.None));
        Assert.Equal("external edit", File.ReadAllText(Target));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task LinkActionsOpenCapturedAddressWithoutPickerOrDownload()
    {
        var opened = new List<string>();
        using var http = Client(_ => throw new InvalidOperationException("Unexpected network"));
        var service = new ComponentRowActionService(new OptiFineService(new VersionRepository(_root), http: http), http,
            (_, _) => throw new InvalidOperationException("Unexpected picker"), opened.Add);
        var request = new ComponentRowActionRequest("更新日志", "https://optifine.net/changelog?f=selected.jar");
        Assert.True(await service.ExecuteAsync(request, () => true, CancellationToken.None));
        Assert.Equal(request.Url, Assert.Single(opened));
        Assert.False(await service.ExecuteAsync(request, () => false, CancellationToken.None));
        Assert.Single(opened);
    }

    [Fact]
    public async Task OptiFineSaveFallsBackToTheExactMirrorWithoutCatalogLookupOrRunningAnInstaller()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var stream = archive.CreateEntry("fixture.txt", CompressionLevel.NoCompression).Open())
            stream.Write(new byte[70 * 1024]);
        var bytes = buffer.ToArray();
        var selected = new OptiFineVersion("1.20.1", "HD_U", "I6_pre1",
            "preview_OptiFine_1.20.1_HD_U_I6_pre1.jar", "");
        var urls = new List<string>();
        using var http = Client(request =>
        {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.Host == "optifine.net"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        Assert.True(await Service(http).ExecuteAsync(new("另存为", OptiFine: selected), () => true, CancellationToken.None));
        Assert.Equal(2, urls.Count);
        Assert.Equal("https://optifine.net/adloadx?f=" + selected.FileName, urls[0]);
        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.20.1/HD_U_I6/pre1", urls[1]);
        Assert.Equal(bytes, File.ReadAllBytes(Target));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions")));
        Assert.False(Directory.Exists(Path.Combine(_root, "cache")));
        AssertNoPartFiles();
    }

    private ComponentRowActionService Service(HttpClient http, Func<string, CancellationToken, Task<string?>>? pick = null) =>
        new(new OptiFineService(new VersionRepository(_root), http: http, installerCacheDirectory: Path.Combine(_root, "cache")),
            http, pick ?? ((_, _) => Task.FromResult<string?>(Target)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceTimeoutFallsBackButCallerCancellationStopsImmediately(bool cancel)
    {
        File.WriteAllText(Target, "original");
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var http = Client(_ =>
        {
            if (++requests == 1)
            {
                if (cancel) cancellation.Cancel();
                throw new TaskCanceledException("fixture source timeout");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_jar) };
        });
        var task = Service(http).ExecuteAsync(Request(), () => true, cancellation.Token);
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal("original", File.ReadAllText(Target));
            Assert.Equal(1, requests);
        }
        else
        {
            Assert.True(await task);
            Assert.Equal(_jar, File.ReadAllBytes(Target));
            Assert.Equal(2, requests);
        }
        AssertNoPartFiles();
    }

    [Fact]
    public async Task OfficialOptiFineAddressTimeoutStillDownloadsCapturedMirror()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var stream = archive.CreateEntry("fixture.txt", CompressionLevel.NoCompression).Open())
            stream.Write(new byte[70 * 1024]);
        var bytes = buffer.ToArray();
        var selected = new OptiFineVersion("1.20.1", "HD_U", "I6", "OptiFine_1.20.1_HD_U_I6.jar", "");
        using var http = Client(request =>
        {
            if (request.RequestUri!.Host == "optifine.net") throw new TaskCanceledException("fixture address timeout");
            Assert.Equal(OptiFineService.GetMirrorDownloadUrl(selected), request.RequestUri.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        Assert.True(await Service(http).ExecuteAsync(new("另存为", OptiFine: selected), () => true, CancellationToken.None));
        Assert.Equal(bytes, File.ReadAllBytes(Target));
        AssertNoPartFiles();
    }

    private static ComponentRowActionRequest Request() => new("另存为", Artifact: new InstallerArtifact("selected.jar",
        ["https://fixture.test/mirror.jar", "https://fixture.test/official.jar"]));

    private void AssertNoPartFiles() => Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));

    private static byte[] Jar()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("fixture.txt").Open());
            writer.Write("fixture installer, not executable");
        }
        return buffer.ToArray();
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
