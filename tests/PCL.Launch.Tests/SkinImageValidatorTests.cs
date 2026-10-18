using PCL.Launch.Auth;

namespace PCL.Launch.Tests;

public class SkinImageValidatorTests
{
    [Theory]
    [InlineData(64, 64)]
    [InlineData(64, 32)]
    public void AcceptsMinecraftDimensions(int width, int height)
    {
        var path = WriteHeader(width, height);
        try
        {
            Assert.Equal((width, height), SkinImageValidator.ReadDimensions(path));
            SkinImageValidator.ValidateMinecraftSkin(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsWrongDimensions()
    {
        var path = WriteHeader(128, 128);
        try
        {
            Assert.Throws<InvalidDataException>(
                () => SkinImageValidator.ValidateMinecraftSkin(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteHeader(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), "skin-" + Guid.NewGuid().ToString("N") + ".png");
        var data = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(data, 0);
        new byte[] { 0, 0, 0, 13 }.CopyTo(data, 8);
        "IHDR"u8.ToArray().CopyTo(data, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        File.WriteAllBytes(path, data);
        return path;
    }
}
