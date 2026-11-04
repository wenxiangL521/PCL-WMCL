using WMCL.App.ViewModels.Accounts;

namespace WMCL.App.Tests;

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
