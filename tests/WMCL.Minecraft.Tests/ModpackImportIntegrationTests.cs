using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modpack;
using WMCL.Modding.Modrinth;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-pack-import-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentDictionary<string, byte[]> _responses = new();
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly HttpClient _http;
    private readonly VersionRepository _repository;
    private readonly ModpackInstaller _packs;
    private readonly byte[] _client = Encoding.UTF8.GetBytes("client jar fixture");

    public ModpackImportIntegrationTests()
    {
        _http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            _requests.Enqueue(path);
            if (_responses.TryGetValue(path, out var bytes))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        _repository = new VersionRepository(_root);
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        _packs = new ModpackInstaller(_repository, installer,
            new FabricInstaller(_repository, installer, FabricFamilyKind.Fabric, _http),
            quilt: new FabricInstaller(_repository, installer, FabricFamilyKind.Quilt, _http), downloads: downloads,
            archiveCacheDirectory: Path.Combine(_root, "cache"));
        Directory.CreateDirectory(_repository.GetVersionDirectory("1.20.1"));
        File.WriteAllText(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json"), JsonSerializer.Serialize(new
        {
            id = "1.20.1",
            type = "release",
            mainClass = "net.minecraft.client.main.Main",
            arguments = new { game = new[] { "--username", "${auth_player_name}" } },
            libraries = Array.Empty<object>(),
            downloads = new { client = new { url = "https://example.invalid/client.jar", sha1 = Hash(_client), size = _client.Length } }
        }));
        _responses["/client.jar"] = _client;
        foreach (var kind in new[] { "fabric", "quilt" })
        {
            var library = Encoding.UTF8.GetBytes(kind + " loader jar fixture");
            _responses[$"/{kind}.jar"] = library;
            _responses[$"/v{(kind == "quilt" ? 3 : 2)}/versions/loader/1.20.1"] = Encoding.UTF8.GetBytes(
                """[{"loader":{"version":"0.28.0","stable":true}}]""");
            _responses[$"/v{(kind == "quilt" ? 3 : 2)}/versions/loader/1.20.1/0.28.0/profile/json"] =
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    id = kind + "-loader-0.28.0-1.20.1",
                    inheritsFrom = "1.20.1",
                    mainClass = kind == "quilt" ? "org.quiltmc.loader.impl.launch.knot.KnotClient" : "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    libraries = new[] { new
                    {
                        name = kind == "quilt" ? "org.quiltmc:quilt-loader:0.28.0" : "net.fabricmc:fabric-loader:0.28.0",
                        downloads = new { artifact = new { path = kind + "/loader.jar", url = "https://example.invalid/" + kind + ".jar", sha1 = Hash(library), size = library.Length } }
                    } }
                });
        }
    }

    [Theory]
    [InlineData("quilt")]
    [InlineData("fabric")]
    public async Task ImportsExportsAndReimportsLoaderPackWithoutTouchingExistingProfiles(string kind)
    {
        var existing = _repository.GetVersionDirectory(kind + "-loader-0.28.0-1.20.1");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "user.txt"), "keep existing loader");
        var path = Pack(Index(kind + "-loader"), ("overrides/config/test.txt", "configuration"));
        var first = await _packs.InstallFromFileAsync(path, "Pack");
        var version = await _repository.GetVersionAsync(first);
        Assert.Equal(kind, MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion(first, version));
        Assert.Null(version.InheritsFrom);
        Assert.Contains(kind == "quilt" ? "org.quiltmc" : "net.fabricmc", version.MainClass);
        Assert.Equal(_client, await File.ReadAllBytesAsync(Path.Combine(_repository.GetVersionDirectory(first), first + ".jar")));
        Assert.Equal(_repository.GetVersionDirectory(first), IsolationPolicy.GetGameDirectory(_root, first, version, IsolationPolicy.Global));
        var manifest = InstallManifestStore.Load(_root, first)!;
        Assert.Equal("0.28.0", manifest.Components[kind]);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, first)).IsHealthy);

        var exported = Path.Combine(_root, "exported.mrpack");
        await ModpackExporter.ExportAsync(_root, first, exported);
        var second = await _packs.InstallFromFileAsync(exported, "Pack");
        Assert.Equal("Pack-2", second);
        Assert.Equal("configuration", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(second), "config", "test.txt")));
        Assert.Equal(kind, MinecraftVersionUtil.GetLoader(await _repository.GetVersionAsync(second)));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, second)).IsHealthy);
        Assert.Equal("keep existing loader", await File.ReadAllTextAsync(Path.Combine(existing, "user.txt")));
        Assert.Single(Directory.GetFiles(existing));
    }

    [Theory]
    [InlineData("mmc", true)]
    [InlineData("mmc", false)]
    [InlineData("mcbbs", true)]
    [InlineData("mcbbs", false)]
    [InlineData("curseforge", true)]
    [InlineData("curseforge", false)]
    public async Task OtherPackFormatsKeepDefaultLoaderProfilesAndUseIndependentInstances(string format, bool existingLoader)
    {
        const string sourceId = "fabric-loader-0.28.0-1.20.1";
        var source = _repository.GetVersionDirectory(sourceId);
        if (existingLoader)
        {
            Directory.CreateDirectory(Path.Combine(source, "saves"));
            await File.WriteAllTextAsync(Path.Combine(source, sourceId + ".json"), "existing custom profile");
            await File.WriteAllTextAsync(Path.Combine(source, "saves", "world.txt"), "keep world");
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "launcher_profiles.json"), "keep launcher profiles");
        var path = Path.Combine(_root, format + ".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var metadata = format switch
            {
                "mmc" => (Name: "mmc-pack.json", Content: """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.28.0"}]}"""),
                "mcbbs" => (Name: "mcbbs.packmeta", Content: """{"addons":[{"id":"game","version":"1.20.1"},{"id":"fabric","version":"0.28.0"}]}"""),
                _ => (Name: "manifest.json", Content: """{"minecraft":{"version":"1.20.1","modLoaders":[{"id":"fabric-0.28.0","primary":true}]},"files":[],"overrides":"overrides"}""")
            };
            using (var writer = new StreamWriter(archive.CreateEntry(metadata.Name).Open())) writer.Write(metadata.Content);
            var prefix = format == "mmc" ? "minecraft/" : "overrides/";
            using (var writer = new StreamWriter(archive.CreateEntry(prefix + "config/test.txt").Open())) writer.Write("pack config");
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var fabric = new FabricInstaller(_repository, installer, http: _http);
        var id = format == "curseforge"
            ? await new CurseForgeModpackInstaller(_repository, installer, fabric, isolationMode: IsolationPolicy.Global).InstallFromFileAsync(path, "Imported")
            : await new LocalModpackInstaller(_repository, installer, fabric, isolationMode: IsolationPolicy.Global).InstallFromFileAsync(path, "Imported");
        var version = await _repository.GetVersionAsync(id);
        Assert.Null(version.InheritsFrom);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal("fabric", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(_repository.GetVersionDirectory(id), IsolationPolicy.GetGameDirectory(_root, id, version, IsolationPolicy.Global));
        Assert.Equal("pack config", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal("keep launcher profiles", await File.ReadAllTextAsync(Path.Combine(_root, "launcher_profiles.json")));
        if (existingLoader)
        {
            Assert.Equal("existing custom profile", await File.ReadAllTextAsync(Path.Combine(source, sourceId + ".json")));
            Assert.Equal("keep world", await File.ReadAllTextAsync(Path.Combine(source, "saves", "world.txt")));
        }
        else Assert.False(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(_root, "config")));
    }

    [Theory]
    [InlineData("mmc-quilt", "quilt", true)]
    [InlineData("mmc-quilt", "quilt", false)]
    [InlineData("mcbbs-quilt", "quilt", true)]
    [InlineData("mcbbs-quilt", "quilt", false)]
    [InlineData("hmcl-patched", "quilt", true)]
    [InlineData("hmcl-patched", "quilt", false)]
    [InlineData("hmcl-flattened", "fabric", true)]
    [InlineData("hmcl-flattened", "fabric", false)]
    [InlineData("hmcl-vanilla", "vanilla", false)]
    public async Task LocalQuiltAndHmclImportsInstallDeclaredComponentsAndRepairOnlyContent(string format, string kind, bool existingLoader)
    {
        var sourceId = kind + "-loader-0.28.0-1.20.1";
        var source = _repository.GetVersionDirectory(sourceId);
        if (existingLoader)
        {
            Directory.CreateDirectory(Path.Combine(source, "saves"));
            await File.WriteAllTextAsync(Path.Combine(source, sourceId + ".json"), "keep profile");
            await File.WriteAllTextAsync(Path.Combine(source, "saves", "world.txt"), "keep world");
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "launcher_profiles.json"), "keep launcher profiles");
        var path = Path.Combine(_root, format + ".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var metadata = format switch
            {
                "mmc-quilt" => (Name: "mmc-pack.json", Content: """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"org.quiltmc.quilt-loader","version":"0.28.0"}]}"""),
                "mcbbs-quilt" => (Name: "manifest.json", Content: """{"addons":{"game":"1.20.1","quilt":"0.28.0"},"launchInfo":{"javaArgument":["-Dpack=right"]}}"""),
                _ => (Name: "modpack.json", Content: """{"name":"HMCL Test","gameVersion":"intentionally-wrong"}""")
            };
            using (var writer = new StreamWriter(archive.CreateEntry("wrapped/" + metadata.Name).Open())) writer.Write(metadata.Content);
            var prefix = format == "mcbbs-quilt" ? "overrides/" : "minecraft/";
            using (var writer = new StreamWriter(archive.CreateEntry("wrapped/" + prefix + "config/test.txt").Open())) writer.Write("correct pack config");
            if (format.StartsWith("hmcl", StringComparison.Ordinal))
            {
                var profile = format switch
                {
                    "hmcl-patched" => """{"jar":"1.20.1","patches":[{"id":"game","version":"1.20.1","priority":0},{"id":"quilt","version":"0.28.0","priority":30000}]}""",
                    "hmcl-flattened" => """{"jar":"1.20.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.28.0"}],"mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient"}""",
                    _ => """{"jar":"1.20.1","mainClass":"net.minecraft.client.main.Main"}"""
                };
                using var writer = new StreamWriter(archive.CreateEntry("wrapped/minecraft/pack.json").Open());
                writer.Write(profile);
            }
            else if (format == "mmc-quilt")
            {
                using (var writer = new StreamWriter(archive.CreateEntry("other/instance.cfg").Open())) writer.Write("OverrideJavaArgs=true\nJvmArgs=-Dpack=wrong");
                using var correct = new StreamWriter(archive.CreateEntry("wrapped/instance.cfg").Open());
                correct.Write("OverrideJavaArgs=true\nJvmArgs=-Dpack=right");
            }
            else
            {
                using var unrelated = new StreamWriter(archive.CreateEntry("other/deeper/manifest.json").Open());
                unrelated.Write("""{"addons":{"game":"1.20.1"},"launchInfo":{"javaArgument":["-Dpack=wrong"]}}""");
            }
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var local = new LocalModpackInstaller(_repository, installer,
            new FabricInstaller(_repository, installer, http: _http), isolationMode: IsolationPolicy.Global,
            quilt: new FabricInstaller(_repository, installer, FabricFamilyKind.Quilt, _http));
        var id = await local.InstallFromFileAsync(path, "Imported");
        var version = await _repository.GetVersionAsync(id);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Null(version.InheritsFrom);
        Assert.Equal(kind, MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(_client, await File.ReadAllBytesAsync(Path.Combine(_repository.GetVersionDirectory(id), id + ".jar")));
        Assert.Equal(_repository.GetVersionDirectory(id), IsolationPolicy.GetGameDirectory(_root, id, version, IsolationPolicy.Global));
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.Equal(kind == "vanilla" ? "" : "0.28.0", manifest.Components[kind]);
        Assert.Equal(3, manifest.ManagedFiles.Count);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        if (!format.StartsWith("hmcl", StringComparison.Ordinal))
            Assert.Equal("-Dpack=right", InstanceSettingsStore.Load(_repository.GetVersionDirectory(id)).ExtraJvmArgs);
        var config = Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt");
        await File.WriteAllTextAsync(config, "corrupt");
        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal("correct pack config", await File.ReadAllTextAsync(config));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "pack.json")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal("keep launcher profiles", await File.ReadAllTextAsync(Path.Combine(_root, "launcher_profiles.json")));
        if (existingLoader)
        {
            Assert.Equal("keep profile", await File.ReadAllTextAsync(Path.Combine(source, sourceId + ".json")));
            Assert.Equal("keep world", await File.ReadAllTextAsync(Path.Combine(source, "saves", "world.txt")));
        }
        else Assert.False(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(_root, "config")));
        if (kind == "quilt") Assert.Contains("/v3/versions/loader/1.20.1/0.28.0/profile/json", _requests);
        if (kind == "fabric") Assert.Contains("/v2/versions/loader/1.20.1/0.28.0/profile/json", _requests);
    }

    [Theory]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"org.quiltmc.quilt-loader","version":"0.28.0"},{"uid":"net.minecraftforge","version":"47.2.18"}]}""", null)]
    [InlineData("mcbbs.packmeta", """{"addons":{"game":"1.20.1","fabric":"0.28.0","quilt":"0.28.0"}}""", null)]
    [InlineData("modpack.json", """{"gameVersion":"1.20.1"}""", """{"jar":"1.20.1","patches":[{"id":"fabric","version":"0.28.0"},{"id":"quilt","version":"0.28.0"}]}""")]
    [InlineData("modpack.json", """{"gameVersion":"1.20.1"}""", null)]
    public async Task InvalidLocalComponentPlansFailBeforeNetworkOrInstanceCreation(string name, string metadata, string? hmclProfile)
    {
        var path = Path.Combine(_root, "invalid-components.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(metadata);
            if (hmclProfile is not null)
            {
                using var writer = new StreamWriter(archive.CreateEntry("minecraft/pack.json").Open());
                writer.Write(hmclProfile);
            }
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModpackInstaller(_repository).InstallFromFileAsync(path, "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HmclFlattenedProfileKeepsCustomLibrariesAndLaunchArguments(bool completeArguments, bool loaderPatch)
    {
        var custom = Encoding.UTF8.GetBytes("custom launch library");
        _responses["/custom.jar"] = custom;
        var vanillaPath = Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json");
        var vanilla = JsonNode.Parse(await File.ReadAllTextAsync(vanillaPath))!;
        vanilla["arguments"] = JsonNode.Parse("""{"game":["--username","${auth_player_name}"],"jvm":["-cp","${classpath}"]}""");
        await File.WriteAllTextAsync(vanillaPath, vanilla.ToJsonString());
        var path = Path.Combine(_root, "custom-hmcl.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("modpack.json").Open())) writer.Write("{}");
            await using var profile = archive.CreateEntry("minecraft/pack.json").Open();
            await JsonSerializer.SerializeAsync(profile, new
            {
                jar = "1.20.1",
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                patches = loaderPatch ? new[] { new { id = "fabric", version = "0.28.0", priority = 30000 } } : null,
                libraries = new object[]
                {
                    new { name = "net.fabricmc:fabric-loader:0.28.0" },
                    new { name = "custom:launch:1", downloads = new { artifact = new { path = "custom/launch.jar", url = "https://example.invalid/custom.jar", sha1 = Hash(custom), size = custom.Length } } }
                },
                arguments = new
                {
                    game = completeArguments ? new[] { "--username", "${auth_player_name}", "--fullscreen" } : new[] { "--fullscreen" },
                    jvm = completeArguments ? new[] { "-cp", "${classpath}", "-Dpack=custom" } : new[] { "-Dpack=custom" }
                }
            });
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var id = await new LocalModpackInstaller(_repository, installer, new FabricInstaller(_repository, installer, http: _http))
            .InstallFromFileAsync(path, "Custom");
        var version = await _repository.GetVersionAsync(id);
        Assert.Equal(new[] { "--username", "${auth_player_name}", "--fullscreen" }, version.Arguments!.Game.SelectMany(argument => argument.Values));
        Assert.Equal(new[] { "-cp", "${classpath}", "-Dpack=custom" }, version.Arguments.Jvm.SelectMany(argument => argument.Values));
        Assert.Contains(version.Libraries, library => library.Name == "custom:launch:1");
        var customRelative = "libraries/wmcl-packs/" + id + "/custom/launch.jar";
        Assert.Equal(custom, await File.ReadAllBytesAsync(Path.Combine(_root, customRelative)));
        var customManaged = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles,
            file => file.Component == "HMCL-library" && file.RelativePath == customRelative);
        Assert.Equal(Hash(custom), customManaged.Sha1);
        Assert.Contains("/custom.jar", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task HmclBundledLibraryUsesAnInstancePathAndArchiveRepair()
    {
        var bytes = Encoding.UTF8.GetBytes("bundled HMCL library");
        var path = Path.Combine(_root, "bundled-hmcl.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("modpack.json").Open())) writer.Write("{}");
            await using (var profile = archive.CreateEntry("minecraft/pack.json").Open())
                await JsonSerializer.SerializeAsync(profile, new
                {
                    jar = "1.20.1",
                    mainClass = "net.minecraft.client.main.Main",
                    libraries = new[]
                    {
                        new
                        {
                            name = "custom:bundled:1",
                            downloads = new { artifact = new { path = "custom/bundled.jar", sha1 = Hash(bytes), size = bytes.Length } }
                        }
                    }
                });
            using var library = archive.CreateEntry("minecraft/libraries/custom/bundled.jar").Open();
            await library.WriteAsync(bytes);
        }

        var id = await new LocalModpackInstaller(_repository,
            new VersionInstaller(_repository, new DownloadManager(_http)))
            .InstallFromFileAsync(path, "Bundled");
        var scoped = Path.Combine(_root, "libraries", "wmcl-packs", id, "custom", "bundled.jar");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scoped));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "libraries", "custom", "bundled.jar")));
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles,
            file => file.Component == "HMCL-library");
        Assert.Equal("minecraft/libraries/custom/bundled.jar", managed.ArchivePath);
        await File.WriteAllBytesAsync(scoped, new byte[bytes.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scoped));
        Assert.Equal(0, await OverridesRepair.RepairAsync(_root, id));
        Assert.False(File.Exists(Path.Combine(_root, "libraries", "custom", "bundled.jar")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task HmclRootPatchLibrariesAreAppliedAfterComponentInstallation()
    {
        var bytes = Encoding.UTF8.GetBytes("root patch library");
        _responses["/root.jar"] = bytes;
        var path = Path.Combine(_root, "root-patch-hmcl.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("modpack.json").Open())) writer.Write("{}");
            await using var profile = archive.CreateEntry("minecraft/pack.json").Open();
            await JsonSerializer.SerializeAsync(profile, new
            {
                root = true,
                mainClass = "ignored.cached.Main",
                libraries = new[] { new { name = "cached:ignored:1" } },
                arguments = new { game = new[] { "--cached" }, jvm = new[] { "-Dcached=true" } },
                patches = new[]
                {
                    new
                    {
                        id = "game", version = "1.20.1",
                        arguments = new { game = new[] { "--fullscreen" }, jvm = new[] { "-Dpatch=true" } },
                        libraries = new[]
                        {
                            new
                            {
                                name = "custom:root:1",
                                downloads = new { artifact = new { url = "https://example.invalid/root.jar", sha1 = Hash(bytes), size = bytes.Length } }
                            }
                        }
                    }
                }
            });
        }

        var id = await new LocalModpackInstaller(_repository,
            new VersionInstaller(_repository, new DownloadManager(_http)))
            .InstallFromFileAsync(path, "RootPatch");
        var relative = "libraries/wmcl-packs/" + id + "/custom/root/1/root-1.jar";
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, relative)));
        var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles,
            file => file.Component == "HMCL-library");
        Assert.Equal(relative, managed.RelativePath);
        Assert.Equal("https://example.invalid/root.jar", managed.Url);
        Assert.Contains("/root.jar", _requests);
        var version = await _repository.GetVersionAsync(id);
        Assert.Equal("net.minecraft.client.main.Main", version.MainClass);
        Assert.DoesNotContain(version.Libraries, library => library.Name == "cached:ignored:1");
        Assert.Equal(new[] { "--username", "${auth_player_name}", "--fullscreen" },
            version.Arguments!.Game.SelectMany(argument => argument.Values));
        Assert.Equal(new[] { "-Dpatch=true" }, version.Arguments.Jvm.SelectMany(argument => argument.Values));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("1.12.2", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.20.5", 21)]
    public async Task LocalForgePacksRequestTheAppropriateJavaMajor(string minecraft, int major)
    {
        var path = Path.Combine(_root, "java-selection.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await using var metadata = archive.CreateEntry("mcbbs.packmeta").Open();
            await JsonSerializer.SerializeAsync(metadata, new { addons = new Dictionary<string, string> { ["game"] = minecraft, ["forge"] = "47.2.18" } });
        }
        var requested = 0;
        var service = new LocalModpackInstaller(_repository, javaResolver: (value, _) =>
        {
            requested = value;
            throw new OperationCanceledException("Stop before starting installer");
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.InstallFromFileAsync(path, "Java"));
        Assert.Equal(major, requested);
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Java")));
    }

    [Theory]
    [InlineData("Folder\\Pack", "Folder-Pack")]
    [InlineData("..", "LocalPack")]
    [InlineData("LongName", "LongName")]
    public async Task LocalPackNamesRemainValidAndBoundedWhenAnInstanceAlreadyExists(string name, string expected)
    {
        if (name == "LongName") name = expected = new string('a', 64);
        Directory.CreateDirectory(_repository.GetVersionDirectory(expected));
        await File.WriteAllTextAsync(Path.Combine(_repository.GetVersionDirectory(expected), "user.txt"), "keep");
        var path = Path.Combine(_root, "name.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var metadata = new StreamWriter(archive.CreateEntry("mcbbs.packmeta").Open());
            metadata.Write("""{"addons":{"game":"1.20.1"}}""");
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var id = await new LocalModpackInstaller(_repository, installer).InstallFromFileAsync(path, name);
        Assert.Equal(expected[..Math.Min(expected.Length, 62)] + "-2", id);
        Assert.True(id.Length <= 64);
        Assert.DoesNotContain('\\', id);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(expected), "user.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("mmc", "WMCL/instance.json")]
    [InlineData("mcbbs", "Imported.json")]
    [InlineData("curseforge", "PCL/install.json")]
    [InlineData("mmc", "../outside.txt")]
    [InlineData("mcbbs", "/absolute.txt")]
    [InlineData("curseforge", "config/./alias.txt")]
    public async Task OtherPackFormatsRejectMetadataOverridesBeforeInstalling(string format, string relative)
    {
        var path = Path.Combine(_root, "invalid.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var metadata = format switch
            {
                "mmc" => (Name: "mmc-pack.json", Content: """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.28.0"}]}"""),
                "mcbbs" => (Name: "mcbbs.packmeta", Content: """{"addons":[{"id":"game","version":"1.20.1"},{"id":"fabric","version":"0.28.0"}]}"""),
                _ => (Name: "manifest.json", Content: """{"minecraft":{"version":"1.20.1","modLoaders":[{"id":"fabric-0.28.0"}]},"files":[],"overrides":"overrides"}""")
            };
            using (var writer = new StreamWriter(archive.CreateEntry(metadata.Name).Open())) writer.Write(metadata.Content);
            using var content = new StreamWriter(archive.CreateEntry((format == "mmc" ? "minecraft/" : "overrides/") + relative).Open());
            content.Write("invalid");
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var fabric = new FabricInstaller(_repository, installer, http: _http);
        await Assert.ThrowsAsync<InvalidDataException>(() => format == "curseforge"
            ? new CurseForgeModpackInstaller(_repository, installer, fabric).InstallFromFileAsync(path, "Imported")
            : new LocalModpackInstaller(_repository, installer, fabric).InstallFromFileAsync(path, "Imported"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Imported")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurseForgeManifestRecordsActualInstanceFilesAndFinalOverrides(bool overrideDownload)
    {
        var bytes = Encoding.UTF8.GetBytes("downloaded CurseForge mod");
        _responses["/mod.jar"] = bytes;
        _responses["/v1/mods/111/files/222"] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            data = new
            {
                id = 222,
                displayName = "Test Mod",
                fileName = "mod.jar",
                fileLength = bytes.Length,
                downloadUrl = "https://example.invalid/mod.jar",
                hashes = new[] { new { algo = 1, value = Hash(bytes) } }
            }
        });
        var path = Path.Combine(_root, "cf-files.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write("""{"minecraft":{"version":"1.20.1","modLoaders":[{"id":"fabric-0.28.0"}]},"files":[{"projectID":111,"fileID":222,"required":true}],"overrides":"overrides"}""");
            if (overrideDownload)
            {
                using var writer = new StreamWriter(archive.CreateEntry("overrides/mods/mod.jar").Open());
                writer.Write("bundled override");
            }
        }
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        var fabric = new FabricInstaller(_repository, installer, http: _http);
        var service = new CurseForgeModpackInstaller(_repository, installer, fabric,
            client: new CurseForgeClient("key", _http), downloads: downloads);
        var id = await service.InstallFromFileAsync(path, "CurseFiles");
        var manifest = InstallManifestStore.Load(_root, id)!;
        var managed = Assert.Single(manifest.ManagedFiles, file => file.RelativePath.EndsWith("mods/mod.jar", StringComparison.Ordinal));
        Assert.Equal($"versions/{id}/mods/mod.jar", managed.RelativePath);
        Assert.Equal(overrideDownload ? "overrides" : "CurseForge", managed.Component);
        Assert.Equal(overrideDownload ? null : "https://example.invalid/mod.jar", managed.Url);
        Assert.Equal(overrideDownload ? "bundled override" : "downloaded CurseForge mod",
            await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "mods", "mod.jar")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Contains("/mod.jar", _requests);
    }

    [Fact]
    public async Task VanillaPackUsesAnIsolatedUniqueInstanceWithoutSelfInheritance()
    {
        var path = Pack(Index(), ("overrides/options.txt", "pack options"));
        var id = await _packs.InstallFromFileAsync(path, "1.20.1");
        var version = await _repository.GetVersionAsync(id);
        Assert.NotEqual("1.20.1", id);
        Assert.Null(version.InheritsFrom);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal("vanilla", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(_repository.GetVersionDirectory(id), IsolationPolicy.GetGameDirectory(_root, id, version, IsolationPolicy.Global));
        Assert.Equal("pack options", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "options.txt")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        var exported = Path.Combine(_root, "vanilla.mrpack");
        await ModpackExporter.ExportAsync(_root, id, exported);
        var second = await _packs.InstallFromFileAsync(exported, "VanillaCopy");
        Assert.Equal("vanilla", MinecraftVersionUtil.GetLoader(await _repository.GetVersionAsync(second)));
        Assert.Equal("pack options", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(second), "options.txt")));
    }

    [Fact]
    public async Task ClientOverridesWinOverDownloadsAndCommonOverridesAndRepairKeepsTheOrder()
    {
        var index = Index();
        AddFile(index, "config/test.txt", "downloaded");
        var path = Pack(index,
            ("client-overrides/config/test.txt", "client"),
            ("overrides/config/test.txt", "common"),
            ("server-overrides/config/server.txt", "server"));
        var id = await _packs.InstallFromFileAsync(path, "Ordered");
        var directory = _repository.GetVersionDirectory(id);
        var config = Path.Combine(directory, "config", "test.txt");
        Assert.Equal("client", await File.ReadAllTextAsync(config));
        Assert.False(File.Exists(Path.Combine(directory, "config", "server.txt")));
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.Equal(new[] { "overrides/", "client-overrides/" }, manifest.OverridesPrefixes);
        var managed = Assert.Single(manifest.ManagedFiles, file => file.Component == "overrides");
        Assert.Equal(Hash(Encoding.UTF8.GetBytes("client")), managed.Sha1);
        Assert.Null(managed.Url);
        await File.WriteAllTextAsync(config, "corrupted");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("overrides/WMCL/install.json").Open());
            writer.Write("not part of the original managed files");
        }
        Assert.False((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(2, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal("client", await File.ReadAllTextAsync(config));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("hashes", "{}")]
    [InlineData("hashes", "{\"sha1\":\"abc\"}")]
    [InlineData("fileSize", "-1")]
    [InlineData("env", "{\"client\":\"invalid\"}")]
    [InlineData("downloads", "[\"file:///etc/passwd\"]")]
    public async Task InvalidFileMetadataFailsBeforeCreatingAnInstance(string property, string value)
    {
        var index = Index("quilt-loader");
        AddFile(index, "mods/test.jar", "content");
        index["files"]![0]![property] = JsonNode.Parse(value);
        await Assert.ThrowsAsync<InvalidDataException>(() => _packs.InstallFromFileAsync(Pack(index), "Invalid"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Invalid")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalSelectionAndUnsupportedClientFilesControlActualDownloads(bool includeOptional)
    {
        var index = Index();
        AddFile(index, "mods/required.jar", "required");
        AddFile(index, "mods/selected.jar", "selected", "optional");
        AddFile(index, "mods/skipped.jar", "skipped", "optional");
        AddFile(index, "mods/server.jar", "server", "unsupported");
        var path = Pack(index);
        Assert.Equal(2, (await ModpackInstaller.GetOptionalFilesAsync(path)).Count);
        var id = await _packs.InstallFromFileAsync(path, "Optional", includeOptional: includeOptional,
            selectedOptionalPaths: new HashSet<string> { "mods/selected.jar" });
        var mods = Path.Combine(_repository.GetVersionDirectory(id), "mods");
        Assert.True(File.Exists(Path.Combine(mods, "required.jar")));
        Assert.Equal(includeOptional, File.Exists(Path.Combine(mods, "selected.jar")));
        Assert.False(File.Exists(Path.Combine(mods, "skipped.jar")));
        Assert.False(File.Exists(Path.Combine(mods, "server.jar")));
        Assert.DoesNotContain("/mods/skipped.jar", _requests);
        Assert.DoesNotContain("/mods/server.jar", _requests);
        var manifest = InstallManifestStore.Load(_root, id)!;
        Assert.Contains(manifest.ManagedFiles, file => file.Url == "https://example.invalid/mods/required.jar");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("config/./alias.txt")]
    [InlineData("config//alias.txt")]
    [InlineData("WMCL/install.json")]
    [InlineData("PCL/instance.json")]
    [InlineData("Pack.json")]
    [InlineData("Pack.jar")]
    public async Task RejectsUnsafeOverridesBeforeInstallingOrUsingNetwork(string relative)
    {
        var path = Pack(Index("quilt-loader"), ("overrides/" + relative, "invalid"));
        await Assert.ThrowsAsync<InvalidDataException>(() => _packs.InstallFromFileAsync(path, "Pack"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Pack")));
    }

    [Theory]
    [InlineData("formatVersion", "2")]
    [InlineData("game", "\"other\"")]
    [InlineData("game", "[]")]
    [InlineData("dependencies", "[]")]
    [InlineData("dependencies", "{\"minecraft\":\"1.20.1\",\"unknown-loader\":\"1\"}")]
    [InlineData("dependencies", "{\"minecraft\":\"1.20.1\",\"quilt-loader\":\"1\",\"fabric-loader\":\"1\"}")]
    [InlineData("dependencies", "{\"minecraft\":\"1.20.1\",\"quilt-loader\":5}")]
    [InlineData("files", "{}")]
    [InlineData("files", "[{}]")]
    public void RejectsUnsupportedOrMalformedIndex(string property, string value)
    {
        var index = Index();
        index[property] = JsonNode.Parse(value);
        if (value.Contains("unknown-loader", StringComparison.Ordinal))
            Assert.Throws<NotSupportedException>(() => ModpackInstaller.ParseIndex(index.ToJsonString()));
        else
            Assert.Throws<InvalidDataException>(() => ModpackInstaller.ParseIndex(index.ToJsonString()));
    }

    [Fact]
    public async Task RejectsDuplicatePathsAndMissingDownloadBeforeCreatingTheInstance()
    {
        var duplicate = Index();
        AddFile(duplicate, "mods/test.jar", "first");
        AddFile(duplicate, "MODS/test.jar", "second");
        await Assert.ThrowsAsync<InvalidDataException>(() => _packs.InstallFromFileAsync(Pack(duplicate), "Duplicate"));
        var missing = Index();
        AddFile(missing, "mods/missing.jar", "required");
        missing["files"]![0]!["downloads"] = new JsonArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => _packs.InstallFromFileAsync(Pack(missing), "Missing"));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Duplicate")));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Missing")));
    }

    [Fact]
    public async Task FailedContentHashNeverCreatesASuccessManifest()
    {
        var index = Index();
        AddFile(index, "mods/broken.jar", "expected");
        _responses["/mods/broken.jar"] = Encoding.UTF8.GetBytes("corrupt");
        await Assert.ThrowsAsync<DownloadException>(() => _packs.InstallFromFileAsync(Pack(index), "Failed"));
        Assert.Null(InstallManifestStore.Load(_root, "Failed"));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory("Failed"), "mods", "broken.jar")));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutWritingASuccessManifest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _packs.InstallFromFileAsync(Pack(Index("quilt-loader")), "Cancelled", ct: cancellation.Token));
        Assert.Empty(_requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Cancelled")));
    }

    [Fact]
    public async Task CancellingAfterBaseInstallationKeepsTheBaseAndDoesNotReportSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(update =>
        {
            if (update.Stage == "解压整合包覆写") cancellation.Cancel();
        });
        var path = Pack(Index(), ("overrides/config/test.txt", "config"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _packs.InstallFromFileAsync(path, "Partial", progress, cancellation.Token));
        Assert.True(File.Exists(Path.Combine(_repository.GetVersionDirectory("Partial"), "Partial.json")));
        Assert.Null(InstallManifestStore.Load(_root, "Partial"));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory("Partial"), "config", "test.txt")));
    }

    [Fact]
    public async Task RemoteArchivesWithTheSameFilenameUseHashesAndRepairCorruptCacheEntries()
    {
        var firstBytes = Encoding.UTF8.GetBytes("first archive");
        var secondBytes = Encoding.UTF8.GetBytes("second archive");
        _responses["/first.mrpack"] = firstBytes;
        _responses["/second.mrpack"] = secondBytes;
        var firstFile = new ModrinthFile("pack.mrpack", "https://example.invalid/first.mrpack", firstBytes.Length, Hash(firstBytes), true);
        var secondFile = new ModrinthFile("pack.mrpack", "https://example.invalid/second.mrpack", secondBytes.Length, Hash(secondBytes), true);
        var first = await _packs.DownloadArchiveAsync(firstFile);
        var second = await _packs.DownloadArchiveAsync(secondFile);
        try
        {
            Assert.NotEqual(first, second);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first));
            Assert.Equal(secondBytes, await File.ReadAllBytesAsync(second));
            await File.WriteAllTextAsync(first, new string('x', firstBytes.Length));
            await _packs.DownloadArchiveAsync(firstFile);
            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(first));
            var requestCount = _requests.Count;
            await _packs.DownloadArchiveAsync(firstFile);
            Assert.Equal(requestCount, _requests.Count);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    private static JsonObject Index(string? loader = null)
    {
        var dependencies = new JsonObject { ["minecraft"] = "1.20.1" };
        if (loader is not null) dependencies[loader] = "0.28.0";
        return new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = "Test",
            ["versionId"] = "1.0",
            ["dependencies"] = dependencies,
            ["files"] = new JsonArray()
        };
    }

    private void AddFile(JsonObject index, string path, string content, string environment = "required")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        _responses["/" + path] = bytes;
        index["files"]!.AsArray().Add(new JsonObject
        {
            ["path"] = path,
            ["hashes"] = new JsonObject { ["sha1"] = Hash(bytes) },
            ["fileSize"] = bytes.Length,
            ["downloads"] = new JsonArray("https://example.invalid/" + path),
            ["env"] = new JsonObject { ["client"] = environment }
        });
    }

    private string Pack(JsonObject index, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".mrpack");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open())) writer.Write(index.ToJsonString());
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
            writer.Write(entry.Content);
        }
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class CallbackProgress(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }
}
