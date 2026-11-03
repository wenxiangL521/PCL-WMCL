using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Launch.Java;

namespace WMCL.Launch.Tests;

public sealed class ExactJavaInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-exact-java-" + Guid.NewGuid().ToString("N"));
    private static (string Os, string Architecture) Platform => JavaProvisioner.TemurinPlatform(OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture);

    [Theory]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(17)]
    [InlineData(21)]
    [InlineData(25)]
    public async Task ExactInstallRequestsTheChosenMajorAndVerifiesTheInstalledRuntime(int major)
    {
        var archive = Archive();
        var requests = new List<Uri>();
        var locator = new JavaLocator(_ => [], (path, _) =>
        {
            Assert.True(File.Exists(path));
            return Task.FromResult<JavaRuntime?>(new(path, new Version(major, 0, 1), major,
                Platform.Architecture, Path.GetDirectoryName(Path.GetDirectoryName(path))!, "fixture"));
        });
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(request.RequestUri!.Host == "api.adoptium.net"
                ? Json(Metadata(major, archive))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }));
        var service = new JavaProvisioner(locator, http, _root);
        var result = await service.EnsureExactAsync(major);
        Assert.Equal(major, result.MajorVersion);
        Assert.True(File.Exists(result.Path));
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, result.Path);
        Assert.Contains($"/latest/{major}/hotspot", requests[0].AbsolutePath);
        Assert.Contains("architecture=" + Platform.Architecture, requests[0].Query);
        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain(Directory.GetDirectories(_root), path => Path.GetFileName(path).StartsWith(".java-install-"));
        // The managed runtime is probed/reused, without downloading the archive again.
        Assert.Equal(result.Path, (await service.EnsureExactAsync(major)).Path);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task ForbiddenMetadataApiFallsBackToTheSameVendorReleaseWithoutChangingMajorOrArchitecture()
    {
        var archive = OperatingSystem.IsWindows() ? Archive() : TarArchive();
        var extension = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        var architecture = Platform.Architecture == "x86" ? "x86-32" : Platform.Architecture;
        var name = $"OpenJDK11U-jre_{architecture}_{Platform.Os}_hotspot_fixture{extension}";
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "api.adoptium.net") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (request.RequestUri.Host == "api.github.com") return Task.FromResult(Json(JsonSerializer.Serialize(new
            {
                assets = new[] { new { name, browser_download_url = "https://github.com/adoptium/runtime" + extension,
                    size = archive.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(archive)) } }
            })));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }));
        var locator = new JavaLocator(_ => [], (path, _) => Task.FromResult<JavaRuntime?>(
            new(path, new Version(11, 0, 1), 11, Platform.Architecture, Path.GetDirectoryName(Path.GetDirectoryName(path))!, "fixture")));
        var runtime = await new JavaProvisioner(locator, http, _root).EnsureExactAsync(11);
        Assert.Equal(11, runtime.MajorVersion);
        Assert.True(File.Exists(runtime.Path));
        Assert.Equal(3, requests.Count);
        Assert.Contains("/temurin11-binaries/", requests[1].AbsolutePath);
    }

    [Fact]
    public async Task ANewerSystemRuntimeNeverSatisfiesAnExplicitJavaElevenSelection()
    {
        var locator = new JavaLocator(_ => ["system"], (path, _) => Task.FromResult<JavaRuntime?>(
            new(path, new Version(21, 0, 2), 21, Platform.Architecture, "system", "fixture")));
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(Json("[]"));
        }));
        var service = new JavaProvisioner(locator, http, _root);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.EnsureExactAsync(11));
        Assert.Equal(2, requests.Count); // JRE, then JDK fallback, both exact 11.
        Assert.All(requests, address => Assert.Contains("/latest/11/hotspot", address.AbsolutePath));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(true, false, false, Architecture.X64, "windows", "x64")]
    [InlineData(true, false, false, Architecture.X86, "windows", "x86")]
    [InlineData(true, false, false, Architecture.Arm64, "windows", "aarch64")]
    [InlineData(false, true, false, Architecture.X64, "mac", "x64")]
    [InlineData(false, true, false, Architecture.Arm64, "mac", "aarch64")]
    [InlineData(false, false, true, Architecture.X64, "linux", "x64")]
    [InlineData(false, false, true, Architecture.X86, "linux", "x86")]
    [InlineData(false, false, true, Architecture.Arm64, "linux", "aarch64")]
    public void MetadataQueriesKeepTheNativePlatformWithoutPretendingArmIsX86(bool windows, bool mac, bool linux,
        Architecture architecture, string expectedOs, string expectedArch)
    {
        Assert.Equal((expectedOs, expectedArch), JavaProvisioner.TemurinPlatform(windows, mac, linux, architecture));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(25)]
    public void MetadataRejectsADifferentMajorOrArchitectureInsteadOfSilentlySubstituting(int major)
    {
        var archive = Archive();
        Assert.Null(JavaProvisioner.SelectTemurinAsset(Metadata(major + 1, archive), major, Platform.Os, Platform.Architecture));
        Assert.Null(JavaProvisioner.SelectTemurinAsset(Metadata(major, archive), major, Platform.Os,
            Platform.Architecture == "x64" ? "aarch64" : "x64"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChecksumOrExecutableVersionFailureNeverPublishesTheRuntime(bool wrongVersion)
    {
        var archive = Archive();
        var metadata = wrongVersion ? Metadata(25, archive) : Metadata(25, archive).Replace(Convert.ToHexString(SHA256.HashData(archive)), new string('0', 64));
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.adoptium.net"
            ? Json(metadata) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) })));
        var locator = new JavaLocator(_ => [], (path, _) => Task.FromResult<JavaRuntime?>(
            new(path, new Version(17, 0, 1), 17, Platform.Architecture, "fixture", "fixture")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new JavaProvisioner(locator, http, _root).EnsureExactAsync(25));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    public async Task ArchiveTraversalIsRejected(string entry)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "bad.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await using var stream = zip.CreateEntry(entry).Open();
            await stream.WriteAsync("bad"u8.ToArray());
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => JavaProvisioner.ExtractTemurinAsync(path, Path.Combine(_root, "extract"), true, default));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "extract"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TarInternalLinksAreMaterializedButExternalLinksAreRejected()
    {
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, "fixture.tar.gz");
        await using (var stream = File.Create(archive))
        await using (var gzip = new GZipStream(stream, CompressionMode.Compress))
        using (var tar = new TarWriter(gzip))
        {
            var file = new PaxTarEntry(TarEntryType.RegularFile, "jdk/lib/original") { DataStream = new MemoryStream("fixture"u8.ToArray()) };
            await tar.WriteEntryAsync(file);
            await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "jdk/lib/copy") { LinkName = "original" });
        }
        var extracted = Path.Combine(_root, "extract");
        await JavaProvisioner.ExtractTemurinAsync(archive, extracted, false, default);
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(extracted, "jdk", "lib", "copy")));
        Assert.Null(new FileInfo(Path.Combine(extracted, "jdk", "lib", "copy")).LinkTarget);
        var escaped = Path.Combine(_root, "escape.tar.gz");
        await using (var stream = File.Create(escaped))
        await using (var gzip = new GZipStream(stream, CompressionMode.Compress))
        using (var tar = new TarWriter(gzip))
            await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "jdk/outside") { LinkName = "../../outside" });
        await Assert.ThrowsAsync<InvalidDataException>(() => JavaProvisioner.ExtractTemurinAsync(escaped, Path.Combine(_root, "reject"), false, default));
    }

    private static byte[] TarArchive()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "jdk/bin/java")
            {
                DataStream = new MemoryStream("fixture runtime, never executed"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            });
        return output.ToArray();
    }
    private static byte[] Archive()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var file = zip.CreateEntry("jdk/bin/" + (OperatingSystem.IsWindows() ? "java.exe" : "java")).Open();
            file.Write("fixture runtime, never executed"u8);
        }
        return output.ToArray();
    }
    private static string Metadata(int major, byte[] archive) => JsonSerializer.Serialize(new[]
    {
        new { version = new { major }, binary = new { os = Platform.Os, architecture = Platform.Architecture, package = new
        { link = "https://github.com/adoptium/fixture/runtime.zip", checksum = Convert.ToHexString(SHA256.HashData(archive)), size = archive.Length, name = "runtime.zip" } } }
    });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
