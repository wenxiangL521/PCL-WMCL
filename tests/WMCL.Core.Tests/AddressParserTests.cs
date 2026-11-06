using WMCL.Core;

namespace WMCL.Core.Tests;

[Trait("Kind", "Unit")]
public sealed class AddressParserTests
{
    [Theory]
    [InlineData("mc.example.com", null, "mc.example.com", 25565)]
    [InlineData("mc.example.com:25566", null, "mc.example.com", 25566)]
    [InlineData("[::1]:25567", null, "::1", 25567)]
    [InlineData("mc.example.com", 25570, "mc.example.com", 25570)]
    [InlineData("play.example.com:1000", 25570, "play.example.com", 25570)]
    [InlineData("fe80::1", null, "fe80::1", 25565)]
    public void SplitsHostAndPort(string input, int? port, string host, int expected)
    {
        var (actualHost, actualPort) = AddressParser.Split(input, port, 25565);
        Assert.Equal(host, actualHost);
        Assert.Equal(expected, actualPort);
    }
}
