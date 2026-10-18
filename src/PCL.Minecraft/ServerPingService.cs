using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCL.Minecraft;

public sealed record ServerPingResult(
    string? Version,
    int? Online,
    int? Max,
    string? Motd,
    long LatencyMs,
    string? Favicon = null);

/// <summary>Minecraft Server List Ping（1.7+）状态查询。</summary>
public static class ServerPingService
{
    private const int ProtocolVersion = 769; // 1.21.4 兼容握手；状态请求不依赖精确版本

    public static async Task<ServerPingResult> PingAsync(
        string address,
        int? port = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var (host, parsedPort) = SplitAddress(address, port);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        await client.ConnectAsync(host, parsedPort, timeoutCts.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();

        await WritePacketAsync(stream, BuildHandshake(host, parsedPort), timeoutCts.Token)
            .ConfigureAwait(false);
        await WritePacketAsync(stream, [0x00], timeoutCts.Token).ConfigureAwait(false);

        var length = await ReadVarIntAsync(stream, timeoutCts.Token).ConfigureAwait(false);
        if (length <= 0 || length > 4 * 1024 * 1024)
            throw new InvalidDataException("服务器返回了无效的状态包长度。");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, timeoutCts.Token).ConfigureAwait(false);
        using var payloadStream = new MemoryStream(payload);
        var packetId = await ReadVarIntAsync(payloadStream, timeoutCts.Token).ConfigureAwait(false);
        if (packetId != 0) throw new InvalidDataException("服务器返回了非状态包。");
        var jsonLength = await ReadVarIntAsync(payloadStream, timeoutCts.Token).ConfigureAwait(false);
        var jsonBytes = new byte[jsonLength];
        await payloadStream.ReadExactlyAsync(jsonBytes, timeoutCts.Token).ConfigureAwait(false);
        stopwatch.Stop();

        using var document = JsonDocument.Parse(jsonBytes);
        var root = document.RootElement;
        string? version = null;
        if (root.TryGetProperty("version", out var versionElement) &&
            versionElement.TryGetProperty("name", out var versionName))
            version = versionName.GetString();

        int? online = null, max = null;
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var onlineValue) &&
                onlineValue.TryGetInt32(out var onlineNumber))
                online = onlineNumber;
            if (players.TryGetProperty("max", out var maxValue) &&
                maxValue.TryGetInt32(out var maxNumber))
                max = maxNumber;
        }

        var motd = root.TryGetProperty("description", out var description)
            ? FlattenDescription(description)
            : null;
        var favicon = root.TryGetProperty("favicon", out var faviconElement)
            ? faviconElement.GetString()
            : null;
        return new ServerPingResult(
            version, online, max, motd, stopwatch.ElapsedMilliseconds, favicon);
    }

    public static (string Host, int Port) SplitAddress(string address, int? port = null)
    {
        var value = address.Trim();
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
        return (value, port ?? 25565);
    }

    private static byte[] BuildHandshake(string host, int port)
    {
        using var stream = new MemoryStream();
        WriteVarInt(stream, 0x00);
        WriteVarInt(stream, ProtocolVersion);
        WriteVarInt(stream, Encoding.UTF8.GetByteCount(host));
        stream.Write(Encoding.UTF8.GetBytes(host));
        stream.WriteByte((byte)(port >> 8));
        stream.WriteByte((byte)(port & 0xFF));
        WriteVarInt(stream, 1);
        return stream.ToArray();
    }

    private static async Task WritePacketAsync(
        NetworkStream stream, byte[] payload, CancellationToken ct)
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload);
        await stream.WriteAsync(packet.ToArray(), ct).ConfigureAwait(false);
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var unsigned = (uint)value;
        do
        {
            var temp = (byte)(unsigned & 0x7F);
            unsigned >>= 7;
            if (unsigned != 0) temp |= 0x80;
            stream.WriteByte(temp);
        } while (unsigned != 0);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 7)
        {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            result |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0) return result;
        }
        throw new InvalidDataException("VarInt 过长。");
    }

    private static string? FlattenDescription(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind != JsonValueKind.Object) return null;
        var builder = new StringBuilder();
        if (element.TryGetProperty("text", out var text)) builder.Append(text.GetString());
        if (element.TryGetProperty("extra", out var extra) &&
            extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in extra.EnumerateArray())
                builder.Append(FlattenDescription(child));
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}
