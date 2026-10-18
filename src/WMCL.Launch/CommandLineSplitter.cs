using System.Text;

namespace WMCL.Launch;

/// <summary>把实例设置中的额外参数按 shell 风格拆分（支持单/双引号）。</summary>
public static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        var pending = false;

        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                pending = true;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                pending = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
                pending = true;
            }
        }

        Flush();
        return result;

        void Flush()
        {
            if (!pending) return;
            result.Add(current.ToString());
            current.Clear();
            pending = false;
        }
    }
}
