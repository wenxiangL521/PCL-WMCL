using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.Link;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class NetworkSubnetProbeTests
{
    [Fact]
    public void IsInPrefixMatchesOnlyIpv4InSameSubnet()
    {
        Assert.True(NetworkSubnetProbe.IsInPrefix("10.126.126.5", NetworkSubnetProbe.EasyTierDefaultPrefix));
        Assert.False(NetworkSubnetProbe.IsInPrefix("10.126.127.5", NetworkSubnetProbe.EasyTierDefaultPrefix));
        Assert.False(NetworkSubnetProbe.IsInPrefix("::1", NetworkSubnetProbe.EasyTierDefaultPrefix));
        Assert.False(NetworkSubnetProbe.IsInPrefix("not-an-ip", NetworkSubnetProbe.EasyTierDefaultPrefix));
    }

    [Fact]
    public void FindAddressesReturnsListAndNeverThrows()
    {
        var found = NetworkSubnetProbe.FindAddresses(NetworkSubnetProbe.EasyTierDefaultPrefix);
        Assert.NotNull(found);
        Assert.All(found, address => Assert.StartsWith("10.126.126.", address));
    }

    [AvaloniaFact]
    public async Task JoinWithForeignVirtualIpIsRejectedBeforeStarting()
    {
        var settings = new AppSettings { GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "subnet-" + Guid.NewGuid().ToString("N")) };
        var model = new LinkViewModel(new AppServices(settings, saveSettings: _ => { }));
        var json = """{"v":1,"game_id":"mc","game_name":"Minecraft","network_name":"astral","network_secret":"astral","peers":[{"uri":"tcp://1.2.3.4:11010"}]}""";
        model.AstralInvite = "AG1." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        model.AstralVirtualIp = "192.168.1.50";
        await model.JoinAstralCommand.ExecuteAsync(null);
        Assert.Contains("网段", model.AstralStatus);
        Assert.False(model.IsRunning);
    }
}
