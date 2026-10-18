using PCL.Minecraft;
using PCL.Minecraft.Models;

namespace PCL.Minecraft.Tests;

public class LegacyAssetExtractorTests
{
    [Fact]
    public void CopiesVirtualAssetsAndSkipsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-legacy-" + Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "assets");
        var objects = Path.Combine(assets, "objects", "ab");
        Directory.CreateDirectory(objects);
        File.WriteAllText(Path.Combine(objects, "abcdef"), "data");
        var index = new AssetIndex
        {
            Virtual = true,
            Objects = new Dictionary<string, AssetObject>
            {
                ["sound/example.ogg"] = new() { Hash = "abcdef", Size = 4 },
                ["../escape.txt"] = new() { Hash = "abcdef", Size = 4 }
            }
        };
        try
        {
            var copied = LegacyAssetExtractor.Extract(index, assets, root, "legacy");
            Assert.Equal(1, copied);
            Assert.True(File.Exists(Path.Combine(
                assets, "virtual", "legacy", "sound", "example.ogg")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
