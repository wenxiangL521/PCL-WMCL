using PCL.Launch.Crash;

namespace PCL.Launch.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void DetectsOutOfMemory()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "java.lang.OutOfMemoryError: Java heap space\nCrash report saved to: crash-reports/crash.txt",
            1);

        Assert.True(report.HasFindings);
        Assert.Contains(report.Findings, finding => finding.Title == "内存不足");
    }

    [Fact]
    public void DetectsJavaVersionAndModDependency()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "java.lang.UnsupportedClassVersionError: class file version 65.0\n" +
            "Missing or unsupported mandatory dependencies:\n" +
            "Mod ID: 'foo', Requested by: 'bar'",
            1);

        Assert.Contains(report.Findings, finding => finding.Title == "Java 版本过低");
        Assert.Contains(report.Findings, finding => finding.Title == "Mod 依赖或兼容性问题");
    }

    [Fact]
    public void DetectsMixinAndLoginFailures()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "Mixin apply failed: mixin.example.json\nInvalid session\n" +
            "Address already in use",
            1);
        Assert.Contains(report.Findings, finding => finding.Title == "Mixin 注入失败");
        Assert.Contains(report.Findings, finding => finding.Title == "登录会话失效");
        Assert.Contains(report.Findings, finding => finding.Title == "端口被占用");
    }

    [Fact]
    public void DetectsJvmAndFileFailures()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "Unrecognized VM option 'UseG1GC'\njava.io.FileNotFoundException: missing.jar",
            1);
        Assert.Contains(report.Findings, finding => finding.Title == "JVM 参数/Java 启动失败");
        Assert.Contains(report.Findings, finding => finding.Title == "游戏文件缺失");
    }

    [Fact]
    public void DetectsAdditionalCommonFailures()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "java.lang.StackOverflowError\n" +
            "java.lang.ExceptionInInitializerError\n" +
            "java.net.UnknownHostException: example.com\n" +
            "Name cannot be longer than 16\n" +
            "java.lang.OutOfMemoryError: Metaspace\n" +
            "java.lang.SecurityException: denied\n" +
            "java.lang.AssertionError: failed",
            1);
        Assert.Contains(report.Findings, finding => finding.Title == "栈溢出");
        Assert.Contains(report.Findings, finding => finding.Title == "Mod 初始化失败");
        Assert.Contains(report.Findings, finding => finding.Title == "网络连接失败");
        Assert.Contains(report.Findings, finding => finding.Title == "玩家名无效");
        Assert.Contains(report.Findings, finding => finding.Title == "Java 元空间不足");
        Assert.Contains(report.Findings, finding => finding.Title == "安全权限被拒绝");
        Assert.Contains(report.Findings, finding => finding.Title == "断言失败");
    }

    [Fact]
    public void DetectsPcl2StyleAdditionalRules()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "module java.base does not export\n" +
            "OpenJ9 is incompatible\n" +
            "Extracted mod jars found\n" +
            "ClassNotFoundException: org.spongepowered.asm.launch.MixinTweaker\n" +
            "Shaders Mod detected. Please remove it\n" +
            "Invalid maximum heap size\n" +
            "Cannot find launch target fmlclient\n" +
            "A potential solution has been determined\n" +
            "Maybe try a lower resolution resourcepack\n" +
            "1282: Invalid operation\n" +
            "Manually triggered debug crash",
            1);
        Assert.Contains(report.Findings, f => f.Title == "Java 版本过高");
        Assert.Contains(report.Findings, f => f.Title == "Java 运行时不受支持");
        Assert.Contains(report.Findings, f => f.Title == "Mod 文件被解压");
        Assert.Contains(report.Findings, f => f.Title == "Mixin 引导缺失");
        Assert.Contains(report.Findings, f => f.Title == "Shaders Mod 与 OptiFine 冲突");
        Assert.Contains(report.Findings, f => f.Title == "32 位 Java / 内存分配失败");
        Assert.Contains(report.Findings, f => f.Title == "Forge 安装不完整");
        Assert.Contains(report.Findings, f => f.Title == "Fabric 给出了解决建议");
        Assert.Contains(report.Findings, f => f.Title == "资源包/材质过大");
        Assert.Contains(report.Findings, f => f.Title == "OpenGL 1282 错误");
        Assert.Contains(report.Findings, f => f.Title == "手动调试崩溃");
    }

    [Fact]
    public void DetectsAdditionalPcl2StyleRules()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "The driver does not appear to support OpenGL\n" +
            "ClassCastException: class jdk.internal.loader.ClassLoaders$AppClassLoader\n" +
            "OptiFine NoSuchMethodError\n" +
            "ParsingException: Not enough data available\n" +
            "Invalid module name: '' is not a Java identifier\n" +
            "maximum id range exceeded\n" +
            "Suspected Mods: example\n" +
            "Failed loading config file test.cfg\n" +
            "Multiple entries with same key: test\n" +
            "signer information does not match\n" +
            "compatibility level JAVA_11\n" +
            "java.lang.ClassCastException: example",
            1);
        Assert.Contains(report.Findings, f => f.Title == "显卡不支持 OpenGL");
        Assert.Contains(report.Findings, f => f.Title == "使用了 JDK 而非 JRE");
        Assert.Contains(report.Findings, f => f.Title == "OptiFine 与 Forge 不兼容");
        Assert.Contains(report.Findings, f => f.Title == "NightConfig 配置解析失败");
        Assert.Contains(report.Findings, f => f.Title == "Mod 名称含非法字符");
        Assert.Contains(report.Findings, f => f.Title == "Mod 数量超过 ID 限制");
        Assert.Contains(report.Findings, f => f.Title == "怀疑 Mod 导致崩溃");
        Assert.Contains(report.Findings, f => f.Title == "Mod 配置文件加载失败");
        Assert.Contains(report.Findings, f => f.Title == "多个 Mod 注册冲突");
        Assert.Contains(report.Findings, f => f.Title == "Jar 签名校验失败");
        Assert.Contains(report.Findings, f => f.Title == "Mod 需要 Java 11");
        Assert.Contains(report.Findings, f => f.Title == "类型转换异常");
    }

    [Fact]
    public void IgnoresNormalExit()
    {
        var report = CrashAnalyzer.AnalyzeText("Sound engine started\nStopping!", 0);
        Assert.False(report.HasFindings);
    }

    [Fact]
    public void DetectsGlfwFailure()
    {
        var report = CrashAnalyzer.AnalyzeText(
            "GLFW error 65542: Failed to create window\nFailed to create window",
            1);

        Assert.Contains(report.Findings, finding => finding.Title == "图形/窗口初始化失败");
    }
}
