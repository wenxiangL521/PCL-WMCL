using WMCL.Link;
using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class EasyTierElevationTests
{
    [Fact]
    public void PkexecDetectionFollowsPath()
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        var directory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "fake-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory);
            Assert.False(EasyTierService.PkexecAvailable());
            File.WriteAllText(Path.Combine(directory, "pkexec"), "#!/bin/sh\n");
            Assert.True(EasyTierService.PkexecAvailable());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
