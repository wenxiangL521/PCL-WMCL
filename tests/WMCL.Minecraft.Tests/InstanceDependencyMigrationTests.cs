using System.Text.Json.Nodes;
using WMCL.Core.IO;

namespace WMCL.Minecraft.Tests;

public sealed class InstanceDependencyMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-instance-dependencies-" + Guid.NewGuid().ToString("N"));

    public InstanceDependencyMigrationTests()
    {
        Profile("Parent", """{"id":"Parent","clientVersion":"1.20.1","mainClass":"Parent.Main","libraries":[]}""");
        Profile("Child", """{"id":"Child","inheritsFrom":"Parent","jar":"Parent","mainClass":"Child.Main","custom":"Parent"}""");
        Profile("Grandchild", """{"id":"Grandchild","inheritsFrom":"Child","mainClass":"Grandchild.Main"}""");
        Profile("Unrelated", """{"id":"Unrelated","clientVersion":"1.20.4","custom":"Parent","libraries":[]}""");
        Profile("JarOnly", """{"id":"JarOnly","clientVersion":"1.20.1","jar":"Parent","libraries":[]}""");
        foreach (var id in new[] { "Child", "JarOnly" })
            InstallManifestStore.Save(_root, id, new InstallManifest
            {
                ManagedFiles = [new("versions/" + id + "/" + id + ".json", FileUtil.Sha1(PathFor(id)),
                    "merged", "https://example.invalid/original.json", "original.json")]
            });
    }

    [Theory]
    [InlineData("Renamed", false)]
    [InlineData("parent", false)]
    [InlineData("Renamed", true)]
    public async Task RenameMigratesDirectDependenciesAndTheirManagedProfileHashes(string target, bool legacy)
    {
        if (legacy)
        {
            var old = Path.Combine(_root, "versions/Child/PCL/install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            File.Move(InstallManifestStore.GetPath(_root, "Child"), old);
        }
        var grandchild = File.ReadAllBytes(PathFor("Grandchild"));
        var unrelated = File.ReadAllBytes(PathFor("Unrelated"));
        InstanceManager.Rename(_root, "Parent", target);
        var child = JsonNode.Parse(File.ReadAllText(PathFor("Child")))!;
        Assert.Equal(target, child["inheritsFrom"]!.GetValue<string>());
        Assert.Equal(target, child["jar"]!.GetValue<string>());
        Assert.Equal("Parent", child["custom"]!.GetValue<string>());
        Assert.Equal(target, JsonNode.Parse(File.ReadAllText(PathFor("JarOnly")))!["jar"]!.GetValue<string>());
        Assert.Equal(grandchild, File.ReadAllBytes(PathFor("Grandchild")));
        Assert.Equal(unrelated, File.ReadAllBytes(PathFor("Unrelated")));
        var resolved = await new VersionRepository(_root).GetVersionAsync("Grandchild");
        Assert.Equal("1.20.1", resolved.BaseGameVersion);
        Assert.Equal("Grandchild.Main", resolved.MainClass);
        foreach (var id in new[] { "Child", "JarOnly" })
        {
            Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
            var managed = Assert.Single(InstallManifestStore.Load(_root, id)!.ManagedFiles);
            Assert.Null(managed.Url);
            Assert.Null(managed.ArchivePath);
        }
    }

    [Fact]
    public void CloneKeepsExistingChildrenAttachedToTheirOriginalParent()
    {
        var child = File.ReadAllBytes(PathFor("Child"));
        var manifest = File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Child"));
        InstanceManager.Clone(_root, "Parent", "Cloned");
        Assert.Equal(child, File.ReadAllBytes(PathFor("Child")));
        Assert.Equal(manifest, File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Child")));
    }

    [Fact]
    public void DeleteRejectsReferencedParentWithTheDependentNames()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => InstanceManager.Delete(_root, "Parent"));
        Assert.Contains("Child", exception.Message);
        Assert.Contains("JarOnly", exception.Message);
        Assert.True(File.Exists(PathFor("Parent")));
    }

    [Fact]
    public void BrokenDependentManifestPreventsAnyRenameMutation()
    {
        File.WriteAllText(InstallManifestStore.GetPath(_root, "Child"), "invalid json");
        var source = File.ReadAllBytes(PathFor("Parent"));
        var child = File.ReadAllBytes(PathFor("Child"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => InstanceManager.Rename(_root, "Parent", "Renamed"));
        Assert.Equal(source, File.ReadAllBytes(PathFor("Parent")));
        Assert.Equal(child, File.ReadAllBytes(PathFor("Child")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions/Renamed")));
    }

    [Fact]
    public void RenameFailureRestoresParentAndLeavesDependentMetadataUnchanged()
    {
        File.WriteAllText(Path.Combine(_root, "versions/Parent/Renamed.jar"), "collision");
        File.WriteAllText(Path.Combine(_root, "versions/Parent/Parent.jar"), "client");
        var child = File.ReadAllBytes(PathFor("Child"));
        var manifest = File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Child"));
        Assert.Throws<System.IO.IOException>(() => InstanceManager.Rename(_root, "Parent", "Renamed"));
        Assert.True(File.Exists(PathFor("Parent")));
        Assert.Equal(child, File.ReadAllBytes(PathFor("Child")));
        Assert.Equal(manifest, File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Child")));
    }

    private void Profile(string id, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathFor(id))!);
        File.WriteAllText(PathFor(id), text);
    }

    private string PathFor(string id) => Path.Combine(_root, "versions", id, id + ".json");
    public void Dispose() => Directory.Delete(_root, true);
}
