using WMCL.Core.IO;

namespace WMCL.Core.Tests;

public class FileUtilTests
{
    [Fact]
    public void SafeChildPathBlocksTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-safe");
        Assert.Equal(Path.Combine(root, "a.jar"), FileUtil.SafeChildPath(root, "a.jar"));
        Assert.Throws<InvalidDataException>(() => FileUtil.SafeChildPath(root, "../evil.jar"));
        Assert.Throws<InvalidDataException>(() => FileUtil.SafeChildPath(root, "/tmp/evil.jar"));
    }

    [Fact]
    public void SafeFileNameDropsDirectories()
    {
        Assert.Equal("a.jar", FileUtil.SafeFileName("../a.jar"));
        Assert.Equal("a.jar", FileUtil.SafeFileName("dir\\a.jar"));
        Assert.Throws<InvalidDataException>(() => FileUtil.SafeFileName(".."));
    }
}
