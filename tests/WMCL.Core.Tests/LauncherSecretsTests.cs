using WMCL.Core;

namespace WMCL.Core.Tests;

[Trait("Kind", "Unit")]
public sealed class LauncherSecretsTests
{
    [Fact]
    public void BuiltInSecretsAreReadableAndNonNull()
    {
        // 本地构建通过 secrets.local.props 注入；仓库克隆（无该文件）时为空串。
        Assert.NotNull(LauncherSecrets.MicrosoftClientId);
        Assert.NotNull(LauncherSecrets.CurseForgeApiKey);
    }

    [Fact]
    public void EnvironmentVariableIsPickedUpByEnvironmentReader()
    {
        var original = Environment.GetEnvironmentVariable("WMCL_MS_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("WMCL_MS_CLIENT_ID", "env-client");
            Assert.Equal("env-client", WmclEnvironment.Get("MS_CLIENT_ID"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WMCL_MS_CLIENT_ID", original);
        }
    }
}
