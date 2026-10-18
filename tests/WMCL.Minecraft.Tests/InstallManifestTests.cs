using WMCL.Minecraft;

namespace WMCL.Minecraft.Tests;

public class InstallManifestTests
{
    [Fact]
    public async Task VerifiesManagedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-verify-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Test");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "Test.json"), "{}");
        try
        {
            var files = await InstallManifestStore.CollectManagedFilesAsync(root, "Test");
            files[0] = files[0] with { Url = "https://example.com/file" };
            InstallManifestStore.Save(root, "Test", new InstallManifest { ManagedFiles = files });
            Assert.Equal("https://example.com/file",
                InstallManifestStore.Load(root, "Test")!.ManagedFiles[0].Url);
            var healthy = await InstallManifestStore.VerifyAsync(root, "Test");
            Assert.True(healthy.IsHealthy);
            File.WriteAllText(Path.Combine(version, "Test.json"), "changed");
            var broken = await InstallManifestStore.VerifyAsync(root, "Test");
            Assert.Equal(1, broken.Corrupted);
            Assert.False(broken.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CollectsJsonJarAndNatives()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-manifest-" + Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "Test");
        Directory.CreateDirectory(Path.Combine(version, "natives"));
        File.WriteAllText(Path.Combine(version, "Test.json"), "{}");
        File.WriteAllText(Path.Combine(version, "Test.jar"), "jar");
        File.WriteAllText(Path.Combine(version, "natives", "lib.so"), "native");
        try
        {
            var files = await InstallManifestStore.CollectManagedFilesAsync(root, "Test");
            Assert.Equal(3, files.Count);
            Assert.Contains(files, f => f.RelativePath == "versions/Test/Test.json");
            Assert.Contains(files, f => f.RelativePath == "versions/Test/natives/lib.so");
            Assert.All(files, f => Assert.Equal(40, f.Sha1.Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
