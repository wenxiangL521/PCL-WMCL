using PCL.Core.Update;

namespace PCL.Core.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("v0.2.0", "0.1.9", true)]
    [InlineData("1.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("invalid", "0.1.0", false)]
    public void ComparesVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(candidate, current));
    }

    [Fact]
    public void ParsesManifest()
    {
        const string json = """
            {
              "version": "0.2.0",
              "url": "https://example.invalid/PCL-linux-x64.tar.gz",
              "sha256": "abc",
              "notes": "test"
            }
            """;

        var info = UpdateService.ParseManifest(json);

        Assert.NotNull(info);
        Assert.Equal("0.2.0", info.Version);
        Assert.Equal("https://example.invalid/PCL-linux-x64.tar.gz", info.Url);
        Assert.Equal("abc", info.Sha256);
        Assert.Equal("test", info.Notes);
    }
}
