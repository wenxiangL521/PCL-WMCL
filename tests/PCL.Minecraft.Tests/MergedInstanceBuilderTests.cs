using System.Text.Json;

namespace PCL.Minecraft.Tests;

public class MergedInstanceBuilderTests
{
    [Fact]
    public async Task FlattensInheritedLoaderIntoStandaloneInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-merge-" + Guid.NewGuid().ToString("N"));
        var vanilla = Path.Combine(root, "versions", "1.20.1");
        var fabric = Path.Combine(root, "versions", "fabric-loader");
        Directory.CreateDirectory(vanilla);
        Directory.CreateDirectory(fabric);
        await File.WriteAllTextAsync(Path.Combine(vanilla, "1.20.1.json"), """
            {"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"a:b:1"}],"arguments":{"game":["--gameDir","${game_directory}"],"jvm":["-Dfoo=1"]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(vanilla, "1.20.1.jar"), "vanilla");
        await File.WriteAllTextAsync(Path.Combine(fabric, "fabric-loader.json"), """
            {"id":"fabric-loader","inheritsFrom":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:1"}],"arguments":{"game":["--fabric"],"jvm":[]}}
            """);
        await File.WriteAllTextAsync(Path.Combine(fabric, "fabric-loader.jar"), "fabric-jar");

        try
        {
            await MergedInstanceBuilder.BuildAsync(root, "fabric-loader", "MyPack");
            var json = await File.ReadAllTextAsync(Path.Combine(root, "versions", "MyPack", "MyPack.json"));
            using var document = JsonDocument.Parse(json);
            Assert.Equal("MyPack", document.RootElement.GetProperty("id").GetString());
            Assert.False(document.RootElement.TryGetProperty("inheritsFrom", out var inherit) &&
                         inherit.ValueKind != JsonValueKind.Null);
            Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient",
                document.RootElement.GetProperty("mainClass").GetString());
            Assert.Equal(2, document.RootElement.GetProperty("libraries").GetArrayLength());
            Assert.Equal("fabric-jar",
                await File.ReadAllTextAsync(Path.Combine(root, "versions", "MyPack", "MyPack.jar")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            InstallManifestStore.Save(root, "Pack", new InstallManifest
            {
                Components = { ["minecraft"] = "1.20.1", ["fabric"] = "0.19.5" },
                ManagedFiles = [new InstallManifestFile("mods/a.jar", "abc", "Fabric")]
            });
            var manifest = InstallManifestStore.Load(root, "Pack");
            Assert.NotNull(manifest);
            Assert.Equal("0.19.5", manifest.Components["fabric"]);
            Assert.Single(manifest.ManagedFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
