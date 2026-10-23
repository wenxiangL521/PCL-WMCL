using System.IO.Compression;
using System.Text;
using WMCL.Download;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("same-size")]
    [InlineData("healthy")]
    public async Task ManagedArchiveRepairRestoresVerifiedBytesAndKeepsUnmanagedFiles(string state)
    {
        var expected = Encoding.UTF8.GetBytes("local library fixture");
        var archive = RepairArchive(("wrapped/libraries/custom.jar", expected));
        var path = Path.Combine(_root, "libraries", "local", "custom.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (state != "missing") await File.WriteAllBytesAsync(path, state == "healthy" ? expected : new byte[expected.Length]);
        var userFile = Path.Combine(_root, "libraries", "user.txt");
        await File.WriteAllTextAsync(userFile, "keep user file");
        InstallManifestStore.Save(_root, "Archive", new InstallManifest
        {
            SourceArchive = archive,
            ManagedFiles = [new("libraries/local/custom.jar", Hash(expected), "MMC-library", ArchivePath: "wrapped/libraries/custom.jar")]
        });
        var loaded = InstallManifestStore.Load(_root, "Archive")!;
        Assert.Equal("wrapped/libraries/custom.jar", loaded.ManagedFiles[0].ArchivePath);
        Assert.Equal(state == "healthy" ? 0 : 1, await ManagedFileRepair.RepairAsync(_root, "Archive"));
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        Assert.Equal("keep user file", await File.ReadAllTextAsync(userFile));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Archive")).IsHealthy);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("missing-entry")]
    [InlineData("duplicate")]
    [InlineData("symlink")]
    [InlineData("traversal")]
    public async Task ManagedArchiveRepairRejectsInvalidSourceAndPreservesExistingDestination(string mode)
    {
        var expected = Encoding.UTF8.GetBytes("valid library");
        var files = mode switch
        {
            "tampered" => new[] { ("libraries/custom.jar", Encoding.UTF8.GetBytes("bad library")) },
            "missing-entry" => [("libraries/other.jar", expected)],
            "duplicate" => [("libraries/custom.jar", expected), ("libraries/custom.jar", expected)],
            _ => [("libraries/custom.jar", expected)]
        };
        var archive = RepairArchive(files);
        if (mode == "symlink")
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Update);
            zip.GetEntry("libraries/custom.jar")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        var destination = Path.Combine(_root, "custom.jar");
        await File.WriteAllTextAsync(destination, "preserve existing bytes");
        InstallManifestStore.Save(_root, "InvalidArchive", new InstallManifest
        {
            SourceArchive = archive,
            ManagedFiles = [new("custom.jar", Hash(expected), "MMC-library", ArchivePath: mode == "traversal" ? "../custom.jar" : "libraries/custom.jar")]
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => ManagedFileRepair.RepairAsync(_root, "InvalidArchive"));
        Assert.Equal("preserve existing bytes", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedArchiveRepairUsesUrlWhenArchiveIsGoneAndOtherwiseReportsTheMissingSource(bool remote)
    {
        var expected = Encoding.UTF8.GetBytes("fallback library");
        _responses["/fallback.jar"] = expected;
        InstallManifestStore.Save(_root, "MissingArchive", new InstallManifest
        {
            SourceArchive = Path.Combine(_root, "deleted.zip"),
            ManagedFiles = [new("custom.jar", Hash(expected), "MMC-library", remote ? "https://example.invalid/fallback.jar" : null, "libraries/custom.jar")]
        });
        if (remote)
        {
            Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, "MissingArchive", downloads: new DownloadManager(_http)));
            Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(_root, "custom.jar")));
        }
        else
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => ManagedFileRepair.RepairAsync(_root, "MissingArchive"));
            Assert.False(File.Exists(Path.Combine(_root, "custom.jar")));
        }
    }

    private string RepairArchive(params (string Path, byte[] Content)[] files)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            using var target = zip.CreateEntry(file.Path).Open();
            target.Write(file.Content);
        }
        return path;
    }
}
