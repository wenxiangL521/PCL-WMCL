using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsLibrariesKeepSharedAndOtherPackBytesIndependent(bool sameLength)
    {
        var firstBytes = Encoding.UTF8.GetBytes("first pack library");
        var secondBytes = Encoding.UTF8.GetBytes("other pack library");
        var sharedBytes = new byte[firstBytes.Length + (sameLength ? 0 : 10)];
        const string mavenPath = "org/test/extra/1/extra-1.jar";
        var shared = Path.Combine(_root, "libraries", mavenPath);
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllBytesAsync(shared, sharedBytes);
        var instances = new List<(string Id, byte[] Bytes)>();
        foreach (var bytes in new[] { firstBytes, secondBytes })
        {
            _responses["/extra.jar"] = bytes;
            var index = McbbsIndex();
            index["libraries"] = new JsonArray(McbbsLibrary("org.test:extra:1", "/extra.jar", bytes));
            var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "LibraryPack");
            instances.Add((id, bytes));
            Assert.Equal(sharedBytes, await File.ReadAllBytesAsync(shared));
        }
        foreach (var (id, bytes) in instances)
        {
            var version = await _repository.GetVersionAsync(id);
            var resolved = Assert.Single(LibraryResolver.ResolveArtifacts(version, RuleContext.CreateDefault()));
            Assert.Equal("wmcl-packs/" + id + "/" + mavenPath, resolved.RelativePath);
            var destination = Path.Combine(_root, "libraries", resolved.RelativePath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
            var plan = LaunchPlanBuilder.Build(new LaunchRequest
            {
                Version = version, VersionId = id, GameDirectory = _repository.GetVersionDirectory(id),
                MinecraftDirectory = _root, Java = new JavaRuntime("java", new Version(21, 0), 21, "x86_64", _root, null),
                Account = AccountInfo.Offline("Test")
            });
            Assert.Contains(destination, plan.Classpath.Split(Path.PathSeparator));
            Assert.DoesNotContain(shared, plan.Classpath.Split(Path.PathSeparator));
            var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.Component == "MCBBS-library");
            Assert.Equal(Hash(bytes), managed.Sha1);
            Assert.Equal("https://example.invalid/extra.jar", managed.Url);
            Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        }
        var first = instances[0];
        _responses["/extra.jar"] = first.Bytes;
        var firstManaged = InstallManifestStore.Load(_root, first.Id)!.ManagedFiles.Single(file => file.Component == "MCBBS-library");
        await File.WriteAllBytesAsync(Path.Combine(_root, firstManaged.RelativePath), new byte[first.Bytes.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, first.Id, downloads: new DownloadManager(_http)));
        Assert.Equal(first.Bytes, await File.ReadAllBytesAsync(Path.Combine(_root, firstManaged.RelativePath)));
        Assert.Equal(sharedBytes, await File.ReadAllBytesAsync(shared));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, instances[1].Id)).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsLibrariesUseTheMavenRepositoryWhenArtifactUrlsAreOmitted(bool customRepository)
    {
        var bytes = Encoding.UTF8.GetBytes("repository library");
        const string mavenPath = "org/test/repository/1/repository-1.jar";
        var repository = customRepository ? "https://example.invalid/maven/" : "https://libraries.minecraft.net/";
        _responses[(customRepository ? "/maven/" : "/") + mavenPath] = bytes;
        var library = McbbsLibrary("org.test:repository:1", "/unused.jar", bytes, "custom/renamed.jar");
        library["downloads"]!["artifact"]!.AsObject().Remove("url");
        if (customRepository) library["url"] = repository;
        var index = McbbsIndex();
        index["libraries"] = new JsonArray(library);
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "Repository");
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.Component == "MCBBS-library");
        Assert.Equal("libraries/wmcl-packs/" + id + "/custom/renamed.jar", managed.RelativePath);
        Assert.Equal(repository + mavenPath, managed.Url);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, managed.RelativePath)));
        Assert.DoesNotContain("/unused.jar", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task McbbsLibrariesKeepConditionalOverridesAndNativeOnlyDownloads()
    {
        var baseBytes = Encoding.UTF8.GetBytes("base platform library");
        _responses["/base.jar"] = baseBytes;
        var basePath = Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json");
        var original = JsonNode.Parse(await File.ReadAllTextAsync(basePath))!.AsObject();
        original["libraries"] = new JsonArray(McbbsLibrary("org.test:platform:1", "/base.jar", baseBytes));
        await File.WriteAllTextAsync(basePath, original.ToJsonString());
        var platforms = new[] { "linux", "windows", "osx" };
        var libraries = new JsonArray();
        var nativeBytes = McbbsNativeLibrary();
        var natives = new JsonObject();
        var classifiers = new JsonObject();
        foreach (var platform in platforms)
        {
            var bytes = Encoding.UTF8.GetBytes(platform + " library");
            _responses["/" + platform + ".jar"] = bytes;
            var library = McbbsLibrary("org.test:platform:2", "/" + platform + ".jar", bytes, platform + ".jar");
            library["rules"] = new JsonArray(new JsonObject
            {
                ["action"] = "allow", ["os"] = new JsonObject { ["name"] = platform }
            });
            libraries.Add(library);
            natives[platform] = "natives-" + platform + "-${arch}";
            classifiers["natives-" + platform + "-64"] = new JsonObject
            {
                ["url"] = "https://example.invalid/native-" + platform + ".zip",
                ["sha1"] = Hash(nativeBytes), ["size"] = nativeBytes.Length
            };
            _responses["/native-" + platform + ".zip"] = nativeBytes;
        }
        libraries.Add(new JsonObject
        {
            ["name"] = "org.test:native:1", ["natives"] = natives,
            ["downloads"] = new JsonObject { ["classifiers"] = classifiers },
            ["extract"] = new JsonObject { ["exclude"] = new JsonArray("excluded/") }
        });
        var index = McbbsIndex();
        index["libraries"] = libraries;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "Platform");
        var version = await _repository.GetVersionAsync(id);
        foreach (var platform in platforms)
        {
            var context = new RuleContext(platform, "x86_64", "", new Dictionary<string, bool>());
            var artifact = Assert.Single(LibraryResolver.ResolveArtifacts(version, context));
            Assert.Equal("2", artifact.Name.Version);
            Assert.Equal("wmcl-packs/" + id + "/" + platform + ".jar", artifact.RelativePath);
            var native = Assert.Single(LibraryResolver.ResolveNatives(version, context));
            Assert.Equal("wmcl-packs/" + id + "/org/test/native/1/native-1-natives-" + platform + "-64.jar", native.RelativePath);
        }
        var other = new RuleContext("other", "x86_64", "", new Dictionary<string, bool>());
        Assert.Equal("1", Assert.Single(LibraryResolver.ResolveArtifacts(version, other)).Name.Version);
        foreach (var platform in platforms.Where(platform => platform != RuleContext.GetOsName()))
        {
            Assert.DoesNotContain("/" + platform + ".jar", _requests);
            Assert.DoesNotContain("/native-" + platform + ".zip", _requests);
        }
        Assert.Equal("native content", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "natives", "test.native")));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "natives", "excluded", "test.native")));
        var managed = InstallManifestStore.Load(_root, id)!.ManagedFiles.Where(file => file.Component == "MCBBS-library").ToList();
        Assert.Equal(2, managed.Count);
        foreach (var file in managed) await File.WriteAllTextAsync(Path.Combine(_root, file.RelativePath), "corrupt");
        Assert.Equal(2, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"org.test:../extra:1\"}]")]
    [InlineData("[{\"name\":\"org..test:extra:1\"}]")]
    [InlineData("[{\"name\":\"org.test:extra:\"}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"artifact\":{\"path\":\"../other.jar\"}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"artifact\":{\"sha1\":\"bad\"}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"artifact\":{\"size\":-1}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"artifact\":{\"url\":\"file:///tmp/extra.jar\"}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"classifiers\":{\"../extra\":{}}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\",\"downloads\":{\"classifiers\":{\"native\":null}}}]")]
    [InlineData("[{\"name\":\"org.test:extra:1\"},{\"name\":\"org.test:extra:2\"}]")]
    [InlineData("[{\"name\":\"org.test:first:1\",\"downloads\":{\"artifact\":{\"path\":\"same.jar\"}}},{\"name\":\"org.test:second:1\",\"downloads\":{\"artifact\":{\"path\":\"SAME.jar\"}}}]")]
    [InlineData("[{\"name\":\"org.test:first:1\",\"downloads\":{\"artifact\":{\"path\":\"directory\"}}},{\"name\":\"org.test:second:1\",\"downloads\":{\"artifact\":{\"path\":\"directory/file.jar\"}}}]")]
    public async Task McbbsInvalidLibrariesFailBeforeGameInstallation(string declarations)
    {
        var index = McbbsIndex();
        index["libraries"] = JsonNode.Parse(declarations);
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsService().InstallFromFileAsync(McbbsPack(index), "InvalidLibraries"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("InvalidLibraries")));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("cancel")]
    public async Task McbbsLibraryDownloadFailuresKeepSharedBytesAndDoNotSaveASuccessManifest(string problem)
    {
        var bytes = Encoding.UTF8.GetBytes("expected library");
        var shared = Path.Combine(_root, "libraries", "org", "test", "extra", "1", "extra-1.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllBytesAsync(shared, bytes);
        _responses["/extra.jar"] = problem == "hash" ? new byte[bytes.Length] : bytes;
        var library = McbbsLibrary("org.test:extra:1", "/extra.jar", bytes);
        if (problem == "size") library["downloads"]!["artifact"]!["size"] = bytes.Length + 1;
        var index = McbbsIndex();
        index["libraries"] = new JsonArray(library);
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(update =>
        {
            if (problem == "cancel" && update.Stage == "下载 MCBBS 额外运行库") cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<Exception>(() => McbbsService().InstallFromFileAsync(
            McbbsPack(index), "FailedLibraries", progress, cancellation.Token));
        Assert.Null(InstallManifestStore.Load(_root, "FailedLibraries"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(shared));
        Assert.False(File.Exists(Path.Combine(_root, "libraries", "wmcl-packs", "FailedLibraries", "org", "test", "extra", "1", "extra-1.jar")));
    }

    private static byte[] McbbsNativeLibrary()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var path in new[] { "test.native", "excluded/test.native" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write("native content");
            }
        return stream.ToArray();
    }

    private static JsonObject McbbsLibrary(string name, string url, byte[] bytes, string? path = null) => new()
    {
        ["name"] = name,
        ["downloads"] = new JsonObject
        {
            ["artifact"] = new JsonObject
            {
                ["url"] = "https://example.invalid" + url,
                ["path"] = path,
                ["sha1"] = Hash(bytes),
                ["size"] = bytes.Length
            }
        }
    };
}
