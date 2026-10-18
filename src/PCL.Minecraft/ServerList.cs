using System.IO.Compression;
using System.Text;

namespace PCL.Minecraft;

public sealed record MinecraftServer(string Name, string Address, string? Icon = null);

/// <summary>
/// 读写 .minecraft/servers.dat（未压缩 NBT；兼容 gzip 读取）。
/// 只保留 name/ip/icon 三个字段，其余未知字段在保存时会丢失。
/// </summary>
public static class ServerListService
{
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    public static IReadOnlyList<MinecraftServer> Load(string gameDirectory) =>
        LoadFile(Path.Combine(gameDirectory, "servers.dat"));

    public static IReadOnlyList<MinecraftServer> LoadFile(string file)
    {
        if (!File.Exists(file)) return [];
        try
        {
            using var raw = File.OpenRead(file);
            using Stream stream = IsGzip(raw) ? new GZipStream(raw, CompressionMode.Decompress) : raw;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            var type = reader.ReadByte();
            if (type != TagCompound) return [];
            ReadString(reader);
            return ReadRoot(reader);
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string gameDirectory, IEnumerable<MinecraftServer> servers) =>
        SaveFile(Path.Combine(gameDirectory, "servers.dat"), servers);

    public static void SaveFile(string file, IEnumerable<MinecraftServer> servers)
    {
        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using var stream = File.Create(file);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(TagCompound);
        WriteString(writer, "");
        writer.Write(TagList);
        WriteString(writer, "servers");
        writer.Write(TagCompound);
        var list = servers.ToList();
        writer.Write(list.Count);
        foreach (var server in list)
        {
            writer.Write(TagString);
            WriteString(writer, "name");
            WriteString(writer, server.Name);
            writer.Write(TagString);
            WriteString(writer, "ip");
            WriteString(writer, server.Address);
            if (!string.IsNullOrWhiteSpace(server.Icon))
            {
                writer.Write(TagString);
                WriteString(writer, "icon");
                WriteString(writer, server.Icon);
            }
            writer.Write(TagEnd);
        }
        writer.Write(TagEnd);
    }

    private static List<MinecraftServer> ReadRoot(BinaryReader reader)
    {
        var servers = new List<MinecraftServer>();
        while (true)
        {
            var type = reader.ReadByte();
            if (type == TagEnd) break;
            var name = ReadString(reader);
            if (type != TagList || name != "servers")
            {
                Skip(reader, type);
                continue;
            }

            var elementType = reader.ReadByte();
            var count = reader.ReadInt32();
            if (elementType != TagCompound)
            {
                for (var i = 0; i < count; i++) Skip(reader, elementType);
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                string? serverName = null, address = null, icon = null;
                while (true)
                {
                    var fieldType = reader.ReadByte();
                    if (fieldType == TagEnd) break;
                    var fieldName = ReadString(reader);
                    switch (fieldName)
                    {
                        case "name" when fieldType == TagString:
                            serverName = ReadString(reader);
                            break;
                        case "ip" when fieldType == TagString:
                            address = ReadString(reader);
                            break;
                        case "icon" when fieldType == TagString:
                            icon = ReadString(reader);
                            break;
                        default:
                            Skip(reader, fieldType);
                            break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(address))
                    servers.Add(new MinecraftServer(serverName ?? address, address, icon));
            }
        }
        return servers;
    }

    private static bool IsGzip(Stream stream)
    {
        var position = stream.Position;
        var first = stream.ReadByte();
        var second = stream.ReadByte();
        stream.Position = position;
        return first == 0x1F && second == 0x8B;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static void Skip(BinaryReader reader, byte type)
    {
        switch (type)
        {
            case TagByte: reader.ReadByte(); break;
            case TagShort: reader.ReadInt16(); break;
            case TagInt: reader.ReadInt32(); break;
            case TagLong: reader.ReadInt64(); break;
            case TagFloat: reader.ReadSingle(); break;
            case TagDouble: reader.ReadDouble(); break;
            case TagByteArray:
                var byteLength = reader.ReadInt32();
                reader.ReadBytes(byteLength);
                break;
            case TagString: ReadString(reader); break;
            case TagList:
                var element = reader.ReadByte();
                var count = reader.ReadInt32();
                for (var i = 0; i < count; i++) Skip(reader, element);
                break;
            case TagCompound:
                while (true)
                {
                    var child = reader.ReadByte();
                    if (child == TagEnd) break;
                    ReadString(reader);
                    Skip(reader, child);
                }
                break;
            case TagIntArray:
                var intLength = reader.ReadInt32();
                reader.ReadBytes(intLength * 4);
                break;
            case TagLongArray:
                var longLength = reader.ReadInt32();
                reader.ReadBytes(longLength * 8);
                break;
            default:
                throw new InvalidDataException($"未知 NBT 类型：{type}");
        }
    }
}
