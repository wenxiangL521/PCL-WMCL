namespace WMCL.Core;

/// <summary>
/// 发行方内置密钥。真实值在构建时由未提交的 <c>secrets.local.props</c> 注入，
/// 仓库里默认是空串；用户设置与环境变量优先，便于自建构建覆盖。
/// 对照 PCL2：官方发布版把密钥编进 ModSecret.vb，开源版只保留读取环境变量的空实现。
/// </summary>
public static class LauncherSecrets
{
    /// <summary>微软 OAuth Client ID（设备码登录）。</summary>
    public static string MicrosoftClientId { get; } =
        FirstNonEmpty(WmclEnvironment.Get("MS_CLIENT_ID"), LauncherSecretsDefaults.MicrosoftClientId);

    /// <summary>CurseForge API Key，缺失时客户端走镜像。</summary>
    public static string CurseForgeApiKey { get; } =
        FirstNonEmpty(WmclEnvironment.Get("CURSEFORGE_API_KEY"), LauncherSecretsDefaults.CurseForgeApiKey);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
