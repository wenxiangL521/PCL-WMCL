using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Fact]
    public async Task McbbsImportsExtraLibrariesAndLaunchConstraintsIntoTheInstance()
    {
        var library = Encoding.UTF8.GetBytes("mcbbs extra library");
        _responses["/extra.jar"] = library;
        var index = McbbsIndex();
        index["libraries"] = new JsonArray(new JsonObject
        {
            ["name"] = "org.test:extra:1",
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["url"] = "https://example.invalid/extra.jar",
                    ["sha1"] = Hash(library),
                    ["size"] = library.Length
                }
            }
        });
        index["launchInfo"] = new JsonObject
        {
            ["minMemory"] = 6144,
            ["supportJava"] = new JsonArray(17, 21),
            ["javaArgument"] = new JsonArray("-Dpack=true"),
            ["launchArgument"] = new JsonArray("--fullscreen")
        };
        index["serverInfo"] = new JsonObject
        {
            ["authlibInjectorServer"] = "https://example.invalid/api/yggdrasil"
        };
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "ExtraLibraries");
        var libraryPath = Path.Combine(_root, "libraries", "wmcl-packs", id, "org", "test", "extra", "1", "extra-1.jar");
        Assert.Equal(library, await File.ReadAllBytesAsync(libraryPath));
        var version = await _repository.GetVersionAsync(id);
        Assert.Contains(version.Libraries, item => item.Name == "org.test:extra:1");
        var settings = InstanceSettingsStore.Load(_repository.GetVersionDirectory(id));
        Assert.Equal(6144, settings.MemoryMb);
        Assert.Equal("-Dpack=true", settings.ExtraJvmArgs);
        Assert.Equal("--fullscreen", settings.ExtraGameArgs);
        Assert.Equal("https://example.invalid/api/yggdrasil", settings.AuthServer);
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.Contains(manifest.ManagedFiles, item => item.Component == "MCBBS-library" &&
            item.RelativePath == "libraries/wmcl-packs/" + id + "/org/test/extra/1/extra-1.jar");
        Assert.Equal(6144, manifest.McbbsMinMemoryMb);
        Assert.Equal(new[] { 17, 21 }, manifest.McbbsSupportJava);
        Assert.Equal("https://example.invalid/api/yggdrasil", manifest.McbbsAuthlibInjectorServer);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("", "mcbbs.packmeta", false)]
    [InlineData("wrapped/", "manifest.json", false)]
    [InlineData("wrapped/", "mcbbs.packmeta", true)]
    public async Task McbbsImportsBundledAndRemoteFilesWithFinalHashesAndIndependentIdentity(string root, string metadata, bool quilt)
    {
        var mod = Encoding.UTF8.GetBytes("curse mod");
        var remote = Encoding.UTF8.GetBytes("remote configuration");
        var bundled = Encoding.UTF8.GetBytes("bundled file");
        CfFileResponse(111, 222, "mod.jar", mod, null);
        _responses["/pack/overrides/config/remote.txt"] = remote;
        var index = McbbsIndex(new JsonObject { ["type"] = "curse", ["projectID"] = 111, ["fileID"] = 222 },
            Addon("config/remote.txt", remote), Addon("config/bundled.txt", bundled));
        if (quilt) index["addons"]!.AsArray().Add(new JsonObject { ["id"] = "quilt", ["version"] = "0.28.0" });
        var existing = _repository.GetVersionDirectory("quilt-loader-0.28.0-1.20.1");
        Directory.CreateDirectory(existing);
        var sentinel = Path.Combine(existing, "custom.json");
        await File.WriteAllTextAsync(sentinel, "preserve profile");
        var path = McbbsPack(index, root, metadata, ("config/bundled.txt", "bundled file"));
        var id = await McbbsService().InstallFromFileAsync(path, "1.20.1");
        var directory = _repository.GetVersionDirectory(id);
        Assert.NotEqual("1.20.1", id);
        Assert.Equal(mod, await File.ReadAllBytesAsync(Path.Combine(directory, "mods", "mod.jar")));
        Assert.Equal(remote, await File.ReadAllBytesAsync(Path.Combine(directory, "config", "remote.txt")));
        Assert.Equal(bundled, await File.ReadAllBytesAsync(Path.Combine(directory, "config", "bundled.txt")));
        Assert.Equal("preserve profile", await File.ReadAllTextAsync(sentinel));
        var version = await _repository.GetVersionAsync(id);
        Assert.Null(version.InheritsFrom);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal(quilt ? "quilt" : "vanilla", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(directory, IsolationPolicy.GetGameDirectory(_root, id, version, IsolationPolicy.Global));
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.Equal(2, manifest.ManagedFiles.Count(file => file.Component == "MCBBS"));
        Assert.Contains(manifest.ManagedFiles, file => file.Component == "overrides" && file.RelativePath.EndsWith("/config/bundled.txt"));
        Assert.All(manifest.ManagedFiles.Where(file => file.Component == "MCBBS"), file => Assert.NotNull(file.Url));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.DoesNotContain("/pack/overrides/config/bundled.txt", _requests);
        Assert.DoesNotContain("/pack/manifest.json", _requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task McbbsCurseFilesUseExplicitUrlsAndBundledOverridesWithoutAnApiKey(bool bundled)
    {
        _responses["/direct.jar"] = Encoding.UTF8.GetBytes("remote mod");
        var index = McbbsIndex(new JsonObject
        {
            ["type"] = "curse",
            ["projectID"] = 111,
            ["fileID"] = 222,
            ["fileName"] = "custom.jar",
            ["url"] = bundled ? null : "https://example.invalid/direct.jar"
        });
        var path = McbbsPack(index, entries: bundled ? [("mods/custom.jar", "local override")] : []);
        var id = await McbbsService(configured: false).InstallFromFileAsync(path, "Direct");
        Assert.Equal(bundled ? "local override" : "remote mod",
            await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "mods", "custom.jar")));
        Assert.DoesNotContain(_requests, request => request.Contains("/mods/111/"));
        if (bundled) Assert.DoesNotContain("/direct.jar", _requests);
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.RelativePath.EndsWith("/mods/custom.jar"));
        Assert.Equal(bundled ? "overrides" : "MCBBS", managed.Component);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task McbbsCompletesMissingCurseFilenameOrUrlUsingPinnedMetadata(bool nameKnown)
    {
        var bytes = Encoding.UTF8.GetBytes("pinned curse content");
        CfFileResponse(111, 222, "original.jar", bytes, null);
        _responses["/alternate.jar"] = bytes;
        var index = McbbsIndex(new JsonObject
        {
            ["type"] = "curse",
            ["projectID"] = 111,
            ["fileID"] = 222,
            ["fileName"] = nameKnown ? "renamed.jar" : null,
            ["url"] = nameKnown ? null : "https://example.invalid/alternate.jar"
        });
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "Pinned");
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.Component == "MCBBS");
        Assert.EndsWith(nameKnown ? "/mods/renamed.jar" : "/mods/original.jar", managed.RelativePath);
        Assert.Equal(Hash(bytes), managed.Sha1);
        Assert.Contains("/v1/mods/111/files/222", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task McbbsReplacesCorruptBundledAddonAndKeepsOverrideRepairFromRestoringIt()
    {
        var bytes = Encoding.UTF8.GetBytes("correct bytes");
        _responses["/pack/overrides/config/test.txt"] = bytes;
        var index = McbbsIndex(Addon("config/test.txt", bytes));
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index, entries: [("config/test.txt", "corrupt bytes")]), "Repaired");
        var destination = Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(0, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.Component == "MCBBS");
        Assert.Equal(Hash(bytes), managed.Sha1);
        Assert.DoesNotContain(InstallManifestStore.Load(_root, id)!.ManagedFiles, file => file.Component == "overrides");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task McbbsEncodesRemotePathsWithoutChangingTheInstancePath()
    {
        var bytes = Encoding.UTF8.GetBytes("configuration");
        var relative = "config/选项 #1%.txt";
        _responses["/pack/overrides/config/%E9%80%89%E9%A1%B9%20%231%25.txt"] = bytes;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex(Addon(relative, bytes))), "Encoded");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_repository.GetVersionDirectory(id), relative)));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("files", "null")]
    [InlineData("files", "{}")]
    [InlineData("files", "[null]")]
    [InlineData("files", "[{\"type\":\"addon\",\"path\":\"../outside\",\"hash\":\"0000000000000000000000000000000000000000\"}]")]
    [InlineData("files", "[{\"type\":\"addon\",\"path\":\"WMCL/install.json\",\"hash\":\"0000000000000000000000000000000000000000\"}]")]
    [InlineData("files", "[{\"type\":\"addon\",\"path\":\"config/a\",\"hash\":\"bad\"}]")]
    [InlineData("files", "[{\"type\":\"curse\",\"projectID\":0,\"fileID\":222}]")]
    [InlineData("files", "[{\"type\":\"curse\",\"projectID\":111,\"fileID\":222,\"fileName\":\"../escape.jar\"}]")]
    [InlineData("files", "[{\"type\":\"curse\",\"projectID\":111,\"fileID\":222,\"url\":\"file:///tmp/a\"}]")]
    [InlineData("files", "[{\"type\":\"curse\",\"projectID\":111,\"fileID\":222,\"force\":\"false\"}]")]
    [InlineData("fileApi", "\"file:///tmp/root\"")]
    [InlineData("fileApi", "\"https://example.invalid/root?query\"")]
    [InlineData("manifestVersion", "2")]
    [InlineData("manifestType", "\"otherGame\"")]
    public async Task McbbsRejectsMalformedContentBeforeAnyNetworkOrInstanceCreation(string field, string value)
    {
        var index = McbbsIndex();
        index[field] = JsonNode.Parse(value);
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsService().InstallFromFileAsync(McbbsPack(index), "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Fact]
    public async Task McbbsRejectsUnknownContentTypesInsteadOfReportingIncompleteSuccess()
    {
        var index = McbbsIndex(new JsonObject { ["type"] = "unknown" });
        await Assert.ThrowsAsync<NotSupportedException>(() => McbbsService().InstallFromFileAsync(McbbsPack(index), "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsMissingOrCorruptAddonWithoutFileApiFailsBeforeInstalling(bool bundled)
    {
        var index = McbbsIndex(Addon("config/test.txt", Encoding.UTF8.GetBytes("expected")));
        index.Remove("fileApi");
        var path = McbbsPack(index, entries: bundled ? [("config/test.txt", "corrupt")] : []);
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsService().InstallFromFileAsync(path, "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData("duplicate-addon")]
    [InlineData("duplicate-curse")]
    [InlineData("addon-curse")]
    [InlineData("case-mismatch")]
    public async Task McbbsRejectsAmbiguousPathsAndIdentities(string problem)
    {
        var bytes = Encoding.UTF8.GetBytes("content");
        var addon = Addon("mods/same.jar", bytes);
        var curse = new JsonObject
        {
            ["type"] = "curse",
            ["projectID"] = 111,
            ["fileID"] = 222,
            ["fileName"] = "same.jar",
            ["url"] = "https://example.invalid/same.jar"
        };
        var index = problem switch
        {
            "duplicate-addon" => McbbsIndex(addon, Addon("mods/SAME.jar", bytes)),
            "duplicate-curse" => McbbsIndex(curse, curse.DeepClone().AsObject()),
            "addon-curse" => McbbsIndex(addon, curse),
            _ => McbbsIndex(addon)
        };
        var path = McbbsPack(index, entries: problem == "case-mismatch" ? [("mods/SAME.jar", "content")] : []);
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsService().InstallFromFileAsync(path, "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Fact]
    public async Task McbbsCancellingRemoteContentDoesNotSaveASuccessManifest()
    {
        var bytes = Encoding.UTF8.GetBytes("remote content");
        _responses["/pack/overrides/config/test.txt"] = bytes;
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(update =>
        {
            if (update.Stage == "下载 MCBBS 整合包文件") cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McbbsService().InstallFromFileAsync(
            McbbsPack(McbbsIndex(Addon("config/test.txt", bytes))), "Cancelled", progress, cancellation.Token));
        Assert.Null(InstallManifestStore.Load(_root, "Cancelled"));
        Assert.DoesNotContain("/pack/overrides/config/test.txt", _requests);
    }

    [Fact]
    public async Task McbbsAddonDownloadMustMatchTheDeclaredHash()
    {
        _responses["/pack/overrides/config/test.txt"] = Encoding.UTF8.GetBytes("wrong");
        var index = McbbsIndex(Addon("config/test.txt", Encoding.UTF8.GetBytes("correct")));
        await Assert.ThrowsAsync<DownloadException>(() => McbbsService().InstallFromFileAsync(McbbsPack(index), "Invalid"));
        Assert.Null(InstallManifestStore.Load(_root, "Invalid"));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory("Invalid"), "config", "test.txt")));
    }

    [Fact]
    public async Task McbbsRemoteMetadataAndSettingsBelongToTheSelectedPackRoot()
    {
        var bytes = Encoding.UTF8.GetBytes("correct");
        _responses["/direct.jar"] = bytes;
        var selected = McbbsIndex(new JsonObject
        {
            ["type"] = "curse",
            ["projectID"] = 111,
            ["fileID"] = 222,
            ["fileName"] = "selected.jar",
            ["url"] = "https://example.invalid/direct.jar"
        });
        selected["launchInfo"] = new JsonObject { ["javaArgument"] = new JsonArray("-Dselected=true") };
        var path = McbbsPack(selected, "wrapped/");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("wrapped/manifest.json").Open());
            var other = McbbsIndex(new JsonObject { ["type"] = "unknown" });
            other["launchInfo"] = new JsonObject { ["javaArgument"] = new JsonArray("-Dwrong=true") };
            writer.Write(other.ToJsonString());
        }
        var id = await McbbsService(configured: false).InstallFromFileAsync(path, "Selected");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_repository.GetVersionDirectory(id), "mods", "selected.jar")));
        Assert.Equal("-Dselected=true", InstanceSettingsStore.Load(_repository.GetVersionDirectory(id)).ExtraJvmArgs);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task McbbsMissingCurseMetadataFailsBeforeCreatingAnInstance()
    {
        var index = McbbsIndex(new JsonObject { ["type"] = "curse", ["projectID"] = 111, ["fileID"] = 222 });
        var exception = await Assert.ThrowsAsync<CurseForgeFileUnavailableException>(() =>
            McbbsService().InstallFromFileAsync(McbbsPack(index), "Missing"));
        Assert.Equal(111, exception.ProjectId);
        Assert.Equal(222, exception.FileId);
        Assert.Contains("/v1/mods/111/files/222", _requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Missing")));
        Assert.Null(InstallManifestStore.Load(_root, "Missing"));
    }

    [Fact]
    public async Task McbbsContentCannotUseAnOverrideFileAsADirectory()
    {
        var index = McbbsIndex(Addon("config/test.txt", Encoding.UTF8.GetBytes("remote")));
        await Assert.ThrowsAsync<InvalidDataException>(() => McbbsService().InstallFromFileAsync(
            McbbsPack(index, entries: [("config", "file instead of directory")]), "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    private LocalModpackInstaller McbbsService(bool configured = true)
    {
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        return new LocalModpackInstaller(_repository, installer,
            new FabricInstaller(_repository, installer, http: _http),
            quilt: new FabricInstaller(_repository, installer, FabricFamilyKind.Quilt, _http),
            curseForge: new CurseForgeClient(configured ? "key" : null, _http), downloads: downloads);
    }

    private static JsonObject Addon(string path, byte[] bytes) => new()
    {
        ["type"] = "addon",
        ["path"] = path,
        ["hash"] = Hash(bytes),
        ["force"] = false
    };

    private static JsonObject McbbsIndex(params JsonObject[] files) => new()
    {
        ["manifestType"] = "minecraftModpack",
        ["manifestVersion"] = 1,
        ["name"] = "MCBBS content",
        ["addons"] = new JsonArray(new JsonObject { ["id"] = "game", ["version"] = "1.20.1" }),
        ["fileApi"] = "https://example.invalid/pack/",
        ["files"] = new JsonArray(files.Select(file => (JsonNode)file).ToArray())
    };

    private string McbbsPack(JsonObject index, string root = "", string metadata = "mcbbs.packmeta",
        params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(root + metadata).Open())) writer.Write(index.ToJsonString());
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(root + "overrides/" + entry.Path).Open());
            writer.Write(entry.Content);
        }
        return path;
    }
}
