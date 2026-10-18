namespace PCL.Launch.Crash;

public sealed record CrashFinding(
    string Title,
    string Detail,
    string Suggestion);

public sealed record CrashReport(
    IReadOnlyList<CrashFinding> Findings,
    string? LogPath,
    string? CrashReportPath = null)
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
        string? crashReportPath = null;
        var text = output ?? "";

        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            var candidate = Path.Combine(gameDirectory, "logs", "latest.log");
            if (File.Exists(candidate))
            {
                logPath = candidate;
                text = ReadTail(candidate) + Environment.NewLine + text;
            }

            var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
            if (Directory.Exists(crashDirectory))
            {
                var newest = Directory.EnumerateFiles(crashDirectory, "*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest is not null)
                {
                    crashReportPath = newest;
                    text = ReadTail(newest) + Environment.NewLine + text;
                }
            }
        }

        return AnalyzeText(text, exitCode, logPath, crashReportPath);
    }

    public static CrashReport AnalyzeText(
        string text,
        int exitCode,
        string? logPath = null,
        string? crashReportPath = null)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "";

        var crashed = exitCode != 0 ||
                      CrashMarkers.Any(marker =>
                          text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (!crashed) return new CrashReport([], logPath, crashReportPath);

        var findings = new List<CrashFinding>();
        void Add(bool condition, string title, string detail, string suggestion)
        {
            if (condition &&
                findings.All(finding => !string.Equals(finding.Title, title, StringComparison.Ordinal)))
                findings.Add(new CrashFinding(title, detail, suggestion));
        }

        var description = System.Text.RegularExpressions.Regex.Match(
            text, @"Description:\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Add(description.Success,
            "崩溃报告摘要",
            "Minecraft 崩溃报告：" + description.Groups[1].Value.Trim(),
            "打开 crash-reports 查看完整堆栈，或使用本页“诊断包”收集日志。");

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

        Add(Has(text, "Mixin apply failed", "MixinTransformerError", "Mixin config",
                "org.spongepowered.asm.mixin"),
            "Mixin 注入失败",
            "某个 Mod 的 Mixin 与当前游戏/加载器版本不兼容。",
            "更新或移除最近安装的 Mod，确认其支持当前 Minecraft/Forge/Fabric 版本。");

        Add(Has(text, "Invalid session", "Failed to verify username",
                "Authentication servers are down", "Invalid credentials"),
            "登录会话失效",
            "账号会话已过期，或认证服务器不可达。",
            "重新登录账号；第三方登录请确认认证服务器地址和实例的认证服务器设置。");

        Add(Has(text, "Address already in use", "BindException"),
            "端口被占用",
            "游戏或某个服务无法绑定所需端口。",
            "关闭占用该端口的程序，或重启电脑后再试。");

        Add(Has(text, "Unrecognized VM option", "Could not create the Java Virtual Machine",
                "Error occurred during initialization of VM"),
            "JVM 参数/Java 启动失败",
            "JVM 无法识别启动参数，或 Java 运行时无法初始化。",
            "清空/检查额外 JVM 参数，确认 Java 路径与版本匹配。");

        Add(Has(text, "FileNotFoundException", "NoSuchFileException",
                "Couldn't download", "Failed to download"),
            "游戏文件缺失",
            "启动所需的文件不存在或下载失败。",
            "在“下载”页校验/修复版本文件，检查磁盘空间与网络。");

        Add(Has(text, "Region file", "Chunk file", "corrupt", "Corrupted"),
            "存档/区块损坏",
            "世界存档中的区域或区块文件无法读取。",
            "备份并恢复存档；必要时用存档修复工具处理损坏的 region 文件。");

        Add(Has(text, "StackOverflowError"),
            "栈溢出",
            "某个 Mod、世界生成或渲染逻辑递归过深。",
            "移除最近安装的 Mod/数据包，检查世界生成或渲染相关配置。");

        Add(Has(text, "ExceptionInInitializerError", "LinkageError",
                "InvocationTargetException"),
            "Mod 初始化失败",
            "Mod 静态初始化/反射调用失败，常见于版本不匹配或缺少前置。",
            "检查最近安装的 Mod 是否匹配游戏/加载器版本，补齐前置依赖。");

        Add(Has(text, "ConnectException", "UnknownHostException",
                "Connection refused", "Connection timed out"),
            "网络连接失败",
            "游戏/登录服务无法连接目标服务器。",
            "检查网络、代理和防火墙；第三方登录需确认认证服务器地址。");

        Add(Has(text, "Name cannot be longer than 16", "Invalid username",
                "Invalid player name"),
            "玩家名无效",
            "玩家名超过 16 字符或包含不允许字符。",
            "在启动页/实例设置中改用合法的 Minecraft 玩家名。");

        Add(Has(text, "Metaspace", "Compressed class space"),
            "Java 元空间不足",
            "加载了过多 Mod/类，元空间耗尽。",
            "提高 Metaspace（如 -XX:MaxMetaspaceSize=512m）或减少 Mod。");

        Add(Has(text, "SecurityException", "AccessControlException"),
            "安全权限被拒绝",
            "Java 安全管理器/系统权限阻止了文件或网络访问。",
            "检查启动器/游戏目录权限，避免在受限目录或安全软件拦截下运行。");

        Add(Has(text, "AssertionError", "Assertion failed"),
            "断言失败",
            "Mod 或游戏内部断言失败，通常是版本/环境不匹配。",
            "确认加载器、Mod 与游戏版本匹配；必要时移除最近安装的 Mod。");

        Add(Has(text, "Unable to make protected final java.lang.Class",
                "module java.base does not export", "NoSuchFieldException: ucp",
                "ClassNotFoundException: jdk.nashorn"),
            "Java 版本过高",
            "当前 Java 与游戏的反射/模块系统不兼容。",
            "切换到 Java 8/17/21 中该游戏版本要求的版本。");

        Add(Has(text, "Open J9 is not supported", "OpenJ9 is incompatible",
                ".J9VMInternals."),
            "Java 运行时不受支持",
            "OpenJ9 与当前 Minecraft/Mod 组合不兼容。",
            "改用 HotSpot/Oracle/Adoptium 等标准 OpenJDK 运行时。");

        Add(Has(text, "Extracted mod jars found",
                "The directories below appear to be extracted jar files"),
            "Mod 文件被解压",
            "Mods 目录里出现了被解压的目录，加载器不会继续。",
            "删除 mods 下被解压的目录，只保留原始 .jar 文件。");

        Add(Has(text, "ClassNotFoundException: org.spongepowered.asm.launch.MixinTweaker"),
            "Mixin 引导缺失",
            "Forge/加载器缺少 Mixin 依赖。",
            "重新安装 Forge/整合包，确认 libraries 中的 Mixin 完整。");

        Add(Has(text, "Shaders Mod detected. Please remove it"),
            "Shaders Mod 与 OptiFine 冲突",
            "OptiFine 已内置光影支持，不能再安装 Shaders Mod。",
            "移除 Shaders Mod，只保留 OptiFine。");

        Add(Has(text, "Invalid maximum heap size", "Could not reserve enough space",
                "for 1048576KB object heap"),
            "32 位 Java / 内存分配失败",
            "JVM 无法分配所设置的内存，常见于 32 位 Java 或系统内存不足。",
            "改用 64 位 Java，或降低内存设置。");

        Add(Has(text, "Cannot find launch target fmlclient",
                "Invalid paths argument, contained no existing paths"),
            "Forge 安装不完整",
            "Forge 的核心文件/库缺失或版本 JSON 不完整。",
            "重新安装 Forge，检查 libraries/net/minecraftforge。");

        Add(Has(text, "A potential solution has been determined"),
            "Fabric 给出了解决建议",
            "Fabric Loader 在日志中提供了依赖/版本建议。",
            "查看日志中“A potential solution has been determined”后的建议并调整 Mod。");

        Add(Has(text, "Maybe try a lower resolution resourcepack"),
            "资源包/材质过大",
            "资源包分辨率过高导致显存/内存不足。",
            "降低资源包分辨率或移除大型材质包。");

        Add(Has(text, "1282: Invalid operation"),
            "OpenGL 1282 错误",
            "光影/资源包或显卡驱动触发了 OpenGL 1282。",
            "移除最近的光影/资源包，更新显卡驱动。");

        Add(Has(text, "Manually triggered debug crash"),
            "手动调试崩溃",
            "这是玩家手动触发的调试崩溃（F3+C）。",
            "不是游戏故障；重新启动即可。");

        if (findings.Count == 0)
        {
            findings.Add(new CrashFinding(
                "游戏异常退出",
                $"进程退出码 {exitCode}，日志中出现崩溃标志。",
                "打开日志目录查看 latest.log / crash-reports，并尝试移除最近安装的 Mod。"));
        }

        return new CrashReport(findings, logPath, crashReportPath);
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
