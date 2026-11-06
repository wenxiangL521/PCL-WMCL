using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WMCL.App.Services;
using WMCL.Link;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class AstralInviteParserTests
{
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string OriginalCode(object payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            gzip.Write(json);
        }
        return Base64Url(buffer.ToArray());
    }

    [Fact]
    public void ParsesOriginalAstralShareLink()
    {
        var code = OriginalCode(new
        {
            n = "朋友房",
            r = "room-abc",
            p = "secret-xyz",
            e = 0,
            s = new[] { "tcp://public.easytier.top:11010", "udp://relay.example:11010" }
        });
        Assert.True(AstralInviteParser.TryParse($"astral://room?code={code}", out var room, out var error), error);
        Assert.NotNull(room);
        Assert.Equal("room-abc", room!.NetworkName);
        Assert.Equal("secret-xyz", room.NetworkSecret);
        Assert.Equal(2, room.Peers.Count);
        Assert.Equal("Astral", room.Source);
    }

    [Fact]
    public void ParsesAstralGameOfflineInvite()
    {
        var json = JsonSerializer.Serialize(new
        {
            v = 1,
            game_id = "minecraft",
            game_name = "Minecraft",
            network_name = "astral",
            network_secret = "astral",
            peers = new object[] { new { uri = "tcp://1.2.3.4:11010" }, new { uri = "udp://5.6.7.8:11010" } },
            display_name = "小房间"
        });
        var code = "AG1." + Base64Url(Encoding.UTF8.GetBytes(json));
        Assert.True(AstralInviteParser.TryParse(code, out var room, out var error), error);
        Assert.Equal("astral", room!.NetworkName);
        Assert.Equal("astral", room.NetworkSecret);
        Assert.Equal(2, room.Peers.Count);
        Assert.Equal("Astral Game", room.Source);
    }

    [Fact]
    public void ShortCodeProducesRoomThatNeedsResolution()
    {
        Assert.True(AstralInviteParser.TryParse("A1B2C3", out var room, out var error), error);
        Assert.NotNull(room);
        Assert.Equal("A1B2C3", room!.ShortCode);
        Assert.True(room.NeedsResolve);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.False(AstralInviteParser.TryParse("hello world", out _, out var error));
        Assert.NotNull(error);
    }
}
