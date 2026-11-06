using WMCL.Core;

namespace WMCL.Core.Tests;

public sealed class DownloadsDirectoryTests
{
    [Fact]
    public void OverrideEnvironmentVariableWins()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        var target = Path.Combine(Path.GetTempPath(), "wmcl-downloads-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", target);
            Assert.Equal(Path.GetFullPath(target), WmclPaths.DownloadsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", previous);
        }
    }

    [Fact]
    public void FallsBackToUserDownloadsDirectory()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", null);
            Assert.True(Path.IsPathRooted(WmclPaths.DownloadsDirectory));
            Assert.EndsWith("Downloads", WmclPaths.DownloadsDirectory.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", previous);
        }
    }
}
