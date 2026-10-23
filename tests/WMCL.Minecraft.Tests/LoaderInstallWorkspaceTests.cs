using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;

namespace WMCL.Minecraft.Tests;

public sealed class LoaderInstallWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-workspace-test-" + Guid.NewGuid().ToString("N"));
    private readonly VersionRepository _repository;

    public LoaderInstallWorkspaceTests()
    {
        _repository = new VersionRepository(_root);
        var vanilla = _repository.GetVersionDirectory("1.20.1");
        Directory.CreateDirectory(vanilla);
        File.WriteAllText(Path.Combine(vanilla, "1.20.1.json"), """
            {"id":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"test:vanilla:1","downloads":{"artifact":{"path":"test/vanilla.jar"}}}]}
            """);
        File.WriteAllText(Path.Combine(vanilla, "1.20.1.jar"), "original client");
        Directory.CreateDirectory(Path.Combine(_root, "libraries", "test"));
        File.WriteAllText(Path.Combine(_root, "libraries", "test", "vanilla.jar"), "original vanilla library");
        File.WriteAllText(Path.Combine(_root, "launcher_profiles.json"), "original launcher profiles");
    }

    [Fact]
    public async Task SnapshotsVanillaWithIndependentCopiesAndDisposesOnlyItsOwnRoot()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json"));
        string staging;
        using (var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, "1.20.1"))
        {
            staging = workspace.Repository.GameDirectory;
            Assert.NotEqual(_root, staging);
            var directory = workspace.Repository.GetVersionDirectory("1.20.1");
            Assert.Equal("original client", await File.ReadAllTextAsync(Path.Combine(directory, "1.20.1.jar")));
            await File.WriteAllTextAsync(Path.Combine(directory, "1.20.1.jar"), "installer changed client");
            await File.WriteAllTextAsync(Path.Combine(directory, "1.20.1.json"), "installer changed profile");
            await File.WriteAllTextAsync(Path.Combine(staging, "libraries", "test", "vanilla.jar"), "installer changed library");
            await File.WriteAllTextAsync(Path.Combine(staging, "launcher_profiles.json"), "installer profiles");
        }
        Assert.False(Directory.Exists(staging));
        Assert.Equal("original client", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.jar")));
        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("1.20.1"), "1.20.1.json")));
        Assert.Equal("original vanilla library", await File.ReadAllTextAsync(Path.Combine(_root, "libraries", "test", "vanilla.jar")));
        Assert.Equal("original launcher profiles", await File.ReadAllTextAsync(Path.Combine(_root, "launcher_profiles.json")));
    }

    [Theory]
    [InlineData("forge", "net.minecraftforge:forge:1.20.1-47.2.18")]
    [InlineData("neoforge", "net.neoforged:neoforge:20.1.80")]
    public async Task MergesInstalledProfileAndGeneratedLibrariesWithoutChangingExistingInstances(string kind, string library)
    {
        var existing = _repository.GetVersionDirectory("existing-" + kind);
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "user.txt"), "existing user data");
        using var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, "1.20.1");
        var source = workspace.Repository.GetVersionDirectory("installed");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "installed.json"), """
            {"id":"installed","inheritsFrom":"1.20.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"LIBRARY"}]}
            """.Replace("LIBRARY", library));
        var stagedLibrary = Path.Combine(workspace.Repository.GameDirectory, "libraries", "test", "generated.jar");
        await File.WriteAllTextAsync(stagedLibrary, "installer output");
        await workspace.MergeAsync("installed", "NewPack");
        var version = await _repository.GetVersionAsync("NewPack");
        Assert.Null(version.InheritsFrom);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal(kind, MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", version.MainClass);
        Assert.Equal("original client", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory("NewPack"), "NewPack.jar")));
        Assert.Equal("installer output", await File.ReadAllTextAsync(Path.Combine(_root, "libraries", "test", "generated.jar")));
        Assert.Equal("existing user data", await File.ReadAllTextAsync(Path.Combine(existing, "user.txt")));
        Assert.Equal("original launcher profiles", await File.ReadAllTextAsync(Path.Combine(_root, "launcher_profiles.json")));
    }

    [Fact]
    public async Task ConflictingSharedLibrariesArePreservedAndNoTargetInstanceIsCreated()
    {
        using var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, "1.20.1");
        await File.WriteAllTextAsync(Path.Combine(workspace.Repository.GameDirectory, "libraries", "test", "vanilla.jar"), "different installer output");
        var error = await Assert.ThrowsAsync<IOException>(() => workspace.MergeAsync("1.20.1", "Conflict"));
        Assert.Contains("vanilla.jar", error.Message);
        Assert.Equal("original vanilla library", await File.ReadAllTextAsync(Path.Combine(_root, "libraries", "test", "vanilla.jar")));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Conflict")));
    }

    [Fact]
    public async Task ExistingTargetIsRejectedBeforeCopyingInstallerLibraries()
    {
        var target = _repository.GetVersionDirectory("Existing");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "user.txt"), "keep");
        using var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, "1.20.1");
        await File.WriteAllTextAsync(Path.Combine(workspace.Repository.GameDirectory, "libraries", "test", "new.jar"), "new library");
        await Assert.ThrowsAsync<IOException>(() => workspace.MergeAsync("1.20.1", "Existing"));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(target, "user.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "libraries", "test", "new.jar")));
        var fabric = new FabricInstaller(_repository);
        await Assert.ThrowsAsync<IOException>(() => fabric.InstallMergedAsync("1.20.1", "0.28.0", "Existing"));
        var forge = new ForgelikeInstaller(_repository);
        await Assert.ThrowsAsync<IOException>(() => forge.InstallMergedAsync(ForgelikeKind.Forge, "1.20.1", "47.2.18", "Existing", "unused-java"));
    }

    [Fact]
    public async Task CancellationAndFailureDisposeStagingWithoutChangingTheGameRoot()
    {
        string staging;
        using (var workspace = await LoaderInstallWorkspace.CreateAsync(_repository, "1.20.1"))
        {
            staging = workspace.Repository.GameDirectory;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.MergeAsync("1.20.1", "Cancelled", cancellation.Token));
            await Assert.ThrowsAsync<FileNotFoundException>(() => workspace.MergeAsync("missing-loader", "Failed"));
        }
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Cancelled")));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Failed")));
        Assert.Equal("original launcher profiles", await File.ReadAllTextAsync(Path.Combine(_root, "launcher_profiles.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
