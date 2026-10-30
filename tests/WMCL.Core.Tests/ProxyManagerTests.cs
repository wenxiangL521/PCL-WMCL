using WMCL.Core;

namespace WMCL.Core.Tests;

// Apply 直接改写 HTTP_PROXY/HTTPS_PROXY/NO_PROXY 等进程级环境变量，
// 与 ProxyManagerParseTests 共用一个 Collection 串行执行；
// ctor/Dispose 保存并恢复原值，避免污染同程序集的其他测试。
[Collection("proxy-environment")]
public sealed class ProxyManagerTests : IDisposable
{
    private readonly string? _http;
    private readonly string? _https;
    private readonly string? _all;
    private readonly string? _no;
    private readonly ProxyConfig _originalConfig;
    private readonly string? _originalSystemAddress;

    public ProxyManagerTests()
    {
        _http = Environment.GetEnvironmentVariable("HTTP_PROXY");
        _https = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        _all = Environment.GetEnvironmentVariable("ALL_PROXY");
        _no = Environment.GetEnvironmentVariable("NO_PROXY");
        _originalConfig = ProxyManager.Current;
        _originalSystemAddress = ProxyManager.SystemProxyAddress;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", _http);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", _https);
        Environment.SetEnvironmentVariable("ALL_PROXY", _all);
        Environment.SetEnvironmentVariable("NO_PROXY", _no);
        ProxyManager.SystemProxyAddress = _originalSystemAddress;
        ProxyManager.Apply(_originalConfig);
    }

    [Fact]
    public void NormalizeAddress_AddsHttpScheme()
    {
        var result = ProxyManager.NormalizeAddress("127.0.0.1:7890");
        Assert.NotNull(result);
        Assert.StartsWith("http://", result);
    }

    [Fact]
    public void NormalizeAddress_KeepsExistingScheme()
    {
        var result = ProxyManager.NormalizeAddress("https://proxy.example.com:8080");
        Assert.Equal("https://proxy.example.com:8080", result);
    }

    [Fact]
    public void NormalizeAddress_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(ProxyManager.NormalizeAddress(null));
        Assert.Null(ProxyManager.NormalizeAddress(""));
        Assert.Null(ProxyManager.NormalizeAddress("   "));
    }

    [Fact]
    public void NormalizeAddress_InvalidScheme_ReturnsNull()
    {
        Assert.Null(ProxyManager.NormalizeAddress("ftp://proxy.example.com"));
    }

    [Fact]
    public void Apply_NoProxy_ClearsEnvVars()
    {
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://old:8080");
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://old:8080");
        ProxyManager.Apply(new ProxyConfig(ProxyMode.NoProxy));
        Assert.Null(Environment.GetEnvironmentVariable("HTTP_PROXY"));
        Assert.Null(Environment.GetEnvironmentVariable("HTTPS_PROXY"));
    }

    [Fact]
    public void Apply_CustomProxy_SetsEnvVars()
    {
        ProxyManager.Apply(new ProxyConfig(
            ProxyMode.CustomProxy, "127.0.0.1:7890"));
        Assert.Equal("http://127.0.0.1:7890",
            Environment.GetEnvironmentVariable("HTTP_PROXY"));
        Assert.Equal("http://127.0.0.1:7890",
            Environment.GetEnvironmentVariable("HTTPS_PROXY"));
    }

    [Fact]
    public void Apply_CustomProxy_WithBypassList()
    {
        ProxyManager.Apply(new ProxyConfig(
            ProxyMode.CustomProxy, "127.0.0.1:7890",
            BypassList: "localhost,example.com"));
        Assert.Equal("localhost,example.com",
            Environment.GetEnvironmentVariable("NO_PROXY"));
    }

    [Fact]
    public void Apply_SystemProxy_UsesSystemAddress()
    {
        ProxyManager.SystemProxyAddress = "http://system:8080";
        ProxyManager.Apply(new ProxyConfig(ProxyMode.SystemProxy));
        Assert.Equal("http://system:8080",
            Environment.GetEnvironmentVariable("HTTP_PROXY"));
    }

    [Fact]
    public void ResolvedAddress_NoProxy()
    {
        ProxyManager.Apply(new ProxyConfig(ProxyMode.NoProxy));
        Assert.Equal("不使用代理", ProxyManager.ResolvedAddress);
    }

    [Fact]
    public void ResolvedAddress_CustomProxy()
    {
        ProxyManager.Apply(new ProxyConfig(
            ProxyMode.CustomProxy, "127.0.0.1:7890"));
        Assert.Contains("127.0.0.1:7890", ProxyManager.ResolvedAddress);
    }

    [Fact]
    public void Describe_NoProxy()
    {
        ProxyManager.Apply(new ProxyConfig(ProxyMode.NoProxy));
        Assert.Equal("不使用代理", ProxyManager.Describe());
    }

    [Fact]
    public void Describe_CustomProxy()
    {
        ProxyManager.Apply(new ProxyConfig(
            ProxyMode.CustomProxy, "127.0.0.1:7890"));
        Assert.Contains("自定义代理", ProxyManager.Describe());
        Assert.Contains("127.0.0.1:7890", ProxyManager.Describe());
    }

    [Fact]
    public void Describe_SystemProxy_WithoutDetection()
    {
        ProxyManager.SystemProxyAddress = null;
        ProxyManager.Apply(new ProxyConfig(ProxyMode.SystemProxy));
        Assert.Contains("系统代理", ProxyManager.Describe());
        Assert.Contains("未检测到", ProxyManager.Describe());
    }
}
