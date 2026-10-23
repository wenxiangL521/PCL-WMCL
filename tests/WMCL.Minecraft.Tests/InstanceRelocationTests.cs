using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;

namespace WMCL.Minecraft.Tests;

public sealed class InstanceRelocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-relocate-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "versions", "Source");
    private string SourceLibraries => Path.Combine(_root, "libraries", "wmcl-packs", "Source");

    public InstanceRelocationTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "config"));
        Directory.CreateDirectory(Path.Combine(Source, "saves", "World"));
        Directory.CreateDirectory(Path.Combine(Source, "Source-natives"));
        Directory.CreateDirectory(Path.Combine(SourceLibraries, "custom"));
        Directory.CreateDirectory(Path.Combine(_root, "libraries", "shared"));
        File.WriteAllText(Path.Combine(Source, "Source.json"), """
            {"id":"Source","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main",
             "downloads":{"client":{"path":"Source.jar"}},"libraries":[
               {"name":"custom:launch:1","downloads":{"artifact":{"path":"wmcl-packs/Source/custom/launch.jar"},
                 "classifiers":{"natives-windows":{"path":"wmcl-packs/Source/custom/windows.jar"}}}},
               {"name":"shared:launch:1","downloads":{"artifact":{"path":"shared/launch.jar"}}}
             ],"customMetadata":{"description":"Source should remain in this text"}}
            """);
        File.WriteAllText(Path.Combine(Source, "Source.jar"), "client");
        File.WriteAllText(Path.Combine(Source, "Source-natives", "native.bin"), "native");
        File.WriteAllText(Path.Combine(Source, "config", "test.txt"), "original config");
        File.WriteAllText(Path.Combine(Source, "saves", "World", "level.dat"), "player progress");
        File.WriteAllText(Path.Combine(SourceLibraries, "custom", "launch.jar"), "custom library");
        File.WriteAllText(Path.Combine(SourceLibraries, "custom", "windows.jar"), "inactive native");
        File.WriteAllText(Path.Combine(_root, "libraries", "shared", "launch.jar"), "shared library");
        using (var archive = ZipFile.Open(Path.Combine(Source, "pack.zip"), ZipArchiveMode.Create))
        {
            Write(archive, "minecraft/config/test.txt", "original config");
            Write(archive, "minecraft/libraries/custom/launch.jar", "custom library");
            Write(archive, "minecraft/libraries/custom/windows.jar", "inactive native");
        }
        var manifest = new InstallManifest
        {
            SourceArchive = Path.Combine(Source, "pack.zip"),
            OverridesRoot = Source,
            OverridesPrefix = "minecraft/",
            OverridesExcludedPaths = ["libraries/custom/launch.jar", "libraries/custom/windows.jar"],
            Components = { ["minecraft"] = "1.20.1" },
            ManagedArchives = { ["map"] = new(["https://example.invalid/Source.zip"]) }
        };
        foreach (var relative in new[] { "Source.json", "Source.jar", "Source-natives/native.bin", "config/test.txt", "saves/World/level.dat" })
            manifest.ManagedFiles.Add(new("versions/Source/" + relative, FileUtil.Sha1(Path.Combine(Source, relative)),
                relative.StartsWith("config/") || relative.StartsWith("saves/") ? "overrides" : "merged"));
        foreach (var name in new[] { "launch.jar", "windows.jar" })
            manifest.ManagedFiles.Add(new("libraries/wmcl-packs/Source/custom/" + name,
                FileUtil.Sha1(Path.Combine(SourceLibraries, "custom", name)), "HMCL-library",
                ArchivePath: "minecraft/libraries/custom/" + name));
        InstallManifestStore.Save(_root, "Source", manifest);
        InstanceSettingsStore.Save(Source, new InstanceSettings
        {
            LogoPath = Path.Combine(Source, "icon.png"),
            PluginDirectory = Path.Combine(Source, "plugins"),
            JavaPath = Path.Combine(_root, "external-java"),
            Description = "Source description",
            IsolationMode = IsolationPolicy.Isolated
        });
    }

    [Theory]
    [InlineData("Target", false)]
    [InlineData("Target", true)]
    [InlineData("source", false)]
    [InlineData("source", true)]
    public void BeforeCommitFailureRestoresMetadataSettingsLibrariesAndNativeNames(string targetId, bool legacy)
    {
        if (legacy)
        {
            Directory.CreateDirectory(Path.Combine(Source, "PCL"));
            File.Move(InstallManifestStore.GetPath(_root, "Source"), Path.Combine(Source, "PCL", "install.json"));
        }
        var original = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(_root, path), path => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(path))));
        var callbackCount = 0;
        var failure = new IOException("before commit failure");
        Assert.Same(failure, Assert.Throws<IOException>(() => InstanceManager.Rename(_root, "Source", targetId, () =>
        {
            callbackCount++;
            var target = Path.Combine(_root, "versions", targetId);
            Assert.True(File.Exists(Path.Combine(target, targetId + ".json")));
            Assert.True(File.Exists(Path.Combine(target, targetId + "-natives", "native.bin")));
            Assert.True(File.Exists(Path.Combine(_root, "libraries", "wmcl-packs", targetId, "custom", "launch.jar")));
            throw failure;
        })));
        Assert.Equal(1, callbackCount);
        var restored = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(_root, path), path => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(path))));
        Assert.Equal(original.OrderBy(pair => pair.Key), restored.OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-relocate-*"));
    }

    [Fact]
    public async Task SuccessfulBeforeCommitObservesPublishedInstanceAndCommitsLibraryCleanup()
    {
        var callbackCount = 0;
        InstanceManager.Rename(_root, "Source", "Target", () =>
        {
            callbackCount++;
            Assert.False(Directory.Exists(Source));
            Assert.True(File.Exists(Path.Combine(_root, "versions/Target/Target.json")));
            Assert.True(File.Exists(Path.Combine(_root, "libraries/wmcl-packs/Target/custom/launch.jar")));
        });
        Assert.Equal(1, callbackCount);
        Assert.False(Directory.Exists(SourceLibraries));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Target")).IsHealthy);
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-relocate-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelocationMigratesIdentityManifestAndSettings(bool clone)
    {
        Relocate(clone);
        var target = Path.Combine(_root, "versions", "Target");
        var manifest = InstallManifestStore.Load(_root, "Target")!;
        Assert.Equal(Path.Combine(target, "pack.zip"), manifest.SourceArchive);
        Assert.Equal(target, manifest.OverridesRoot);
        Assert.Equal("minecraft/", manifest.OverridesPrefix);
        Assert.Equal("minecraft/libraries/custom/launch.jar",
            Assert.Single(manifest.ManagedFiles, file => file.RelativePath.EndsWith("custom/launch.jar")).ArchivePath);
        Assert.Equal("https://example.invalid/Source.zip", manifest.ManagedArchives["map"].Urls.Single());
        Assert.DoesNotContain(manifest.ManagedFiles, file => file.RelativePath.StartsWith("versions/Source/"));
        Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == "versions/Target/Target-natives/native.bin");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Target")).IsHealthy);
        using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "Target.json")));
        Assert.Equal("Target", profile.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1", profile.RootElement.GetProperty("clientVersion").GetString());
        Assert.Equal("Target.jar", profile.RootElement.GetProperty("downloads").GetProperty("client").GetProperty("path").GetString());
        Assert.Equal("Source should remain in this text",
            profile.RootElement.GetProperty("customMetadata").GetProperty("description").GetString());
        var settings = InstanceSettingsStore.Load(target);
        Assert.Equal(Path.Combine(target, "icon.png"), settings.LogoPath);
        Assert.Equal(Path.Combine(target, "plugins"), settings.PluginDirectory);
        Assert.Equal(Path.Combine(_root, "external-java"), settings.JavaPath);
        Assert.Equal("Source description", settings.Description);
        Assert.Equal(clone, Directory.Exists(Source));
        if (clone) Assert.True((await InstallManifestStore.VerifyAsync(_root, "Source")).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TargetLibrariesAndRepairRemainIndependentAfterSourceDeletion(bool clone)
    {
        Relocate(clone);
        if (clone) InstanceManager.Delete(_root, "Source");
        var repository = new VersionRepository(_root);
        var profile = await repository.GetVersionAsync("Target");
        var custom = profile.Libraries.First();
        Assert.Equal("wmcl-packs/Target/custom/launch.jar", custom.Downloads!.Artifact!.Path);
        Assert.Equal("wmcl-packs/Target/custom/windows.jar", custom.Downloads.Classifiers!["natives-windows"].Path);
        var targetLibrary = Path.Combine(_root, "libraries", "wmcl-packs", "Target", "custom", "launch.jar");
        Assert.Equal("custom library", File.ReadAllText(targetLibrary));
        Assert.Equal("inactive native", File.ReadAllText(Path.Combine(Path.GetDirectoryName(targetLibrary)!, "windows.jar")));
        File.WriteAllText(targetLibrary, "damaged");
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, "Target"));
        Assert.Equal("custom library", File.ReadAllText(targetLibrary));
        File.WriteAllText(Path.Combine(_root, "versions", "Target", "config", "test.txt"), "damaged config");
        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, "Target"));
        Assert.Equal("player progress", File.ReadAllText(Path.Combine(_root, "versions", "Target", "saves", "World", "level.dat")));
        Assert.Equal("shared library", File.ReadAllText(Path.Combine(_root, "libraries", "shared", "launch.jar")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Target")).IsHealthy);
        Assert.False(Directory.Exists(SourceLibraries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedModHashesAreNotSilentlyAcceptedByRelocation(bool clone)
    {
        File.WriteAllText(Path.Combine(Source, "config", "test.txt"), "user edit");
        Relocate(clone);
        var result = await InstallManifestStore.VerifyAsync(_root, "Target");
        Assert.Equal(1, result.Corrupted);
        Assert.Contains(result.Problems, problem => problem.Contains("config/test.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRelocationRestoresSourceAndRemovesOnlyItsOwnStaging(bool clone)
    {
        File.WriteAllText(Path.Combine(Source, "Target.jar"), "unrelated existing file");
        var original = File.ReadAllBytes(Path.Combine(Source, "Source.json"));
        var manifest = File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Source"));
        Assert.Throws<IOException>(() => Relocate(clone));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Source, "Source.json")));
        Assert.Equal(manifest, File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Source")));
        Assert.Equal("client", File.ReadAllText(Path.Combine(Source, "Source.jar")));
        Assert.Equal("unrelated existing file", File.ReadAllText(Path.Combine(Source, "Target.jar")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "Target")));
        Assert.False(Directory.Exists(Path.Combine(_root, "libraries", "wmcl-packs", "Target")));
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "versions")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleManifestIsRejectedBeforeMovingOrCopyingTheInstance(bool clone)
    {
        var manifest = InstallManifestStore.Load(_root, "Source")!;
        manifest.ManagedFiles.Add(new("versions/Other/config.txt", new string('0', 40), "overrides"));
        InstallManifestStore.Save(_root, "Source", manifest);
        Assert.Throws<InvalidDataException>(() => Relocate(clone));
        Assert.True(File.Exists(Path.Combine(Source, "Source.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "Target")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyManifestAndCaseOnlyRenameKeepVerificationWorking(bool legacy)
    {
        if (legacy)
        {
            Directory.CreateDirectory(Path.Combine(Source, "PCL"));
            File.Move(InstallManifestStore.GetPath(_root, "Source"), Path.Combine(Source, "PCL", "install.json"));
        }
        InstanceManager.Rename(_root, "Source", "source");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "source")).IsHealthy);
        Assert.True(File.Exists(Path.Combine(_root, "versions", "source", "source.jar")));
    }

    [Fact]
    public void CloneDoesNotReuseAnExistingLibraryNamespace()
    {
        var existing = Path.Combine(_root, "libraries", "wmcl-packs", "Target");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep existing library namespace");
        Assert.Throws<IOException>(() => InstanceManager.Rename(_root, "Source", "Target"));
        Assert.Equal("Target-2", InstanceManager.Clone(_root, "Source", "Target"));
        Assert.Equal("keep existing library namespace", File.ReadAllText(Path.Combine(existing, "keep.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencedOldLibraryNamespaceIsPreserved(bool rename)
    {
        var legacy = Path.Combine(_root, "versions", "Legacy");
        Directory.CreateDirectory(legacy);
        File.Copy(Path.Combine(Source, "Source.json"), Path.Combine(legacy, "Legacy.json"));
        if (rename) InstanceManager.Rename(_root, "Source", "Target");
        else InstanceManager.Delete(_root, "Source");
        Assert.Equal("custom library", File.ReadAllText(Path.Combine(SourceLibraries, "custom", "launch.jar")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelocationRejectsSymbolicLinksWithoutChangingTheSource(bool library)
    {
        var link = Path.Combine(library ? SourceLibraries : Source, "link");
        File.CreateSymbolicLink(link, Path.Combine(_root, "outside"));
        try
        {
            Assert.Throws<InvalidDataException>(() => Relocate(clone: true));
            Assert.True(File.Exists(Path.Combine(Source, "Source.json")));
            Assert.False(Directory.Exists(Path.Combine(_root, "versions", "Target")));
        }
        finally { File.Delete(link); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyVanillaBaseIdentitySurvivesRelocation(bool clone)
    {
        var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(Source, "Source.json")))!;
        profile.AsObject().Remove("clientVersion");
        File.WriteAllText(Path.Combine(Source, "Source.json"), profile.ToJsonString());
        Relocate(clone);
        Assert.Equal("1.20.1", (await new VersionRepository(_root).GetVersionAsync("Target")).BaseGameVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedMetadataDoesNotLeaveAPartialInstance(bool settings)
    {
        File.WriteAllText(settings ? InstanceSettingsStore.PathFor(Source) : InstallManifestStore.GetPath(_root, "Source"), "invalid json");
        Assert.ThrowsAny<JsonException>(() => Relocate(clone: false));
        Assert.True(File.Exists(Path.Combine(Source, "Source.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "Target")));
        Assert.False(Directory.Exists(Path.Combine(_root, "libraries", "wmcl-packs", "Target")));
    }

    [Fact]
    public void UnknownManifestPropertiesAndExternalArchiveArePreserved()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(InstallManifestStore.GetPath(_root, "Source")))!;
        var external = Path.Combine(_root, "external-pack.zip");
        File.Copy(Path.Combine(Source, "pack.zip"), external);
        manifest["SourceArchive"] = external;
        manifest["FutureMetadata"] = new JsonObject { ["text"] = "Source" };
        File.WriteAllText(InstallManifestStore.GetPath(_root, "Source"), manifest.ToJsonString());
        Relocate(clone: true);
        var saved = JsonNode.Parse(File.ReadAllText(InstallManifestStore.GetPath(_root, "Target")))!;
        Assert.Equal("Source", saved["FutureMetadata"]!["text"]!.GetValue<string>());
        Assert.Equal(external, InstallManifestStore.Load(_root, "Target")!.SourceArchive);
    }

    [Fact]
    public void UnreadableOtherProfileKeepsOldLibrariesAfterRename()
    {
        var other = Path.Combine(_root, "versions", "Other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Other.json"), "unreadable metadata");
        Relocate(clone: false);
        Assert.Equal("custom library", File.ReadAllText(Path.Combine(SourceLibraries, "custom", "launch.jar")));
    }

    [Fact]
    public void CloneHasIndependentLibraryFilesAndDeleteKeepsItsSource()
    {
        Relocate(clone: true);
        File.WriteAllText(Path.Combine(_root, "libraries/wmcl-packs/Target/custom/launch.jar"), "clone changed");
        Assert.Equal("custom library", File.ReadAllText(Path.Combine(SourceLibraries, "custom/launch.jar")));
        InstanceManager.Delete(_root, "Target");
        Assert.False(Directory.Exists(Path.Combine(_root, "libraries/wmcl-packs/Target")));
        Assert.Equal("custom library", File.ReadAllText(Path.Combine(SourceLibraries, "custom/launch.jar")));
        Assert.True(File.Exists(Path.Combine(Source, "Source.json")));
    }

    [Fact]
    public void LinkedConsumerKeepsTheOldLibrariesWithoutFailingTheRename()
    {
        var linked = Path.Combine(_root, "versions", "Linked");
        Directory.CreateSymbolicLink(linked, Source);
        try
        {
            Relocate(clone: false);
            Assert.Equal("custom library", File.ReadAllText(Path.Combine(SourceLibraries, "custom/launch.jar")));
            Assert.True(File.Exists(Path.Combine(_root, "versions/Target/Target.json")));
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(linked);
            else File.Delete(linked);
        }
    }

    private void Relocate(bool clone)
    {
        if (clone) Assert.Equal("Target", InstanceManager.Clone(_root, "Source", "Target"));
        else InstanceManager.Rename(_root, "Source", "Target");
    }

    private static void Write(ZipArchive archive, string path, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(value);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
