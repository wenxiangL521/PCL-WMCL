using PCL.Minecraft;

namespace PCL.Minecraft.Tests;

public class MavenNameTests
{
    [Fact]
    public void ParsesSimpleName()
    {
        var name = MavenName.Parse("org.lwjgl:lwjgl:3.3.3");

        Assert.Equal("org.lwjgl", name.Group);
        Assert.Equal("lwjgl", name.Artifact);
        Assert.Equal("3.3.3", name.Version);
        Assert.Null(name.Classifier);
        Assert.Equal("jar", name.Extension);
        Assert.Equal("org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar", name.Path);
    }

    [Fact]
    public void ParsesClassifier()
    {
        var name = MavenName.Parse("org.lwjgl:lwjgl:3.3.3:natives-linux");

        Assert.Equal("natives-linux", name.Classifier);
        Assert.Equal(
            "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-linux.jar",
            name.Path);
    }

    [Fact]
    public void ParsesExtension()
    {
        var name = MavenName.Parse("com.example:demo:1.0@zip");

        Assert.Equal("zip", name.Extension);
        Assert.Equal("com/example/demo/1.0/demo-1.0.zip", name.Path);
    }
}
