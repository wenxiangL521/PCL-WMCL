using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using WMCL.Download;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HmclSameCoordinateLibrariesUseIndependentBytesAndRepairPaths(bool bundled)
    {
        var shared = Path.Combine(_root, "libraries", "custom", "launch.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "SHARED");
        var firstBytes = Encoding.UTF8.GetBytes("FIRSTX");
        var secondBytes = Encoding.UTF8.GetBytes("SECOND");
        var installer = new LocalModpackInstaller(_repository,
            new VersionInstaller(_repository, new DownloadManager(_http)));
        var first = await installer.InstallFromFileAsync(HmclLibraryPack(firstBytes, bundled), "First");
        var second = await installer.InstallFromFileAsync(HmclLibraryPack(secondBytes, bundled), "Second");
        var firstRelative = "libraries/wmcl-packs/" + first + "/custom/launch.jar";
        var secondRelative = "libraries/wmcl-packs/" + second + "/custom/launch.jar";
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(Path.Combine(_root, firstRelative)));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(Path.Combine(_root, secondRelative)));
        Assert.Equal("SHARED", await File.ReadAllTextAsync(shared));
        foreach (var instance in new[] { first, second })
        {
            var version = await _repository.GetVersionAsync(instance);
            var library = Assert.Single(LibraryResolver.ResolveArtifacts(version, RuleContext.CreateDefault()));
            Assert.Equal("wmcl-packs/" + instance + "/custom/launch.jar", library.RelativePath);
            Assert.True((await InstallManifestStore.VerifyAsync(_root, instance)).IsHealthy);
        }
        await File.WriteAllBytesAsync(Path.Combine(_root, firstRelative), new byte[firstBytes.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, first, downloads: new DownloadManager(_http)));
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(Path.Combine(_root, firstRelative)));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(Path.Combine(_root, secondRelative)));
        Assert.Equal("SHARED", await File.ReadAllTextAsync(shared));
    }

    [Fact]
    public async Task HmclLibraryWrongDeclaredSizeDoesNotCreateSuccessManifest()
    {
        var bytes = Encoding.UTF8.GetBytes("wrong declared length");
        var path = HmclLibraryPack(bytes, false, bytes.Length + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModpackInstaller(_repository,
            new VersionInstaller(_repository, new DownloadManager(_http))).InstallFromFileAsync(path, "WrongSize"));
        Assert.Null(InstallManifestStore.Load(_root, "WrongSize"));
    }

    [Fact]
    public async Task HmclPatchArgumentsFollowPriorityInsteadOfArchiveOrder()
    {
        var path = Path.Combine(_root, "ordered-hmcl.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("modpack.json").Open())) writer.Write("{}");
            using var profile = new StreamWriter(archive.CreateEntry("minecraft/pack.json").Open());
            profile.Write("""
                {"root":true,"patches":[
                  {"id":"fabric","version":"0.28.0","priority":30000,"arguments":{"jvm":["-Dorder=loader"]}},
                  {"id":"game","version":"1.20.1","arguments":{"jvm":["-Dorder=game"]}}
                ]}
                """);
        }
        var installer = new VersionInstaller(_repository, new DownloadManager(_http));
        var id = await new LocalModpackInstaller(_repository, installer,
                new FabricInstaller(_repository, installer, http: _http))
            .InstallFromFileAsync(path, "OrderedPatches");
        var version = await _repository.GetVersionAsync(id);
        Assert.Equal(new[] { "-Dorder=game", "-Dorder=loader" },
            version.Arguments!.Jvm.SelectMany(argument => argument.Values));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task HmclInactiveBundledLibraryKeepsArchiveRepairAndOverridesExclusion()
    {
        var bytes = Encoding.UTF8.GetBytes("library for another platform");
        var archive = HmclLibraryPack(bytes, true, inactive: true);
        var id = await new LocalModpackInstaller(_repository,
            new VersionInstaller(_repository, new DownloadManager(_http))).InstallFromFileAsync(archive, "InactiveLibrary");
        var version = await _repository.GetVersionAsync(id);
        Assert.Empty(LibraryResolver.ResolveArtifacts(version, RuleContext.CreateDefault()));
        var manifest = InstallManifestStore.Load(_root, id)!;
        var managed = Assert.Single(manifest.ManagedFiles, file => file.Component == "HMCL-library");
        Assert.Equal("minecraft/libraries/custom/launch.jar", managed.ArchivePath);
        Assert.Contains("libraries/custom/launch.jar", manifest.OverridesExcludedPaths);
        var scoped = Path.Combine(_root, managed.RelativePath);
        await File.WriteAllBytesAsync(scoped, new byte[bytes.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(scoped));
        Assert.Equal(0, await OverridesRepair.RepairAsync(_root, id));
        Assert.False(File.Exists(Path.Combine(_repository.GetVersionDirectory(id), "libraries", "custom", "launch.jar")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    private string HmclLibraryPack(byte[] bytes, bool bundled, int? size = null, bool inactive = false)
    {
        var urlPath = "/hmcl-" + Hash(bytes) + ".jar";
        _responses[urlPath] = bytes;
        var artifact = new JsonObject
        {
            ["path"] = "custom/launch.jar",
            ["sha1"] = Hash(bytes),
            ["size"] = size ?? bytes.Length
        };
        if (!bundled) artifact["url"] = "https://example.invalid" + urlPath;
        var profile = new JsonObject
        {
            ["jar"] = "1.20.1",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "custom:launch:1",
                ["downloads"] = new JsonObject { ["artifact"] = artifact }
            })
        };
        if (inactive)
            profile["libraries"]![0]!["rules"] = new JsonArray(new JsonObject
            {
                ["action"] = "allow",
                ["os"] = new JsonObject { ["name"] = "another-platform" }
            });
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("modpack.json").Open())) writer.Write("{}");
        using (var writer = new StreamWriter(archive.CreateEntry("minecraft/pack.json").Open()))
            writer.Write(profile.ToJsonString());
        if (bundled)
        {
            using var stream = archive.CreateEntry("minecraft/libraries/custom/launch.jar").Open();
            stream.Write(bytes);
        }
        return path;
    }
}
