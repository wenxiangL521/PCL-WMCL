using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WMCL.Link;

/// <summary>
/// 检测本机是否已占用某个网段。
/// EasyTier 默认网段是 10.126.126.0/24，Astral 也用它；本机残留的 tun 设备
/// 或其它 EasyTier 实例占着同一网段时，再加入外部房间会出现路由冲突。
/// </summary>
public static class NetworkSubnetProbe
{
    /// <summary>EasyTier / Astral 默认虚拟网段。</summary>
    public const string EasyTierDefaultPrefix = "10.126.126.";

    /// <summary>返回本机在该网段上已有的地址（不含环回）。</summary>
    public static IReadOnlyList<string> FindAddresses(string prefix)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(prefix)) return found;
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var text = unicast.Address.ToString();
                    if (text.StartsWith(prefix, StringComparison.Ordinal) && !found.Contains(text))
                        found.Add(text);
                }
            }
        }
        catch
        {
            // 取不到网卡信息时按「无冲突」处理
        }
        return found;
    }

    /// <summary>地址是否属于给定 /24 前缀。</summary>
    public static bool IsInPrefix(string address, string prefix) =>
        IPAddress.TryParse(address, out var parsed) &&
        parsed.AddressFamily == AddressFamily.InterNetwork &&
        parsed.ToString().StartsWith(prefix, StringComparison.Ordinal);
}
