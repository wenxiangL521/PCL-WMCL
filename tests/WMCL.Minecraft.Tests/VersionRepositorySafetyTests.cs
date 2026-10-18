using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class VersionRepositorySafetyTests
{
    [Fact]
    public void RejectsTraversalVersionIds()
    {
        var repository = new VersionRepository(
            Path.Combine(Path.GetTempPath(), "wmcl-version-safety"));
        Assert.Throws<ArgumentException>(() => repository.GetVersionDirectory("../escape"));
        Assert.Throws<ArgumentException>(() => repository.GetVersionDirectory("a/b"));
    }
}
