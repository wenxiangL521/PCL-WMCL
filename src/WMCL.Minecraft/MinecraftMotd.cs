using System.Text;

namespace WMCL.Minecraft;

public sealed record MotdRun(
    string Text,
    string? Color = null,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false);

/// <summary>解析 Minecraft MOTD 的 § 格式码，同时提供纯文本。</summary>
public static class MinecraftMotd
{
    private static readonly Dictionary<char, string> Colors = new()
    {
        ['0'] = "#000000", ['1'] = "#0000AA", ['2'] = "#00AA00", ['3'] = "#00AAAA",
        ['4'] = "#AA0000", ['5'] = "#AA00AA", ['6'] = "#FFAA00", ['7'] = "#AAAAAA",
        ['8'] = "#555555", ['9'] = "#5555FF", ['a'] = "#55FF55", ['b'] = "#55FFFF",
        ['c'] = "#FF5555", ['d'] = "#FF55FF", ['e'] = "#FFFF55", ['f'] = "#FFFFFF"
    };

    public static IReadOnlyList<MotdRun> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var runs = new List<MotdRun>();
        var current = new StringBuilder();
        string? color = null;
        var bold = false;
        var italic = false;
        var underline = false;
        var strikethrough = false;

        void Flush()
        {
            if (current.Length == 0) return;
            runs.Add(new MotdRun(
                current.ToString(), color, bold, italic, underline, strikethrough));
            current.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '§' || i + 1 >= text.Length)
            {
                current.Append(text[i]);
                continue;
            }

            var code = char.ToLowerInvariant(text[++i]);
            if (Colors.TryGetValue(code, out var mapped))
            {
                Flush();
                color = mapped;
                bold = false;
                italic = false;
                underline = false;
                strikethrough = false;
            }
            else
            {
                switch (code)
                {
                    case 'l': Flush(); bold = true; break;
                    case 'o': Flush(); italic = true; break;
                    case 'n': Flush(); underline = true; break;
                    case 'm': Flush(); strikethrough = true; break;
                    case 'r':
                        Flush();
                        color = null;
                        bold = false;
                        italic = false;
                        underline = false;
                        strikethrough = false;
                        break;
                    // k/m 等格式暂时忽略
                }
            }
        }

        Flush();
        return runs;
    }

    public static string ToPlainText(IEnumerable<MotdRun> runs) =>
        string.Concat(runs.Select(run => run.Text));
}
