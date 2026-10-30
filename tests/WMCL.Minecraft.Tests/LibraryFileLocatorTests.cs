using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class LibraryFileLocatorTests
{
    [Fact]
    public void ResolvesFileWithDifferentCasing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-lib-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var actual = Path.Combine(directory, "FishModLoader-v3.4.2.jar");
            File.WriteAllText(actual, "jar");

            var expected = Path.Combine(directory, "fishmodloader-v3.4.2.jar");
            Assert.Equal(actual, LibraryFileLocator.Resolve(expected));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void KeepsExpectedPathWhenMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-lib-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = Path.Combine(directory, "NotThere.jar");
            Assert.Equal(expected, LibraryFileLocator.Resolve(expected));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
