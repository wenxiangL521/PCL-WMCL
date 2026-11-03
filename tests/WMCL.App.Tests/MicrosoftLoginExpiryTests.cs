using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class MicrosoftLoginExpiryTests
{
    private static LaunchViewModel Model()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "ms-expiry-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
    }

    [Fact]
    public void SigningInAgainClearsTheExpiredBanner()
    {
        var model = Model();
        model.MicrosoftLoginExpired = true;
        model.SetMicrosoftAccount(new MicrosoftAccount("Player", "uuid", "token", "refresh", DateTimeOffset.MaxValue));
        Assert.False(model.MicrosoftLoginExpired);
    }

    [Fact]
    public void SwitchingToOfflineOrThirdPartyClearsTheExpiredBanner()
    {
        var model = Model();
        model.MicrosoftLoginExpired = true;
        model.SelectOfflineAccountCommand.Execute(null);
        Assert.False(model.MicrosoftLoginExpired);

        model.MicrosoftLoginExpired = true;
        model.SetAuthlibAccount(new AuthlibAccount("https://example.invalid/api/yggdrasil", "Third", "uuid", "token", "client"));
        Assert.False(model.MicrosoftLoginExpired);
    }

    [Theory]
    [InlineData("刷新微软登录失败：invalid_grant AADSTS70008", true)]
    [InlineData("must sign in again", true)]
    [InlineData("The refresh token is not valid: expired", true)]
    [InlineData("No such host is known (login.live.com)", false)]
    [InlineData("The operation was canceled.", false)]
    public void OnlyCredentialErrorsAreTreatedAsExpiry(string message, bool expired)
    {
        var method = typeof(LaunchViewModel).GetMethod("IsCredentialExpired",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expired, (bool)method.Invoke(null, [new InvalidOperationException(message)])!);
    }
}
