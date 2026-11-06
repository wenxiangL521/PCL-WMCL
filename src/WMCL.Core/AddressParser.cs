namespace WMCL.Core;

/// <summary>通用地址解析：支持 IPv4/域名、<c>host:port</c> 与 <c>[IPv6]:port</c>。</summary>
public static class AddressParser
{
    /// <summary>
    /// 拆分 <paramref name="address"/> 的主机与端口；<paramref name="port"/> 显式给出时优先使用。
    /// 未指定端口时返回 <paramref name="defaultPort"/>。
    /// </summary>
    public static (string Host, int Port) Split(string address, int? port, int defaultPort)
    {
        var value = (address ?? "").Trim();
        if (value.StartsWith('[') && value.Contains("]:"))
        {
            var end = value.IndexOf("]:", StringComparison.Ordinal);
            if (end > 0 && int.TryParse(value[(end + 2)..], out var parsed))
                return (value[1..end], port ?? parsed);
        }
        var index = value.LastIndexOf(':');
        if (index > 0 && value.Count(c => c == ':') == 1 &&
            int.TryParse(value[(index + 1)..], out var addressPort))
            return (value[..index], port ?? addressPort);
        return (value, port ?? defaultPort);
    }
}
