using WMCL.App.Services;
using WMCL.Core.Jobs;

namespace WMCL.App.Tests;

/// <summary>.part 临时文件清理的回归测试：白名单删除、占用跳过、根不存在安全。</summary>
[Trait("Kind", "Unit")]
public sealed class PartFileCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-part-" + Guid.NewGuid().ToString("N"));

    public PartFileCleanerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void CleanPartFiles_RemovesOnlyPartSuffixFiles()
    {
        File.WriteAllText(Path.Combine(_root, "a.part"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "b.part"), "x");
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "particle.log"), "x");
        File.WriteAllText(Path.Combine(_root, "partiality"), "x");

        var removed = PartFileCleaner.CleanPartFiles(_root);

        Assert.Equal(2, removed);
        Assert.False(File.Exists(Path.Combine(_root, "a.part")));
        Assert.False(File.Exists(Path.Combine(_root, "sub", "b.part")));
        Assert.True(File.Exists(Path.Combine(_root, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "particle.log")));
        Assert.True(File.Exists(Path.Combine(_root, "partiality")));
    }

    [Fact]
    public void CleanPartFiles_SkipsUndeletableFileWithoutThrowing()
    {
        // Windows 用 FileShare.None 强锁；Unix 为 advisory 锁，改用只读目录制造删除失败。
        var lockedDir = Path.Combine(_root, "locked-dir");
        Directory.CreateDirectory(lockedDir);
        var locked = Path.Combine(lockedDir, "locked.part");
        var free = Path.Combine(_root, "free.part");
        File.WriteAllText(locked, "x");
        File.WriteAllText(free, "x");

        FileStream? handle = null;
        if (OperatingSystem.IsWindows())
        {
            handle = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else
        {
            File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        try
        {
            var removed = PartFileCleaner.CleanPartFiles(_root);
            Assert.Equal(1, removed);
            Assert.True(File.Exists(locked));
            Assert.False(File.Exists(free));
        }
        finally
        {
            handle?.Dispose();
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(lockedDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void CleanPartFiles_MissingRoot_ReturnsZero()
    {
        Assert.Equal(0, PartFileCleaner.CleanPartFiles(Path.Combine(_root, "missing")));
        Assert.Equal(0, PartFileCleaner.CleanPartFiles(""));
    }
}

/// <summary>统一进度格式的回归测试：与提取前的 DownloadViewModel.FormatProgress 输出逐字节一致。</summary>
public sealed class ProgressFormatterTests
{
    [Theory]
    [InlineData("下载资源", 0.5, 1048576.0, "详情文本", "下载资源 50.0% · 1.0 MB/s · 详情文本")]
    [InlineData("下载资源", 0.25, 0.0, "", "下载资源 25.0%")]
    [InlineData("校验", 0.0, 0.0, "", "校验 0.0%")]
    [InlineData("校验", 1.0, 2097152.0, "", "校验 100.0% · 2.0 MB/s")]
    public void Format_MatchesLegacyOutput(string stage, double percent, double speed, string detail, string expected)
    {
        var progress = new JobProgress(Stage: stage, Percent: percent,
            BytesPerSecond: speed > 0 ? (long)speed : null, Detail: detail);
        Assert.Equal(expected, ProgressFormatter.Format(progress));
    }

    [Fact]
    public void Format_NullPercent_OmitsPercentage()
    {
        var progress = new JobProgress(Stage: "准备");
        Assert.Equal("准备", ProgressFormatter.Format(progress));
    }

    [Fact]
    public void Format_WhitespaceDetail_OmitsDetailSection()
    {
        var progress = new JobProgress(Stage: "准备", Percent: 0.1, Detail: "   ");
        Assert.Equal("准备 10.0%", ProgressFormatter.Format(progress));
    }

    [Theory]
    [InlineData(0.0, "0 B/s")]
    [InlineData(512.0, "512 B/s")]
    [InlineData(2048.0, "2.0 KB/s")]
    [InlineData(1572864.0, "1.5 MB/s")]
    public void FormatSpeed_MatchesLegacyBuckets(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, ProgressFormatter.FormatSpeed(bytesPerSecond));
    }
}

