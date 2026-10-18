using System.Net;
using System.Net.Sockets;
using System.Text;
using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class DnsSrvResolverTests
{
    [Fact]
    public async Task QueriesLocalDnsAndParsesSrv()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        var server = Task.Run(async () =>
        {
            var request = await listener.ReceiveAsync();
            var query = request.Buffer;
            var id = new[] { query[0], query[1] };
            var question = query[12..];
            var target = EncodeName("mc.example.com");
            var answer = new List<byte>();
            answer.AddRange(new byte[] { 0xC0, 0x0C });
            answer.AddRange(new byte[] { 0x00, 33, 0x00, 0x01 });
            answer.AddRange(new byte[] { 0, 0, 0, 60 });
            answer.AddRange(new byte[] { 0, (byte)(6 + target.Length) });
            answer.AddRange(new byte[] { 0, 0, 0, 5, 0x63, 0xDE });
            answer.AddRange(target);
            var response = new List<byte>();
            response.AddRange(id);
            response.AddRange(new byte[] { 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 });
            response.AddRange(question);
            response.AddRange(answer);
            await listener.SendAsync(response.ToArray(), request.RemoteEndPoint);
        });

        var record = await DnsSrvResolver.QueryAsync(
            "_minecraft._tcp.test", $"127.0.0.1:{port}", TimeSpan.FromSeconds(3));
        Assert.NotNull(record);
        Assert.Equal("mc.example.com", record.Target);
        Assert.Equal(25566, record.Port);
        Assert.Equal(0, record.Priority);
        Assert.Equal(5, record.Weight);
        await server;
    }

    [Fact]
    public void ParsesPriorityAndSkipsNonSrvAnswers()
    {
        var id = (ushort)0x1234;
        var response = new List<byte> { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 2, 0, 0, 0, 0 };
        response.AddRange(EncodeName("_minecraft._tcp.test"));
        response.AddRange(new byte[] { 0, 33, 0, 1 });
        // A 记录先出现，应被跳过
        response.AddRange(new byte[] { 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 1, 2, 3, 4 });
        response.AddRange(new byte[] { 0xC0, 0x0C, 0, 33, 0, 1, 0, 0, 0, 60, 0, 8 });
        response.AddRange(new byte[] { 0, 10, 0, 1, 0x63, 0xDE, 0xC0, 0x0C });
        var record = DnsSrvResolver.ParseResponse(response.ToArray(), id);
        Assert.NotNull(record);
        Assert.Equal(25566, record.Port);
        Assert.Equal(10, record.Priority);
        Assert.Equal(1, record.Weight);
    }

    private static byte[] EncodeName(string name)
    {
        var bytes = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            var value = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)value.Length);
            bytes.AddRange(value);
        }
        bytes.Add(0);
        return bytes.ToArray();
    }
}
