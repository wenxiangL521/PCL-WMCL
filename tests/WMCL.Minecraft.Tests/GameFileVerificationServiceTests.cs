using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

[Trait("Kind", "Unit")]
public sealed class GameFileVerificationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-launch-files-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];
    private readonly List<(string Relative, byte[] Data)> _files = [];
    private static readonly RuleContext Context = new("linux", "x86_64", "1", new Dictionary<string, bool>());

    public GameFileVerificationServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairsMissingAndSameLengthCorruptFilesIncludingNativeArchives(bool corrupt)
    {
        var version = FullProfile();
        await SaveProfile(version);
        if (corrupt)
        {
            foreach (var (relative, data) in _files)
                await Put(relative, new byte[data.Length]);
        }
        using var http = Client();
        var reports = new List<JobProgress>();

        var result = await Service(http).EnsureAsync("test", new InstallOptions { Mirror = MirrorMode.Official },
            new InlineProgress(reports.Add));

        Assert.Equal(6, result.CheckedFiles);
        Assert.Equal(6, result.RepairedFiles);
        Assert.Equal(6, _requests.Count);
        Assert.Equal(Path.Combine(_root, "versions", "test", "test.jar"), result.ClientJarPath);
        foreach (var (relative, data) in _files)
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(_root, relative)));
        Assert.Equal(1, reports[^1].Percent);
        Assert.All(reports.Zip(reports.Skip(1)), pair => Assert.True(pair.First.Percent <= pair.Second.Percent));
        Assert.Contains(reports, report => report.Stage == "校验资源文件");
        Assert.Contains(reports, report => report.Stage == "补全资源文件");
    }

    [Fact]
    public async Task IntactInstallationIsOfflineAndDoesNotRewriteAnyFilesEvenWithForce()
    {
        await SaveProfile(FullProfile());
        await Populate();
        await Put("versions/test/mods/user.jar", "mod"u8.ToArray());
        await Put("versions/test/config/options.json", "config"u8.ToArray());
        await Put("versions/test/saves/world/level.dat", "world"u8.ToArray());
        await Put("screenshots/keep.png", "screenshot"u8.ToArray());
        var before = Snapshot();
        using var http = Client();

        var result = await Service(http).EnsureAsync("test", new InstallOptions { Force = true, VerifyExisting = false });

        Assert.Equal(0, result.RepairedFiles);
        Assert.Empty(_requests);
        AssertSnapshot(before);
    }

    [Fact]
    public async Task SizeOnlyAndUnhashedExistingFilesDoNotCauseDownloads()
    {
        var version = FullProfile();
        version.Downloads!["client"].Sha1 = null;
        version.Libraries[0].Downloads!.Artifact!.Sha1 = null;
        version.Libraries[1].Downloads!.Classifiers!["natives-linux"].Sha1 = null;
        version.AssetIndex!.Sha1 = null;
        version.Logging!.Client!.File!.Sha1 = null;
        await SaveProfile(version);
        await Populate();
        using var http = Client();

        await Service(http).EnsureAsync("test");

        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("client.jar")]
    public async Task LocalCustomClientWithoutDownloadSourceIsPreserved(string? path)
    {
        var version = new VersionJson { Id = "test" };
        if (path is not null) version.Downloads = new() { ["client"] = new DownloadInfo { Path = path } };
        version.Libraries.Add(new Library { Name = "custom:loader:1" });
        await SaveProfile(version);
        await Put("versions/test/" + (path ?? "test.jar"), "custom-client"u8.ToArray());
        await Put("libraries/custom/loader/1/LOADER-1.jar", "local-loader"u8.ToArray());
        var before = Snapshot();
        using var http = Client();

        var result = await Service(http).EnsureAsync("test");

        Assert.EndsWith(path ?? "test.jar", result.ClientJarPath);
        Assert.Equal(0, result.RepairedFiles);
        Assert.Empty(_requests);
        AssertSnapshot(before);
    }

    [Fact]
    public async Task MissingCoreWithoutSourceFailsClearlyWithoutRequestingManifest()
    {
        await SaveProfile(new VersionJson { Id = "test" });
        using var http = Client();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("未提供下载来源", error.Message);
        Assert.Contains("test.jar", error.Message);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("base.jar", "test.jar")]
    [InlineData("client.jar", "client.jar")]
    public async Task MergedProfilesUseSelectedDirectoryAndPreserveExplicitClientPath(string parentPath, string expectedPath)
    {
        var client = Download("/client", "versions/test/" + expectedPath, "inherited-client"u8.ToArray());
        client.Path = parentPath;
        await SaveProfile(new VersionJson { Id = "base", Downloads = new() { ["client"] = client } });
        await SaveProfile(new VersionJson { Id = "test", InheritsFrom = "base" });
        var parentJson = await File.ReadAllTextAsync(Path.Combine(_root, "versions/base/base.json"));
        var childJson = await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/test.json"));
        using var http = Client();

        var result = await Service(http).EnsureAsync("test");

        Assert.Equal(Path.Combine(_root, "versions", "test", expectedPath), result.ClientJarPath);
        Assert.Equal("test", result.Version.Id);
        Assert.True(File.Exists(result.ClientJarPath));
        Assert.False(File.Exists(Path.Combine(_root, "versions", "base", parentPath)));
        Assert.Equal(parentJson, await File.ReadAllTextAsync(Path.Combine(_root, "versions/base/base.json")));
        Assert.Equal(childJson, await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/test.json")));
    }

    [Fact]
    public async Task WrongDownloadedHashFailsAndKeepsOriginalDestination()
    {
        var client = Download("/client", "versions/test/test.jar", "client"u8.ToArray());
        await SaveProfile(new VersionJson { Id = "test", Downloads = new() { ["client"] = client } });
        await Put("versions/test/test.jar", "broken"u8.ToArray());
        _responses["/client"] = "wrong!"u8.ToArray();
        using var http = Client();

        var error = await Assert.ThrowsAsync<DownloadException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("不能继续启动", error.Message);
        Assert.Equal("broken", await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/test.jar")));
        Assert.False(File.Exists(Path.Combine(_root, "versions/test/test.jar.part")));
    }

    [Fact]
    public async Task OfflineMissingFilesFailRatherThanPretendLaunchIsReady()
    {
        await SaveProfile(FullProfile());
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("offline")));

        var error = await Assert.ThrowsAsync<DownloadException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("启动文件补全失败", error.Message);
        Assert.False(File.Exists(Path.Combine(_root, "versions/test/test.jar")));
    }

    [Fact]
    public async Task CancellationAtVerificationStageRunsOffCallerAndLeavesFilesUnchanged()
    {
        await SaveProfile(FullProfile());
        await Populate();
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        using var http = Client();
        var workerObserved = false;
        var progress = new InlineProgress(report =>
        {
            if (report.Stage != "校验客户端与运行库") return;
            workerObserved = SynchronizationContext.Current is null;
            cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(http).EnsureAsync("test", progress: progress, ct: cancellation.Token));

        Assert.True(workerObserved);
        Assert.Empty(_requests);
        AssertSnapshot(before);
    }

    [Fact]
    public async Task CancellationDuringDownloadPropagatesWithoutSuccessOrOverwrite()
    {
        await SaveProfile(FullProfile());
        await Put("versions/test/test.jar", "original"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        }));
        var reports = new List<JobProgress>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(http).EnsureAsync("test",
            progress: new InlineProgress(reports.Add), ct: cancellation.Token));

        Assert.DoesNotContain(reports, report => report.Percent == 1);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/test.jar")));
    }

    [Fact]
    public async Task MalformedUnhashedIndexIsDownloadedAndParsedAgain()
    {
        var index = Download("/index", "assets/indexes/index.json", "{\"objects\":{}}"u8.ToArray());
        await SaveProfile(new VersionJson { Id = "test", AssetIndex = new AssetIndexInfo { Id = "index", Url = index.Url! } });
        await Put("versions/test/test.jar", "custom"u8.ToArray());
        await Put("assets/indexes/index.json", "invalid"u8.ToArray());
        using var http = Client();

        var result = await Service(http).EnsureAsync("test");

        Assert.Equal(1, result.RepairedFiles);
        Assert.Single(_requests);
        Assert.Equal("{\"objects\":{}}", await File.ReadAllTextAsync(Path.Combine(_root, "assets/indexes/index.json")));
    }

    [Fact]
    public async Task MalformedTrustedIndexFailsInsteadOfSilentlySkippingAssets()
    {
        var info = Download("/index", "assets/indexes/index.json", "{}"u8.ToArray());
        await SaveProfile(new VersionJson
        {
            Id = "test",
            AssetIndex = new AssetIndexInfo
            {
                Id = "index",
                Url = info.Url!,
                Sha1 = info.Sha1,
                Size = info.Size
            }
        });
        await Put("versions/test/test.jar", "custom"u8.ToArray());
        await Populate();
        using var http = Client();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("资源索引内容无效", error.Message);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyMappingsAreRepairedFromVerifiedObjectsWithoutNetwork(bool mapToResources)
    {
        var data = "legacy-sound"u8.ToArray();
        var hash = Hash(data);
        var indexData = JsonSerializer.SerializeToUtf8Bytes(new AssetIndex
        {
            Virtual = !mapToResources,
            MapToResources = mapToResources,
            Objects = new() { ["sound/click.ogg"] = new AssetObject { Hash = hash, Size = data.Length } }
        });
        var info = Download("/index", "assets/indexes/legacy.json", indexData);
        await SaveProfile(new VersionJson
        {
            Id = "test",
            AssetIndex = new AssetIndexInfo
            {
                Id = "legacy",
                Url = info.Url!,
                Sha1 = info.Sha1,
                Size = info.Size
            }
        });
        await Populate();
        await Put("versions/test/test.jar", "custom"u8.ToArray());
        await Put($"assets/objects/{hash[..2]}/{hash}", data);
        var mapped = mapToResources ? "resources/sound/click.ogg" : "assets/virtual/legacy/sound/click.ogg";
        await Put(mapped, new byte[data.Length]);
        await Put("versions/test/saves/world/level.dat", "keep"u8.ToArray());
        using var http = Client();

        await Service(http).EnsureAsync("test");

        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(_root, mapped)));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/saves/world/level.dat")));
        Assert.Empty(_requests);
        var before = Snapshot();
        await Service(http).EnsureAsync("test");
        AssertSnapshot(before);
    }

    [Fact]
    public async Task MaliciousClientPathCannotOverwriteUserMods()
    {
        var info = Download("/client", "versions/test/mods/user.jar", "client"u8.ToArray());
        info.Path = "mods/user.jar";
        await SaveProfile(new VersionJson { Id = "test", Downloads = new() { ["client"] = info } });
        await Put("versions/test/mods/user.jar", "user-data"u8.ToArray());
        using var http = Client();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("用户数据", error.Message);
        Assert.Empty(_requests);
        Assert.Equal("user-data", await File.ReadAllTextAsync(Path.Combine(_root, "versions/test/mods/user.jar")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureLibrariesMatchInjectedLaunchRules(bool customResolution)
    {
        var library = Download("/library", "libraries/example/lib/1/lib-1.jar", "library"u8.ToArray());
        library.Path = "example/lib/1/lib-1.jar";
        await SaveProfile(new VersionJson
        {
            Id = "test",
            Libraries = [new Library
            {
                Name = "example:lib:1", Downloads = new LibraryDownloads { Artifact = library },
                Rules = [new Rule { Action = "allow", Features = new() { ["has_custom_resolution"] = true } }]
            }]
        });
        await Put("versions/test/test.jar", "custom"u8.ToArray());
        using var http = Client();
        var context = Context with { Features = new Dictionary<string, bool> { ["has_custom_resolution"] = customResolution } };

        await new GameFileVerificationService(new VersionRepository(_root), new DownloadManager(http), context).EnsureAsync("test");

        Assert.Equal(customResolution ? 1 : 0, _requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsExistingAndDanglingPartSymlinksBeforeDownloading(bool dangling)
    {
        if (OperatingSystem.IsWindows()) return;
        var info = Download("/client", "versions/test/test.jar", "client"u8.ToArray());
        await SaveProfile(new VersionJson { Id = "test", Downloads = new() { ["client"] = info } });
        var target = Path.Combine(_root, "untouched.bin");
        if (!dangling) await File.WriteAllTextAsync(target, "original");
        File.CreateSymbolicLink(Path.Combine(_root, "versions/test/test.jar.part"), target);
        using var http = Client();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Service(http).EnsureAsync("test"));

        Assert.Contains("符号链接", error.Message);
        Assert.Empty(_requests);
        if (dangling) Assert.False(File.Exists(target));
        else Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    private VersionJson FullProfile()
    {
        var client = Download("/client", "versions/test/test.jar", "client"u8.ToArray());
        var library = Download("/library", "libraries/example/lib/1/lib-1.jar", "library"u8.ToArray());
        library.Path = "example/lib/1/lib-1.jar";
        var native = Download("/native", "libraries/example/native/1/native-1-natives-linux.jar", "native"u8.ToArray());
        native.Path = "example/native/1/native-1-natives-linux.jar";
        var asset = "asset-object"u8.ToArray();
        var hash = Hash(asset);
        _responses[$"/{hash[..2]}/{hash}"] = asset;
        _files.Add(($"assets/objects/{hash[..2]}/{hash}", asset));
        var index = Download("/index", "assets/indexes/index.json", JsonSerializer.SerializeToUtf8Bytes(new AssetIndex
        {
            Objects = new() { ["sound/example"] = new AssetObject { Hash = hash, Size = asset.Length } }
        }));
        var logging = Download("/logging", "assets/log_configs/client.xml", "logging"u8.ToArray());
        logging.Id = "client.xml";
        return new VersionJson
        {
            Id = "test",
            Downloads = new() { ["client"] = client },
            AssetIndex = new AssetIndexInfo { Id = "index", Url = index.Url!, Sha1 = index.Sha1, Size = index.Size },
            Logging = new LoggingInfo { Client = new LoggingClient { File = logging } },
            Libraries =
            [
                new Library { Name = "example:lib:1", Downloads = new LibraryDownloads { Artifact = library } },
                new Library { Name = "example:native:1", Natives = new() { ["linux"] = "natives-linux" },
                    Downloads = new LibraryDownloads { Classifiers = new() { ["natives-linux"] = native } } }
            ]
        };
    }

    private DownloadInfo Download(string urlPath, string relative, byte[] data)
    {
        _responses[urlPath] = data;
        _files.Add((relative, data));
        return new DownloadInfo { Url = "https://example.invalid" + urlPath, Sha1 = Hash(data), Size = data.Length };
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA1.HashData(data));
    private Task SaveProfile(VersionJson version) => Put($"versions/{version.Id}/{version.Id}.json", JsonSerializer.SerializeToUtf8Bytes(version));
    private async Task Put(string relative, byte[] data)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data);
    }
    private async Task Populate()
    {
        foreach (var (relative, data) in _files) await Put(relative, data);
    }
    private GameFileVerificationService Service(HttpClient http) => new(new VersionRepository(_root), new DownloadManager(http), Context);
    private HttpClient Client() => new(new Handler((request, _) =>
    {
        lock (_requests) _requests.Add(request.RequestUri!.AbsoluteUri);
        var data = _responses[request.RequestUri!.AbsolutePath];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
    }));
    private Dictionary<string, (byte[] Data, DateTime Written)> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));
    private void AssertSnapshot(Dictionary<string, (byte[] Data, DateTime Written)> before)
    {
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, previous) in before)
        {
            Assert.Equal(previous.Data, File.ReadAllBytes(path));
            Assert.Equal(previous.Written, File.GetLastWriteTimeUtc(path));
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
    private sealed class InlineProgress(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }
}
