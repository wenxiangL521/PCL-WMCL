using System.Net;
using System.Text;
using WMCL.Launch.Auth;

namespace WMCL.Launch.Tests;

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
        var path = Path.Combine(Path.GetTempPath(), "wmcl-auth-" + Guid.NewGuid().ToString("N") + ".json");
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
    public void BuildsNideUrlAndStripsAuthserverSuffix()
    {
        Assert.Equal(
            "https://auth.mc-user.com:233/12345",
            AuthlibAuthService.NideServerUrl("12345"));
        Assert.Equal(
            "https://auth.mc-user.com:233/12345",
            AuthlibAuthService.NormalizeServer("https://auth.mc-user.com:233/12345/authserver"));
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

public class AuthlibAccountStoreMultiTests
{
    [Fact]
    public void StoresMultipleServersAndRemovesOne()
    {
        var path = Path.Combine(Path.GetTempPath(), "wmcl-accounts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AuthlibAccountStore.SaveAccount(
                new AuthlibAccount("https://a.example", "A", "uuid-a", "t1", "c1"), path);
            AuthlibAccountStore.SaveAccount(
                new AuthlibAccount("https://b.example", "B", "uuid-b", "t2", "c2"), path);
            var all = AuthlibAccountStore.LoadAll(path);
            Assert.Equal(2, all.Count);
            Assert.Equal("B", AuthlibAccountStore.Load(path)!.Name);

            AuthlibAccountStore.Remove("https://a.example", "uuid-a", path);
            Assert.Single(AuthlibAccountStore.LoadAll(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
