using System.Text.RegularExpressions;

namespace WMCL.App.Services;

/// <summary>
/// 共享脱敏过滤器：任务日志、失败堆栈与配置导出共用同一套敏感模式
/// （代理密码、CurseForge API Key、URL 内嵌凭据、Bearer 令牌）。
/// 替换值为 ***，重复应用结果不变（幂等）。
/// </summary>
internal static class SensitiveTextRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    [
        (new("(?i:(\"(?:proxypassword|curseforgeapikey)\"\\s*:\\s*\"))[^\"]*(\")", RegexOptions.Compiled), "$1***$2"),
        (new("(?i:((?:proxypassword|curseforgeapikey|wmcl_curseforge_api_key)\\s*[=:]\\s*))\\S+", RegexOptions.Compiled), "$1***"),
        (new("([a-zA-Z][a-zA-Z0-9+.-]*://)([^:/@\\s]+):([^@/\\s]+)@", RegexOptions.Compiled), "$1***@"),
        (new("(?i:(authorization\\s*[:=]\\s*Bearer\\s+))\\S+", RegexOptions.Compiled), "$1***")
    ];

    /// <summary>返回脱敏后的文本；null 输入返回空串，任何输入都不抛出。</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text;
        foreach (var (pattern, replacement) in Patterns)
        {
            try
            {
                result = pattern.Replace(result, replacement);
            }
            catch
            {
                // 单一模式失败不中断其余脱敏
            }
        }
        return result;
    }

    /// <summary>配置导出的对象级脱敏：敏感字段置空（导入方需重新填写）。</summary>
    internal static void RedactSettings(AppSettings settings)
    {
        settings.ProxyPassword = "";
        settings.CurseForgeApiKey = "";
    }
}
