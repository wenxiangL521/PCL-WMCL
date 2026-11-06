using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

[Trait("Kind", "Unit")]
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
                {"version":{"name":"1.21.4"},
                 "description":{"text":"§aHello ","extra":[{"text":"World"}]},
                 "players":{"online":5,"max":20,"sample":[{"name":"Alice"},{"name":"Bob"}]},
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
            Assert.Equal(["Alice", "Bob"], result.PlayerSample);
            Assert.StartsWith("data:image/png;base64,", result.Favicon);
            Assert.True(result.LatencyMs >= 0);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task PingsServerWithModInfo()
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
                {"version":{"name":"1.21.4","protocol":769},
                 "description":"Fabric Server",
                 "players":{"online":3,"max":10},
                 "modinfo":{"type":"fabric","modList":[
                     {"modId":"fabric-api","modVersion":"0.100.0+1.21.4"},
                     {"modId":"sodium","modVersion":"0.5.10"},
                     {"modId":"lithium","modVersion":"0.20.1"}
                 ]},
                 "preventsChatReports":true}
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
            Assert.Equal("Fabric Server", result.Motd);
            Assert.Equal("fabric", result.ModInfoType);
            Assert.NotNull(result.ModList);
            Assert.Equal(3, result.ModList!.Count);
            Assert.Equal("fabric-api", result.ModList[0].ModId);
            Assert.Equal("0.100.0+1.21.4", result.ModList[0].ModVersion);
            Assert.Equal("sodium", result.ModList[1].ModId);
            Assert.Equal("lithium", result.ModList[2].ModId);
            Assert.True(result.PreventsChatReports);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task PingsServerWithForgeDataMods()
    {
        // 现代 Forge/NeoForge（1.13+）通过 forgeData.mods 报告 Mod 列表。
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
                {"version":{"name":"1.21.1","protocol":767},
                 "description":"NeoForge Server",
                 "players":{"online":2,"max":20},
                 "forgeData":{"fmlNetworkVersion":3,"mods":[
                     {"modId":"minecraft","modmarker":"1.21.1"},
                     {"modId":"neoforge","modmarker":"21.1.80"},
                     {"modId":"create","modmarker":"6.0.0"}
                 ]}}
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
            Assert.Equal("FML", result.ModInfoType);
            Assert.NotNull(result.ModList);
            Assert.Equal(3, result.ModList!.Count);
            Assert.Equal("minecraft", result.ModList[0].ModId);
            Assert.Equal("1.21.1", result.ModList[0].ModVersion);
            Assert.Equal("neoforge", result.ModList[1].ModId);
            Assert.Equal("21.1.80", result.ModList[1].ModVersion);
            Assert.Equal("create", result.ModList[2].ModId);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task PingsServerWithLegacyLowercaseModInfo()
    {
        // 旧版 FML（≤1.12）modinfo 使用小写 modid/modmarker 字段名。
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
                {"version":{"name":"1.12.2"},
                 "description":"Legacy Forge",
                 "players":{"online":1,"max":20},
                 "modinfo":{"type":"FML","modList":[
                     {"modid":"minecraft","modmarker":"1.12.2"},
                     {"modid":"forge","modmarker":"14.23.5.2859"}
                 ]}}
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
            Assert.Equal("FML", result.ModInfoType);
            Assert.NotNull(result.ModList);
            Assert.Equal(2, result.ModList!.Count);
            Assert.Equal("minecraft", result.ModList[0].ModId);
            Assert.Equal("1.12.2", result.ModList[0].ModVersion);
            Assert.Equal("forge", result.ModList[1].ModId);
            Assert.Equal("14.23.5.2859", result.ModList[1].ModVersion);
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    [Fact]
    public async Task PingsServerWithoutModInfo_ReturnsNulls()
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
            var json = """{"version":{"name":"1.20.1"},"description":"Vanilla","players":{"online":1,"max":20}}""";
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
            Assert.Null(result.ModInfoType);
            Assert.Null(result.ModList);
            Assert.Null(result.PreventsChatReports);
        }
        finally
        {
            listener.Stop();
            await server;
        }
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
