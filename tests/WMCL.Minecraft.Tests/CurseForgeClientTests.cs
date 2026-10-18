using System.Net;
using System.Text;
using WMCL.Modding.CurseForge;

namespace WMCL.Minecraft.Tests;

public class CurseForgeClientTests
{
    [Fact]
    public async Task SearchesAndPicksNewestFile()
    {
        var client = new CurseForgeClient("test-key", new HttpClient(new FakeHandler()));

        var search = await client.SearchAsync("jei", "1.20.1", "forge");
        Assert.Single(search.Projects);
        Assert.Equal("Just Enough Items", search.Projects[0].Name);

        var file = await client.FindFileAsync(238222, "1.20.1", "forge");
        Assert.NotNull(file);
        Assert.Equal("jei-1.20.1-forge-15.71.0.224.jar", file.FileName);
        Assert.Equal("def", file.Sha1);
        Assert.Equal(2, file.Dependencies.Count);
    }


    [Fact]
    public async Task TestsApiKey()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            };
        });
        var client = new CurseForgeClient("secret-key", new HttpClient(handler));
        await client.TestKeyAsync();
        Assert.NotNull(captured);
        Assert.Contains("/games", captured!.RequestUri!.AbsolutePath);
        Assert.Equal("secret-key", captured.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task SearchPassesOffsetIndex()
    {
        Uri? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[],"pagination":{"totalCount":100}}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new CurseForgeClient("key", new HttpClient(handler));
        await client.SearchAsync("", "1.20.1", "forge", 20, default, 6, true, 40);
        Assert.NotNull(captured);
        Assert.Contains("index=40", captured!.Query);
    }

    [Fact]
    public async Task ResourceSearchUsesClassIdWithoutLoaderFilter()
    {
        Uri? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"id":1,"name":"Pack","slug":"pack","summary":"s","downloadCount":1,"logo":null}],"pagination":{"totalCount":1}}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new CurseForgeClient("key", new HttpClient(handler));
        var result = await client.SearchAsync(
            "", "1.20.1", "", 20, default, CurseForgeClient.ClassIdResourcePacks, false);
        Assert.Single(result.Projects);
        Assert.NotNull(captured);
        Assert.Contains("classId=12", captured!.Query);
        Assert.DoesNotContain("modLoaderType", captured.Query);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task ListsCompatibleFilesNewestFirst()
    {
        var client = new CurseForgeClient("test-key", new HttpClient(new FakeHandler()));
        var files = await client.GetFilesAsync(238222, "1.20.1", "forge");
        Assert.Equal(2, files.Count);
        Assert.Equal("jei-1.20.1-forge-15.71.0.224.jar", files[0].FileName);
    }

    [Fact]
    public async Task RequiresApiKey()
    {
        var client = new CurseForgeClient("", new HttpClient(new FakeHandler()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SearchAsync("jei", "1.20.1", "forge"));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isSearch = request.RequestUri!.AbsolutePath.Contains("/mods/search");
            var json = isSearch
                ? """
                  {"data":[{"id":238222,"name":"Just Enough Items","slug":"jei","summary":"items","downloadCount":100,"logo":{"thumbnailUrl":null}}],"pagination":{"totalCount":1}}
                  """
                : """
                  {"data":[
                    {"id":100,"displayName":"old","fileName":"jei-old.jar","fileDate":"2024-01-01T00:00:00Z","fileLength":10,"downloadUrl":"https://edge.forgecdn.net/files/0/100/jei-old.jar","gameVersions":["1.20.1","Forge"],"hashes":[{"value":"abc","algo":1}],"dependencies":[]},
                    {"id":200,"displayName":"new","fileName":"jei-1.20.1-forge-15.71.0.224.jar","fileDate":"2024-06-01T00:00:00Z","fileLength":20,"downloadUrl":null,"gameVersions":["1.20.1","Forge"],"hashes":[{"value":"def","algo":1}],"dependencies":[{"modId":1,"relationType":3},{"modId":2,"relationType":2}]}
                  ]}
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
