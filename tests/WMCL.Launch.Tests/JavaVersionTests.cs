using WMCL.Launch.Java;

namespace WMCL.Launch.Tests;

public class JavaVersionTests
{
    [Theory]
    [InlineData("1.6.4", 8)]
    [InlineData("1.12.2", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17.1", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.1", 21)]
    [InlineData("26.1", 21)]
    [InlineData("24w14a", 21)]
    public void InfersJavaMajorVersion(string version, int expected)
    {
        Assert.Equal(expected, JavaLocator.InferMajorVersion(version));
    }
}
