using System.Text;
using WMCL.Download;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("same-size")]
    [InlineData("shorter")]
    [InlineData("missing")]
    [InlineData("healthy")]
    public async Task ManagedRepairDownloadsCorruptOrMissingFilesAndReusesVerifiedFiles(string state)
    {
        var expected = Encoding.UTF8.GetBytes("correct file");
        _responses["/repair.jar"] = expected;
        var instance = _repository.GetVersionDirectory("Repair");
        Directory.CreateDirectory(instance);
        var path = Path.Combine(instance, "repair.jar");
        if (state != "missing") await File.WriteAllBytesAsync(path,
            state == "healthy" ? expected : state == "same-size" ? new byte[expected.Length] : []);
        InstallManifestStore.Save(_root, "Repair", new InstallManifest
        {
            ManagedFiles = [new("versions/Repair/repair.jar", Hash(expected), "MCBBS", "https://example.invalid/repair.jar")]
        });
        var repaired = await ManagedFileRepair.RepairAsync(_root, "Repair", downloads: new DownloadManager(_http));
        Assert.Equal(state == "healthy" ? 0 : 1, repaired);
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        Assert.Equal(state == "healthy" ? 0 : 1, _requests.Count(request => request == "/repair.jar"));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Repair")).IsHealthy);
    }

    [Fact]
    public async Task ManagedRepairCompletesMcbbsDownloadsWithoutReapplyingCorruptBundledFiles()
    {
        var expected = Encoding.UTF8.GetBytes("correct file");
        _responses["/pack/overrides/config/test.txt"] = expected;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(McbbsIndex(Addon("config/test.txt", expected)),
            entries: [("config/test.txt", "corrupt archive")]), "Repair");
        var destination = Path.Combine(_repository.GetVersionDirectory(id), "config", "test.txt");
        await File.WriteAllBytesAsync(destination, new byte[expected.Length]);
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(_root, id, downloads: new DownloadManager(_http)));
        Assert.Equal(0, await OverridesRepair.RepairAsync(_root, id));
        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Theory]
    [InlineData("../outside.jar", "https://example.invalid/repair.jar", "0000000000000000000000000000000000000000")]
    [InlineData("mods/test.jar", "file:///tmp/repair.jar", "0000000000000000000000000000000000000000")]
    [InlineData("mods/test.jar", "https://example.invalid/repair.jar", "bad hash")]
    public async Task ManagedRepairRejectsInvalidMetadataBeforeDownloading(string path, string url, string hash)
    {
        InstallManifestStore.Save(_root, "Invalid", new InstallManifest { ManagedFiles = [new(path, hash, "MCBBS", url)] });
        await Assert.ThrowsAsync<InvalidDataException>(() => ManagedFileRepair.RepairAsync(_root, "Invalid", downloads: new DownloadManager(_http)));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task ManagedRepairCancellationDoesNotTouchFiles()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ManagedFileRepair.RepairAsync(_root, "Missing", ct: cancellation.Token));
        Assert.Empty(_requests);
    }
}
