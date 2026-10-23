using System.IO.Compression;
using System.Text;
using WMCL.Download;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Fact]
    public async Task PlayedCurseForgeWorldIsNotReportedAsCorruptOrOverwrittenByRepair()
    {
        CfFileResponse(111, 222, "map.zip", ZipBytes([
            ("World/level.dat", "original metadata"), ("World/region/r.0.0.mca", "original region")]), ["level.dat"]);
        var id = await CfService().InstallFromFileAsync(CfPack(), "PlayedWorld");
        var level = Path.Combine(_repository.GetVersionDirectory(id), "saves", "World", "level.dat");
        await File.WriteAllTextAsync(level, "player progress");
        _requests.Clear();

        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(0, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal("player progress", await File.ReadAllTextAsync(level));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("nested")]
    [InlineData("multiple")]
    public async Task CurseForgeWorldMissingFilesAreRecoveredFromTheirOriginalRemoteArchive(string layout)
    {
        var prefix = layout == "root" ? "" : "export/World/";
        var files = new List<(string Path, string Content)>
        {
            (prefix + "level.dat", "original metadata"), (prefix + "region/r.0.0.mca", "original region")
        };
        if (layout == "multiple") files.Add(("Other/level.dat", "other world"));
        CfFileResponse(111, 222, "map.zip", ZipBytes(files), ["level.dat"]);
        var pack = CfPack();
        var id = await CfService().InstallFromFileAsync(pack, "MissingWorldFile");
        var directory = _repository.GetVersionDirectory(id);
        var world = Path.Combine(directory, "saves", layout == "root" ? "map" : "World");
        var level = Path.Combine(world, "level.dat");
        var region = Path.Combine(world, "region", "r.0.0.mca");
        await File.WriteAllTextAsync(level, "player progress");
        File.Delete(region);
        if (layout == "multiple") Directory.Delete(Path.Combine(directory, "saves", "Other"), true);
        File.Delete(pack);
        _requests.Clear();

        Assert.False((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(layout == "multiple" ? 2 : 1,
            await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal("original region", await File.ReadAllTextAsync(region));
        Assert.Equal("player progress", await File.ReadAllTextAsync(level));
        Assert.Single(_requests, path => path == "/map.zip");
        Assert.Empty(Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task WorldOverridesKeepPlayerProgressAndRestoreMissingBundledFiles()
    {
        var archive = CfPack(files: [], overrides: [
            ("overrides/saves/World/level.dat", "original"), ("overrides/saves/World/region/r.0.0.mca", "region")]);
        var id = await CfService().InstallFromFileAsync(archive, "BundledWorld");
        var world = Path.Combine(_repository.GetVersionDirectory(id), "saves", "World");
        var level = Path.Combine(world, "level.dat");
        var region = Path.Combine(world, "region", "r.0.0.mca");
        await File.WriteAllTextAsync(level, "player progress");
        File.Delete(region);

        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal("player progress", await File.ReadAllTextAsync(level));
        Assert.Equal("region", await File.ReadAllTextAsync(region));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("wrong-size")]
    [InlineData("missing-entry")]
    [InlineData("duplicate")]
    [InlineData("symlink")]
    [InlineData("changed-entry")]
    [InlineData("invalid-zip")]
    public async Task InvalidRemoteWorldArchiveDoesNotApplyAnyFiles(string problem)
    {
        var entries = new[] { ("World/level.dat", "original"), ("World/region/r.0.0.mca", "region") };
        var bytes = ZipBytes(entries);
        CfFileResponse(111, 222, "map.zip", bytes, ["level.dat"]);
        var id = await CfService().InstallFromFileAsync(CfPack(), "BrokenArchive");
        var directory = _repository.GetVersionDirectory(id);
        Directory.Delete(Path.Combine(directory, "saves"), true);
        var manifest = InstallManifestStore.Load(_root, id)!;
        var archiveId = manifest.ManagedArchives.Keys.Single();
        var changed = problem switch
        {
            "missing-entry" => ZipBytes(entries.Take(1)),
            "duplicate" => ZipBytes([.. entries, entries[1]]),
            "changed-entry" => ZipBytes([entries[0], (entries[1].Item1, "wrong region")]),
            "invalid-zip" => Encoding.UTF8.GetBytes("not a zip"),
            _ => bytes
        };
        if (problem == "symlink")
        {
            using var stream = new MemoryStream();
            stream.Write(bytes);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, true))
                archive.GetEntry("World/region/r.0.0.mca")!.ExternalAttributes = unchecked((int)0xA1FF0000);
            changed = stream.ToArray();
        }
        _responses["/map.zip"] = changed;
        manifest.ManagedArchives[archiveId] = new InstallManifestArchive(["https://example.invalid/map.zip"],
            Hash(changed), changed.Length + (problem == "wrong-size" ? 1 : 0));
        InstallManifestStore.Save(_root, id, manifest);
        var savedManifest = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        _requests.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.False(Directory.Exists(Path.Combine(directory, "saves")));
        Assert.Single(_requests, path => path == "/map.zip");
        Assert.Equal(savedManifest, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
    }

    [Theory]
    [InlineData("archive-path")]
    [InlineData("destination")]
    [InlineData("url")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("missing-source")]
    public async Task RemoteWorldRepairRejectsInvalidManifestBeforeDownloading(string problem)
    {
        var hash = new string('0', 40);
        var relative = problem == "destination" ? "../outside.dat" : "versions/Invalid/saves/World/level.dat";
        InstallManifestStore.Save(_root, "Invalid", new InstallManifest
        {
            ManagedArchives = problem == "missing-source" ? [] : new()
            {
                ["map"] = new InstallManifestArchive([problem == "url" ? "file:///map.zip" : "https://example.invalid/map.zip"],
                    problem == "hash" ? "invalid" : hash, problem == "size" ? -1 : 1)
            },
            ManagedFiles = [new(relative, hash, "CurseForge-world", ArchivePath: problem == "archive-path" ? "../level.dat" : "World/level.dat", ArchiveId: "map")]
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ManagedFileRepair.RepairAsync(_root, "Invalid", downloads: new DownloadManager(_http)));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteWorldRepairPreservesFilesCreatedDuringDownloadAndSupportsCancellation(bool cancel)
    {
        CfFileResponse(111, 222, "map.zip", ZipBytes([("World/level.dat", "original")]), ["level.dat"]);
        var id = await CfService().InstallFromFileAsync(CfPack(), "DuringRepair");
        var destination = Path.Combine(_repository.GetVersionDirectory(id), "saves", "World", "level.dat");
        File.Delete(destination);
        using var cancellation = new CancellationTokenSource();
        var progress = new RepairProgress(_ =>
        {
            if (cancel) cancellation.Cancel();
            else File.WriteAllText(destination, "new player progress");
        });
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ManagedFileRepair.RepairAsync(_root, id,
                progress, cancellation.Token, new DownloadManager(_http)));
            Assert.False(File.Exists(destination));
        }
        else
        {
            Assert.Equal(0, await ManagedFileRepair.RepairAsync(_root, id, progress, downloads: new DownloadManager(_http)));
            Assert.Equal("new player progress", await File.ReadAllTextAsync(destination));
        }
    }

    [Fact]
    public async Task RepairCannotFollowAWorldDirectorySymlink()
    {
        var outside = Path.Combine(_root, "outside-world");
        Directory.CreateDirectory(outside);
        var directory = Path.Combine(_repository.GetVersionDirectory("Linked"), "saves");
        Directory.CreateDirectory(directory);
        Directory.CreateSymbolicLink(Path.Combine(directory, "World"), outside);
        try
        {
            InstallManifestStore.Save(_root, "Linked", new InstallManifest
            {
                ManagedFiles = [new("versions/Linked/saves/World/level.dat", new string('0', 40), "CurseForge-world", "https://example.invalid/file")]
            });
            Assert.False((await InstallManifestStore.VerifyAsync(_root, "Linked")).IsHealthy);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ManagedFileRepair.RepairAsync(_root, "Linked", downloads: new DownloadManager(_http)));
            Assert.Empty(_requests);
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally { Directory.Delete(Path.Combine(directory, "World")); }
    }

    private sealed class RepairProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleManifestCannotRestoreFilesIntoAnotherInstance(bool overrides)
    {
        var bytes = Encoding.UTF8.GetBytes("world metadata");
        _responses["/world.dat"] = bytes;
        InstallManifestStore.Save(_root, "NewName", new InstallManifest
        {
            SourceArchive = RepairArchive(("overrides/saves/World/level.dat", bytes)),
            OverridesRoot = _repository.GetVersionDirectory("OldName"),
            OverridesPrefixes = ["overrides/"],
            ManagedFiles = [new("versions/OldName/saves/World/level.dat", Hash(bytes),
                overrides ? "overrides" : "CurseForge-world", overrides ? null : "https://example.invalid/world.dat")]
        });
        Assert.False((await InstallManifestStore.VerifyAsync(_root, "NewName")).IsHealthy);
        await Assert.ThrowsAsync<InvalidDataException>(() => overrides
            ? OverridesRepair.RepairAsync(_root, "NewName")
            : ManagedFileRepair.RepairAsync(_root, "NewName", downloads: new DownloadManager(_http)));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("OldName")));
    }
}
