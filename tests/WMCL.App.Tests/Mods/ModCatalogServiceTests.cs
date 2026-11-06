using System.Net;
using System.Text.Json;
using WMCL.App.Services;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class ModCatalogServiceTests
{
    [Theory]
    [InlineData("Modrinth", "")]
    [InlineData("Modrinth", "fabric")]
    [InlineData("CurseForge", "")]
    [InlineData("CurseForge", "fabric")]
    public async Task SearchUsesModFiltersAndRequestedPage(string platform, string loader)
    {
        Uri? requested = null;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(SearchResponse(platform, 1, 41));
        }));
        var service = Create(http);

        var page = await service.SearchAsync(platform, "example mod", "1.20.1", loader, 20);

        Assert.NotNull(requested);
        var query = Uri.UnescapeDataString(requested.Query);
        Assert.Contains("1.20.1", query);
        Assert.Contains(platform == "CurseForge" ? "searchFilter=example mod" : "query=example mod", query);
        if (platform == "CurseForge")
        {
            Assert.Equal("/v1/mods/search", requested.AbsolutePath);
            Assert.Contains("classId=6", query);
            Assert.Contains("pageSize=20", query);
            Assert.Contains("index=20", query);
            Assert.Equal(loader.Length > 0, query.Contains("modLoaderType="));
        }
        else
        {
            Assert.Equal("/v2/search", requested.AbsolutePath);
            Assert.Contains("project_type:mod", query);
            Assert.Contains("limit=20", query);
            Assert.Contains("offset=20", query);
            Assert.Equal(loader.Length > 0, query.Contains("categories:fabric"));
        }
        Assert.Single(page.Projects);
        Assert.Equal(41, page.TotalCount);
        Assert.Equal(21, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData("Modrinth", 20, 100, 0, true)]
    [InlineData("CurseForge", 20, 100, 0, true)]
    [InlineData("Modrinth", 20, 40, 20, false)]
    [InlineData("CurseForge", 20, 40, 20, false)]
    [InlineData("Modrinth", 19, 100, 20, false)]
    [InlineData("CurseForge", 19, 100, 20, false)]
    [InlineData("Modrinth", 0, 100, 20, false)]
    [InlineData("CurseForge", 0, 100, 20, false)]
    public async Task PaginationRequiresAFullPageAndRemainingResults(
        string platform, int count, int total, int offset, bool hasMore)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(SearchResponse(platform, count, total))));
        var page = await Create(http).SearchAsync(platform, "", "", "", offset);
        Assert.Equal(count, page.Projects.Count);
        Assert.Equal(total, page.TotalCount);
        Assert.Equal(offset + count, page.NextOffset);
        Assert.Equal(hasMore, page.HasMore);
    }

    [Fact]
    public async Task CurseForgeProjectsAreNormalizedWithoutLosingMetadataOrOverflowingDownloads()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new
        {
            data = new[] { new { id = 123, slug = "example", name = "Example", summary = "Description",
                downloadCount = long.MaxValue, logo = new { thumbnailUrl = "https://example.invalid/icon.png" } } },
            pagination = new { totalCount = 1 }
        }))));
        var project = Assert.Single((await Create(http).SearchAsync("CurseForge", "", "", "")).Projects);
        Assert.Equal("123", project.Id);
        Assert.Equal("example", project.Slug);
        Assert.Equal("Example", project.Title);
        Assert.Equal("Description", project.Description);
        Assert.Equal("mod", project.ProjectType);
        Assert.Equal(int.MaxValue, project.Downloads);
        Assert.Equal("https://example.invalid/icon.png", project.IconUrl);
        Assert.Empty(project.Categories);
    }

    [Fact]
    public async Task ModrinthVersionsExcludeMissingFilesButKeepIncompatibleVersionsInDateOrder()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/v2/project/example/version", request.RequestUri!.AbsolutePath);
            Assert.DoesNotContain("game_versions=", request.RequestUri.Query);
            Assert.DoesNotContain("loaders=", request.RequestUri.Query);
            return Task.FromResult(Json(new[]
            {
                MrVersion("old", "1.20.1", "Fabric", 1),
                MrVersion("empty", "1.20.1", "fabric", 4, false),
                MrVersion("wrong-game", "1.21.1", "fabric", 3),
                MrVersion("wrong-loader", "1.20.1", "forge", 2)
            }));
        }));
        var versions = await Create(http).GetVersionsAsync("Modrinth", "example", "1.20.1", "fabric");
        Assert.Equal(["wrong-game", "wrong-loader", "old"], versions.Select(version => version.Modrinth!.Id));
        Assert.Equal([false, false, true], versions.Select(version => version.IsCompatible));
        Assert.All(versions, version => Assert.Null(version.CurseForge));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "", true)]
    [InlineData("1.20.1", "vanilla", true)]
    [InlineData("1.20.1", "FABRIC", true)]
    [InlineData("1.21.1", "vanilla", false)]
    [InlineData(null, "forge", false)]
    public async Task ModrinthCompatibilityAllowsBrowsingWithoutAnInstallTarget(string? game, string? loader, bool compatible)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new[] { MrVersion("main", "1.20.1", "fabric", 1) }))));
        var version = Assert.Single(await Create(http).GetVersionsAsync("Modrinth", "example", game, loader));
        Assert.Equal(compatible, version.IsCompatible);
        Assert.Equal("main.jar", Assert.Single(version.Modrinth!.Files).Filename);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("1.20.1", "vanilla", true)]
    [InlineData("1.20.1", "FABRIC", true)]
    [InlineData("1.20.1", "forge", false)]
    [InlineData("1.21.1", "fabric", false)]
    public async Task CurseForgeCompatibilityUsesGameAndLoaderTagsWithoutFilteringTheCatalog(
        string? game, string? loader, bool compatible)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/v1/mods/123/files", request.RequestUri!.AbsolutePath);
            Assert.DoesNotContain("modLoaderType=", request.RequestUri.Query);
            Assert.Contains("gameVersion=&", request.RequestUri.Query);
            return Task.FromResult(Json(new { data = new[] { CfFile(1, 1), CfFile(2, 2) } }));
        }));
        var versions = await Create(http).GetVersionsAsync("CurseForge", "123", game, loader);
        Assert.Equal([2L, 1L], versions.Select(version => version.CurseForge!.Id));
        Assert.All(versions, version =>
        {
            Assert.Null(version.Modrinth);
            Assert.Equal(compatible, version.IsCompatible);
            Assert.Equal(123, version.CurseForge!.ProjectId);
        });
    }

    [Fact]
    public async Task InvalidCurseForgeProjectIdDoesNotIssueARequest()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected request")));
        Assert.Empty(await Create(http).GetVersionsAsync("CurseForge", "not-a-number", null, null));
    }

    [Fact]
    public async Task AvailabilityTracksApiKeyChangesAndMissingKeyNeverIssuesARequest()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected request")));
        var curseForge = new CurseForgeClient("", http, http);
        var service = new ModCatalogService(new ModrinthClient(http, http), curseForge);
        Assert.True(service.IsAvailable("Modrinth"));
        Assert.False(service.IsAvailable("CurseForge"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("CurseForge", "", "", ""));
        curseForge.ApiKey = "new-key";
        Assert.True(service.IsAvailable("CurseForge"));
    }

    [Theory]
    [InlineData("Modrinth", false)]
    [InlineData("CurseForge", false)]
    [InlineData("Modrinth", true)]
    [InlineData("CurseForge", true)]
    public async Task CancellationReachesSearchAndVersionRequests(string platform, bool versions)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Canceled request continued");
        }));
        var service = Create(http);
        using var cancellation = new CancellationTokenSource();
        Task operation = versions
            ? service.GetVersionsAsync(platform, "123", "1.20.1", "fabric", cancellation.Token)
            : service.SearchAsync(platform, "", "", "", ct: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static ModCatalogService Create(HttpClient http) =>
        new(new ModrinthClient(http, http), new CurseForgeClient("fixture", http, http));

    private static HttpResponseMessage SearchResponse(string platform, int count, int total) => platform == "CurseForge"
        ? Json(new
        {
            data = Enumerable.Range(1, count).Select(id => new { id, name = "Example", slug = "example", summary = "Description", downloadCount = 1 }),
            pagination = new { totalCount = total }
        })
        : Json(new
        {
            hits = Enumerable.Range(1, count).Select(id => new
            {
                project_id = id.ToString(),
                title = "Example",
                slug = "example",
                description = "Description",
                project_type = "mod",
                downloads = 1,
                categories = new[] { "fabric" }
            }),
            total_hits = total
        });

    private static object MrVersion(string id, string game, string loader, int day, bool files = true) => new
    {
        id,
        project_id = "example",
        name = id,
        version_number = id,
        version_type = "release",
        date_published = DateTimeOffset.UnixEpoch.AddDays(day),
        game_versions = new[] { game },
        loaders = new[] { loader },
        files = files ? new[] { new { filename = id + ".jar", url = "https://example.invalid/" + id + ".jar", size = 1, primary = true } } : []
    };

    private static object CfFile(long id, int day) => new
    {
        id,
        modId = 123,
        displayName = "Example",
        fileName = id + ".jar",
        fileDate = DateTimeOffset.UnixEpoch.AddDays(day),
        fileLength = 1,
        downloadUrl = "https://example.invalid/" + id + ".jar",
        gameVersions = new[] { "1.20.1", "Fabric Loader" }
    };

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value))
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
