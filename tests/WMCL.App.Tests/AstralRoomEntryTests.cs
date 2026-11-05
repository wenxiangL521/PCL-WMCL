using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Link;

namespace WMCL.App.Tests;

public sealed class AstralRoomEntryTests
{
    [Fact]
    public void ShortCodeSurvivesSerialization()
    {
        var entry = new AstralRoomEntry("Astral 房间", "", "", [], "Astral Game", DateTimeOffset.UnixEpoch, "9Q0Q58");
        var json = JsonSerializer.Serialize(entry);
        var back = JsonSerializer.Deserialize<AstralRoomEntry>(json);
        Assert.NotNull(back);
        Assert.Equal("9Q0Q58", back!.ShortCode);
        Assert.Contains("短码 9Q0Q58", back.Summary);
    }

    [Fact]
    public void EntryWithoutShortCodeKeepsNodeSummary()
    {
        var entry = new AstralRoomEntry("房间", "net", "secret", ["tcp://a:1", "tcp://b:2"], "Astral", DateTimeOffset.UnixEpoch);
        Assert.Equal("2 个节点 · 不保护", entry.Summary);
        Assert.Equal("保护", (entry with { IsProtected = true }).TypeText);
    }

    [AvaloniaFact]
    public async Task JoiningIncompleteEntryReportsError()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "entry-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);
        var astral = new AstralViewModel(link);
        astral.Rooms.Add(new AstralRoomEntry("空房间", "", "", [], "Astral Game", DateTimeOffset.Now));
        astral.SelectedRoom = astral.Rooms[0];
        await astral.JoinSelectedCommand.ExecuteAsync(null);
        Assert.Contains("不完整", astral.Status);
        Assert.False(link.IsRunning);
    }
}
