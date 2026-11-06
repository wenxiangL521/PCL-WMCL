using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class MicrosoftAccountStorePathTests
{
    [Fact]
    public void ListPathFallsBackWhenAccountPathIsNull()
    {
        // 默认安装路径下 accountDirectory 为 null，之前会抛 ArgumentNullException 导致启动崩溃
        var path = MicrosoftAccountStore.ListPathFor(null);
        Assert.EndsWith("microsoft-accounts.json", path);
        Assert.False(string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void ListPathSitsNextToGivenAccountFile()
    {
        var accountPath = Path.Combine(Path.GetTempPath(), "wmcl-test-accounts", "microsoft.json");
        var path = MicrosoftAccountStore.ListPathFor(accountPath);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "wmcl-test-accounts", "microsoft-accounts.json"), path);
    }
}
