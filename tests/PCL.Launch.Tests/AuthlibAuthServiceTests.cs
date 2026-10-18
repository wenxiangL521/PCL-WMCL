using System.Net;
using System.Text;
using PCL.Launch.Auth;

namespace PCL.Launch.Tests;

public class AuthlibAuthServiceTests
{
    [Fact]
    public async Task AuthenticateParsesProfileAndDashesUuid()
    {
        var handler = new StubHandler(request =>
        {
            Assert.EndsWith("/authserver/authenticate", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"accessToken":"access","clientToken":"client","selectedProfile":{"id":"069a79f444e94726a5befca90e38aaf5","name":"Steve"}}
                    """, Encoding.UTF8, "application/json")
            };
        });
        var service = new AuthlibAuthService(new HttpClient(handler));

        var account = await service.AuthenticateAsync("littleskin.cn/api/yggdrasil", "a@b.c", "pw");

        Assert.Equal("https://littleskin.cn/api/yggdrasil", account.Server);
        Assert.Equal("Steve", account.Name);
        Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", account.Uuid);
        Assert.Equal("access", account.AccessToken);
        Assert.Equal("client", account.ClientToken);
    }

    [Fact]
    public async Task AuthenticateReadsServerError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"errorMessage":"密码错误"}""", Encoding.UTF8, "application/json")
        });
        var service = new AuthlibAuthService(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AuthenticateAsync("https://example.com", "a", "b"));
        Assert.Contains("密码错误", exception.Message);
    }

    [Fact]
    public async Task AccountStoreRoundTripsAndClears()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-auth-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var account = new AuthlibAccount("https://example.com", "Alex", "uuid", "token", "client");
            AuthlibAccountStore.Save(account, path);
            Assert.Equal(account, AuthlibAccountStore.Load(path));
            AuthlibAccountStore.Clear(path);
            Assert.Null(AuthlibAccountStore.Load(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeServerAddsHttpsAndTrimsSlash()
    {
        Assert.Equal("https://example.com/api/yggdrasil",
            AuthlibAuthService.NormalizeServer("example.com/api/yggdrasil/"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
