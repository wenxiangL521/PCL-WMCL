using System.IO.Compression;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;

namespace WMCL.Minecraft.Tests;

public sealed class InstanceImportMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-import-migration-" + Guid.NewGuid().ToString("N"));
    private string SourceRoot => Path.Combine(_root, "source");
    private string Source => Path.Combine(SourceRoot, "versions", "Source");

    [Theory]
    [InlineData(false, "Target", "Target")]
    [InlineData(false, null, "Source-2")]
    [InlineData(true, "Target", "Target")]
    public async Task ExportImportVerifyAndRepairUseOnlyExportedTargetContent(bool otherRoot, string? requested, string expected)
    {
        var archive = await CreateExportAsync();
        var original = Snapshot(Source);
        var targetRoot = otherRoot ? Path.Combine(_root, "target") : SourceRoot;
        var imported = await InstanceImporter.ImportAsync(targetRoot, archive, requested, installMissing: false);
        Assert.Equal(expected, imported.Id);
        Assert.True((await InstallManifestStore.VerifyAsync(targetRoot, expected)).IsHealthy);
        var manifest = InstallManifestStore.Load(targetRoot, expected)!;
        Assert.Equal(imported.VersionDirectory, manifest.OverridesRoot);
        Assert.StartsWith(imported.VersionDirectory, manifest.SourceArchive!);
        Assert.DoesNotContain(manifest.ManagedFiles, file => file.RelativePath.StartsWith("libraries/") || file.RelativePath.StartsWith("assets/"));
        Assert.DoesNotContain(manifest.ManagedFiles, file => file.RelativePath.Contains("user.txt") || file.RelativePath.Contains("omitted.txt"));
        Assert.All(manifest.ManagedFiles, file =>
        {
            Assert.StartsWith("versions/" + expected + "/", file.RelativePath);
            Assert.Null(file.Url);
            Assert.Null(file.ArchiveId);
            Assert.StartsWith("instance/", file.ArchivePath!);
        });
        var targetProfile = Path.Combine(imported.VersionDirectory, expected + ".json");
        var profile = JsonNode.Parse(await File.ReadAllTextAsync(targetProfile))!;
        Assert.Equal(expected, profile["id"]!.GetValue<string>());
        Assert.Equal(expected, profile["jar"]!.GetValue<string>());
        Assert.Equal(expected + ".jar", profile["downloads"]!["client"]!["path"]!.GetValue<string>());
        Assert.Equal("wmcl-packs/" + expected + "/custom.jar", profile["libraries"]![0]!["downloads"]!["artifact"]!["path"]!.GetValue<string>());
        Assert.Equal("shared/library.jar", profile["libraries"]![1]!["downloads"]!["artifact"]!["path"]!.GetValue<string>());
        var rewritten = await File.ReadAllTextAsync(targetProfile);
        File.WriteAllText(targetProfile, "damaged profile");
        File.WriteAllText(Path.Combine(imported.VersionDirectory, "config/test.txt"), "damaged config");
        File.WriteAllText(Path.Combine(imported.VersionDirectory, "saves/World/level.dat"), "new player progress");
        File.Delete(archive);
        Assert.Equal(2, await ManagedFileRepair.RepairAsync(targetRoot, expected));
        Assert.Equal(rewritten, await File.ReadAllTextAsync(targetProfile));
        File.WriteAllText(Path.Combine(imported.VersionDirectory, "config/test.txt"), "damaged again");
        Assert.Equal(1, await OverridesRepair.RepairAsync(targetRoot, expected));
        Assert.Equal("config", File.ReadAllText(Path.Combine(imported.VersionDirectory, "config/test.txt")));
        Assert.Equal("new player progress", File.ReadAllText(Path.Combine(imported.VersionDirectory, "saves/World/level.dat")));
        Assert.True((await InstallManifestStore.VerifyAsync(targetRoot, expected)).IsHealthy);
        Assert.Equal(original, Snapshot(Source));
        Assert.Empty(Directory.GetDirectories(targetRoot, ".wmcl-import-*"));
    }

    [Theory]
    [InlineData("instance/../../outside.txt")]
    [InlineData("instance//absolute.txt")]
    [InlineData("instance/C:/absolute.txt")]
    [InlineData("../outside.txt")]
    public async Task RejectsIllegalArchivePathsWithoutChangingSource(string entryName)
    {
        var archive = await CreateExportAsync();
        var original = Snapshot(Source);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write("unsafe");
        await Assert.ThrowsAsync<InvalidDataException>(() => InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false));
        Assert.False(Directory.Exists(Path.Combine(SourceRoot, "versions/Target")));
        Assert.Equal(original, Snapshot(Source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationAfterPublicationRollsBackNewInstance(bool cancel)
    {
        var archive = await CreateExportAsync();
        var original = Snapshot(Source);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value.Percent != 1) return;
            if (cancel) cancellation.Cancel();
            else throw new IOException("publication failure");
        });
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false, progress, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false, progress));
        Assert.False(Directory.Exists(Path.Combine(SourceRoot, "versions/Target")));
        Assert.Empty(Directory.GetDirectories(SourceRoot, ".wmcl-import-*"));
        Assert.Equal(original, Snapshot(Source));
    }

    [Fact]
    public async Task ExportedUserEditKeepsOriginalVerificationHashWithoutInvalidRepairSource()
    {
        await CreateExportAsync();
        File.WriteAllText(Path.Combine(Source, "config/test.txt"), "user edit");
        var archive = Path.Combine(_root, "edited-export.zip");
        await InstanceExporter.ExportAsync(SourceRoot, "Source", archive);
        var imported = await InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false);
        var manifest = InstallManifestStore.Load(SourceRoot, "Target")!;
        var file = Assert.Single(manifest.ManagedFiles, item => item.RelativePath.EndsWith("config/test.txt"));
        Assert.Null(file.ArchivePath);
        Assert.Equal(1, (await InstallManifestStore.VerifyAsync(SourceRoot, "Target")).Corrupted);
        Assert.Equal(0, await ManagedFileRepair.RepairAsync(SourceRoot, "Target"));
        Assert.Equal(0, await OverridesRepair.RepairAsync(SourceRoot, "Target"));
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(imported.VersionDirectory, "config/test.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalExternalArchiveIsNeverUsedForRepair(bool available)
    {
        await CreateExportAsync();
        var external = Path.Combine(_root, "external-original.zip");
        if (available) File.WriteAllText(external, "not a trusted archive");
        var manifest = InstallManifestStore.Load(SourceRoot, "Source")!;
        manifest.SourceArchive = external;
        InstallManifestStore.Save(SourceRoot, "Source", manifest);
        var archive = Path.Combine(_root, "external-export.zip");
        await InstanceExporter.ExportAsync(SourceRoot, "Source", archive);
        var imported = await InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false);
        File.WriteAllText(Path.Combine(imported.VersionDirectory, "config/test.txt"), "damaged");
        Assert.Equal(1, await ManagedFileRepair.RepairAsync(SourceRoot, "Target"));
        Assert.True((await InstallManifestStore.VerifyAsync(SourceRoot, "Target")).IsHealthy);
        if (available) Assert.Equal("not a trusted archive", File.ReadAllText(external));
        else Assert.False(File.Exists(external));
    }

    [Fact]
    public async Task MissingPrivateLibrarySourceFailsWithoutReadingSourceLibrary()
    {
        await CreateExportAsync();
        var profilePath = Path.Combine(Source, "Source.json");
        var profile = JsonNode.Parse(File.ReadAllText(profilePath))!;
        profile["libraries"]![0]!["downloads"]!["artifact"]!["url"] = null;
        File.WriteAllText(profilePath, profile.ToJsonString());
        var archive = Path.Combine(_root, "no-library.zip");
        await InstanceExporter.ExportAsync(SourceRoot, "Source", archive);
        var original = Snapshot(Source);
        await Assert.ThrowsAsync<InvalidDataException>(() => InstanceImporter.ImportAsync(SourceRoot, archive, "Target", false));
        Assert.Equal(original, Snapshot(Source));
        Assert.False(Directory.Exists(Path.Combine(SourceRoot, "versions/Target")));
        Assert.Empty(Directory.GetDirectories(SourceRoot, ".wmcl-import-*"));
    }

    private async Task<string> CreateExportAsync()
    {
        Directory.CreateDirectory(Path.Combine(Source, "config"));
        Directory.CreateDirectory(Path.Combine(Source, "saves/World"));
        File.WriteAllText(Path.Combine(Source, "Source.json"), """
            {"id":"Source","clientVersion":"1.20.1","jar":"Source","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
             "downloads":{"client":{"path":"Source.jar"}},"libraries":[
             {"name":"custom:launch:1","downloads":{"artifact":{"path":"wmcl-packs/Source/custom.jar","url":"https://example.invalid/custom.jar"}}},
             {"name":"shared:library:1","downloads":{"artifact":{"path":"shared/library.jar"}}}]}
            """);
        File.WriteAllText(Path.Combine(Source, "Source.jar"), "jar");
        File.WriteAllText(Path.Combine(Source, "config/test.txt"), "config");
        File.WriteAllText(Path.Combine(Source, "config/user.txt"), "unmanaged user content");
        File.WriteAllText(Path.Combine(Source, "saves/World/level.dat"), "player progress");
        var manifest = new InstallManifest
        {
            SourceArchive = "/nonexistent/external/source.zip",
            OverridesRoot = Source,
            OverridesPrefix = "overrides/",
            ManagedArchives = { ["old"] = new(["https://example.invalid/old.zip"]) }
        };
        foreach (var relative in new[] { "Source.json", "Source.jar", "config/test.txt", "saves/World/level.dat" })
            manifest.ManagedFiles.Add(new("versions/Source/" + relative,
                await InstallManifestStore.ComputeFileSha1Async(Path.Combine(Source, relative)),
                relative.StartsWith("config/") || relative.StartsWith("saves/") ? "overrides" : "merged",
                "https://example.invalid/Source.json", "overrides/Source.json", "old"));
        manifest.ManagedFiles.Add(new("libraries/wmcl-packs/Source/custom.jar", new string('0', 40), "library"));
        manifest.ManagedFiles.Add(new("assets/shared", new string('0', 40), "assets"));
        manifest.ManagedFiles.Add(new("versions/Source/config/omitted.txt", new string('0', 40), "overrides"));
        InstallManifestStore.Save(SourceRoot, "Source", manifest);
        var archive = Path.Combine(_root, "export.zip");
        await InstanceExporter.ExportAsync(SourceRoot, "Source", archive);
        return archive;
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path))))
        .ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class InlineProgress(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }
}
