using System.IO.Compression;
using System.Net;
using System.Text;
using PCL.Modding.CurseForge;
using PCL.Modding.Modpack;

namespace PCL.Minecraft.Tests;

public class CurseForgeModpackTests
{
    [Fact]
    public async Task ListsOptionalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-cf-opt-" + Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(root, "pack.zip");
        Directory.CreateDirectory(root);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write("""
                {"minecraft":{"version":"1.20.1"},"name":"Pack","version":"1",
                 "files":[{"projectID":111,"fileID":222,"required":false}]}
                """);
        }
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"data":{"id":222,"displayName":"Optional JEI","fileName":"jei.jar"}}
                """, Encoding.UTF8, "application/json")
        });
        try
        {
            var installer = new CurseForgeModpackInstaller(
                new VersionRepository(root),
                client: new CurseForgeClient("key", new HttpClient(handler)));
            var files = await installer.GetOptionalFilesAsync(zip);
            Assert.Single(files);
            Assert.Equal("Optional JEI", files[0].DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public void ParsesManifestAndPrimaryLoader()
    {
        var manifest = CurseForgeModpackInstaller.ParseManifest("""
        {
          "minecraft": {"version": "1.20.1", "modLoaders": [{"id": "forge-47.2.18", "primary": true}]},
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "Test Pack",
          "version": "1.2.3",
          "files": [
            {"projectID": 238222, "fileID": 5000000, "required": true},
            {"projectID": 111, "fileID": 222, "required": false}
          ],
          "overrides": "overrides"
        }
        """);

        Assert.Equal("Test Pack", manifest.Name);
        Assert.Equal("1.20.1", manifest.Minecraft);
        Assert.Equal("forge-47.2.18", manifest.LoaderId);
        Assert.Equal("overrides", manifest.Overrides);
        Assert.Equal(2, manifest.Files.Count);
        Assert.True(manifest.Files[0].Required);
        Assert.False(manifest.Files[1].Required);
    }
}
