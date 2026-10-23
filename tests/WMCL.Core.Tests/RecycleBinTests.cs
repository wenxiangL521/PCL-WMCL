using WMCL.Core.IO;

namespace WMCL.Core.Tests;

public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-recycle-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FreedesktopTrashKeepsOriginalNamesBytesAndEncodedRecoveryPath()
    {
        var source = Path.Combine(_root, "更新备份 # %");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllBytes(Path.Combine(source, "mod-old.jar"), [0, 1, 2, 255]);
        File.WriteAllText(Path.Combine(source, "nested", "another.jar"), "original content");
        var trash = Path.Combine(_root, "trash");
        var destination = RecycleBin.MoveToFreedesktopTrash(source, trash);
        Assert.False(Directory.Exists(source));
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, File.ReadAllBytes(Path.Combine(destination, "mod-old.jar")));
        Assert.Equal("original content", File.ReadAllText(Path.Combine(destination, "nested", "another.jar")));
        if (!OperatingSystem.IsWindows())
            foreach (var path in new[] { trash, Path.Combine(trash, "files"), Path.Combine(trash, "info") })
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
        var info = File.ReadAllLines(Path.Combine(trash, "info", Path.GetFileName(destination) + ".trashinfo"));
        Assert.Equal("[Trash Info]", info[0]);
        Assert.Equal(Path.GetFullPath(source), Uri.UnescapeDataString(info[1]["Path=".Length..]));
        Assert.Contains("%23", info[1]);
        Assert.Contains("%25", info[1]);
        Assert.StartsWith("DeletionDate=", info[2]);
        Assert.True(DateTime.TryParseExact(info[2]["DeletionDate=".Length..], "yyyy-MM-ddTHH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
    }

    [Fact]
    public void RepeatedNamesNeverOverwritePreviousRecoveryCopies()
    {
        var source = Path.Combine(_root, "backup");
        var trash = Path.Combine(_root, "trash");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "old.jar"), "first");
        var first = RecycleBin.MoveToFreedesktopTrash(source, trash);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "old.jar"), "second");
        var second = RecycleBin.MoveToFreedesktopTrash(source, trash);
        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(Path.Combine(first, "old.jar")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(second, "old.jar")));
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(trash, "info")).Count());
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("same")]
    [InlineData("blocked")]
    public void InvalidTrashTargetsPreserveTheSource(string target)
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "old.jar"), "original");
        var trash = target == "inside" ? Path.Combine(source, "trash") : target == "same" ? source : Path.Combine(_root, "blocked");
        if (target == "blocked") File.WriteAllText(trash, "user file");
        Assert.ThrowsAny<IOException>(() => RecycleBin.MoveToFreedesktopTrash(source, trash));
        Assert.Equal("original", File.ReadAllText(Path.Combine(source, "old.jar")));
        if (target == "blocked") Assert.Equal("user file", File.ReadAllText(trash));
    }

    [Fact]
    public void FilesystemRootIsRejectedBeforeCreatingTrashMetadata()
    {
        var source = Path.GetPathRoot(Path.GetFullPath(_root))!;
        var trash = Path.Combine(_root, "trash");
        Assert.Throws<IOException>(() => RecycleBin.MoveToFreedesktopTrash(source, trash));
        Assert.Throws<IOException>(() => RecycleBin.MoveDirectory(source));
        Assert.False(Directory.Exists(trash));
    }

    [Fact]
    public void MissingSourceDoesNotCreateRecoveryMetadata()
    {
        var trash = Path.Combine(_root, "trash");
        Assert.Throws<DirectoryNotFoundException>(() => RecycleBin.MoveToFreedesktopTrash(Path.Combine(_root, "missing"), trash));
        Assert.False(Directory.Exists(trash));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
