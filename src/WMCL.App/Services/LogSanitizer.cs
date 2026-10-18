namespace WMCL.App.Services;

/// <summary>把 Minecraft 的 log4j XML 控制台输出整理成可读文本。</summary>
public static class LogSanitizer
{
    private const string MessageStart = "<log4j:Message><![CDATA[";
    private const string MessageEnd = "]]></log4j:Message>";

    public static string? Clean(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("<log4j:", StringComparison.Ordinal) ||
            trimmed.StartsWith("</log4j:", StringComparison.Ordinal))
        {
            var start = line.IndexOf(MessageStart, StringComparison.Ordinal);
            if (start < 0) return null;

            start += MessageStart.Length;
            var end = line.IndexOf(MessageEnd, start, StringComparison.Ordinal);
            if (end <= start) return null;

            return StripStderr(line[start..end]);
        }

        return StripStderr(line);
    }

    private static string StripStderr(string value) =>
        value.StartsWith("[STDERR]: ", StringComparison.Ordinal) ? value[10..] : value;
}
