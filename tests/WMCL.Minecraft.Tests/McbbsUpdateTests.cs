using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsUpdateMergesChangesAndDoesNotReapplyOldArchive(bool force)
    {
        var original = McbbsIndex(
            TextAddon("config/changed.txt", "original"),
            TextAddon("config/user.txt", "original user"),
            TextAddon("config/removed.txt", "remove"),
            TextAddon("config/removed-user.txt", "remove user"),
            TextAddon("config/missing.txt", "restore"));
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original, entries:
        [
            ("config/changed.txt", "original"), ("config/user.txt", "original user"),
            ("config/removed.txt", "remove"), ("config/removed-user.txt", "remove user"),
            ("config/missing.txt", "restore"), ("config/unlisted.txt", "unlisted default")
        ]), "Update");
        var directory = _repository.GetVersionDirectory(id);
        await File.WriteAllTextAsync(Path.Combine(directory, "config/user.txt"), "user choice");
        await File.WriteAllTextAsync(Path.Combine(directory, "config/removed-user.txt"), "user removed choice");
        await File.WriteAllTextAsync(Path.Combine(directory, "config/new-collision.txt"), "user collision");
        Directory.CreateDirectory(Path.Combine(directory, "saves"));
        await File.WriteAllTextAsync(Path.Combine(directory, "saves/world.txt"), "world");
        File.Delete(Path.Combine(directory, "config/missing.txt"));
        var changedUser = TextAddon("config/user.txt", "remote user");
        changedUser["force"] = force;
        var remote = McbbsIndex(TextAddon("config/changed.txt", "updated"), changedUser,
            TextAddon("config/missing.txt", "restore"), TextAddon("config/new.txt", "new"),
            TextAddon("config/new-collision.txt", "remote collision"));
        remote["version"] = "2.0";
        remote["forceUpdate"] = true;
        SetMcbbsRemote(remote);
        var result = await McbbsUpdater().UpdateAsync(id);
        Assert.Equal("2.0", result.Version);
        Assert.Equal(force ? 4 : 3, result.Downloaded);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(force ? 2 : 3, result.Preserved.Count);
        Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(directory, "config/changed.txt")));
        Assert.Equal(force ? "remote user" : "user choice", await File.ReadAllTextAsync(Path.Combine(directory, "config/user.txt")));
        Assert.Equal("user removed choice", await File.ReadAllTextAsync(Path.Combine(directory, "config/removed-user.txt")));
        Assert.Equal("user collision", await File.ReadAllTextAsync(Path.Combine(directory, "config/new-collision.txt")));
        Assert.False(File.Exists(Path.Combine(directory, "config/removed.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(directory, "config/new.txt")));
        Assert.Equal("restore", await File.ReadAllTextAsync(Path.Combine(directory, "config/missing.txt")));
        Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(directory, "saves/world.txt")));
        var saved = InstallManifestStore.Load(_root, id)!;
        Assert.All(result.Preserved, path => Assert.DoesNotContain(saved.ManagedFiles,
            file => file.RelativePath == "versions/" + id + "/" + path));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(0, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(directory, "config/changed.txt")));
        Assert.Equal(force ? "remote user" : "user choice", await File.ReadAllTextAsync(Path.Combine(directory, "config/user.txt")));
        Assert.False(File.Exists(Path.Combine(directory, "config/removed.txt")));
        var repeated = await McbbsUpdater().UpdateAsync(id);
        Assert.Equal(0, repeated.Downloaded);
        Assert.Equal(0, repeated.Deleted);
        Assert.Contains("config/new-collision.txt", repeated.Preserved);
        Assert.Equal(force ? "remote user" : "user choice", await File.ReadAllTextAsync(Path.Combine(directory, "config/user.txt")));
    }

    [Fact]
    public async Task McbbsUpdatePinsCurseIdentityAndKeepsPrefetchedNameAndUrl()
    {
        var first = Encoding.UTF8.GetBytes("first mod");
        var next = Encoding.UTF8.GetBytes("next mod");
        CfFileResponse(111, 222, "original.jar", first, null);
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex(Curse(111, 222))), "CurseUpdate");
        var directory = _repository.GetVersionDirectory(id);
        File.Delete(Path.Combine(directory, "mods/original.jar"));
        var remote = McbbsIndex(Curse(111, 222));
        SetMcbbsRemote(remote);
        _requests.Clear();
        var repaired = await McbbsUpdater(configured: false).UpdateAsync(id);
        Assert.Equal(1, repaired.Downloaded);
        Assert.DoesNotContain(_requests, request => request.StartsWith("/v1/mods/"));
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(directory, "mods/original.jar")));
        CfFileResponse(111, 223, "next.jar", next, null);
        SetMcbbsRemote(McbbsIndex(Curse(111, 223)));
        var updated = await McbbsUpdater().UpdateAsync(id);
        Assert.Equal(1, updated.Downloaded);
        Assert.Equal(1, updated.Deleted);
        Assert.False(File.Exists(Path.Combine(directory, "mods/original.jar")));
        Assert.Equal(next, await File.ReadAllBytesAsync(Path.Combine(directory, "mods/next.jar")));
        Assert.Contains("/v1/mods/111/files/223", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task McbbsUpdateReplacesNewCurseIdAtSamePathAndPreservesModifiedRemovedMod()
    {
        _responses["/first.jar"] = Encoding.UTF8.GetBytes("old");
        _responses["/next.jar"] = Encoding.UTF8.GetBytes("next");
        var original = McbbsIndex(Curse(111, 222, "same.jar", "/first.jar"), TextAddon("mods/removed.jar", "old removed"));
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original, entries:
            [("mods/removed.jar", "old removed")]), "SamePath");
        var directory = _repository.GetVersionDirectory(id);
        await File.WriteAllTextAsync(Path.Combine(directory, "mods/removed.jar"), "user mod");
        SetMcbbsRemote(McbbsIndex(Curse(111, 223, "same.jar", "/next.jar")));
        var updated = await McbbsUpdater(configured: false).UpdateAsync(id);
        Assert.Equal(1, updated.Downloaded);
        Assert.Equal(0, updated.Deleted);
        Assert.Contains("mods/removed.jar", updated.Preserved);
        Assert.Equal("next", await File.ReadAllTextAsync(Path.Combine(directory, "mods/same.jar")));
        Assert.Equal("user mod", await File.ReadAllTextAsync(Path.Combine(directory, "mods/removed.jar")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsUpdateUsesPersistedBaselineWithoutOriginalArchiveAndMigratesOldManifests(bool legacy)
    {
        var original = McbbsIndex(TextAddon("config/test.txt", "original"));
        var archive = McbbsPack(original, "wrapped/", "manifest.json", ("config/test.txt", "original"));
        var id = await McbbsService().InstallFromFileAsync(archive, "Baseline");
        if (legacy)
        {
            var install = InstallManifestStore.Load(_root, id)!;
            install.ModpackKind = null;
            install.ModpackManifestJson = null;
            install.ModpackFileHashes.Clear();
            InstallManifestStore.Save(_root, id, install);
        }
        else File.Delete(archive);
        SetMcbbsRemote(McbbsIndex(TextAddon("config/test.txt", "updated")));
        await McbbsUpdater().UpdateAsync(id);
        Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config/test.txt")));
        var saved = InstallManifestStore.Load(_root, id)!;
        Assert.Equal("MCBBS", saved.ModpackKind);
        Assert.NotNull(saved.ModpackManifestJson);
        File.Delete(archive);
        Assert.Equal(0, (await McbbsUpdater().UpdateAsync(id)).Downloaded);
    }

    [Theory]
    [InlineData("download")]
    [InlineData("hash")]
    [InlineData("cancel-download")]
    [InlineData("cancel-commit")]
    [InlineData("concurrent-edit")]
    [InlineData("commit-failure")]
    public async Task McbbsFailedUpdateLeavesOldFilesAndManifestIntact(string problem)
    {
        var original = McbbsIndex(TextAddon("config/a.txt", "old a"), TextAddon("config/removed.txt", "old removed"));
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original, entries:
            [("config/a.txt", "old a"), ("config/removed.txt", "old removed")]), "Failed");
        var directory = _repository.GetVersionDirectory(id);
        var originalManifest = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        var remote = McbbsIndex(TextAddon("config/a.txt", "new a"), TextAddon("config/z.txt", "new z"));
        SetMcbbsRemote(remote);
        if (problem == "download") _responses.TryRemove("/pack/overrides/config/z.txt", out _);
        if (problem == "hash") _responses["/pack/overrides/config/z.txt"] = Encoding.UTF8.GetBytes("wrong");
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(update =>
        {
            if (problem == "cancel-download" && update.Stage == "下载整合包更新") cancellation.Cancel();
            if (problem == "cancel-commit" && update.Stage == "应用整合包更新" && update.Detail == "config/a.txt")
                cancellation.Cancel();
            if (problem == "concurrent-edit" && update.Stage == "应用整合包更新" && update.Detail is null)
                File.WriteAllText(Path.Combine(directory, "config/a.txt"), "user concurrent edit");
            if (problem == "commit-failure" && update.Stage == "应用整合包更新" && update.Detail == "config/a.txt")
                Directory.CreateDirectory(Path.Combine(directory, "config/z.txt"));
        });
        await Assert.ThrowsAnyAsync<Exception>(() => McbbsUpdater().UpdateAsync(id, progress, cancellation.Token));
        Assert.Equal(problem == "concurrent-edit" ? "user concurrent edit" : "old a",
            await File.ReadAllTextAsync(Path.Combine(directory, "config/a.txt")));
        Assert.Equal("old removed", await File.ReadAllTextAsync(Path.Combine(directory, "config/removed.txt")));
        Assert.False(File.Exists(Path.Combine(directory, "config/z.txt")));
        Assert.Equal(originalManifest, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(directory, "WMCL"), "update-*"));
    }

    [Theory]
    [InlineData("addons", "[{\"id\":\"game\",\"version\":\"1.21\"}]")]
    [InlineData("addons", "[{\"id\":\"game\",\"version\":\"1.20.1\"},{\"id\":\"fabric\",\"version\":\"0.28.0\"}]")]
    [InlineData("libraries", "[{\"name\":\"org.test:extra:1\"}]")]
    [InlineData("launchInfo", "{\"javaArgument\":[\"-Dchanged=true\"]}")]
    [InlineData("settings", "{\"install_mods\":false}")]
    [InlineData("files", "null")]
    [InlineData("files", "[{\"type\":\"addon\",\"path\":\"../outside.txt\",\"hash\":\"0000000000000000000000000000000000000000\"}]")]
    [InlineData("files", "[{\"type\":\"addon\",\"path\":\"WMCL/install.json\",\"hash\":\"0000000000000000000000000000000000000000\"}]")]
    [InlineData("forceUpdate", "\"true\"")]
    [InlineData("manifestVersion", "2")]
    [InlineData("manifestType", "null")]
    public async Task McbbsUpdateRejectsUnsupportedOrMalformedManifestWithoutMutations(string field, string value)
    {
        var original = McbbsIndex(TextAddon("config/test.txt", "old"));
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original, entries: [("config/test.txt", "old")]), "Invalid");
        var before = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        var remote = McbbsIndex();
        remote[field] = JsonNode.Parse(value);
        SetMcbbsRemote(remote);
        await Assert.ThrowsAnyAsync<Exception>(() => McbbsUpdater().UpdateAsync(id));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config/test.txt")));
        Assert.Equal(before, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
        Assert.DoesNotContain(_requests, request => request.StartsWith("/pack/overrides/"));
    }

    [Fact]
    public async Task McbbsUpdateRequiresExplicitFilesAndChecksResolvedCursePathCollisions()
    {
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex()), "Collision");
        var missing = McbbsIndex();
        missing.Remove("files");
        SetMcbbsRemote(missing);
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsUpdater().UpdateAsync(id));
        CfFileResponse(111, 222, "same.jar", Encoding.UTF8.GetBytes("mod"), null);
        SetMcbbsRemote(McbbsIndex(Curse(111, 222), TextAddon("mods/same.jar", "addon")));
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsUpdater().UpdateAsync(id));
        Assert.False(Directory.Exists(Path.Combine(_repository.GetVersionDirectory(id), "mods")));
    }

    [Fact]
    public async Task McbbsUpdateRejectsSymlinkTargetsAndProtectsOutsideFiles()
    {
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex()), "Symlink");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "test.txt"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(_repository.GetVersionDirectory(id), "config"), outside);
        SetMcbbsRemote(McbbsIndex(TextAddon("config/test.txt", "remote")));
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsUpdater().UpdateAsync(id));
        Assert.Equal("outside", await File.ReadAllTextAsync(Path.Combine(outside, "test.txt")));
    }

    [Fact]
    public async Task McbbsUpdateFollowsChangedFileApiAndEscapesDownloadPaths()
    {
        const string path = "config/选项 #1%.txt";
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex()), "Endpoint");
        var remote = McbbsIndex(TextAddon(path, "new endpoint"));
        remote["fileApi"] = "https://example.invalid/next";
        _responses["/next/overrides/config/%E9%80%89%E9%A1%B9%20%231%25.txt"] = Encoding.UTF8.GetBytes("new endpoint");
        SetMcbbsRemote(remote);
        var result = await McbbsUpdater().UpdateAsync(id);
        Assert.Equal(1, result.Downloaded);
        Assert.Equal("new endpoint", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), path)));
        _responses["/next/manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(remote);
        await McbbsUpdater().UpdateAsync(id);
        Assert.Contains("/next/manifest.json", _requests);
    }

    [Theory]
    [InlineData("no-api")]
    [InlineData("legacy-no-archive")]
    [InlineData("not-mcbbs")]
    [InlineData("cancelled")]
    public async Task McbbsUpdateFailsClearlyWhenThereIsNoUsableBaseline(string problem)
    {
        var original = McbbsIndex();
        if (problem == "no-api") original.Remove("fileApi");
        var path = McbbsPack(original);
        var id = await McbbsService().InstallFromFileAsync(path, "NoBaseline");
        var install = InstallManifestStore.Load(_root, id)!;
        if (problem == "legacy-no-archive")
        {
            install.ModpackManifestJson = null;
            File.Delete(path);
        }
        if (problem == "not-mcbbs")
        {
            install.ModpackKind = null;
            install.Components["local-pack"] = "Mmc";
        }
        InstallManifestStore.Save(_root, id, install);
        using var cancellation = new CancellationTokenSource();
        if (problem == "cancelled") cancellation.Cancel();
        _requests.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() => McbbsUpdater().UpdateAsync(id, ct: cancellation.Token));
        Assert.Empty(_requests);
    }

    private McbbsModpackUpdater McbbsUpdater(bool configured = true) =>
        new(_repository, new CurseForgeClient(configured ? "key" : null, _http), new DownloadManager(_http), _http);

    [Fact]
    public async Task McbbsUnchangedBundledCurseKeepsArchiveRepairSourceWithoutApiKey()
    {
        var index = McbbsIndex(Curse(111, 222, "bundled.jar"));
        var id = await McbbsService(configured: false).InstallFromFileAsync(
            McbbsPack(index, entries: [("mods/bundled.jar", "bundled")]), "BundledUpdate");
        SetMcbbsRemote(McbbsIndex(Curse(111, 222)));
        var result = await McbbsUpdater(configured: false).UpdateAsync(id);
        Assert.Equal(0, result.Downloaded);
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles,
            file => file.RelativePath.EndsWith("/mods/bundled.jar"));
        Assert.Equal("overrides/mods/bundled.jar", managed.ArchivePath);
        var target = Path.Combine(_repository.GetVersionDirectory(id), "mods/bundled.jar");
        File.Delete(target);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal("bundled", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(_requests, request => request.StartsWith("/v1/"));
    }

    [Fact]
    public async Task McbbsUpdateRejectsWrongCurseSizeWithoutReplacingExistingFiles()
    {
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex()), "WrongSize");
        var bytes = Encoding.UTF8.GetBytes("new mod");
        CfFileResponse(111, 222, "sized.jar", bytes, null);
        var metadata = JsonNode.Parse(_responses["/v1/mods/111/files/222"])!;
        metadata["data"]!["fileLength"] = bytes.Length + 1;
        _responses["/v1/mods/111/files/222"] = JsonSerializer.SerializeToUtf8Bytes(metadata);
        SetMcbbsRemote(McbbsIndex(Curse(111, 222)));
        var before = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsUpdater().UpdateAsync(id));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "mods/sized.jar")));
        Assert.Equal(before, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
    }

    [Fact]
    public async Task McbbsUpdateRetainsEndpointWhenRemoteOmitsIt()
    {
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex()), "RetainedApi");
        var remote = McbbsIndex(TextAddon("config/test.txt", "updated"));
        remote.Remove("fileApi");
        SetMcbbsRemote(remote);
        Assert.Equal(1, (await McbbsUpdater().UpdateAsync(id)).Downloaded);
        Assert.Equal(0, (await McbbsUpdater().UpdateAsync(id)).Downloaded);
        var json = InstallManifestStore.Load(_root, id)!.ModpackManifestJson!;
        Assert.Equal("https://example.invalid/pack/", JsonNode.Parse(json)!["fileApi"]!.GetValue<string>());
    }

    [Fact]
    public async Task McbbsEmptyRemoteFileListDeletesOnlyUnmodifiedDeclaredFiles()
    {
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(
            McbbsIndex(TextAddon("config/old.txt", "old")),
            entries: [("config/old.txt", "old"), ("config/unlisted.txt", "user default")]), "EmptyUpdate");
        SetMcbbsRemote(McbbsIndex());
        var result = await McbbsUpdater().UpdateAsync(id);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(0, result.Downloaded);
        Assert.Empty(result.Preserved);
        var directory = _repository.GetVersionDirectory(id);
        Assert.False(File.Exists(Path.Combine(directory, "config/old.txt")));
        Assert.Equal("user default", await File.ReadAllTextAsync(Path.Combine(directory, "config/unlisted.txt")));
        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, id));
        Assert.False(File.Exists(Path.Combine(directory, "config/old.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    private JsonObject TextAddon(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        _responses["/pack/overrides/" + encoded] = bytes;
        return Addon(path, bytes);
    }

    private static JsonObject Curse(long project, long file, string? name = null, string? url = null) => new()
    {
        ["type"] = "curse",
        ["projectID"] = project,
        ["fileID"] = file,
        ["fileName"] = name,
        ["url"] = url is null ? null : "https://example.invalid" + url
    };

    private void SetMcbbsRemote(JsonObject remote)
    {
        _responses["/pack/manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(remote);
    }
}
