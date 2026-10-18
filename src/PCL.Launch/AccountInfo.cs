using System.Security.Cryptography;
using System.Text;

namespace PCL.Launch;

public sealed record AccountInfo(
    string Name,
    string Uuid,
    string AccessToken,
    string UserType = "legacy",
    string? Xuid = null,
    string? ClientId = null)
{
    public static AccountInfo Offline(string name) =>
        new(name, OfflineUuid(name), "0");

    /// <summary>等价于 Java 的 UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))。</summary>
    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant
        var hex = Convert.ToHexStringLower(hash);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
