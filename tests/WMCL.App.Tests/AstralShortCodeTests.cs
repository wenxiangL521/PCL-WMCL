using System.Net;
using System.Text;
using WMCL.Link;

namespace WMCL.App.Tests;

public sealed class AstralShortCodeTests
{
    [Theory]
    [InlineData("9Q0Q58")]
    [InlineData(" 9q0q58 ")]
    [InlineData("https://next.astral.fan/j?c=9Q0Q58")]
    [InlineData("astralgame://join?c=9Q0Q58")]
    public void ParsesShortCodeFromLinksAndRawCodes(string input)
    {
        Assert.True(AstralInviteParser.TryParse(input, out var room, out var error), error);
        Assert.NotNull(room);
        Assert.Equal("9Q0Q58", room!.ShortCode);
        Assert.True(room.NeedsResolve);
    }

    [Theory]
    [InlineData("O", "0")]
    [InlineData("i", "1")]
    [InlineData("L-9 q", "19Q")]
    public void NormalizesAmbiguousCharacters(string raw, string expected)
    {
        Assert.Equal(expected, AstralInviteParser.NormalizeShortCode(raw));
    }

    [Fact]
    public void ShortLinkUsesAstralGameHost()
    {
        Assert.Equal("https://next.astral.fan/j?c=9Q0Q58", AstralInviteParser.ShortLink("9Q0Q58"));
    }

    [Fact]
    public async Task ResolveReturnsRoomFromPayload()
    {
        const string json = """
        {"v":1,"game_id":"minecraft","game_name":"我的世界","network_name":"astral-test",
         "network_secret":"secret","display_name":"测试房间",
         "peers":[{"uri":"tcp://1.2.3.4:11010"}]}
        """;
        var service = new AstralShareCodeService(FakeClient(json), "http://fake/");
        var room = await service.ResolveAsync("9Q0Q58");
        Assert.Equal("astral-test", room.NetworkName);
        Assert.Equal("secret", room.NetworkSecret);
        Assert.Equal("测试房间", room.Name);
        Assert.Single(room.Peers);
        Assert.False(room.NeedsResolve);
    }

    [Fact]
    public async Task ResolveReportsExpiredCode()
    {
        var service = new AstralShareCodeService(FakeClient("", HttpStatusCode.NotFound), "http://fake/");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync("9Q0Q58"));
        Assert.Contains("无效或已过期", ex.Message);
    }

    [Fact]
    public async Task CreateReturnsShortCode()
    {
        var service = new AstralShareCodeService(
            FakeClient("""{"code":"AB12CD","expires_at":"2026-01-01T00:00:00Z"}"""), "http://fake/");
        var room = new AstralRoom("房间", "net", "secret", ["tcp://1.2.3.4:11010"], "Astral");
        Assert.Equal("AB12CD", await service.CreateAsync(room));
    }

    private static HttpClient FakeClient(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }
}
