namespace PCL.Launch.Crash;

public sealed record CrashFinding(
    string Title,
    string Detail,
    string Suggestion);

public sealed record CrashReport(
    IReadOnlyList<CrashFinding> Findings,
    string? LogPath)
{
    public bool HasFindings => Findings.Count > 0;

    public string Summary => Findings.Count == 0
        ? "未发现明确的崩溃原因"
        : string.Join("；", Findings.Select(finding => finding.Title));
}

/// <summary>
/// 精简崩溃分析。对照 PCL 的 CrashAnalyzer：从 latest.log / 启动输出中匹配常见错误，
/// 给出中文原因与处理建议；不追求覆盖 PCL 的全部规则。
/// </summary>
public static class CrashAnalyzer
{
    private static readonly string[] CrashMarkers =
    [
        "crash report saved to",
        "this crash report has been saved to",
        "unable to launch",
        "an exception was thrown, the game will display an error screen and halt",
        "exception in thread \"main\"",
        "a fatal error has been detected by the java runtime",
        "failed to create window"
    ];

    public static CrashReport AnalyzeGameDirectory(
        string? gameDirectory,
        int exitCode,
        string? output = null)
    {
        string? logPath = null;
        var text = output ?? "";

        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            var candidate = Path.Combine(gameDirectory, "logs", "latest.log");
            if (File.Exists(candidate))
            {
                logPath = candidate;
                text = ReadTail(candidate) + Environment.NewLine + text;
            }
        }

        return AnalyzeText(text, exitCode, logPath);
    }

    public static CrashReport AnalyzeText(
        string text,
        int exitCode,
        string? logPath = null)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "";

        var crashed = exitCode != 0 ||
                      CrashMarkers.Any(marker =>
                          text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (!crashed) return new CrashReport([], logPath);

        var findings = new List<CrashFinding>();
        void Add(bool condition, string title, string detail, string suggestion)
        {
            if (condition &&
                findings.All(finding => !string.Equals(finding.Title, title, StringComparison.Ordinal)))
                findings.Add(new CrashFinding(title, detail, suggestion));
        }

        Add(Has(text, "OutOfMemoryError", "Java heap space", "insufficient memory"),
            "内存不足",
            "Java 堆内存耗尽（OutOfMemoryError）。",
            "在启动页提高内存（如 4096 MB），或减少同时加载的 Mod/光影。");

        Add(Has(text, "UnsupportedClassVersionError", "class file version"),
            "Java 版本过低",
            "Mod 或游戏使用了更高版本的 Java 编译。",
            "在设置/启动页切换到 Java 17 或 21（1.20.5+ 建议 21）。");

        Add(Has(text, "Missing or unsupported mandatory dependencies",
                "ModResolutionException", "Incompatible mod set",
                "Mod loading has failed"),
            "Mod 依赖或兼容性问题",
            "Forge/Fabric/NeoForge 无法解析 Mod 依赖或版本冲突。",
            "检查是否缺少前置 Mod、Mod 是否匹配当前游戏版本，并查看最新日志中的依赖列表。");

        Add(Has(text, "DuplicateModsFoundException", "Duplicate mods"),
            "重复安装 Mod",
            "同一个 Mod 存在多个版本文件。",
            "在“模组”页删除旧版本，只保留一个与游戏版本匹配的 jar。");

        Add(Has(text, "GLFW error", "Failed to create window", "Pixel format not accelerated",
                "org.lwjgl.opengl"),
            "图形/窗口初始化失败",
            "OpenGL 或 GLFW 无法创建游戏窗口。",
            "检查显卡驱动；Wayland 下可尝试 XWayland，或安装 mesa/libgl。");

        Add(Has(text, "Could not find or load main class", "ClassNotFoundException",
                "NoClassDefFoundError"),
            "主类/依赖缺失",
            "启动类或某个依赖 jar 未找到，可能是版本文件或库损坏。",
            "在“下载”页对该版本执行校验/修复，或重新安装加载器。");

        Add(Has(text, "NoSuchMethodError", "NoSuchFieldError", "IncompatibleClassChangeError"),
            "Mod 与游戏/加载器不兼容",
            "Mod 调用了当前版本不存在的类或方法。",
            "确认 Mod 支持当前 Minecraft 与加载器版本，移除不兼容的 Mod。");

        Add(Has(text, "AccessDeniedException", "Permission denied",
                "The process cannot access the file"),
            "文件权限/占用",
            "游戏无法写入或读取某个文件。",
            "检查游戏目录权限，关闭占用文件的程序后重试。");

        Add(Has(text, "invalid LOC header", "zip file closed", "ZipException"),
            "Mod/资源包损坏",
            "某个 jar 或 zip 文件无法读取。",
            "删除最近安装的 Mod/资源包，或重新下载损坏的文件。");

        if (findings.Count == 0)
        {
            findings.Add(new CrashFinding(
                "游戏异常退出",
                $"进程退出码 {exitCode}，日志中出现崩溃标志。",
                "打开日志目录查看 latest.log / crash-reports，并尝试移除最近安装的 Mod。"));
        }

        return new CrashReport(findings, logPath);
    }

    private static bool Has(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string ReadTail(string path, int maxChars = 400_000)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, maxChars);
            stream.Seek(-length, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }
}
