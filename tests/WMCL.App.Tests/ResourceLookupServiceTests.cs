using System.Net;
using System.Text;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ResourceLookupServiceTests
{
    [Fact]
    public async Task ResolvesModrinthStringIdAndRejectsNumericIds()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("/project/abc", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"abc","slug":"pack","title":"Pack","description":"desc","project_type":"resourcepack"}
                    """, Encoding.UTF8, "application/json")
            };
        });
        var service = new ResourceLookupService(new ModrinthClient(new HttpClient(handler)));
        var result = await service.ResolveAsync("abc");
        Assert.NotNull(result);
        Assert.Equal("Modrinth", result!.Platform);
        Assert.Equal("resourcepack", result.ResourceType);
        Assert.Equal("Pack", result.Title);
        Assert.Null(await service.ResolveAsync("12345"));
    }

    [Fact]
    public async Task AsyncImportResolvesUnknownModrinthId()
    {
        var settings = new AppSettings();
        var favorites = new ResourceFavoritesService(settings, () => { });
        var viewModel = new ResourceFavoritesViewModel(favorites, _ => Task.CompletedTask,
            lookup: (id, _) => Task.FromResult<FavoriteResource?>(id == "remote"
                ? new FavoriteResource("Modrinth", "remote", "mod", "Remote", "desc", "remote")
                : null));
        Assert.Equal(1, await viewModel.ImportShareCodeAsync("""["remote"]"""));
        Assert.Single(viewModel.Rows);
        Assert.Equal("remote", viewModel.Rows[0].Resource.ProjectId);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
