namespace WMCL.Core;

/// <summary>
/// 发行方内置密钥。真实值在构建时由未提交的 <c>secrets.local.props</c> 注入，
/// 仓库里默认是空串；可用 PCL2 同名的环境变量覆盖，便于自建构建。
/// 对照 PCL2：官方发布版把密钥编进 ModSecret.vb，开源版只保留读取环境变量的空实现。
/// </summary>
public static class LauncherSecrets
{
    /// <summary>微软 OAuth Client ID（设备码登录）。</summary>
    public static string MicrosoftClientId { get; } =
        FirstNonEmpty(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), LauncherSecretsDefaults.MicrosoftClientId);

    /// <summary>CurseForge API Key，缺失时客户端走镜像。</summary>
    public static string CurseForgeApiKey { get; } =
        FirstNonEmpty(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), LauncherSecretsDefaults.CurseForgeApiKey);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
