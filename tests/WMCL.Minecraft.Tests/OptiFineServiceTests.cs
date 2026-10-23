using System.Net;
using WMCL.Modding.OptiFine;

namespace WMCL.Minecraft.Tests;

public class OptiFineServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-optifine-tests-" + Guid.NewGuid().ToString("N"));
    private const string Mirror = """
        [{"mcversion":"1.20.1","type":"HD_U","patch":"I6","filename":"OptiFine_1.20.1_HD_U_I6.jar","forge":"Forge 47.2.18"}]
        """;

    private static string Row(string filename, string forge, string date) => $"""
        <tr class='downloadLine'><td class='colMirror'><a href="adloadx?f={filename}">Mirror</a></td>
        <td><a href="changelog?f={filename}">Changelog</a></td><td class='colForge'>{forge}</td>
        <td class='colDate'>{date}</td></tr>
        """;

    [Fact]
    public void ParsesAndFiltersOptiFineVersions()
    {
        const string json = """
            [
              {"mcversion":"1.20.1","type":"HD_U","patch":"I6","filename":"OptiFine_1.20.1_HD_U_I6.jar","forge":"Forge 47.2.18"},
              {"mcversion":"1.20.1","type":"HD_U_I6","patch":"pre1","filename":"preview_OptiFine_1.20.1_HD_U_I6_pre1.jar","forge":"Forge 47.1.0"},
              {"mcversion":"1.21.1","type":"HD_U","patch":"J1","filename":"OptiFine_1.21.1_HD_U_J1.jar","forge":"Forge N/A"}
            ]
            """;

        var versions = OptiFineService.ParseVersions(json, "1.20.1");

        Assert.Equal(2, versions.Count);
        Assert.Equal("I6", versions[0].Patch);
        Assert.False(versions[0].IsPreview);
        Assert.Equal("47.2.18", versions[0].ForgeVersion);
        Assert.Equal("pre1", versions[1].Patch);
        Assert.True(versions[1].IsPreview);
    }

    [Fact]
    public void OfficialRowsKeepTheirOwnDatesForgeRequirementsAndPreviewIdentity()
    {
        var html = Row("OptiFine_1.21.1_HD_U_J1.jar", "Forge N/A", "01.01.2025") +
            Row("preview_OptiFine_1.20.1_HD_U_I6_pre1.jar", "Forge #47.1.0", "11.02.2024") +
            Row("OptiFine_1.20.1_HD_U_I6.jar", "Forge <b>47.2.18</b>", "12.03.2024");
        var versions = OptiFineService.ParseOfficialVersions(html, "1.20.1");
        Assert.Equal(2, versions.Count);
        Assert.Equal(new DateOnly(2024, 2, 11), versions[0].Published);
        Assert.Equal("47.1.0", versions[0].ForgeVersion);
        Assert.True(versions[0].IsPreview);
        Assert.Equal("I6_pre1", versions[0].Patch);
        Assert.Equal("1.20.1 I6 pre1", versions[0].DisplayName);
        Assert.Equal(new DateOnly(2024, 3, 12), versions[1].Published);
        Assert.Equal("47.2.18", versions[1].ForgeVersion);
        Assert.False(versions[1].IsPreview);
    }

    [Theory]
    [InlineData("Forge N/A", null)]
    [InlineData("", "")]
    [InlineData("Forge #47.2.18", "47.2.18")]
    public void OfficialForgeStatesAndMissingDatesAreNotInvented(string forge, string? expected)
    {
        var version = Assert.Single(OptiFineService.ParseOfficialVersions(
            Row("OptiFine_1.20.1_HD_U_I6.jar", forge, "invalid"), "1.20.1"));
        Assert.Equal(expected, version.ForgeVersion);
        Assert.Null(version.Published);
    }

    [Theory]
    [InlineData("colDate")]
    [InlineData("colForge")]
    public void MissingOfficialCellsCannotBeCorrelatedWithAnotherVersionsMetadata(string missing)
    {
        var html = Row("OptiFine_1.20.1_HD_U_I6.jar", "", "12.03.2024")
            .Replace("class='" + missing + "'", "class='unrelated'") +
            Row("OptiFine_1.20.1_HD_U_I5.jar", "Forge 47.1.0", "11.02.2024");
        Assert.Throws<InvalidDataException>(() => OptiFineService.ParseOfficialVersions(html, "1.20.1"));
    }

    [Fact]
    public void RepeatedLinksAndRowsDoNotCreateDuplicateVersionChoices()
    {
        var row = Row("OptiFine_1.20.1_HD_U_I6.jar", "", "12.03.2024");
        Assert.Single(OptiFineService.ParseOfficialVersions(row + row, "1.20.1"));
    }

    [Fact]
    public void MirrorMissingForgeIsDistinctFromExplicitIncompatibilityAndHasNoFabricatedDate()
    {
        var unknown = Assert.Single(OptiFineService.ParseVersions(Mirror.Replace("Forge 47.2.18", ""), "1.20.1"));
        var incompatible = Assert.Single(OptiFineService.ParseVersions(Mirror.Replace("Forge 47.2.18", "Forge N/A"), "1.20.1"));
        Assert.Equal("", unknown.ForgeVersion);
        Assert.Null(incompatible.ForgeVersion);
        Assert.Null(unknown.Published);
    }

    [Fact]
    public async Task ValidOfficialCatalogNeedsNoMirrorEvenWhenRequestedGameIsAbsent()
    {
        var requests = new List<Uri>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Row("OptiFine_1.20.1_HD_U_I6.jar", "", "12.03.2024"))
            };
        });
        var service = new OptiFineService(new VersionRepository(_root), http: client, installerCacheDirectory: Path.Combine(_root, "cache"));
        var version = Assert.Single(await service.GetVersionsAsync("1.20.1"));
        Assert.Equal(new DateOnly(2024, 3, 12), version.Published);
        Assert.Empty(await service.GetVersionsAsync("1.0"));
        Assert.All(requests, request => Assert.Equal("optifine.net", request.Host));
        Assert.Equal(2, requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialHttpOrMalformedPageFallsBackToMirrorWithoutDate(bool httpError)
    {
        var requests = new List<Uri>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.Host == "optifine.net"
                ? new HttpResponseMessage(httpError ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent("<html>missing catalog</html>") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Mirror) };
        });
        var version = Assert.Single(await new OptiFineService(new VersionRepository(_root), http: client).GetVersionsAsync("1.20.1"));
        Assert.Null(version.Published);
        Assert.Equal(2, requests.Count);
        Assert.Equal("bmclapi2.bangbang93.com", requests[1].Host);
    }

    [Fact]
    public async Task CallerCancellationNeverStartsMirrorFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var client = Client(_ =>
        {
            calls++;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OptiFineService(new VersionRepository(_root), http: client).GetVersionsAsync("1.20.1", cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedVersionGoesDirectlyToItsExactInstallerEvenWithoutCatalog(bool install)
    {
        var patch = "test" + Guid.NewGuid().ToString("N");
        var selected = new OptiFineVersion("1.20.1", "HD_U", patch, $"OptiFine_1.20.1_HD_U_{patch}.jar", "");
        var requests = new List<Uri>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = new OptiFineService(new VersionRepository(_root), http: client, installerCacheDirectory: Path.Combine(_root, "cache"));
        if (install)
            await Assert.ThrowsAsync<HttpRequestException>(() => service.InstallAsync(selected, "fixture-java", completeInstallation: false));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => service.EnsureInstallerAsync(selected));
        var request = Assert.Single(requests);
        Assert.Equal("/adloadx", request.AbsolutePath);
        Assert.Equal("?f=" + selected.FileName, request.Query);
    }

    [Fact]
    public async Task PatchLookupCannotSilentlySubstitutePreviewWhenStableVersionDisappears()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Row("preview_OptiFine_1.20.1_HD_U_I6_pre1.jar", "", "12.03.2024"))
        });
        var service = new OptiFineService(new VersionRepository(_root), http: client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureInstallerAsync("1.20.1", "I6"));
    }

    [Theory]
    [InlineData("../OptiFine_1.20.1_HD_U_I6.jar")]
    [InlineData("OptiFine_1.21.1_HD_U_I6.jar")]
    [InlineData("OptiFine_1.20.1_HD_U_I7.jar")]
    public async Task InvalidCapturedVersionIsRejectedBeforeAnyNetworkOrFileWrite(string filename)
    {
        using var client = Client(_ => throw new InvalidOperationException("Unexpected network"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OptiFineService(new VersionRepository(_root), http: client)
            .EnsureInstallerAsync(new OptiFineVersion("1.20.1", "HD_U", "I6", filename, "")));
        Assert.False(Directory.Exists(_root));
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogUsesOriginalNaturalDisplayNameOrderForEitherSource(bool mirror)
    {
        string[] patches = ["I9", "I10", "I9_pre9", "I9_pre10", "I10_pre1"];
        var html = string.Concat(patches.Select(patch => Row("OptiFine_1.20.1_HD_U_" + patch + ".jar", "", "12.03.2024")));
        var json = System.Text.Json.JsonSerializer.Serialize(patches.Select(patch => new
        {
            mcversion = "1.20.1",
            type = "HD_U",
            patch,
            filename = "OptiFine_1.20.1_HD_U_" + patch + ".jar"
        }));
        using var client = Client(request => mirror && request.RequestUri!.Host == "optifine.net"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mirror ? json : html) });
        var versions = await new OptiFineService(new VersionRepository(_root), http: client).GetVersionsAsync("1.20.1");
        Assert.Equal(new[] { "I10", "I9", "I10_pre1", "I9_pre10", "I9_pre9" }, versions.Select(version => version.Patch));
    }

    [Theory]
    [InlineData("1.8.0", "1.8 I6")]
    [InlineData("1.9.0", "1.9 I6")]
    [InlineData("1.20.1", "1.20.1 I6")]
    public void LegacyDisplayNormalizationPreservesDownloadIdentity(string minecraft, string expected)
    {
        var filename = "OptiFine_" + minecraft + "_HD_U_I6.jar";
        var version = new OptiFineVersion(minecraft, "HD_U", "I6", filename, "");
        Assert.Equal(expected, version.DisplayName);
        Assert.Equal(minecraft, version.Minecraft);
        Assert.Equal(filename, version.FileName);
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
