using WMCL.Launch.Java;
using WMCL.Minecraft.Models;

namespace WMCL.Launch.Tests;

[Trait("Kind", "Unit")]
public class FishModLoaderJavaTests
{
    [Fact]
    public void RequiresJava17ForFishModLoader3()
    {
        var version = new VersionJson
        {
            Id = "1.6.4-MITE",
            ReleaseTime = "2013-09-19T11:52:37-04:00"
        };
        version.Libraries.Add(new Library
        {
            Name = "net.xiaoyu233.fishmodloader:fishmodloader:v3.4.2"
        });

        var requirement = JavaRequirement.ForVersion(version);

        Assert.True(requirement.Contains(new Version(17, 0, 0)));
        Assert.False(requirement.Contains(new Version(8, 0, 0)));
    }

    [Fact]
    public void KeepsJava8ForVanilla164()
    {
        var version = new VersionJson
        {
            Id = "1.6.4",
            ReleaseTime = "2013-09-19T11:52:37-04:00"
        };

        var requirement = JavaRequirement.ForVersion(version);

        Assert.True(requirement.Contains(new Version(8, 0, 0)));
    }
}
