using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.Link;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class AstralPageTests
{
    private static (MainWindowViewModel Main, AstralViewModel Astral) Create()
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "astral-page-" + Guid.NewGuid().ToString("N"))
        };
        var main = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
        return (main, main.Astral);
    }

    [AvaloniaFact]
    public void PreviewShowsRoomNameSourceAndPeerCount()
    {
        var (main, astral) = Create();
        using var _ = main;
        var json = """{"v":1,"game_id":"mc","game_name":"Minecraft","network_name":"astral","network_secret":"astral","peers":[{"uri":"tcp://1.2.3.4:11010"},{"uri":"udp://5.6.7.8:11010"}]}""";
        astral.InviteText = "AG1." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Contains("Minecraft", astral.Preview);
        Assert.Contains("Astral Game", astral.Preview);
        Assert.Contains("2", astral.Preview);
    }

    [AvaloniaFact]
    public void RelaysCanBeAddedToggledAndRemoved()
    {
        var (main, astral) = Create();
        using var _ = main;
        var before = astral.Relays.Count;
        astral.NewRelayUri = "tcp://relay.example:11010";
        astral.NewRelayName = "测试";
        astral.AddRelayCommand.Execute(null);
        Assert.Equal(before + 1, astral.Relays.Count);

        astral.SelectedRelay = astral.Relays.Last();
        astral.ToggleRelayCommand.Execute(null);
        Assert.False(astral.Relays.Last().Enabled);

        astral.RemoveRelayCommand.Execute(null);
        Assert.Equal(before, astral.Relays.Count);
    }

    [AvaloniaFact]
    public void UsernameFollowsMcAccountNameUntilOverridden()
    {
        var (main, astral) = Create();
        using var _ = main;
        main.Services.Settings.PlayerName = "Steve";
        main.Launch.PlayerName = "Steve";
        // 其它用例可能已写入 astral.json 的覆盖用户名，这里先清空保证独立
        try { File.Delete(WMCL.Link.AstralProfileStore.FilePath); } catch { }
        var refreshed = new AstralViewModel(main.Link, main.Launch);
        Assert.Equal("Steve", refreshed.Username);

        refreshed.Username = "自定义名";
        Assert.Equal("自定义名", refreshed.Username);
        refreshed.UseAccountNameCommand.Execute(null);
        Assert.Equal("Steve", refreshed.Username);
    }

    [AvaloniaFact]
    public void ExternalRoomShowsNetworkNameInsteadOfRoomCode()
    {
        var (main, _) = Create();
        using var __ = main;
        var link = main.Link;
        Assert.Contains("房间码", link.RoomCaption);
        link.ExternalNetworkName = "astral";
        Assert.Contains("网络", link.RoomCaption);
        Assert.Contains("astral", link.RoomCaption);
    }
}
