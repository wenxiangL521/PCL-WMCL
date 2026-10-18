using System.Buffers.Binary;

namespace WMCL.Launch.Auth;

/// <summary>只读取 PNG 头，校验 Minecraft 皮肤尺寸（64×64 或旧版 64×32）。</summary>
public static class SkinImageValidator
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static (int Width, int Height) ReadDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        if (stream.Read(header, 0, header.Length) != header.Length)
            throw new InvalidDataException("不是完整的 PNG 文件。");
        if (!header.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("不是 PNG 文件。");
        if (!header.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("PNG 缺少 IHDR。");
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        return (width, height);
    }

    public static void ValidateMinecraftSkin(string path)
    {
        var (width, height) = ReadDimensions(path);
        if (width != 64 || (height != 64 && height != 32))
            throw new InvalidDataException(
                $"皮肤尺寸必须为 64×64 或 64×32，当前为 {width}×{height}。");
    }
}
