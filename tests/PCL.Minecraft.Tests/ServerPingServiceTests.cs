using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class ServerPingServiceTests
{
    [Fact]
    public async Task PingsLocalServerAndParsesStatus()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            await ReadPacketAsync(stream); // handshake
            await ReadPacketAsync(stream); // status request
            var json = """
                {"version":{"name":"1.21.4"},"players":{"online":5,"max":20},
                 "description":{"text":"Hello ","extra":[{"text":"World"}]},
                 "favicon":"data:image/png;base64,iVBORw0KGgo="}
                """;
            var body = new MemoryStream();
            WriteVarInt(body, 0);
            var bytes = Encoding.UTF8.GetBytes(json);
            WriteVarInt(body, bytes.Length);
            body.Write(bytes);
            var packet = new MemoryStream();
            WriteVarInt(packet, (int)body.Length);
            packet.Write(body.ToArray());
            await stream.WriteAsync(packet.ToArray());
        });

        try
        {
            var result = await ServerPingService.PingAsync("127.0.0.1", port);
            Assert.Equal("1.21.4", result.Version);
            Assert.Equal(5, result.Online);
            Assert.Equal(20, result.Max);
            Assert.Equal("Hello World", result.Motd);
            Assert.StartsWith("data:image/png;base64,", result.Favicon);
            Assert.True(result.LatencyMs >= 0);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Theory]
    [InlineData("mc.example.com", null, "mc.example.com", 25565)]
    [InlineData("mc.example.com:25566", null, "mc.example.com", 25566)]
    [InlineData("[::1]:25567", null, "::1", 25567)]
    public void SplitsAddresses(string input, int? port, string host, int expected)
    {
        var result = ServerPingService.SplitAddress(input, port);
        Assert.Equal(host, result.Host);
        Assert.Equal(expected, result.Port);
    }

    private static async Task<byte[]> ReadPacketAsync(NetworkStream stream)
    {
        var length = await ReadVarIntAsync(stream);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
        return payload;
    }

    private static async Task<int> ReadVarIntAsync(Stream stream)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 7)
        {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer);
            result |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0) return result;
        }
        throw new InvalidDataException();
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
}
