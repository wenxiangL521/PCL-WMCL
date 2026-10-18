using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PCL.Minecraft;

public sealed record SrvRecord(string Target, int Port, int Priority, int Weight);

/// <summary>
/// 最小 DNS SRV 查询（UDP）。用于 Minecraft 服务器的 _minecraft._tcp SRV 记录。
/// 仅使用系统 /etc/resolv.conf 的首个 nameserver。
/// </summary>
public static class DnsSrvResolver
{
    public static async Task<SrvRecord?> QueryMinecraftAsync(
        string host,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _)) return null;
        var server = GetSystemNameServer();
        if (string.IsNullOrWhiteSpace(server)) return null;
        return await QueryAsync("_minecraft._tcp." + host, server, timeout, ct)
            .ConfigureAwait(false);
    }

    public static async Task<SrvRecord?> QueryAsync(
        string name,
        string nameServer,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var parsedNameServer = ServerPingService.SplitAddress(nameServer);
        var server = parsedNameServer.Host;
        var port = nameServer.Contains(':') ? parsedNameServer.Port : 53;
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        udp.Client.ReceiveTimeout = (int)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var query = BuildQuery(id, name);
        try
        {
            await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse(server), port), ct)
                .ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
            var result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseResponse(result.Buffer, id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static SrvRecord? ParseResponse(byte[] data, ushort expectedId)
    {
        if (data.Length < 12) return null;
        var id = (ushort)((data[0] << 8) | data[1]);
        if (id != expectedId) return null;
        var flags = (data[2] << 8) | data[3];
        if ((flags & 0x000F) != 0) return null; // non-zero RCODE
        var qdCount = (data[4] << 8) | data[5];
        var anCount = (data[6] << 8) | data[7];
        var offset = 12;
        for (var i = 0; i < qdCount; i++)
        {
            if (!SkipName(data, ref offset) || offset + 4 > data.Length) return null;
            offset += 4;
        }

        SrvRecord? best = null;
        for (var i = 0; i < anCount; i++)
        {
            if (!SkipName(data, ref offset) || offset + 10 > data.Length) return null;
            var type = (data[offset] << 8) | data[offset + 1];
            var klass = (data[offset + 2] << 8) | data[offset + 3];
            var ttl = ((uint)data[offset + 4] << 24) | ((uint)data[offset + 5] << 16) |
                      ((uint)data[offset + 6] << 8) | data[offset + 7];
            var length = (data[offset + 8] << 8) | data[offset + 9];
            offset += 10;
            if (offset + length > data.Length) return null;
            var recordStart = offset;
            if (type == 33 && klass == 1 && length >= 7)
            {
                var priority = (data[offset] << 8) | data[offset + 1];
                var weight = (data[offset + 2] << 8) | data[offset + 3];
                var targetPort = (data[offset + 4] << 8) | data[offset + 5];
                var nameOffset = offset + 6;
                var target = ReadName(data, ref nameOffset);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    var candidate = new SrvRecord(target.TrimEnd('.'), targetPort, priority, weight);
                    if (best is null || candidate.Priority < best.Priority ||
                        (candidate.Priority == best.Priority && candidate.Weight > best.Weight))
                        best = candidate;
                }
            }
            offset = recordStart + length;
        }
        return best;
    }

    private static byte[] BuildQuery(ushort id, string name)
    {
        using var stream = new MemoryStream();
        stream.WriteByte((byte)(id >> 8));
        stream.WriteByte((byte)id);
        stream.WriteByte(0x01); // recursion desired
        stream.WriteByte(0x00);
        stream.WriteByte(0x00); stream.WriteByte(0x01); // QDCOUNT
        stream.WriteByte(0x00); stream.WriteByte(0x00); // ANCOUNT
        stream.WriteByte(0x00); stream.WriteByte(0x00); // NSCOUNT
        stream.WriteByte(0x00); stream.WriteByte(0x00); // ARCOUNT
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("非法域名。", nameof(name));
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        stream.WriteByte(0); stream.WriteByte(33); // SRV
        stream.WriteByte(0); stream.WriteByte(1);  // IN
        return stream.ToArray();
    }

    private static bool SkipName(byte[] data, ref int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (length == 0) return true;
            if ((length & 0xC0) == 0xC0)
            {
                offset++;
                return offset <= data.Length;
            }
            offset += length;
        }
        return false;
    }

    private static string ReadName(byte[] data, ref int offset)
    {
        var builder = new StringBuilder();
        var jumped = false;
        var position = offset;
        for (var guard = 0; guard < 64; guard++)
        {
            if (position >= data.Length) return "";
            var length = data[position++];
            if (length == 0)
            {
                if (!jumped) offset = position;
                return builder.ToString();
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (position >= data.Length) return "";
                var pointer = ((length & 0x3F) << 8) | data[position++];
                if (!jumped) offset = position;
                jumped = true;
                position = pointer;
                continue;
            }
            if (position + length > data.Length) return "";
            builder.Append(Encoding.ASCII.GetString(data, position, length));
            builder.Append('.');
            position += length;
        }
        return "";
    }

    private static string? GetSystemNameServer()
    {
        try
        {
            if (!File.Exists("/etc/resolv.conf")) return null;
            foreach (var line in File.ReadLines("/etc/resolv.conf"))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("nameserver ", StringComparison.Ordinal)) continue;
                var value = trimmed["nameserver ".Length..].Trim().Split(' ')[0];
                if (IPAddress.TryParse(value, out _)) return value;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }
}
