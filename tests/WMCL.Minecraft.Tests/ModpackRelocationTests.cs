using System.Text;
using System.Text.Json.Nodes;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("hmcl", false)]
    [InlineData("hmcl", true)]
    [InlineData("mmc", false)]
    [InlineData("mmc", true)]
    [InlineData("mcbbs", false)]
    [InlineData("mcbbs", true)]
    public async Task ImportedPackRelocationKeepsLaunchAndRepairIndependent(string format, bool clone)
    {
        var bytes = Encoding.UTF8.GetBytes("custom dependency");
        string id;
        if (format == "hmcl")
            id = await new LocalModpackInstaller(_repository, new VersionInstaller(_repository, new DownloadManager(_http)))
                .InstallFromFileAsync(HmclLibraryPack(bytes, true), "Original");
        else if (format == "mmc")
        {
            var patch = MmcPatch("custom.library", 10);
            patch["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "custom:dependency:1",
                ["MMC-hint"] = "local",
                ["MMC-filename"] = "dependency.jar",
                ["downloads"] = new JsonObject { ["artifact"] = new JsonObject { ["sha1"] = Hash(bytes), ["size"] = bytes.Length } }
            });
            id = await MmcService().InstallFromFileAsync(MmcPack([patch], "", ("libraries/dependency.jar", bytes)), "Original");
        }
        else
        {
            _responses["/dependency.jar"] = bytes;
            var index = McbbsIndex(TextAddon("config/test.txt", "original"));
            index["libraries"] = new JsonArray(McbbsLibrary("custom:dependency:1", "/dependency.jar", bytes));
            id = await McbbsService().InstallFromFileAsync(McbbsPack(index, entries: [("config/test.txt", "original")]), "Original");
        }
        if (clone) Assert.Equal("Relocated", InstanceManager.Clone(_root, id, "Relocated"));
        else InstanceManager.Rename(_root, id, "Relocated");
        if (clone)
        {
            Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
            InstanceManager.Delete(_root, id);
        }
        var version = await _repository.GetVersionAsync("Relocated");
        var plan = LaunchPlanBuilder.Build(new LaunchRequest
        {
            Version = version,
            VersionId = "Relocated",
            GameDirectory = _repository.GetVersionDirectory("Relocated"),
            MinecraftDirectory = _root,
            Java = new JavaRuntime("java", new Version(21, 0), 21, "x86_64", _root, null),
            Account = AccountInfo.Offline("Relocation")
        });
        var library = Assert.Single(InstallManifestStore.Load(_root, "Relocated")!.ManagedFiles,
            file => file.Component == format.ToUpperInvariant() + "-library" &&
                    plan.Classpath.Split(Path.PathSeparator).Contains(Path.Combine(_root, file.RelativePath)));
        var destination = Path.Combine(_root, library.RelativePath);
        Assert.Contains(destination, plan.Classpath.Split(Path.PathSeparator));
        Assert.DoesNotContain("wmcl-packs/" + id + "/", plan.Classpath.Replace('\\', '/'));
        await File.WriteAllBytesAsync(destination, new byte[bytes.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, "Relocated", downloads: new DownloadManager(_http)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        if (format == "mcbbs")
        {
            var remote = McbbsIndex(TextAddon("config/test.txt", "updated"));
            remote["libraries"] = new JsonArray(McbbsLibrary("custom:dependency:1", "/dependency.jar", bytes));
            SetMcbbsRemote(remote);
            Assert.Equal(1, (await McbbsUpdater().UpdateAsync("Relocated")).Downloaded);
            Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("Relocated"), "config/test.txt")));
        }
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Relocated")).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelocatedCurseForgeWorldRetainsRemoteRepairAndPlayerProgress(bool clone)
    {
        CfFileResponse(111, 222, "map.zip", ZipBytes([
            ("World/level.dat", "original"), ("World/region/r.0.0.mca", "region")]), ["level.dat"]);
        var pack = CfPack();
        var id = await CfService().InstallFromFileAsync(pack, "Original");
        await File.WriteAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "saves/World/level.dat"), "player progress");
        if (clone)
        {
            Assert.Equal("Relocated", InstanceManager.Clone(_root, id, "Relocated"));
            InstanceManager.Delete(_root, id);
        }
        else InstanceManager.Rename(_root, id, "Relocated");
        File.Delete(pack);
        var directory = _repository.GetVersionDirectory("Relocated");
        File.Delete(Path.Combine(directory, "saves/World/region/r.0.0.mca"));
        _requests.Clear();
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, "Relocated", downloads: new DownloadManager(_http)));
        Assert.Equal("region", await File.ReadAllTextAsync(Path.Combine(directory, "saves/World/region/r.0.0.mca")));
        Assert.Equal("player progress", await File.ReadAllTextAsync(Path.Combine(directory, "saves/World/level.dat")));
        Assert.Single(_requests, path => path == "/map.zip");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Relocated")).IsHealthy);
    }
}
