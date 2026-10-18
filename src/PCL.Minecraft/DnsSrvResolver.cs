using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text;

namespace PCL.Minecraft;

public sealed record SrvRecord(
    string Target, int Port, int Priority, int Weight, int TtlSeconds = 60);

/// <summary>
/// 最小 DNS SRV 查询（UDP，截断时 TCP fallback）。
/// 尝试系统 /etc/resolv.conf 中的 nameserver。
/// </summary>
public static class DnsSrvResolver
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expiry, SrvRecord Record)> Cache = new();

    public static async Task<SrvRecord?> QueryMinecraftAsync(
        string host,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _)) return null;
        foreach (var server in GetSystemNameServers())
        {
            var record = await QueryAsync("_minecraft._tcp." + host, server, timeout, ct)
                .ConfigureAwait(false);
            if (record is not null) return record;
        }
        return null;
    }

    public static async Task<SrvRecord?> QueryAsync(
        string name,
        string nameServer,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var cacheKey = nameServer + "|" + name;
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTimeOffset.UtcNow)
            return cached.Record;
        var (serverAddress, port) = ParseNameServer(nameServer);
        using var udp = new UdpClient(serverAddress.AddressFamily);
        udp.Client.Bind(new IPEndPoint(
            serverAddress.AddressFamily == AddressFamily.InterNetwork
                ? IPAddress.Any
                : IPAddress.IPv6Any, 0));
        udp.Client.ReceiveTimeout = (int)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var query = BuildQuery(id, name);
        try
        {
            await udp.SendAsync(query, new IPEndPoint(serverAddress, port), ct)
                .ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
            var result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            var parsed = IsTruncated(result.Buffer)
                ? await QueryTcpAsync(name, nameServer, timeout, ct).ConfigureAwait(false)
                : ParseResponse(result.Buffer, id);
            if (parsed is not null)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var expired in Cache.Where(pair => pair.Value.Expiry <= now).Select(pair => pair.Key))
                    Cache.TryRemove(expired, out _);
                Cache[cacheKey] = (now.AddSeconds(Math.Max(1, parsed.TtlSeconds)), parsed);
            }
            return parsed;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<SrvRecord?> QueryTcpAsync(
        string name,
        string nameServer,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var (serverAddress, port) = ParseNameServer(nameServer);
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var query = BuildQuery(id, name);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
        try
        {
            using var client = new TcpClient(serverAddress.AddressFamily);
            await client.ConnectAsync(serverAddress, port, timeoutCts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var length = new byte[2];
            length[0] = (byte)(query.Length >> 8);
            length[1] = (byte)query.Length;
            await stream.WriteAsync(length, timeoutCts.Token).ConfigureAwait(false);
            await stream.WriteAsync(query, timeoutCts.Token).ConfigureAwait(false);
            var responseLengthBytes = new byte[2];
            await stream.ReadExactlyAsync(responseLengthBytes, timeoutCts.Token)
                .ConfigureAwait(false);
            var responseLength = (responseLengthBytes[0] << 8) | responseLengthBytes[1];
            if (responseLength <= 0 || responseLength > 65535) return null;
            var response = new byte[responseLength];
            await stream.ReadExactlyAsync(response, timeoutCts.Token).ConfigureAwait(false);
            return ParseResponse(response, id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsTruncated(byte[] data) =>
        data.Length >= 4 && (((data[2] << 8) | data[3]) & 0x0200) != 0;

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
                    var candidate = new SrvRecord(
                        target.TrimEnd('.'), targetPort, priority, weight,
                        (int)Math.Min(ttl, int.MaxValue));
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

    private static (IPAddress Address, int Port) ParseNameServer(string nameServer)
    {
        var value = nameServer.Trim();
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            if (end > 0 && IPAddress.TryParse(value[1..end], out var v6))
            {
                var port = 53;
                if (end + 1 < value.Length && value[end + 1] == ':' &&
                    int.TryParse(value[(end + 2)..], out var parsedPort))
                    port = parsedPort;
                return (v6, port);
            }
        }
        var index = value.LastIndexOf(':');
        if (index > 0 && value.Count(c => c == ':') == 1 &&
            int.TryParse(value[(index + 1)..], out var portValue) &&
            IPAddress.TryParse(value[..index], out var v4))
            return (v4, portValue);
        if (IPAddress.TryParse(value, out var address)) return (address, 53);
        throw new FormatException("非法 DNS 服务器地址：" + nameServer);
    }

    private static List<string> GetSystemNameServers()
    {
        var servers = new List<string>();
        try
        {
            if (!File.Exists("/etc/resolv.conf")) return servers;
            foreach (var line in File.ReadLines("/etc/resolv.conf"))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("nameserver ", StringComparison.Ordinal)) continue;
                var value = trimmed["nameserver ".Length..].Trim().Split(' ')[0];
                if (IPAddress.TryParse(value, out _) && !servers.Contains(value))
                    servers.Add(value);
            }
        }
        catch
        {
            // ignore
        }
        return servers;
    }
}
