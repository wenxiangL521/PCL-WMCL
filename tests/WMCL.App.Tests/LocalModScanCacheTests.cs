using WMCL.Modding.Local;

namespace WMCL.App.Tests;

public sealed class LocalModScanCacheTests
{
    [Fact]
    public void RepeatedScanReturnsCachedResultUntilFilesChange()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "scan-cache-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "mods");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 30; i++)
            File.WriteAllText(Path.Combine(dir, $"mod-{i}.jar"), "fixture");

        var first = LocalModService.List(dir);
        var second = LocalModService.List(dir);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(m => m.FileName), second.Select(m => m.FileName));

        File.WriteAllText(Path.Combine(dir, "new.jar"), "fixture");
        var third = LocalModService.List(dir);
        Assert.Equal(first.Count + 1, third.Count);
    }
}
