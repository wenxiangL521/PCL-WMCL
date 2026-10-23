using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using WMCL.Minecraft.Models;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("wrapped/")]
    public async Task MmcAppliesSelectedPatchesInOrderAndPreservesQuotedArgumentsAndAllTweakers(string root)
    {
        var early = MmcPatch("custom.early", -10);
        early["mainClass"] = "custom.Early";
        early["+jvmArgs"] = new JsonArray("-Dearly=two words");
        early["+tweakers"] = new JsonArray("custom.First", "custom.Second");
        var late = MmcPatch("custom.late", 30);
        late["mainClass"] = "custom.Main";
        late["minecraftArguments"] = "--username ${auth_player_name} --label \"two words\" --empty \"\" --folder 'C:\\pack files'";
        late["+jvmArgs"] = new JsonArray("-Dlate=true");
        late["+tweakers"] = new JsonArray("custom.Second", "custom.Third");
        late["compatibleJavaMajors"] = new JsonArray(17, 21);
        late["+traits"] = new JsonArray("FirstThreadOnMacOS");
        late["requires"] = new JsonArray(new JsonObject { ["uid"] = "custom.early", ["equals"] = "1" });
        late["arguments"] = JsonNode.Parse("""{"game":[{"rules":[{"action":"allow","os":{"name":"linux"}}],"value":["--linux","yes"]}],"jvm":["-Dmodern=true"]}""");
        var pack = MmcPack([late, early], root,
            ("patches/unselected.json", Encoding.UTF8.GetBytes("invalid unselected patch")),
            ("other/patches/custom.late.json", Encoding.UTF8.GetBytes("invalid unrelated patch")),
            ("minecraft/config/test.txt", Encoding.UTF8.GetBytes("pack config")));

        var id = await MmcService().InstallFromFileAsync(pack, "Patched");
        var version = await _repository.GetVersionAsync(id);
        Assert.Equal("custom.Main", version.MainClass);
        Assert.Equal(21, version.JavaVersion!.MajorVersion);
        Assert.Null(version.MinecraftArguments);
        var game = version.Arguments!.Game.SelectMany(argument => argument.Values).ToArray();
        Assert.Contains("two words", game);
        Assert.Contains("", game);
        Assert.Contains("C:\\pack files", game);
        Assert.Equal(["custom.First", "custom.Second", "custom.Third"],
            game.Select((value, index) => (value, index)).Where(item => item.value == "--tweakClass").Select(item => game[item.index + 1]));
        Assert.Equal(["-Dearly=two words", "-Dmodern=true", "-Dlate=true"],
            version.Arguments.Jvm.Where(argument => argument.IsLiteral).SelectMany(argument => argument.Values));
        Assert.Single(version.Arguments.Jvm, argument => argument.Values.Contains("-XstartOnFirstThread"));
        var plan = MmcLaunchPlan(id, version);
        Assert.Contains("two words", plan.Arguments);
        Assert.Contains("-Dearly=two words", plan.Arguments);
        Assert.All(plan.Arguments, argument => Assert.DoesNotContain("${", argument));
        Assert.Equal("pack config", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MmcLibrariesUseInstancePathsAndCanBeRepairedFromTheirArchive(bool fabric)
    {
        var content = Encoding.UTF8.GetBytes("custom local library");
        const string mavenPath = "org/example/custom/1/custom-1.jar";
        var shared = Path.Combine(_root, "libraries", mavenPath);
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "keep other instance library");
        var patch = MmcPatch(fabric ? "net.fabricmc.fabric-loader" : "custom.local", 10);
        if (fabric) patch["version"] = "0.28.0";
        patch["libraries"] = new JsonArray(new JsonObject
        {
            ["name"] = "org.example:custom:1",
            ["MMC-hint"] = "local",
            ["MMC-filename"] = "custom-bundled.jar",
            ["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["sha1"] = Hash(content), ["size"] = content.Length } }
        });
        var archive = MmcPack([patch], "wrapped/", ("libraries/custom-bundled.jar", content));
        var first = await MmcService().InstallFromFileAsync(archive, "Local");
        var second = await MmcService().InstallFromFileAsync(archive, "Local");
        foreach (var id in new[] { first, second })
        {
            var version = await _repository.GetVersionAsync(id);
            var library = LibraryResolver.ResolveArtifacts(version, RuleContext.CreateDefault()).Single(item => item.Name.Artifact == "custom");
            Assert.Equal("wmcl-packs/" + id + "/" + mavenPath, library.RelativePath);
            Assert.Null(library.Url);
            var plan = MmcLaunchPlan(id, version);
            Assert.Contains(Path.Combine(_root, "libraries", library.RelativePath), plan.Classpath);
            Assert.DoesNotContain(shared, plan.Classpath.Split(Path.PathSeparator));
            var manifest = InstallManifestStore.Load(_root, id)!;
            var managed = manifest.ManagedFiles.Single(file => file.RelativePath.EndsWith(mavenPath));
            Assert.Equal("wrapped/libraries/custom-bundled.jar", managed.ArchivePath);
            var target = Path.Combine(_root, managed.RelativePath);
            await File.WriteAllBytesAsync(target, new byte[content.Length]);
            Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
            Assert.Equal(content, await File.ReadAllBytesAsync(target));
            Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        }
        Assert.Equal("keep other instance library", await File.ReadAllTextAsync(shared));
        Assert.Equal("Local-2", second);
    }

    [Fact]
    public async Task MmcRemoteLibraryUsesItsRepositoryAndRepairsItsScopedCopy()
    {
        var content = Encoding.UTF8.GetBytes("remote custom library");
        const string mavenPath = "org/example/remote/2/remote-2.jar";
        _responses["/maven/" + mavenPath] = content;
        var patch = MmcPatch("custom.remote", 0);
        patch["+libraries"] = new JsonArray(new JsonObject { ["name"] = "org.example:remote:2", ["url"] = "https://example.invalid/maven/" });
        var id = await MmcService().InstallFromFileAsync(MmcPack([patch]), "Remote");
        var manifest = InstallManifestStore.Load(_root, id)!;
        var file = manifest.ManagedFiles.Single(item => item.Component == "MMC-library");
        Assert.Equal("libraries/wmcl-packs/Remote/" + mavenPath, file.RelativePath);
        Assert.Equal("https://example.invalid/maven/" + mavenPath, file.Url);
        Assert.Null(file.ArchivePath);
        Assert.Equal(Hash(content), file.Sha1);
        await File.WriteAllBytesAsync(Path.Combine(_root, file.RelativePath), new byte[content.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_root, file.RelativePath)));
        Assert.Equal(2, _requests.Count(request => request == "/maven/" + mavenPath));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task MmcKeepsPlatformVariantsAndInstallsAndRepairsNativeOnlyLibraries()
    {
        var native = MmcNativeArchive();
        var os = RuleContext.GetOsName();
        var patch = MmcPatch("custom.native", 0);
        patch["libraries"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "org.example:native:1",
                ["natives"] = new JsonObject { [os] = "natives-64" },
                ["downloads"] = new JsonObject
                {
                    ["classifiers"] = new JsonObject
                    {
                        ["natives-64"] = new JsonObject { ["sha1"] = Hash(native), ["size"] = native.Length }
                    }
                },
                ["extract"] = new JsonObject { ["exclude"] = new JsonArray("excluded/") }
            },
            MmcPlatformLibrary("linux"), MmcPlatformLibrary("windows"), MmcPlatformLibrary("osx"));
        foreach (var platform in new[] { "linux", "windows", "osx" })
            _responses["/" + platform + ".jar"] = Encoding.UTF8.GetBytes(platform);
        var pack = MmcPack([patch], "", ("libraries/org/example/native/1/native-1-natives-64.jar", native));
        var id = await MmcService().InstallFromFileAsync(pack, "Native");
        var version = await _repository.GetVersionAsync(id);
        foreach (var platform in new[] { "linux", "windows", "osx" })
        {
            var libraries = LibraryResolver.ResolveArtifacts(version, new RuleContext(platform, "x86_64", "", new Dictionary<string, bool>()));
            var variant = Assert.Single(libraries, item => item.Name.Artifact == "platform");
            Assert.EndsWith(platform + ".jar", variant.RelativePath);
            Assert.DoesNotContain(libraries, item => item.Name.Artifact == "native");
        }
        Assert.Equal("native fixture", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "natives", "test.native")));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "natives", "excluded", "test.native")));
        var managed = InstallManifestStore.Load(_root, id)!.ManagedFiles.Single(file => file.RelativePath.EndsWith("native-1-natives-64.jar"));
        await File.WriteAllTextAsync(Path.Combine(_root, managed.RelativePath), "bad zip");
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id));
        await new VersionInstaller(_repository, new DownloadManager(_http)).InstallAsync(id, new InstallOptions { VerifyExisting = true });
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task MmcTopLevelLibrariesCanSupplyInheritedArtifactsWithoutOverwritingSharedFiles()
    {
        var bytes = Encoding.UTF8.GetBytes("inherited library");
        const string path = "org/example/inherited/1/inherited-1.jar";
        var original = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json")))!;
        original["libraries"] = new JsonArray(new JsonObject
        {
            ["name"] = "org.example:inherited:1",
            ["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["path"] = path, ["url"] = "https://example.invalid/inherited.jar", ["sha1"] = Hash(bytes) } }
        });
        await File.WriteAllTextAsync(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json"), original.ToJsonString());
        _responses["/inherited.jar"] = bytes;
        var id = await MmcService().InstallFromFileAsync(MmcPack([], "", ("libraries/" + path, bytes)), "Inherited");
        var library = Assert.Single(LibraryResolver.ResolveArtifacts(await _repository.GetVersionAsync(id), RuleContext.CreateDefault()));
        Assert.Equal("wmcl-packs/Inherited/" + path, library.RelativePath);
        Assert.Null(library.Url);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "libraries", path)));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("uid")]
    [InlineData("version")]
    [InlineData("formatVersion")]
    [InlineData("order")]
    [InlineData("requires")]
    [InlineData("javaVersion")]
    [InlineData("compatibleJavaMajors")]
    [InlineData("arguments")]
    [InlineData("minecraftArguments")]
    [InlineData("+jvmArgs")]
    [InlineData("assetIndex")]
    [InlineData("mainClass")]
    public async Task MmcRejectsInvalidPatchMetadataBeforeInstalling(string field)
    {
        var patch = MmcPatch("custom.invalid", 0);
        patch[field] = field switch
        {
            "uid" => "wrong.uid",
            "version" => "2",
            "formatVersion" => 2,
            "order" => "wrong",
            "requires" => new JsonArray(new JsonObject { ["uid"] = "missing", ["equals"] = "1" }),
            "javaVersion" => new JsonObject { ["majorVersion"] = 0 },
            "compatibleJavaMajors" => new JsonArray(0),
            "arguments" => new JsonObject { ["game"] = null },
            "minecraftArguments" => "--label \"unclosed",
            "+jvmArgs" => new JsonArray(42),
            "assetIndex" => new JsonObject { ["id"] = "../escape", ["url"] = "https://example.invalid/index" },
            _ => ""
        };
        var index = MmcIndex(("custom.invalid", "1"));
        var pack = MmcPack([], "", [("patches/custom.invalid.json", JsonSerializer.SerializeToUtf8Bytes(patch))], index);
        var exception = await Record.ExceptionAsync(() => MmcService().InstallFromFileAsync(pack, "Invalid"));
        Assert.IsType<InvalidDataException>(exception);
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData("mainJar")]
    [InlineData("jarMods")]
    [InlineData("mavenFiles")]
    [InlineData("-libraries")]
    [InlineData("-tweakers")]
    [InlineData("-jvmArgs")]
    [InlineData("+traits")]
    public async Task MmcUnsupportedPatchSemanticsFailExplicitly(string field)
    {
        var patch = MmcPatch("custom.unsupported", 0);
        patch[field] = new JsonArray("unsupported");
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => MmcService().InstallFromFileAsync(MmcPack([patch]), "Invalid"));
        Assert.Contains(field == "+traits" ? "unsupported" : field, exception.Message);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("case")]
    [InlineData("file-directory")]
    [InlineData("symlink")]
    [InlineData("missing-local")]
    [InlineData("conflicting-hash")]
    public async Task MmcRejectsUnsafeOrConflictingLibraryPlansBeforeInstalling(string mode)
    {
        var patch = MmcPatch("custom.library", 0);
        patch["libraries"] = new JsonArray(new JsonObject { ["name"] = "org.example:custom:1", ["MMC-hint"] = "local", ["MMC-filename"] = "custom.jar" });
        var files = new List<(string Path, byte[] Content)>();
        if (mode != "missing-local") files.Add((mode == "traversal" ? "libraries/../custom.jar" : "libraries/custom.jar", [1]));
        if (mode == "duplicate") files.Add(("libraries/custom.jar", [2]));
        if (mode == "case") files.Add(("libraries/CUSTOM.jar", [2]));
        if (mode == "file-directory") files.Add(("libraries/custom.jar/child", [2]));
        if (mode == "conflicting-hash")
        {
            var first = patch["libraries"]![0]!.AsObject();
            first["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["sha1"] = new string('0', 40) } };
            var duplicate = first.DeepClone();
            duplicate["downloads"]!["artifact"]!["sha1"] = new string('1', 40);
            patch["libraries"]!.AsArray().Add(duplicate);
        }
        var pack = MmcPack([patch], "", files.ToArray());
        if (mode == "symlink")
        {
            using var archive = ZipFile.Open(pack, ZipArchiveMode.Update);
            archive.GetEntry("libraries/custom.jar")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => MmcService().InstallFromFileAsync(pack, "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MmcBundledLibraryIntegrityMismatchNeverProducesASuccessManifest(bool size)
    {
        var patch = MmcPatch("custom.corrupt", 0);
        patch["libraries"] = new JsonArray(new JsonObject
        {
            ["name"] = "org.example:custom:1",
            ["MMC-hint"] = "local",
            ["MMC-filename"] = "custom.jar",
            ["downloads"] = new JsonObject
            {
                ["artifact"] = size
                    ? new JsonObject { ["size"] = 100 }
                    : new JsonObject { ["sha1"] = new string('0', 40) }
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => MmcService().InstallFromFileAsync(
            MmcPack([patch], "", ("libraries/custom.jar", [1])), "Corrupt"));
        Assert.Null(InstallManifestStore.Load(_root, "Corrupt"));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Corrupt")));
        Assert.Empty(_requests);
    }

    private LocalModpackInstaller MmcService()
    {
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        return new LocalModpackInstaller(_repository, installer,
            new FabricInstaller(_repository, installer, http: _http), downloads: downloads);
    }

    private string MmcPack(JsonObject[] patches, string root = "", params (string Path, byte[] Content)[] files) =>
        MmcPack(patches, root, files, null);

    private string MmcPack(JsonObject[] patches, string root, (string Path, byte[] Content)[] files, JsonObject? index)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        index ??= MmcIndex(patches.Select(patch =>
            (patch["uid"]!.GetValue<string>(), patch["version"]!.GetValue<string>())).ToArray());
        using (var target = archive.CreateEntry(root + "mmc-pack.json").Open())
            target.Write(JsonSerializer.SerializeToUtf8Bytes(index));
        foreach (var patch in patches)
        {
            using var target = archive.CreateEntry(root + "patches/" + patch["uid"]!.GetValue<string>() + ".json").Open();
            target.Write(JsonSerializer.SerializeToUtf8Bytes(patch));
        }
        foreach (var file in files)
        {
            using var target = archive.CreateEntry(root + file.Path).Open();
            target.Write(file.Content);
        }
        return path;
    }

    private static JsonObject MmcPatch(string uid, int order) => new()
    {
        ["formatVersion"] = 1,
        ["uid"] = uid,
        ["version"] = "1",
        ["order"] = order
    };

    private static JsonObject MmcIndex(params (string Uid, string Version)[] components) => new()
    {
        ["formatVersion"] = 1,
        ["components"] = new JsonArray(new[] { ("net.minecraft", "1.20.1") }.Concat(components)
            .Select(component => (JsonNode)new JsonObject
            {
                ["uid"] = component.Item1,
                ["version"] = component.Item2
            }).ToArray())
    };

    private static JsonObject MmcPlatformLibrary(string os) => new()
    {
        ["name"] = "org.example:platform:1",
        ["rules"] = new JsonArray(new JsonObject
        {
            ["action"] = "allow",
            ["os"] = new JsonObject { ["name"] = os }
        }),
        ["downloads"] = new JsonObject
        {
            ["artifact"] = new JsonObject
            {
                ["path"] = "platform/" + os + ".jar",
                ["url"] = "https://example.invalid/" + os + ".jar"
            }
        }
    };

    private static byte[] MmcNativeArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var path in new[] { "test.native", "excluded/test.native", "META-INF/test" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write("native fixture");
            }
        return buffer.ToArray();
    }

    private LaunchPlan MmcLaunchPlan(string id, VersionJson version)
    {
        return LaunchPlanBuilder.Build(new LaunchRequest
        {
            Version = version,
            VersionId = id,
            GameDirectory = _repository.GetVersionDirectory(id),
            MinecraftDirectory = _root,
            Java = new JavaRuntime("java", new Version(21, 0), 21, "x86_64", _root, null),
            Account = AccountInfo.Offline("Test")
        });
    }
}
