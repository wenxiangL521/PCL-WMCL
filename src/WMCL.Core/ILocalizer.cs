namespace WMCL.Core;

/// <summary>
/// 简单的 YAML 本地化资源加载器。支持从本地化目录加载 zh-CN / en-US 等语言包。
/// 不依赖外部包，用正则解析扁平 YAML（key: value）。
/// </summary>
public static class Localizer
{
    private static readonly Dictionary<string, Dictionary<string, string>> _locales = new(StringComparer.OrdinalIgnoreCase);
    private static string _current = "zh-CN";

    /// <summary>加载本地化资源目录。每个文件命名为 <c>&lt;locale&gt;.yaml</c>，内容为扁平 key-value。</summary>
    public static void LoadFromDirectory(string directory)
    {
        lock (_locales)
        {
            _locales.Clear();
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.GetFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                var locale = Path.GetFileNameWithoutExtension(file);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                    var colon = trimmed.IndexOf(':');
                    if (colon <= 0) continue;
                    var key = trimmed[..colon].Trim();
                    var value = trimmed[(colon + 1)..].Trim();
                    if (value.Length > 1 && value.StartsWith('"') && value.EndsWith('"'))
                        value = value[1..^1];
                    if (key.Length > 0 && !dict.ContainsKey(key))
                        dict[key] = value;
                }
                _locales[locale] = dict;
            }
        }
    }

    public static string CurrentLocale => _current;

    public static IReadOnlyList<string> AvailableLocales
    {
        get
        {
            lock (_locales) return _locales.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static void SetLocale(string locale)
    {
        lock (_locales)
        {
            if (_locales.ContainsKey(locale) || locale == "zh-CN")
                _current = locale;
        }
    }

    /// <summary>
    /// 取键的翻译文本。
    /// 缺键时使用 <paramref name="fallback"/>（未提供则返回键名本身）；
    /// 提供占位符参数时用 string.Format 填充翻译中的 {0}、{1}…。
    /// </summary>
    public static string T(string key, string? fallback = null, params object?[]? args)
    {
        string value;
        lock (_locales)
        {
            if (_locales.TryGetValue(_current, out var dict) &&
                dict.TryGetValue(key, out var current) && current.Length > 0)
                value = current;
            else if (_locales.TryGetValue("zh-CN", out var zh) &&
                     zh.TryGetValue(key, out var zhValue) && zhValue.Length > 0)
                value = zhValue;
            else
                value = fallback ?? key;
        }
        if (args is not { Length: > 0 })
            return value;
        try
        {
            return string.Format(value, args);
        }
        catch (FormatException)
        {
            // 翻译中的占位符与参数不匹配：退回原始文本而不是抛错。
            return value;
        }
    }
}
