using WMCL.Link;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class EasyTierCapabilityTests
{
    [Theory]
    [InlineData("/path/easytier-core cap_net_admin=ep", true)]
    [InlineData("/path/easytier-core cap_net_raw=ep cap_net_admin,cap_net_raw+ep", true)]
    [InlineData("/path/easytier-core cap_net_raw=ep", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParsesGetcapOutput(string? output, bool expected)
    {
        Assert.Equal(expected, EasyTierService.HasNetAdminCapability(output));
    }
}
