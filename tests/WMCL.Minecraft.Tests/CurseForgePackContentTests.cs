using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WMCL.Download;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("mod.jar", null, "mods")]
    [InlineData("mod.jar", "level.dat", "mods")]
    [InlineData("legacy.zip", "mcmod.info", "mods")]
    [InlineData("fabric.zip", "fabric.mod.json", "mods")]
    [InlineData("legacy.zip", "META-INF", "mods")]
    [InlineData("unknown.zip", null, "mods")]
    [InlineData("resources.zip", "pack.mcmeta", "resourcepacks")]
    [InlineData("shaders.zip", "shaders", "shaderpacks")]
    public async Task CurseForgeContentUsesPclModuleClassification(string filename, string? module, string folder)
    {
        var bytes = Encoding.UTF8.GetBytes("downloaded " + filename);
        CfFileResponse(111, 222, filename, bytes, module is null ? null : [module]);
        var id = await CfService().InstallFromFileAsync(CfPack(), "Classified");
        var file = Path.Combine(_repository.GetVersionDirectory(id), folder, filename);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "mods", filename)) && folder != "mods");
        var manifest = InstallManifestStore.Load(_root, id)!;
        var managed = Assert.Single(manifest.ManagedFiles, item => item.Component == "CurseForge");
        Assert.Equal($"versions/{id}/{folder}/{filename}", managed.RelativePath);
        Assert.Equal(Hash(bytes), managed.Sha1);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("folder", false)]
    [InlineData("nested", false)]
    [InlineData("multiple", false)]
    [InlineData("folder", true)]
    public async Task CurseForgeWorldArchivesBecomePlayableSaveDirectories(string layout, bool packOverride)
    {
        var prefix = layout switch { "root" => "", "nested" => "export/World/", _ => "World/" };
        var entries = new List<(string Path, string Content)> { (prefix + "level.dat", "world metadata"), (prefix + "region/r.0.0.mca", "region content") };
        if (layout == "multiple") entries.Add(("Other/level.dat", "other world"));
        CfFileResponse(111, 222, "map.zip", ZipBytes(entries), ["level.dat"]);
        var world = layout == "root" ? "map" : "World";
        var path = CfPack(overrides: packOverride ? [("overrides/saves/World/level.dat", "pack override")] : []);
        var id = await CfService().InstallFromFileAsync(path, "WorldPack");
        var directory = _repository.GetVersionDirectory(id);
        Assert.Equal(packOverride ? "pack override" : "world metadata", await File.ReadAllTextAsync(Path.Combine(directory, "saves", world, "level.dat")));
        Assert.Equal("region content", await File.ReadAllTextAsync(Path.Combine(directory, "saves", world, "region", "r.0.0.mca")));
        if (layout == "multiple") Assert.Equal("other world", await File.ReadAllTextAsync(Path.Combine(directory, "saves", "Other", "level.dat")));
        Assert.Empty(Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories));
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.DoesNotContain(manifest.ManagedFiles, file => file.RelativePath.EndsWith(".zip", StringComparison.Ordinal));
        Assert.All(manifest.ManagedFiles.Where(file => file.Component == "CurseForge-world"), file => Assert.Null(file.Url));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("World/./alias.txt")]
    [InlineData("World/config/C:escape")]
    [InlineData("World/LEVEL.DAT")]
    public async Task CurseForgeWorldPathsAreValidatedBeforeExtracting(string maliciousPath)
    {
        CfFileResponse(111, 222, "map.zip", ZipBytes([("World/level.dat", "world"), (maliciousPath, "malicious")]), ["level.dat"]);
        var path = CfPack();
        await Assert.ThrowsAsync<InvalidDataException>(() => CfService().InstallFromFileAsync(path, "InvalidWorld"));
        Assert.Null(InstallManifestStore.Load(_root, "InvalidWorld"));
        Assert.False(Directory.Exists(Path.Combine(_repository.GetVersionDirectory("InvalidWorld"), "saves")));
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public async Task CurseForgeWorldWithoutLevelDataFailsInsteadOfLeavingAnUnusedZip()
    {
        CfFileResponse(111, 222, "unknown.zip", ZipBytes([("readme.txt", "no world")]), ["unknown-module"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => CfService().InstallFromFileAsync(CfPack(), "NoWorld"));
        Assert.Null(InstallManifestStore.Load(_root, "NoWorld"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurseForgeVanillaAndQuiltPacksUseIndependentInstances(bool quilt)
    {
        var path = CfPack(loader: quilt ? "quilt-0.28.0" : null, files: [], overrides: [("overrides/config/test.txt", "independent")]);
        var id = await CfService().InstallFromFileAsync(path, "1.20.1");
        var version = await _repository.GetVersionAsync(id);
        Assert.NotEqual("1.20.1", id);
        Assert.Null(version.InheritsFrom);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal(quilt ? "quilt" : "vanilla", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(_repository.GetVersionDirectory(id), IsolationPolicy.GetGameDirectory(_root, id, version, IsolationPolicy.Global));
        Assert.Equal("independent", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        if (quilt) Assert.Contains("/v3/versions/loader/1.20.1/0.28.0/profile/json", _requests);
    }

    [Theory]
    [InlineData(".", "")]
    [InlineData("./", "wrapped/")]
    [InlineData("overrides", "wrapped/")]
    [InlineData("", "")]
    public async Task CurseForgeOverridesAndRepairUseTheSameRootAndExcludeManifest(string overrides, string baseFolder)
    {
        var prefix = overrides is "." or "./" or "" ? baseFolder : baseFolder + overrides + "/";
        var path = CfPack(files: [], overrideFolder: overrides, baseFolder: baseFolder,
            overrides: [(prefix + "config/test.txt", "correct"), ("unrelated.txt", "unrelated")]);
        var id = await CfService().InstallFromFileAsync(path, "Overrides");
        var directory = _repository.GetVersionDirectory(id);
        if (overrides.Length == 0)
        {
            Assert.False(Directory.Exists(Path.Combine(directory, "config")));
            Assert.Equal(0, await OverridesRepair.RepairAsync(_root, id));
        }
        else
        {
            var config = Path.Combine(directory, "config", "test.txt");
            Assert.Equal("correct", await File.ReadAllTextAsync(config));
            await File.WriteAllTextAsync(config, "corrupt");
            await OverridesRepair.RepairAsync(_root, id);
            Assert.Equal("correct", await File.ReadAllTextAsync(config));
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("unsupported-1")]
    [InlineData("forge-recommended")]
    [InlineData("quilt-")]
    public async Task CurseForgeInvalidLoadersFailBeforeNetworkOrInstanceCreation(string loader)
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => CfService().InstallFromFileAsync(CfPack(loader: loader), "InvalidLoader"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("InvalidLoader")));
    }

    [Theory]
    [InlineData("file", "../escape.jar")]
    [InlineData("file", "nested/mod.jar")]
    [InlineData("file", "mod:bad.jar")]
    [InlineData("identity", "mod.jar")]
    public async Task CurseForgeBadFileMetadataFailsBeforeInstallingMinecraft(string problem, string filename)
    {
        CfFileResponse(problem == "identity" ? 999 : 111, 222, filename, Encoding.UTF8.GetBytes("mod"), null, responsePath: "/v1/mods/111/files/222");
        await Assert.ThrowsAsync<InvalidDataException>(() => CfService().InstallFromFileAsync(CfPack(), "BadMetadata"));
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("BadMetadata")));
    }

    [Fact]
    public async Task CurseForgeDifferentFilesCannotOverwriteTheSameDestination()
    {
        CfFileResponse(111, 222, "same.jar", Encoding.UTF8.GetBytes("one"), null);
        CfFileResponse(112, 223, "same.jar", Encoding.UTF8.GetBytes("two"), null);
        await Assert.ThrowsAsync<InvalidDataException>(() => CfService().InstallFromFileAsync(
            CfPack(files: [new(111, 222, true), new(112, 223, true)]), "Collision"));
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Collision")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurseForgeOptionalSelectionControlsActualMetadataAndDownloads(bool includeOptional)
    {
        foreach (var file in new[] { (111L, 222L, "required.jar"), (112L, 223L, "selected.jar"), (113L, 224L, "excluded.jar") })
            CfFileResponse(file.Item1, file.Item2, file.Item3, Encoding.UTF8.GetBytes(file.Item3), null);
        var path = CfPack(files: [new(111, 222, true), new(112, 223, false), new(113, 224, false)]);
        var id = await CfService().InstallFromFileAsync(path, "Optional", includeOptional: includeOptional,
            selectedOptionalFileIds: new HashSet<long> { 223 });
        var directory = Path.Combine(_repository.GetVersionDirectory(id), "mods");
        Assert.True(File.Exists(Path.Combine(directory, "required.jar")));
        Assert.Equal(includeOptional, File.Exists(Path.Combine(directory, "selected.jar")));
        Assert.False(File.Exists(Path.Combine(directory, "excluded.jar")));
        Assert.DoesNotContain("/v1/mods/113/files/224", _requests);
        if (!includeOptional) Assert.DoesNotContain("/v1/mods/112/files/223", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurseForgeDeletedSelectedFilesFailBeforeInstalling(bool required)
    {
        var path = CfPack(files: [new(113, 224, required)]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CfService().InstallFromFileAsync(path, "MissingSelected"));
        Assert.Contains(required ? "必需文件" : "已选择的可选文件", exception.Message);
        Assert.Contains("113", exception.Message);
        Assert.Contains("224", exception.Message);
        Assert.IsType<CurseForgeFileUnavailableException>(exception.InnerException);
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("MissingSelected")));
        Assert.Null(InstallManifestStore.Load(_root, "MissingSelected"));
    }

    [Fact]
    public async Task CurseForgeArchiveCacheSeparatesSameNamesAndRepairsCorruption()
    {
        var first = ZipBytes([("manifest.json", "one")]);
        var second = ZipBytes([("manifest.json", "two")]);
        _responses["/first.zip"] = first;
        _responses["/second.zip"] = second;
        var file = ArchiveFile(111, "same.zip", "/first.zip", first);
        var other = ArchiveFile(222, "same.zip", "/second.zip", second);
        var service = CfService();
        var firstPath = await service.DownloadArchiveAsync(file);
        var secondPath = await service.DownloadArchiveAsync(other);
        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(first, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(second, await File.ReadAllBytesAsync(secondPath));
        Assert.Equal(firstPath, await service.DownloadArchiveAsync(file));
        Assert.Single(_requests, path => path == "/first.zip");
        await File.WriteAllBytesAsync(firstPath, new byte[first.Length]);
        await service.DownloadArchiveAsync(file);
        Assert.Equal(2, _requests.Count(path => path == "/first.zip"));
        Assert.Equal(first, await File.ReadAllBytesAsync(firstPath));
        var changedPath = await service.DownloadArchiveAsync(other with { Id = file.Id });
        Assert.NotEqual(firstPath, changedPath);
    }

    [Fact]
    public async Task CurseForgeHashlessCacheDoesNotTrustExistingBytes()
    {
        var bytes = ZipBytes([("manifest.json", "content")]);
        _responses["/archive.zip"] = bytes;
        var file = ArchiveFile(111, "pack.zip", "/archive.zip", bytes) with { Sha1 = null };
        var service = CfService();
        var path = await service.DownloadArchiveAsync(file);
        await File.WriteAllBytesAsync(path, new byte[bytes.Length]);
        await service.DownloadArchiveAsync(file);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, _requests.Count(request => request == "/archive.zip"));
    }

    [Fact]
    public async Task CurseForgeWrongArchiveLengthIsNotRetainedAsAValidCacheEntry()
    {
        var bytes = ZipBytes([("manifest.json", "content")]);
        _responses["/archive.zip"] = bytes;
        var file = ArchiveFile(111, "pack.zip", "/archive.zip", bytes) with { Length = bytes.Length + 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() => CfService().DownloadArchiveAsync(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "cf-cache"), "*.zip"));
    }

    private CurseForgeModpackInstaller CfService()
    {
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        return new CurseForgeModpackInstaller(_repository, installer,
            new FabricInstaller(_repository, installer, http: _http), client: new CurseForgeClient("key", _http),
            downloads: downloads, quilt: new FabricInstaller(_repository, installer, FabricFamilyKind.Quilt, _http),
            archiveCacheDirectory: Path.Combine(_root, "cf-cache"));
    }

    private void CfFileResponse(long project, long id, string filename, byte[] bytes, string[]? modules, string? responsePath = null)
    {
        _responses["/" + filename] = bytes;
        _responses[responsePath ?? $"/v1/mods/{project}/files/{id}"] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            data = new
            {
                id,
                modId = project,
                displayName = filename,
                fileName = filename,
                fileLength = bytes.Length,
                downloadUrl = "https://example.invalid/" + filename,
                hashes = new[] { new { algo = 1, value = Hash(bytes) } },
                modules = modules?.Select(name => new { name }).ToArray()
            }
        });
    }

    private string CfPack(string? loader = null, CurseForgeManifestFile[]? files = null, string overrideFolder = "overrides",
        string baseFolder = "", (string Path, string Content)[]? overrides = null)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(baseFolder + "manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(new
            {
                manifestType = "minecraftModpack",
                manifestVersion = 1,
                name = "Pack",
                version = "1",
                minecraft = new { version = "1.20.1", modLoaders = loader is null ? [] : new[] { new { id = loader, primary = true } } },
                files = (files ?? [new(111, 222, true)]).Select(file => new { projectID = file.ProjectId, fileID = file.FileId, required = file.Required }),
                overrides = overrideFolder
            }));
        foreach (var entry in overrides ?? [])
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
            writer.Write(entry.Content);
        }
        return path;
    }

    private static byte[] ZipBytes(IEnumerable<(string Path, string Content)> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
                writer.Write(entry.Content);
            }
        return stream.ToArray();
    }

    private static CurseForgeFile ArchiveFile(long id, string filename, string url, byte[] bytes) =>
        new(id, filename, filename, DateTimeOffset.UnixEpoch, bytes.Length, "https://example.invalid" + url, Hash(bytes), [], []);
}
