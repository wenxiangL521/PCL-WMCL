using WMCL.Core;

namespace WMCL.Core.Tests;

public sealed class ProxyManagerParseTests
{
    [Theory]
    [InlineData("none", ProxyMode.NoProxy)]
    [InlineData("system", ProxyMode.SystemProxy)]
    [InlineData("custom", ProxyMode.CustomProxy)]
    [InlineData("SYSTEM", ProxyMode.SystemProxy)]
    [InlineData("Custom", ProxyMode.CustomProxy)]
    [InlineData("", ProxyMode.NoProxy)]
    [InlineData(null, ProxyMode.NoProxy)]
    [InlineData("bogus", ProxyMode.NoProxy)]
    [InlineData("  system  ", ProxyMode.SystemProxy)]
    public void ParseMode_MapsUiValuesToEnum(string? stored, ProxyMode expected)
        => Assert.Equal(expected, ProxyManager.ParseMode(stored));

    [Fact]
    public void ParseScutilProxy_ParsesHostAndPortFromSeparateLines()
    {
        // 真实 scutil --proxy 输出：主机与端口是独立字段。
        var output = """
            <dictionary> {
              HTTPEnable : 1
              HTTPProxy : 127.0.0.1
              HTTPPort : 8080
              HTTPSEnable : 0
              HTTPSProxy : 127.0.0.1
              HTTPSPort : 8443
            }
            """;
        Assert.Equal("127.0.0.1:8080", ProxyManager.ParseScutilProxy(output));
    }

    [Fact]
    public void ParseScutilProxy_PrefersHttpsWhenEnabled()
    {
        var output = """
            <dictionary> {
              HTTPEnable : 1
              HTTPProxy : 10.0.0.1
              HTTPPort : 3128
              HTTPSEnable : 1
              HTTPSProxy : 10.0.0.2
              HTTPSPort : 8443
            }
            """;
        Assert.Equal("10.0.0.2:8443", ProxyManager.ParseScutilProxy(output));
    }

    [Fact]
    public void ParseScutilProxy_DisabledProtocolReturnsNull()
    {
        // HTTP 未启用（Enable : 0）：不应返回主机端口。
        var output = """
            <dictionary> {
              HTTPEnable : 0
              HTTPProxy : 127.0.0.1
              HTTPPort : 8080
            }
            """;
        Assert.Null(ProxyManager.ParseScutilProxy(output));
    }

    [Fact]
    public void ParseScutilProxy_NullHostOrZeroPortReturnsNull()
    {
        var nullHost = """
            <dictionary> {
              HTTPEnable : 1
              HTTPProxy : <null>
              HTTPPort : 8080
            }
            """;
        Assert.Null(ProxyManager.ParseScutilProxy(nullHost));

        var zeroPort = """
            <dictionary> {
              HTTPEnable : 1
              HTTPProxy : 127.0.0.1
              HTTPPort : 0
            }
            """;
        Assert.Null(ProxyManager.ParseScutilProxy(zeroPort));
    }

    [Fact]
    public void ParseScutilProxy_EmptyOrGarbageInputReturnsNull()
    {
        Assert.Null(ProxyManager.ParseScutilProxy(""));
        Assert.Null(ProxyManager.ParseScutilProxy("not a dictionary"));
    }
}
