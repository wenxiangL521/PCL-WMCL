using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>失败堆栈载荷与共享脱敏过滤器的回归测试。</summary>
[Trait("Kind", "Unit")]
public sealed class TaskFailureInfoTests
{
    [Fact]
    public void From_ConcatenatesInnerExceptionChain()
    {
        Exception captured;
        try
        {
            throw new InvalidDataException("outer boom", new InvalidOperationException("inner boom"));
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        var info = TaskFailureInfo.From(captured, "安装 1.21.1");

        Assert.Equal("outer boom", info.Message);
        Assert.Contains("InvalidDataException", info.ExceptionType);
        Assert.Contains("outer boom", info.StackTrace);
        Assert.Contains("--->", info.StackTrace);
        Assert.Contains("InvalidOperationException", info.StackTrace);
        Assert.Contains("inner boom", info.StackTrace);
    }

    [Fact]
    public void From_FallsBackToEmptyStackTrace_WhenNull()
    {
        var unthrown = new Exception("never thrown");

        var info = TaskFailureInfo.From(unthrown, "t");

        // StackTrace 为 null 时堆栈载荷退化为类型+消息行，不含堆栈帧，且不抛出。
        Assert.DoesNotContain("   at", info.StackTrace);
        Assert.Contains("never thrown", info.StackTrace);
        Assert.Equal("never thrown", info.Message);
    }

    [Fact]
    public void From_TruncatesOverlongChain()
    {
        Exception deep = new Exception("level 1");
        for (var i = 2; i <= 1200; i++)
            deep = new Exception($"level {i}", deep);
        Exception captured;
        try
        {
            throw deep;
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        var info = TaskFailureInfo.From(captured, "t");

        Assert.True(info.StackTrace.Length <= TaskFailureInfo.MaxStackTraceLength + 64);
        Assert.Contains("已截断", info.StackTrace);
        Assert.Contains("level 1200", info.StackTrace);
    }

    [Fact]
    public void TruncateStackTrace_KeepsHeadAndTailWithMarker()
    {
        var text = new string('a', TaskFailureInfo.MaxStackTraceLength + 4096);

        var truncated = TaskFailureInfo.TruncateStackTrace(text);

        Assert.Contains("已截断", truncated);
        Assert.StartsWith(new string('a', TaskFailureInfo.MaxStackTraceLength / 2), truncated);
        Assert.EndsWith(new string('a', TaskFailureInfo.MaxStackTraceLength / 4), truncated);
    }

    [Fact]
    public void TruncateStackTrace_ReturnsShortTextUnchanged()
    {
        const string text = "short stack";

        Assert.Equal(text, TaskFailureInfo.TruncateStackTrace(text));
    }

    private sealed class EvilException : Exception
    {
        public EvilException() : base("seed")
        {
        }

        public override string Message => throw new InvalidOperationException("evil getter");
    }

    [Fact]
    public void From_NeverThrows_ForAnyException()
    {
        var info = TaskFailureInfo.From(new EvilException(), "t");

        Assert.NotNull(info);
        Assert.Contains("EvilException", info.ExceptionType);
    }

    [Fact]
    public void Redacted_ReplacesSensitivePatterns()
    {
        var info = new TaskFailureInfo(
            "Exception",
            "下载失败 http://user:secret@mirror.example/x proxyPassword=abc123",
            "   at Fetch() in http://alice:hunter2@example.com/File.cs:line 1",
            DateTimeOffset.Now);

        var redacted = info.Redacted();

        Assert.DoesNotContain("secret", redacted.Message);
        Assert.DoesNotContain("abc123", redacted.Message);
        Assert.DoesNotContain("hunter2", redacted.StackTrace);
        Assert.Contains("***", redacted.Message);
        Assert.Contains("下载失败", redacted.Message);
        Assert.Contains("at Fetch()", redacted.StackTrace);
    }

    [Fact]
    public void Redacted_LeavesNonSensitiveTextByteIdentical()
    {
        const string message = "连接超时：launchermeta.mojang.com";
        const string stack = "   at Install() in Installer.cs:line 42";
        var info = new TaskFailureInfo("Exception", message, stack, DateTimeOffset.Now);

        var redacted = info.Redacted();

        Assert.Equal(message, redacted.Message);
        Assert.Equal(stack, redacted.StackTrace);
    }

    [Fact]
    public void Redact_IsIdempotent()
    {
        const string input = "proxyPassword=pw1 http://u:p@example.com Authorization: Bearer tok123";

        var once = SensitiveTextRedactor.Redact(input);
        var twice = SensitiveTextRedactor.Redact(once);

        Assert.Equal(once, twice);
        Assert.DoesNotContain("pw1", once);
        Assert.DoesNotContain("tok123", once);
    }

    [Fact]
    public void Redact_IsNullAndEmptySafe()
    {
        Assert.Equal("", SensitiveTextRedactor.Redact(null));
        Assert.Equal("", SensitiveTextRedactor.Redact(""));
    }

    [Fact]
    public void Redact_HandlesJsonFieldForm()
    {
        const string json = """{"ProxyMode": "custom", "ProxyPassword": "super-secret", "CurseForgeApiKey": "cf-key-123"}""";

        var redacted = SensitiveTextRedactor.Redact(json);

        Assert.DoesNotContain("super-secret", redacted);
        Assert.DoesNotContain("cf-key-123", redacted);
        Assert.Contains("\"custom\"", redacted);
    }
}
