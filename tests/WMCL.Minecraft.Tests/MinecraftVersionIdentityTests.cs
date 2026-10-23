using System.IO.Compression;
using System.Text.Json;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

public sealed partial class MinecraftVersionIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-identity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NestedInheritanceAndFlatteningPreserveTheRootGameVersion()
    {
        await WriteProfileAsync("1.20.1", new VersionJson { Id = "1.20.1" });
        await WriteProfileAsync("loader", new VersionJson { Id = "loader", InheritsFrom = "1.20.1" });
        await WriteProfileAsync("custom", new VersionJson
        {
            Id = "custom",
            InheritsFrom = "loader",
            BaseGameVersion = "stale-child-value"
        });
        await File.WriteAllTextAsync(Path.Combine(ProfileDirectory("1.20.1"), "1.20.1.jar"), "vanilla-client");
        var repository = new VersionRepository(_root);

        var inherited = await repository.GetVersionAsync("custom");
        Assert.Equal("loader", inherited.InheritsFrom);
        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion("custom", inherited));

        await MergedInstanceBuilder.BuildAsync(_root, "custom", "RenamedPack");
        foreach (var source in new[] { "1.20.1", "loader", "custom" })
            Directory.Delete(ProfileDirectory(source), recursive: true);

        var flattened = await repository.GetVersionAsync("RenamedPack");
        Assert.Null(flattened.InheritsFrom);
        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion("RenamedPack", flattened));
        Assert.Equal("vanilla-client", await File.ReadAllTextAsync(Path.Combine(ProfileDirectory("RenamedPack"), "RenamedPack.jar")));
        Assert.Equal("1.20.1", VersionMerger.Clone(flattened).BaseGameVersion);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ProfileDirectory("RenamedPack"), "RenamedPack.json")));
        Assert.Equal("1.20.1", document.RootElement.GetProperty("clientVersion").GetString());
    }

    [Theory]
    [InlineData("WMCL")]
    [InlineData("PCL")]
    public async Task OldFlattenedProfilesUseTheirManifestWithoutRewritingManagedFiles(string manifestDirectory)
    {
        await WriteProfileAsync("MyPack", new VersionJson { Id = "MyPack", Assets = "unrelated-asset-index" });
        InstallManifestStore.Save(_root, "MyPack", new InstallManifest
        {
            Components = { ["minecraft"] = "1.20.1" },
            ManagedFiles = await InstallManifestStore.CollectManagedFilesAsync(_root, "MyPack")
        });
        if (manifestDirectory == "PCL")
        {
            Directory.CreateDirectory(Path.Combine(ProfileDirectory("MyPack"), "PCL"));
            File.Move(InstallManifestStore.GetPath(_root, "MyPack"), Path.Combine(ProfileDirectory("MyPack"), "PCL", "install.json"));
        }
        var jsonPath = Path.Combine(ProfileDirectory("MyPack"), "MyPack.json");
        var originalJson = await File.ReadAllBytesAsync(jsonPath);

        var version = await new VersionRepository(_root).GetVersionAsync("MyPack");

        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion("MyPack", version));
        Assert.Equal(originalJson, await File.ReadAllBytesAsync(jsonPath));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "MyPack")).IsHealthy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Components\":null}")]
    [InlineData("{\"Components\":{\"minecraft\":\" \"}}")]
    public async Task ImportedClientMetadataRecoversIdentityEvenWhenOptionalManifestIsInvalid(string? manifest)
    {
        await WriteProfileAsync("1.99-FakeName", new VersionJson { Id = "1.99-FakeName", Assets = "1.19" });
        if (manifest is not null)
        {
            var manifestPath = InstallManifestStore.GetPath(_root, "1.99-FakeName");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            await File.WriteAllTextAsync(manifestPath, manifest);
        }
        await WriteClientMetadataAsync("1.99-FakeName", "{\"id\":\"24w14a\"}");

        var version = await new VersionRepository(_root).GetVersionAsync("1.99-FakeName");

        Assert.Equal("24w14a", MinecraftVersionUtil.GetBaseGameVersion("1.99-FakeName", version));
    }

    [Fact]
    public async Task PersistedIdentityTakesPrecedenceOverStaleOptionalMetadata()
    {
        await WriteProfileAsync("Pack", new VersionJson { Id = "Pack", BaseGameVersion = "1.20.1" });
        InstallManifestStore.Save(_root, "Pack", new InstallManifest { Components = { ["minecraft"] = "1.19" } });
        await WriteClientMetadataAsync("Pack", "{\"id\":\"1.18\"}");

        var version = await new VersionRepository(_root).GetVersionAsync("Pack");

        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion("Pack", version));
    }

    [Theory]
    [InlineData("{\"clientVersion\":\"1.20.1\"}")]
    [InlineData("{\"arguments\":{\"game\":[\"--fml.mcVersion\",\"1.20.1\"]}}")]
    [InlineData("{\"libraries\":[{\"name\":\"net.fabricmc:intermediary:1.20.1\"}]}")]
    [InlineData("{\"libraries\":[{\"name\":\"org.quiltmc:intermediary:1.20.1\"}]}")]
    [InlineData("{\"libraries\":[{\"name\":\"net.legacyfabric:intermediary:1.20.1\"}]}")]
    public async Task ImportedProfilesUseExplicitGameMetadataWithoutParsingTheirNames(string json)
    {
        var imported = JsonSerializer.Deserialize<VersionJson>(json)!;
        imported.Id = "1.99-FakeName";
        imported.Assets = "1.19";
        await WriteProfileAsync(imported.Id, imported);

        var version = await new VersionRepository(_root).GetVersionAsync(imported.Id);

        Assert.Equal("1.20.1", MinecraftVersionUtil.GetBaseGameVersion(imported.Id, version));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"id\":12}")]
    [InlineData("{\"id\":\" \"}")]
    public async Task InvalidClientMetadataKeepsTheExistingVanillaFallback(string clientMetadata)
    {
        await WriteProfileAsync("1.12.2", new VersionJson { Id = "1.12.2" });
        await WriteClientMetadataAsync("1.12.2", clientMetadata);

        var version = await new VersionRepository(_root).GetVersionAsync("1.12.2");

        Assert.Equal("1.12.2", MinecraftVersionUtil.GetBaseGameVersion("1.12.2", version));
    }

    [Fact]
    public async Task CancellationIsNotHiddenByOptionalMetadataFallback()
    {
        await WriteProfileAsync("Pack", new VersionJson { Id = "Pack" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VersionRepository(_root).GetVersionAsync("Pack", cancellation.Token));
    }

    private string ProfileDirectory(string id) => Path.Combine(_root, "versions", id);

    private async Task WriteProfileAsync(string id, VersionJson version)
    {
        Directory.CreateDirectory(ProfileDirectory(id));
        await File.WriteAllTextAsync(Path.Combine(ProfileDirectory(id), id + ".json"), JsonSerializer.Serialize(version));
    }

    private async Task WriteClientMetadataAsync(string id, string metadata)
    {
        using var archive = ZipFile.Open(Path.Combine(ProfileDirectory(id), id + ".jar"), ZipArchiveMode.Create);
        await using var writer = new StreamWriter(archive.CreateEntry("version.json").Open());
        await writer.WriteAsync(metadata);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
