using WMCL.App.ViewModels.Accounts;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class AccountStatusTextTests
{
    [Theory]
    [InlineData("invalid_grant AADSTS70008", true)]
    [InlineData("must sign in again", true)]
    [InlineData("The refresh token is not valid: expired", true)]
    [InlineData("No such host is known", false)]
    [InlineData("The operation was canceled.", false)]
    public void ClassifiesCredentialFailures(string message, bool expired)
    {
        Assert.Equal(expired, AccountStatusText.IsCredentialExpired(new InvalidOperationException(message)));
    }

    [Fact]
    public void BuildsExpiredAndNetworkMessages()
    {
        Assert.StartsWith("微软登录已失效", AccountStatusText.Expired("bad token"));
        Assert.StartsWith("暂时无法校验", AccountStatusText.NetworkFailure("timeout"));
        Assert.Contains("重新登录", AccountStatusText.ValidateFailure(new InvalidOperationException("invalid_grant")));
        Assert.Contains("网络问题", AccountStatusText.ValidateFailure(new InvalidOperationException("dns")));
    }
}

public sealed class AccountStatusTextRefreshTokenTests
{
    [Theory]
    [InlineData("AADSTS70000: The provided value for the input parameter 'refresh_token' or 'assertion' is not valid.", true)]
    [InlineData("AADSTS700082: The refresh token has expired", true)]
    [InlineData("The token was issued for a different client", true)]
    [InlineData("refresh_token is not valid", true)]
    [InlineData("Connection refused", false)]
    public void ClassifiesRefreshTokenFailures(string message, bool expired)
    {
        Assert.Equal(expired, WMCL.App.ViewModels.Accounts.AccountStatusText.IsCredentialExpired(
            new InvalidOperationException(message)));
    }
}
