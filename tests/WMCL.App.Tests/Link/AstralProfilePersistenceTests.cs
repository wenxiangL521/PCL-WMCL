using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Link;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AstralProfilePersistenceTests
{
    private static AstralViewModel Create(LinkViewModel link) => new(link);

    [AvaloniaFact]
    public void ConstructingViewModelDoesNotWipeSavedRoomsOrRelays()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "persist-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);

        var seed = AstralProfileStore.Load();
        seed.Rooms =
        [
            new AstralRoomEntry("房间A", "net-a", "secret-a", ["tcp://a:1"], "Astral", DateTimeOffset.Now)
        ];
        seed.Relays = [new AstralRelay("tcp://relay:1", "中继", true)];
        AstralProfileStore.Save(seed);

        // 启动时构造视图模型
        var model = Create(link);
        Assert.Single(model.Rooms);
        Assert.Single(model.Relays);

        // 磁盘内容不能被空集合覆盖
        var reloaded = AstralProfileStore.Load();
        Assert.Single(reloaded.Rooms);
        Assert.Single(reloaded.Relays);
    }

    [AvaloniaFact]
    public void RemovingRoomPersistsToDisk()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "persist2-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var link = new LinkViewModel(services);

        var seed = AstralProfileStore.Load();
        seed.Rooms = [new AstralRoomEntry("待删房间", "net-del", "s", [], "Astral", DateTimeOffset.Now)];
        AstralProfileStore.Save(seed);

        var model = Create(link);
        model.SelectedRoom = model.Rooms[0];
        model.RemoveRoomCommand.Execute(null);
        Assert.Empty(model.Rooms);
        Assert.Empty(AstralProfileStore.Load().Rooms);
    }
}
