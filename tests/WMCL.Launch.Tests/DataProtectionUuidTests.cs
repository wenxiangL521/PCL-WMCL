using WMCL.Launch.Auth;

namespace WMCL.Launch.Tests;

[Trait("Kind", "Unit")]
public sealed class DataProtectionUuidTests
{
    // ioreg 输出行的真实形态：属性名与值都带引号，按引号切分后
    // [1] 是属性名、[3] 才是 UUID 值。
    private const string IoregLine = "    \"IOPlatformUUID\" = \"3D82A4C5-1234-4ABC-9DEF-001122334455\"";

    [Fact]
    public void ExtractPlatformUuid_ReturnsUuidValue()
    {
        Assert.Equal("3D82A4C5-1234-4ABC-9DEF-001122334455",
            DataProtectionService.ExtractPlatformUuid(IoregLine));
    }

    [Fact]
    public void ExtractPlatformUuid_FullIoregOutput()
    {
        var output = $$"""
            +-o IOPlatformExpertDevice  {"IOPlatformSerialNumber" = "C02XXXXXXXX"}
              | {
              |   "board-id" = <"Mac-42ED25ECABC4B600">
              | {{IoregLine}}
              | }
            """;
        Assert.Equal("3D82A4C5-1234-4ABC-9DEF-001122334455",
            DataProtectionService.ExtractPlatformUuid(output));
    }

    [Fact]
    public void ExtractPlatformUuid_RejectsPropertyNameOnly()
    {
        // 历史回归：旧实现取 split[1]（属性名 "IOPlatformUUID"），
        // 所有 Mac 因此派生相同密钥。属性名不是 GUID，必须被拒绝。
        var propertyNameLine = "    \"IOPlatformUUID\" = \"\"";
        Assert.NotEqual("IOPlatformUUID",
            DataProtectionService.ExtractPlatformUuid(IoregLine));
        Assert.Null(DataProtectionService.ExtractPlatformUuid(propertyNameLine));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no uuid here")]
    [InlineData("\"IOPlatformUUID\" = \"not-a-guid\"")]
    public void ExtractPlatformUuid_InvalidInputReturnsNull(string? input)
        => Assert.Null(DataProtectionService.ExtractPlatformUuid(input));
}
