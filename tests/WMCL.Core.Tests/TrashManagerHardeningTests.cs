using WMCL.Core;

namespace WMCL.Core.Tests;

/// <summary>
/// 回收站加固项的回归测试：路径穿越、元数据清理、deletedAt 语义。
/// 与 TrashManagerTests 同 Collection：两者都通过 XDG_DATA_HOME 隔离数据目录，
/// 环境变量是进程级全局状态，并行执行会互相踩，必须串行。
/// </summary>
[Collection("TrashManagerTests")]
[Trait("Kind", "Unit")]
public sealed class TrashManagerHardeningTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;

    public TrashManagerHardeningTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-trash-hardening-" + Guid.NewGuid().ToString("N"));
        _oldXdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdgData);
        try { Directory.Delete(_dataDir, true); } catch { }
    }

    [Fact]
    public void MoveToTrash_RejectsVersionIdWithSeparators()
    {
        var root = Path.Combine(_dataDir, "game");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        Assert.Throws<ArgumentException>(() => TrashManager.MoveToTrash(root, "../evil"));
        Assert.Throws<ArgumentException>(() => TrashManager.MoveToTrash(root, "a/b"));
        Assert.Throws<ArgumentException>(() => TrashManager.MoveToTrash(root, "  "));
    }

    [Fact]
    public void Restore_RejectsTamperedVersionIdFromMeta()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        TrashManager.MoveToTrash(root, "1.20.1");

        // 篡改回收站元数据，试图把实例恢复到 versions 之外。
        var entry = TrashManager.ListEntries().Single();
        var meta = Path.Combine(entry.Path, "trash.json");
        File.WriteAllText(meta, """{"versionId":"../../evil","deletedAt":"2026-01-01T00:00:00Z"}""");

        Assert.Throws<InvalidOperationException>(() => TrashManager.Restore(entry.Path, root));
        // 回收站条目仍在原地，未被移动。
        Assert.True(Directory.Exists(entry.Path));
    }

    [Fact]
    public void Restore_RemovesTrashMetaFile()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.1.json"), "{}");

        TrashManager.MoveToTrash(root, "1.20.1");
        var entry = TrashManager.ListEntries().Single();
        TrashManager.Restore(entry.Path, root);

        // 恢复后实例目录不应残留回收站元数据文件。
        Assert.True(Directory.Exists(versionDir));
        Assert.False(File.Exists(Path.Combine(versionDir, "trash.json")));
    }

    [Fact]
    public void ListEntries_UsesDeletedAtFromMeta()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        TrashManager.MoveToTrash(root, "1.20.1");

        var entry = TrashManager.ListEntries().Single();
        // 刚删除的条目 DeletedAt 应接近现在（来自元数据），而非文件系统时间戳。
        Assert.True(DateTime.UtcNow - entry.DeletedAt < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AutoClean_ZeroDaysDoesNothing()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        TrashManager.MoveToTrash(root, "1.20.1");

        // 0 = 不自动清理；此前 days=0 会把 cutoff 算成 now、全部清空。
        Assert.Equal(0, TrashManager.AutoClean(0));
        Assert.Single(TrashManager.ListEntries());
    }

    [Fact]
    public void AutoClean_RemovesOnlyExpiredEntries()
    {
        var root = Path.Combine(_dataDir, "game");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        foreach (var id in new[] { "1.20.1", "1.21" })
        {
            var dir = Path.Combine(root, "versions", id);
            Directory.CreateDirectory(dir);
            TrashManager.MoveToTrash(root, id);
        }

        // 把一个条目的删除时间改成 100 天前。
        var entries = TrashManager.ListEntries();
        var oldEntry = entries.First(e => e.VersionId == "1.20.1");
        File.WriteAllText(Path.Combine(oldEntry.Path, "trash.json"),
            $$"""{"versionId":"1.20.1","deletedAt":"{{DateTime.UtcNow.AddDays(-100):o}}"}""");

        Assert.Equal(1, TrashManager.AutoClean(30));
        var remaining = TrashManager.ListEntries();
        Assert.Single(remaining);
        Assert.Equal("1.21", remaining[0].VersionId);
    }

    [Fact]
    public void ListEntries_SkipsEntryWithUnusableVersionId()
    {
        Directory.CreateDirectory(TrashManager.TrashDirectory);
        // 手工放一个元数据被篡改成路径的条目：列表应跳过而不是崩溃。
        var bad = Path.Combine(TrashManager.TrashDirectory, "bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "trash.json"),
            """{"versionId":"../evil","deletedAt":"2026-01-01T00:00:00Z"}""");

        Assert.DoesNotContain(TrashManager.ListEntries(), e => e.VersionId == "../evil");
    }

    [Fact]
    public void MoveToTrash_SameVersionTwiceInSameSecond_UsesDistinctTargets()
    {
        // 同一秒内出现同名目标（如残留的旧时间戳目录，或另一数据源的并发删除）：
        // 冲突时追加序号，此前直接 Directory.Move 到已存在目标会抛 IOException。
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.1.json"), "{}");

        TrashManager.MoveToTrash(root, "1.20.1");
        var first = TrashManager.ListEntries().Single();
        // 恢复实例，但人为在回收站留下一个同名时间戳目录（模拟冲突源：
        // 残留目录/并发删除），使第二次删除撞上已存在目标。
        TrashManager.Restore(first.Path, root);
        var timestamp = Path.GetFileName(first.Path)[("1.20.1-".Length)..];
        Directory.CreateDirectory(Path.Combine(TrashManager.TrashDirectory, $"1.20.1-{timestamp}"));

        TrashManager.MoveToTrash(root, "1.20.1");

        var entries = TrashManager.ListEntries();
        Assert.Equal(2, entries.Count);
        // 第二个条目带序号后缀，与冲突目录区分。
        Assert.Contains(entries, e => Path.GetFileName(e.Path).Contains($"-{timestamp}-"));
    }
}
