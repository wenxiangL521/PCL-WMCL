using Avalonia.Input;

namespace WMCL.App.Services;

/// <summary>
/// 键盘快捷键解析与匹配。
/// 解析 "Ctrl+1"、"Ctrl+Shift+Tab" 之类的字符串为键 + 修饰键组合，
/// 并与 KeyEventArgs 匹配。
/// </summary>
internal static class ShortcutParser
{
    /// <summary>默认快捷键表。</summary>
    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["Tab1"] = "Ctrl+1",
        ["Tab2"] = "Ctrl+2",
        ["Tab3"] = "Ctrl+3",
        ["Tab4"] = "Ctrl+4",
        ["Tab5"] = "Ctrl+5",
        ["Tab"] = "Ctrl+Tab",
        ["Search"] = "Ctrl+F"
    };

    /// <summary>
    /// 解析自定义快捷键 JSON（CustomShortcuts），失败或缺失项回退默认值。
    /// </summary>
    public static Dictionary<string, string> ParseTable(string? json)
    {
        var result = new Dictionary<string, string>(Defaults);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is not null)
            {
                foreach (var (key, value) in dict)
                    if (!string.IsNullOrWhiteSpace(value) && result.ContainsKey(key))
                        result[key] = value;
            }
        }
        catch
        {
            // 非法 JSON 保留默认值
        }
        return result;
    }

    /// <summary>
    /// 判断按键事件是否命中快捷键。
    /// 修饰键采用“必需齐全”语义（快捷键要求的修饰键都被按下即可），
    /// 这样 Ctrl+Shift+Tab 也能命中 Ctrl+Tab，由调用方用 Shift 区分方向。
    /// </summary>
    public static bool Matches(KeyEventArgs e, string? shortcut)
    {
        var (key, mods) = Parse(shortcut);
        return key != Key.None && e.Key == key && (e.KeyModifiers & mods) == mods;
    }

    /// <summary>解析单个快捷键字符串为（键, 修饰键）。</summary>
    internal static (Key Key, KeyModifiers Modifiers) Parse(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return (Key.None, KeyModifiers.None);
        var key = Key.None;
        var mods = KeyModifiers.None;
        foreach (var part in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= KeyModifiers.Control; break;
                case "shift": mods |= KeyModifiers.Shift; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "super" or "meta": mods |= KeyModifiers.Meta; break;
                default:
                    // 注意：数字不能走 Enum.TryParse——"1" 会按枚举数值解析成
                    // Key.Cancel(=1)，而主键盘数字 1 的键值是 Key.D1。
                    if (part.Length == 1 && part[0] is >= '0' and <= '9')
                        key = Key.D0 + (part[0] - '0');
                    else if (Enum.TryParse<Key>(part, true, out var parsed))
                        key = parsed;
                    break;
            }
        }
        return (key, mods);
    }
}
