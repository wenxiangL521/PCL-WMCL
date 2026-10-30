using WMCL.Core.Localization;
using Xunit;

namespace WMCL.Core.Tests;

public class LocalizerTests
{
    [Fact]
    public void T_ReturnsFallback_WhenKeyMissing()
    {
        Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-locale-dir-wmcl-test"));
        var result = Localizer.T("nonexistent.key", "fallback");
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void T_ReturnsKey_WhenNoFallbackProvided()
    {
        Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-locale-dir-wmcl-test"));
        var result = Localizer.T("nonexistent.key");
        Assert.Equal("nonexistent.key", result);
    }

    [Fact]
    public void T_WithArgs_FillsPlaceholders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-format-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"),
                "test.progress: 第 {0} / {1} 步\ntest.name: {0} 的世界\n");
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");

            Assert.Equal("第 2 / 5 步", Localizer.T("test.progress", null, 2, 5));
            Assert.Equal("Alice 的世界", Localizer.T("test.name", null, "Alice"));
        }
        finally
        {
            Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-locale-dir-wmcl-test"));
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void T_WithArgs_ReturnsKey_WhenMissing()
    {
        Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-locale-dir-wmcl-test"));
        Assert.Equal("nonexistent.key", Localizer.T("nonexistent.key", null, 1, 2));
    }

    [Fact]
    public void T_WithArgs_MismatchedPlaceholdersReturnsRawText()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-format2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // 翻译只有 {1} 占位符、未用 {0}：Format 不抛异常，多余参数被忽略。
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"), "test.one: 值 {1}\n");
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");

            Assert.Equal("值 2", Localizer.T("test.one", null, 1, 2));
        }
        finally
        {
            Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-locale-dir-wmcl-test"));
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LoadFromDirectory_LoadsYamlFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"),
                "nav.launch: 启动\nnav.download: 下载\n# comment\ncommon.cancel: 取消\n");
            File.WriteAllText(Path.Combine(dir, "en-US.yaml"),
                "nav.launch: Launch\nnav.download: Download\ncommon.cancel: Cancel\n");

            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");

            Assert.Equal("启动", Localizer.T("nav.launch"));
            Assert.Equal("下载", Localizer.T("nav.download"));
            Assert.Equal("取消", Localizer.T("common.cancel"));

            Localizer.SetLocale("en-US");
            Assert.Equal("Launch", Localizer.T("nav.launch"));
            Assert.Equal("Download", Localizer.T("nav.download"));
            Assert.Equal("Cancel", Localizer.T("common.cancel"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetLocale_FallsBackToZhCN_ForUnknownLocale()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"), "key1: value1\n");
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("xx-XX"); // unknown
            // 未知 locale 会回退到中文
            Assert.Equal("value1", Localizer.T("key1"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadFromDirectory_SkipsCommentsAndBlankLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-comments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"),
                "# This is a comment\n\nkey1: value1\n# another comment\nkey2: value2\n");
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");
            Assert.Equal("value1", Localizer.T("key1"));
            Assert.Equal("value2", Localizer.T("key2"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void T_UnescapesQuotedValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-locale-quotes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"),
                "key1: \"value with spaces\"\n");
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");
            Assert.Equal("value with spaces", Localizer.T("key1"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
