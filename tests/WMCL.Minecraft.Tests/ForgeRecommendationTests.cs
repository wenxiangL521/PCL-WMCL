using System.Net;
using WMCL.Minecraft;
using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public sealed class ForgeRecommendationTests
{
    private const string Versions = """
        [{"version":"47.2.0","files":[{"category":"installer","format":"jar"}]},
         {"version":"47.1.0","files":[{"category":"installer","format":"jar"}]},
         {"version":"47.0.0","files":[{"category":"universal","format":"zip"}]}]
        """;

    [Fact]
    public void OnlyValidRecommendedPromotionsAreUsed()
    {
        const string json = """
            [{"name":"1.20.1-recommended","build":{"version":"47.1.0"}},
             {"name":"1.20.1-latest","build":{"version":"47.2.0"}},
             {"name":"1.21.1-recommended","build":{"version":"52.0.0"}},
             {"name":"broken-recommended","build":{"version":"not-version"}},
             {"name":123,"build":{"version":"47.3.0"}},
             {"name":"empty-recommended","build":null},null]
            """;
        var recommended = ForgelikeInstaller.ParseForgeRecommendations(json);
        Assert.Equal(2, recommended.Count);
        Assert.Equal("47.1.0", recommended["1.20.1"]);
        Assert.Equal("52.0.0", recommended["1.21.1"]);
    }

    [Theory]
    [InlineData("47.1.0", 1)]
    [InlineData("47.2.0", 1)]
    [InlineData("47.0.0", 0)]
    [InlineData(null, 0)]
    public void RecommendationNeverAddsUninstallableOrMissingVersions(string? recommended, int count)
    {
        var details = ForgelikeInstaller.ParseForgeVersionDetails(Versions, "1.20.1", recommended);
        Assert.Equal(["47.2.0", "47.1.0"], details.Select(detail => detail.Version));
        Assert.Equal(count, details.Count(detail => detail.IsRecommended));
        Assert.All(details.Where(detail => detail.IsRecommended), detail => Assert.Equal(recommended, detail.Version));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecommendationFetchIsCachedAndFailureDoesNotPreventVersionSelection(bool fail)
    {
        var promotions = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            var promotion = request.RequestUri!.AbsolutePath.EndsWith("/promos", StringComparison.Ordinal);
            if (promotion) promotions++;
            return new HttpResponseMessage(promotion && fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(promotion
                    ? """[{"name":"1.20.1-recommended","build":{"version":"47.1.0"}}]""" : Versions)
            };
        }));
        var installer = new ForgelikeInstaller(new VersionRepository(Path.GetTempPath()), http: http);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var details = await installer.GetForgeVersionDetailsAsync("1.20.1");
            Assert.Equal(2, details.Count);
            Assert.Equal(fail ? 0 : 1, details.Count(detail => detail.IsRecommended));
        }
        Assert.Equal(1, promotions);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
