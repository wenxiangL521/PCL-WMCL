using System.Net;
using System.Text;
using PCL.Launch.Auth;

namespace PCL.Launch.Tests;

public class MicrosoftAuthTests
{
    private static MicrosoftEndpoints TestEndpoints => new(
        "https://test/devicecode",
        "https://test/token",
        "https://test/xbl",
        "https://test/xsts",
        "https://test/mc",
        "https://test/profile",
        "https://test/entitlements");

    [Fact]
    public async Task CompletesDeviceCodeLoginFlow()
    {
        var responses = new Queue<(HttpStatusCode, string)>();
        responses.Enqueue((HttpStatusCode.OK, """
            {"device_code":"dev","user_code":"ABCD","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1,"message":"open browser"}
            """));
        responses.Enqueue((HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""));
        responses.Enqueue((HttpStatusCode.OK, """
            {"access_token":"oauth","refresh_token":"refresh","expires_in":3600}
            """));
        responses.Enqueue((HttpStatusCode.OK, """
            {"Token":"xbl","DisplayClaims":{"xui":[{"uhs":"uhs-value"}]}}
            """));
        responses.Enqueue((HttpStatusCode.OK, """
            {"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs-value"}]}}
            """));
        responses.Enqueue((HttpStatusCode.OK, """{"access_token":"mc-token","expires_in":86400}"""));
        responses.Enqueue((HttpStatusCode.OK, """{"items":[{"name":"game_minecraft"}]}"""));
        responses.Enqueue((HttpStatusCode.OK, """{"id":"uuid-value","name":"Steve"}"""));

        var handler = new FakeHandler(responses);
        var service = new MicrosoftAuthService(new HttpClient(handler), TestEndpoints);

        var progress = new List<MicrosoftAuthProgress>();
        var account = await service.LoginAsync(
            "client-id",
            new SyncProgress(progress.Add),
            saveAccount: false);

        Assert.Equal("Steve", account.Name);
        Assert.Equal("uuid-value", account.Uuid);
        Assert.Equal("mc-token", account.AccessToken);
        Assert.Equal("refresh", account.RefreshToken);
        Assert.Contains(progress, p => p.Stage == "等待微软登录" && p.UserCode == "ABCD");
        Assert.Empty(responses);
    }

    [Fact]
    public async Task RefreshesExpiredLogin()
    {
        var responses = new Queue<(HttpStatusCode, string)>();
        responses.Enqueue((HttpStatusCode.OK, """
            {"access_token":"oauth-new","refresh_token":"refresh-new","expires_in":3600}
            """));
        responses.Enqueue((HttpStatusCode.OK, """
            {"Token":"xbl","DisplayClaims":{"xui":[{"uhs":"uhs-value"}]}}
            """));
        responses.Enqueue((HttpStatusCode.OK, """
            {"Token":"xsts","DisplayClaims":{"xui":[{"uhs":"uhs-value"}]}}
            """));
        responses.Enqueue((HttpStatusCode.OK, """{"access_token":"mc-token","expires_in":86400}"""));
        responses.Enqueue((HttpStatusCode.OK, """{"items":[{"name":"game_minecraft"}]}"""));
        responses.Enqueue((HttpStatusCode.OK, """{"id":"uuid-value","name":"Alex"}"""));

        var handler = new FakeHandler(responses);
        var service = new MicrosoftAuthService(new HttpClient(handler), TestEndpoints);

        var account = await service.RefreshAsync(
            "client-id", "refresh-old", saveAccount: false);

        Assert.Equal("Alex", account.Name);
        Assert.Equal("refresh-new", account.RefreshToken);
        Assert.Empty(responses);
    }

    [Fact]
    public void StoresAndLoadsAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-ms-" + Guid.NewGuid().ToString("N") + ".json");
        var account = new MicrosoftAccount(
            "Steve", "uuid", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        try
        {
            MicrosoftAccountStore.Save(account, path);
            var loaded = MicrosoftAccountStore.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal("Steve", loaded.Name);
            Assert.Equal("uuid", loaded.Uuid);
            Assert.Equal("refresh", loaded.RefreshToken);
        }
        finally
        {
            MicrosoftAccountStore.Clear(path);
        }
    }

    private sealed class FakeHandler(Queue<(HttpStatusCode Status, string Json)> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var (status, json) = responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SyncProgress(Action<MicrosoftAuthProgress> action)
        : IProgress<MicrosoftAuthProgress>
    {
        public void Report(MicrosoftAuthProgress value) => action(value);
    }
}
