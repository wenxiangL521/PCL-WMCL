using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Link;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AstralCreateRoomTests
{
    private static AstralViewModel Create(out LinkViewModel link)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "create-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        link = new LinkViewModel(services);
        return new AstralViewModel(link);
    }

    [AvaloniaFact]
    public void CreatePageListsRelaysUncheckedByDefault()
    {
        var model = Create(out _);
        model.Relays.Clear();
        model.Relays.Add(new AstralRelay("tcp://a:1", "A", true));
        model.Relays.Add(new AstralRelay("tcp://b:2", "B", false));
        model.ShowCreateCommand.Execute(null);
        Assert.Equal(2, model.CreateRelays.Count);
        Assert.All(model.CreateRelays, choice => Assert.False(choice.IsSelected));
    }

    [AvaloniaFact]
    public void CreatePageFallsBackToPublicRelaysWhenNoneConfigured()
    {
        var model = Create(out _);
        model.Relays.Clear();
        model.ShowCreateCommand.Execute(null);
        Assert.Equal(AstralInviteParser.DefaultPeers().Count, model.CreateRelays.Count);
        Assert.All(model.CreateRelays, choice => Assert.False(choice.IsSelected));
    }

    [AvaloniaFact]
    public void ProtectedRoomIgnoresManualFields()
    {
        var model = Create(out _);
        model.ShowCreateCommand.Execute(null);
        model.IsProtectedRoom = true;
        model.NewRoomNetwork = "manual-net";
        model.NewRoomSecret = "manual-secret";
        model.NewRoomName = "保护房";
        model.CreateRoomCommand.Execute(null);
        Assert.DoesNotContain("不完整", model.Status);
        // 保护房间的房间号由程序生成，不会用界面手填值
        Assert.NotEqual("manual-net", model.CreateRelays.FirstOrDefault()?.Uri);
    }

    [AvaloniaFact]
    public void UnprotectedRoomAcceptsEmptyPassword()
    {
        var model = Create(out _);
        model.ShowCreateCommand.Execute(null);
        model.IsProtectedRoom = false;
        model.NewRoomSecret = "";
        model.NewRoomName = "开放房";
        model.CreateRoomCommand.Execute(null);
        Assert.DoesNotContain("不完整", model.Status);
    }

    [Fact]
    public void RelayChoiceDisplayIncludesName()
    {
        Assert.Equal("A（tcp://a:1）", new AstralRelayChoice("tcp://a:1", "A", true).Display);
        Assert.Equal("tcp://a:1", new AstralRelayChoice("tcp://a:1", "", true).Display);
    }
}

public sealed class AstralShareLinkTests
{
    [Fact]
    public void LongLinkRoundTripsThroughParser()
    {
        var room = new AstralRoom("测试房", "net-abc", "secret-xyz", ["tcp://1.2.3.4:11010"], "Astral")
        {
            IsProtected = true
        };
        var link = AstralViewModel.BuildLongLink(room);
        Assert.StartsWith("astral://room?code=", link);

        Assert.True(AstralInviteParser.TryParse(link, out var parsed, out var error), error);
        Assert.Equal(room.Name, parsed!.Name);
        Assert.Equal(room.NetworkName, parsed.NetworkName);
        Assert.Equal(room.NetworkSecret, parsed.NetworkSecret);
        Assert.Equal(room.Peers, parsed.Peers);
        Assert.True(parsed.IsProtected);
    }

    [Fact]
    public void LongLinkMarksProtectedRooms()
    {
        var room = new AstralRoom("保护房", "net-p", "secret-p", [], "Astral") { IsProtected = true };
        var link = AstralViewModel.BuildLongLink(room);
        Assert.True(AstralInviteParser.TryParse(link, out var parsed, out var error), error);
        Assert.True(parsed!.IsProtected);
    }

    [Fact]
    public void LongLinkKeepsUnprotectedStateWithoutSecret()
    {
        var room = new AstralRoom("开放房", "net-open", "", [], "Astral");
        var link = AstralViewModel.BuildLongLink(room);
        Assert.True(AstralInviteParser.TryParse(link, out var parsed, out var error), error);
        Assert.Equal("net-open", parsed!.NetworkName);
        Assert.False(parsed.IsProtected);
    }
}

public sealed class AstralCopyLinkTests
{
    private static AstralViewModel Create()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "copy-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);
        return new AstralViewModel(link);
    }

    [AvaloniaFact]
    public async Task CopyingEmptyLinkExplainsItIsNotReady()
    {
        var model = Create();
        await model.CopyShortLinkCommand.ExecuteAsync(null);
        Assert.Contains("还没生成", model.Status);
    }

    [AvaloniaFact]
    public async Task CopyingLinkAlwaysReportsOutcome()
    {
        var model = Create();
        model.LongShareLink = "astral://room?code=abc";
        await model.CopyLongLinkCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrWhiteSpace(model.Status));
        Assert.Contains("长链", model.Status);
    }
}

public sealed class AstralJoinShareLinkTests
{
    [AvaloniaFact]
    public void ShortCodeRoomReusesItsCodeForSharing()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "joinlink-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);
        var model = new AstralViewModel(link);

        var room = new AstralRoom("房间", "net-1", "sec-1", ["tcp://a:1"], "Astral Game")
        {
            ShortCode = "9Q0Q58"
        };
        // 直接调用内部链接生成逻辑（不启动 EasyTier）
        var method = typeof(AstralViewModel).GetMethod("BuildShareLinksAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var task = (Task)method.Invoke(model, [room])!;
        task.GetAwaiter().GetResult();

        Assert.Equal("https://next.astral.fan/j?c=9Q0Q58", model.ShareLink);
        Assert.StartsWith("astral://room?code=", model.LongShareLink);
    }
}
