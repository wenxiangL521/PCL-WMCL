namespace WMCL.Core;

/// <summary>把代理地址注入 .NET 的 HTTP 环境变量（需在创建 HttpClient 前调用）。</summary>
public static class ProxyEnvironment
{
    public static void Apply(string? proxyUrl)
    {
        var value = proxyUrl?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return;

        Environment.SetEnvironmentVariable("HTTP_PROXY", value);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", value);
        Environment.SetEnvironmentVariable("ALL_PROXY", value);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_PROXY")))
            Environment.SetEnvironmentVariable("NO_PROXY", "localhost,127.0.0.1,::1");
    }
}
