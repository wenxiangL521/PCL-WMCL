using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class ExternalLauncherTests
{
    [Theory]
    [InlineData(true, false, "explorer.exe")]
    [InlineData(false, true, "open")]
    [InlineData(false, false, "xdg-open")]
    public void PlatformCommandPreservesTargetAsOneArgument(bool windows, bool macOS, string executable)
    {
        var targets = new[]
        {
            @"C:\游戏 日志\log & file.txt",
            "/home/user/中文 logs/file 'quoted' $(literal).txt",
            "https://example.invalid/login?code=abc&return=a%20b#fragment"
        };
        foreach (var target in targets)
        {
            var info = ExternalLauncher.CreateStartInfo(target, windows, macOS);
            Assert.Equal(executable, info.FileName);
            Assert.False(info.UseShellExecute);
            Assert.Empty(info.Arguments);
            Assert.Equal(target, Assert.Single(info.ArgumentList));
        }
    }
}
