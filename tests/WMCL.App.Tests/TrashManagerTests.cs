using WMCL.Infrastructure;
using WMCL.Core;

namespace WMCL.Core.Tests;

[Collection("TrashManagerTests")]
public sealed class TrashManagerTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;

    public TrashManagerTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-trash-" + Guid.NewGuid().ToString("N"));
        _oldXdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdgData);
        try { Directory.Delete(_dataDir, true); } catch { }
    }

    [Fact]
    public void MoveToTrash_MovesDirectory()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.1.json"), "{}");

        TrashManager.MoveToTrash(root, "1.20.1");

        Assert.False(Directory.Exists(versionDir));
        var entries = TrashManager.ListEntries();
        Assert.Single(entries);
        Assert.StartsWith("1.20.1", entries[0].VersionId);
    }

    [Fact]
    public void Restore_MovesBack()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "1.20.1.json"), "{}");

        TrashManager.MoveToTrash(root, "1.20.1");
        var entries = TrashManager.ListEntries();
        TrashManager.Restore(entries[0].Path, root);

        Assert.True(Directory.Exists(versionDir));
        Assert.Empty(TrashManager.ListEntries());
    }

    [Fact]
    public void PermanentDelete_RemovesEntry()
    {
        var root = Path.Combine(_dataDir, "game");
        var versionDir = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(versionDir);

        TrashManager.MoveToTrash(root, "1.20.1");
        var entries = TrashManager.ListEntries();
        TrashManager.PermanentDelete(entries[0].Path);

        Assert.Empty(TrashManager.ListEntries());
    }

    [Fact]
    public void ClearAll_RemovesAll()
    {
        var root = Path.Combine(_dataDir, "game");
        for (var i = 0; i < 3; i++)
        {
            var versionDir = Path.Combine(root, "versions", $"1.20.{i}");
            Directory.CreateDirectory(versionDir);
            TrashManager.MoveToTrash(root, $"1.20.{i}");
        }
        Assert.Equal(3, TrashManager.ListEntries().Count);

        TrashManager.ClearAll();
        Assert.Empty(TrashManager.ListEntries());
    }

    [Fact]
    public void ListEntries_EmptyTrash_ReturnsEmpty()
    {
        Assert.Empty(TrashManager.ListEntries());
    }
}

/// <summary>串行执行，避免环境变量竞争。</summary>
[CollectionDefinition("TrashManagerTests")]
public sealed class TrashManagerTestCollection : ICollectionFixture<TrashManagerTests> { }
