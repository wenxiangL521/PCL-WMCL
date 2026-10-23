using System.Text.Json.Nodes;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ANewerManifestIsRejectedBeforeStagingOrCallingTheBackend(bool legacy, bool invalidProfile)
    {
        CreateInstance();
        if (invalidProfile) File.WriteAllText(Path.Combine(DirectoryFor(), "Custom.json"), "{");
        var path = InstallManifestStore.GetPath(_root, "Custom");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["SchemaVersion"] = InstallManifestStore.SupportedSchemaVersion + 1;
        manifest["FutureComponent"] = new JsonObject { ["value"] = "preserve" };
        File.WriteAllText(path, manifest.ToJsonString());
        if (legacy)
        {
            var legacyPath = Path.Combine(DirectoryFor(), "PCL/install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.Move(path, legacyPath);
        }
        var original = Snapshot();
        var backend = new Backend();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Service(backend).InstallAsync(Plan(), (_, _, _) => throw new InvalidOperationException("不应开始安装")));
        Assert.Contains("请更新 WMCL", error.Message);
        Assert.Equal(0, backend.VanillaCalls);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMissingSchemaVersionRemainsCompatibleWithExistingManifests(bool legacy)
    {
        CreateInstance();
        var path = InstallManifestStore.GetPath(_root, "Custom");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest.Remove("SchemaVersion");
        File.WriteAllText(path, manifest.ToJsonString());
        if (legacy)
        {
            var legacyPath = Path.Combine(DirectoryFor(), "PCL/install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.Move(path, legacyPath);
        }
        var backend = new Backend();
        await Service(backend).InstallAsync(Plan(), (_, _, _) => { });
        Assert.Equal(1, backend.VanillaCalls);
        Assert.Equal("0.16.0", InstallManifestStore.Load(_root, "Custom")!.Components["fabric"]);
    }
}
