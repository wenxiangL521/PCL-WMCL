using System.Net;
using System.Text.Json;
using WMCL.Minecraft.Models;
using WMCL.Modding.LiteLoader;

namespace WMCL.Minecraft.Tests;

public sealed class LiteLoaderServiceTests
{
    private const string Metadata = """
        {"versions":{"1.12.2":{"snapshots":{"com.mumfrey:liteloader":{"latest":{
          "version":"1.12.2-SNAPSHOT","stream":"snapshot","timestamp":"1500000000",
          "md5":"0123456789abcdef0123456789abcdef","tweakClass":"com.mumfrey.liteloader.launch.LiteLoaderTweaker",
          "libraries":[{"name":"net.minecraft:launchwrapper:1.12","url":"https://libraries.minecraft.net/",
            "rules":[{"action":"allow","os":{"name":"linux"}}]}]
        }}}}}}
        """;

    [Fact]
    public void SnapshotMetadataSurvivesDocumentDisposalAndPreservesLibraryRules()
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata, "1.12.2"));
        Assert.Equal("1.12.2-SNAPSHOT", version.Version);
        Assert.True(version.IsPreview);
        Assert.False(version.IsLegacy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1500000000), version.Published);
        Assert.Equal("0123456789abcdef0123456789abcdef", version.Md5);
        Assert.Equal("linux", version.Libraries[0].GetProperty("rules")[0].GetProperty("os").GetProperty("name").GetString());
        Assert.Empty(LiteLoaderService.ParseVersions(Metadata, "1.7.10"));
    }

    [Fact]
    public void ReleaseArtefactTakesPrecedenceOverSnapshot()
    {
        var metadata = Metadata.Replace("\"snapshots\":", "\"artefacts\":")
            .Replace("\"stream\":\"snapshot\"", "\"stream\":\"release\"");
        Assert.False(Assert.Single(LiteLoaderService.ParseVersions(metadata, "1.12.2")).IsPreview);
    }

    [Theory]
    [InlineData("1.7.10", true, "1.7.10")]
    [InlineData("1.8", false, "1.8.0")]
    [InlineData("1.9", false, "1.9.0")]
    [InlineData("1.12.2", false, "1.12.2")]
    public void LegacyFlagAndInstallerNamesMatchOriginal(string minecraft, bool legacy, string installerMinecraft)
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata.Replace("1.12.2", minecraft), minecraft));
        Assert.Equal(legacy, version.IsLegacy);
        Assert.Equal($"liteloader-installer-{installerMinecraft}-00-SNAPSHOT.jar", version.InstallerFileName);
    }

    [Fact]
    public void ProfileCanInheritForgeWhileRetainingTheOriginalClientJar()
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata, "1.12.2"));
        using var profile = JsonDocument.Parse(LiteLoaderService.CreateProfile(version, "forge-test"));
        Assert.Equal("forge-test", profile.RootElement.GetProperty("inheritsFrom").GetString());
        Assert.Equal("1.12.2", profile.RootElement.GetProperty("jar").GetString());
        Assert.Throws<InvalidOperationException>(() => LiteLoaderService.CreateProfile(version, "1.12.2-LiteLoader"));
        Assert.Throws<ArgumentException>(() => LiteLoaderService.CreateProfile(version, "../forge"));
        Assert.Throws<InvalidDataException>(() => LiteLoaderService.CreateProfile(version with { Version = "../bad" }));
    }

    [Theory]
    [InlineData("1.5.2")]
    [InlineData("1.6.4")]
    [InlineData("1.12.3")]
    [InlineData("1.13")]
    [InlineData("1.20.1")]
    [InlineData("17w43a")]
    [InlineData("../1.12.2")]
    public async Task UnsupportedVersionsDoNotRequestMetadata(string minecraft)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unexpected HTTP")));
        Assert.Empty(await new LiteLoaderService(http).GetVersionsAsync(minecraft));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"versions\":null}")]
    [InlineData("{\"versions\":{\"1.12.2\":{}}}")]
    public void InvalidCatalogIsAnErrorInsteadOfAnEmptySuccessfulCatalog(string metadata) =>
        Assert.Throws<InvalidDataException>(() => LiteLoaderService.ParseVersions(metadata, "1.12.2"));

    [Fact]
    public void MissingDatesStayMissingAndInvalidCoordinatesAreRejected()
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata.Replace("1500000000", "not-a-date"), "1.12.2"));
        Assert.Null(version.Published);
        using var profile = JsonDocument.Parse(LiteLoaderService.CreateProfile(version));
        Assert.False(profile.RootElement.TryGetProperty("releaseTime", out _));
        Assert.Throws<InvalidDataException>(() => LiteLoaderService.ParseVersions(
            Metadata.Replace("1.12.2-SNAPSHOT", "../../bad"), "1.12.2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialFailureFallsBackToMirror(bool invalidOfficial)
    {
        var hosts = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(hosts.Count == 1 && !invalidOfficial
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(hosts.Count == 1 && invalidOfficial ? "{}" : Metadata)
            };
        }));
        Assert.Single(await new LiteLoaderService(http).GetVersionsAsync("1.12.2"));
        Assert.Equal(["dl.liteloader.com", "bmclapi2.bangbang93.com"], hosts);
    }

    [Fact]
    public async Task TwoFailuresDoNotBecomeAnEmptyCatalog()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var exception = await Assert.ThrowsAsync<AggregateException>(() => new LiteLoaderService(http).GetVersionsAsync("1.12.2"));
        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task CancellationDoesNotRequestTheFallbackSource()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LiteLoaderService(http).GetVersionsAsync("1.12.2", cancellation.Token));
        Assert.Equal(1, requests);
    }

    [Fact]
    public void ProfileMatchesOriginalAndDoesNotMutateCatalogLibraries()
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata, "1.12.2"));
        using var document = JsonDocument.Parse(LiteLoaderService.CreateProfile(version));
        var profile = document.RootElement;
        Assert.Equal("1.12.2-LiteLoader", profile.GetProperty("id").GetString());
        Assert.Equal("1.12.2", profile.GetProperty("inheritsFrom").GetString());
        Assert.Equal("1.12.2", profile.GetProperty("jar").GetString());
        Assert.Equal("net.minecraft.launchwrapper.Launch", profile.GetProperty("mainClass").GetString());
        Assert.Equal(18, profile.GetProperty("minimumLauncherVersion").GetInt32());
        Assert.Equal(["--tweakClass", version.TweakClass], profile.GetProperty("arguments").GetProperty("game")
            .EnumerateArray().Select(argument => argument.GetString()));
        Assert.Equal("com.mumfrey:liteloader:1.12.2-SNAPSHOT", profile.GetProperty("libraries")[1].GetProperty("name").GetString());
        Assert.Equal(1, version.Libraries.GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergingModernTweakerPreservesLegacyVanillaAndForgeArguments(bool forge)
    {
        var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata, "1.12.2"));
        var child = JsonSerializer.Deserialize<VersionJson>(LiteLoaderService.CreateProfile(version))!;
        var parent = new VersionJson
        {
            Id = "1.12.2",
            MinecraftArguments = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory}" +
                (forge ? " --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker" : "")
        };
        var merged = VersionMerger.Merge(parent, child);
        Assert.Equal(parent.MinecraftArguments.Split(' ').Concat(["--tweakClass", version.TweakClass]),
            merged.Arguments!.Game.SelectMany(argument => argument.Values));
        Assert.Null(parent.Arguments);
        Assert.Equal(["--tweakClass", version.TweakClass], child.Arguments!.Game.SelectMany(argument => argument.Values));
    }

    [Fact]
    public async Task StagedProfileRequiresVanillaAndNeverOverwritesAnExistingInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-lite-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var version = Assert.Single(LiteLoaderService.ParseVersions(Metadata, "1.12.2"));
            await Assert.ThrowsAsync<FileNotFoundException>(() => LiteLoaderService.InstallProfileAsync(root, version));
            var vanillaDirectory = Path.Combine(root, "versions", "1.12.2");
            Directory.CreateDirectory(vanillaDirectory);
            await File.WriteAllTextAsync(Path.Combine(vanillaDirectory, "1.12.2.json"),
                """{"id":"1.12.2","minecraftArguments":"--username ${auth_player_name}"}""");
            var id = await LiteLoaderService.InstallProfileAsync(root, version);
            var path = Path.Combine(root, "versions", id, id + ".json");
            var original = await File.ReadAllTextAsync(path);
            var merged = await new VersionRepository(root).GetLocalVersionAsync(id);
            Assert.Contains(merged.Arguments!.Game, argument => argument.Values.Contains("${auth_player_name}"));
            await Assert.ThrowsAsync<IOException>(() => LiteLoaderService.InstallProfileAsync(root, version));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LiteLoaderService.InstallProfileAsync(root, version, cancellation.Token));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
