using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using WMCL.Modding.OptiFine;

namespace WMCL.Minecraft.Tests;

public sealed class OptiFabricServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-optifabric-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CatalogUsesExactGameAndLoaderTagsAndSortsByInstant()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = 322385,
            files = new[]
            {
                Entry(5025647, "1.14.3", "2024-01-12T00:24:57Z", ["1.20.1", "Fabric"]),
                Entry(5000001, "1.13.25", "2024-01-12T02:24:56+02:00", ["1.20.1", "Fabric"]),
                Entry(6000001, "2.0", "2025-01-01T00:00:00Z", ["1.20.10", "Fabric"]),
                Entry(6000002, "2.1", "2025-01-01T00:00:00Z", ["1.20.1", "Forge"])
            }
        });
        var files = OptiFabricService.ParseFiles(json, "1.20.1");
        Assert.Equal([5025647L, 5000001L], files.Select(file => file.Id));
        Assert.Equal("1.14.3", files[0].Version);
        Assert.Contains("1.14.3", files[0].Label);
    }

    [Theory]
    [InlineData("{\"id\":1,\"files\":[]}")]
    [InlineData("{\"id\":322385}")]
    public void RejectsAnUnrelatedProjectOrMissingFileList(string json) =>
        Assert.Throws<InvalidDataException>(() => OptiFabricService.ParseFiles(json, "1.20.1"));

    [Theory]
    [InlineData("../optifabric-1.14.3.jar")]
    [InlineData("folder/optifabric-1.14.3.jar")]
    [InlineData("folder\\optifabric-1.14.3.jar")]
    [InlineData("different-mod.jar")]
    public void RejectsUnsafeOrUnrelatedFileNames(string name)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = 322385,
            files = new[] { new { id = 5025647, name, display = "OptiFabric", filesize = 100, uploaded_at = "2024-01-01T00:00:00Z", versions = new[] { "1.20.1", "Fabric" } } }
        });
        Assert.Throws<InvalidDataException>(() => OptiFabricService.ParseFiles(json, "1.20.1"));
    }

    [Fact]
    public async Task FetchesTheOriginalCurseForgeProjectWithoutAnApiKey()
    {
        Uri? requested = null;
        var json = JsonSerializer.Serialize(new { id = 322385, files = new[] { Entry(5025647, "1.14.3", "2024-01-01T00:00:00Z", ["1.20.1", "Fabric"]) } });
        var http = new HttpClient(new Handler(request =>
        {
            requested = request.RequestUri;
            Assert.False(request.Headers.Contains("x-api-key"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var service = new OptiFabricService(http);
        Assert.Single(await service.GetFilesAsync("1.20.1"));
        Assert.Equal("https://api.cfwidget.com/322385", requested!.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.14.4")]
    [InlineData("1.15.2")]
    public async Task OriginsVersionsDoNotSilentlyUseTheRegularOptiFabricProject(string minecraft)
    {
        var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected")));
        Assert.Empty(await new OptiFabricService(http).GetFilesAsync(minecraft));
        Assert.False(OptiFabricService.IsCompatible(File(100, minecraft), minecraft));
    }

    [Fact]
    public async Task InstallsTheCapturedFileAndReusesAValidExistingJar()
    {
        var bytes = Jar("optifabric", "1.14.3");
        var requests = new List<Uri>();
        var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var service = new OptiFabricService(downloadHttp: http);
        var file = File(bytes.Length) with { Id = 5025007 };
        var destination = await service.InstallAsync(file, "1.20.1", _root);
        Assert.Equal(Path.Combine(_root, file.FileName), destination);
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(destination));
        Assert.Equal("https://edge.forgecdn.net/files/5025/007/optifabric-1.14.3.jar", requests.Single().AbsoluteUri);
        Assert.Equal(destination, await service.InstallAsync(file, "1.20.1", _root));
        Assert.Single(requests);
    }

    [Theory]
    [InlineData("other-mod", "1.14.3")]
    [InlineData("optifabric", "1.13.25")]
    public async Task RejectsUnrelatedOrDifferentVersionJarsBeforeInstalling(string modId, string version)
    {
        var bytes = Jar(modId, version);
        var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OptiFabricService(downloadHttp: http)
            .InstallAsync(File(bytes.Length), "1.20.1", _root));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task PreservesAnExistingFileThatDoesNotMatchTheSelectedRelease()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "optifabric-1.14.3.jar");
        var original = Jar("other-mod", "1.14.3");
        await System.IO.File.WriteAllBytesAsync(destination, original);
        var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OptiFabricService(downloadHttp: http)
            .InstallAsync(File(original.Length), "1.20.1", _root));
        Assert.Equal(original, await System.IO.File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task RejectsAFileForAnotherMinecraftVersionAndPropagatesCancellation()
    {
        var service = new OptiFabricService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(File(100), "1.20.4", _root));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(File(100), "1.20.1", _root, ct: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetFilesAsync("1.20.1", cancellation.Token));
        Assert.False(Directory.Exists(_root));
    }

    private static OptiFabricFile File(long size, string minecraft = "1.20.1") =>
        new(5025647, "optifabric-1.14.3.jar", "OptiFabric-v1.14.3", DateTimeOffset.UnixEpoch, size, [minecraft, "Fabric"]);

    private static object Entry(long id, string version, string published, string[] versions) =>
        new { id, name = $"optifabric-{version}.jar", display = "OptiFabric-v" + version, filesize = 100, uploaded_at = published, versions };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalHashAcceptsOnlyTheOriginalJar(bool existing)
    {
        var bytes = Jar("optifabric", "1.14.3");
        var file = File(bytes.Length) with { Sha1 = Convert.ToHexStringLower(SHA1.HashData(bytes)) };
        var requests = 0;
        var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        if (existing)
        {
            Directory.CreateDirectory(_root);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(_root, file.FileName), bytes);
        }
        var destination = await new OptiFabricService(downloadHttp: http).InstallAsync(file, "1.20.1", _root);
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(destination));
        Assert.Equal(existing ? 0 : 1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalHashRejectsSameVersionAndSizeWithDifferentBytes(bool existing)
    {
        var original = Jar("optifabric", "1.14.3");
        var changed = Jar("optifabric", "1.14.3", day: 2);
        Assert.Equal(original.Length, changed.Length);
        Assert.NotEqual(original, changed);
        var file = File(original.Length) with { Sha1 = Convert.ToHexStringLower(SHA1.HashData(original)) };
        var destination = Path.Combine(_root, file.FileName);
        var requests = 0;
        var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(changed) };
        }));
        if (existing)
        {
            Directory.CreateDirectory(_root);
            await System.IO.File.WriteAllBytesAsync(destination, changed);
        }
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => new OptiFabricService(downloadHttp: http).InstallAsync(file, "1.20.1", _root));
        Assert.True(failure.ToString().Contains("SHA1", StringComparison.OrdinalIgnoreCase) || failure.Message.Contains("哈希"));
        if (existing)
        {
            Assert.Equal(0, requests);
            Assert.Equal(changed, await System.IO.File.ReadAllBytesAsync(destination));
        }
        else Assert.False(System.IO.File.Exists(destination));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public async Task MalformedHistoricalHashFailsBeforeDownloading(string sha1)
    {
        var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OptiFabricService(downloadHttp: http)
            .InstallAsync(File(100) with { Sha1 = sha1 }, "1.20.1", _root));
        Assert.False(Directory.Exists(_root));
    }

    private static byte[] Jar(string id, string version, int day = 1)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var metadata = archive.CreateEntry("fabric.mod.json");
            metadata.LastWriteTime = new DateTimeOffset(2024, 1, day, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(metadata.Open());
            writer.Write(JsonSerializer.Serialize(new { id, version }));
        }
        return memory.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
