namespace WMCL.App.ViewModels.Accounts;

/// <summary>账号状态文案与错误分类（从 LaunchViewModel 抽出，便于单测）。</summary>
public static class AccountStatusText
{
    /// <summary>对照 PCL2：按响应文本判断是凭据失效还是网络问题。</summary>
    public static bool IsCredentialExpired(Exception exception)
    {
        var text = exception.ToString();
        return text.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || text.Contains("must sign in again", StringComparison.OrdinalIgnoreCase)
            || text.Contains("password expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AADSTS70008", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AADSTS70000", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AADSTS700082", StringComparison.OrdinalIgnoreCase)
            || text.Contains("issued for a different client", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("not valid", StringComparison.OrdinalIgnoreCase))
            || (text.Contains("refresh token", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    public static string Expired(string detail) => "微软登录已失效，请重新登录：" + detail;

    public static string NetworkFailure(string detail) =>
        "暂时无法校验微软登录（网络问题），稍后会自动重试：" + detail;

    public static string ValidateFailure(Exception exception) =>
        IsCredentialExpired(exception) ? Expired(exception.Message) : NetworkFailure(exception.Message);
}
