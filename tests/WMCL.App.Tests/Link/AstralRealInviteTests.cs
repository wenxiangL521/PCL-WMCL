using WMCL.Link;

namespace WMCL.App.Tests;

/// <summary>用 Astral 原版客户端实际导出的分享码做回归。</summary>
[Trait("Kind", "Unit")]
public sealed class AstralRealInviteTests
{
    private const string WithServer =
        "astral://room?code=H4sIAAAAAAAAAxXKsVEEMQwF0F5-bN1ZltdrqRWGwLZsooMdOCKGCi6-OohoiaEMhhe_DzzD8PN9-_26I-AVhhhlNlemPqJT9typNmZS5bWyyCqLEXDAMNvcdUqiXVKn7ON_xUZ5L9yHdm_SEXCBYdtyHLUvWupOWdqkxp3J06rFN69aJgImjAPeYA-4jsPO56d2mad3b366vhxWmJPgMWDAwLtKZdWYqig-_wAyjb20ywAAAA";

    private const string WithoutServer =
        "astral://room?code=H4sIAAAAAAAAAxXKsQ0CMQwF0F1-HUvx2ZdLsk0cOx0I0SImoGYOKlZCjIF49bvhjI7P-_F9PZFwRUfOEsMbk83spK5GdTBTa7yWiqyyGAkXdMSIo4VsdMhmpD7_Kw_So7DNZj7EkHBCx75rntUWreZOKiNosDH5tmrx3WsrgYRA5_sPuIjVV5UAAAA";

    [Fact]
    public void ParsesInviteWithServer()
    {
        Assert.True(AstralInviteParser.TryParse(WithServer, out var room, out var error), error);
        Assert.NotNull(room);
        Assert.Equal("测试", room!.Name);
        Assert.Equal("003ead91-bc0d-4d4b-8a11-991ff433f6f1", room.NetworkName);
        Assert.Equal("eae79e32-732b-4dc1-990a-4761bc9bda3b", room.NetworkSecret);
        Assert.Equal(["tcp://game.udad.top:61123"], room.Peers);
        Assert.False(room.NeedsResolve);
        Assert.True(room.IsProtected);
    }

    [Fact]
    public void InviteWithoutServerFallsBackToPublicRelays()
    {
        Assert.True(AstralInviteParser.TryParse(WithoutServer, out var room, out var error), error);
        Assert.NotNull(room);
        Assert.Equal("测试", room!.Name);
        Assert.Equal("003ead91-bc0d-4d4b-8a11-991ff433f6f1", room.NetworkName);
        Assert.True(AstralInviteParser.UsedFallbackPeers(room));
    }

    [Fact]
    public void UnprotectedInviteWithoutSecretStillParses()
    {
        // 不保护房间：p 为空、e 为 0
        var payload = """{"n":"开放房间","r":"room-open","p":"","e":0}""";
        using var buffer = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
                   buffer, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            gzip.Write(bytes, 0, bytes.Length);
        }
        var code = Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.True(AstralInviteParser.TryParse("astral://room?code=" + code, out var room, out var error), error);
        Assert.Equal("room-open", room!.NetworkName);
        Assert.Equal("", room.NetworkSecret);
        Assert.False(room.IsProtected);
    }

    [Fact]
    public void SameRoomSharesIdentityAcrossBothInvites()
    {
        AstralInviteParser.TryParse(WithServer, out var withServer, out _);
        AstralInviteParser.TryParse(WithoutServer, out var withoutServer, out _);
        Assert.Equal(withServer!.NetworkName, withoutServer!.NetworkName);
        Assert.Equal(withServer.NetworkSecret, withoutServer.NetworkSecret);
    }
}
