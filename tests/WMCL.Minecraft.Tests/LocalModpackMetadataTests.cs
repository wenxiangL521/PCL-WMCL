using System.IO.Compression;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed class LocalModpackMetadataTests
{
    [Theory]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"org.quiltmc.quilt-loader","version":"0.28.0"}]}""")]
    [InlineData("mcbbs.packmeta", """{"addons":[{"id":"game","version":"1.20.1"},{"id":"quilt","version":"0.28.0"}]}""")]
    [InlineData("manifest.json", """{"addons":{"game":"1.20.1","quilt":"0.28.0"}}""")]
    public void ReadsQuiltFromLocalPackComponents(string name, string metadata)
    {
        using var archive = Archive((name, metadata));
        var info = LocalModpackInstaller.Parse(archive, LocalModpackInstaller.Detect(archive));
        Assert.Equal("1.20.1", info.Minecraft);
        Assert.Equal("0.28.0", info.QuiltLoader);
        Assert.Null(info.FabricLoader);
    }

    [Theory]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.10"},{"uid":"org.quiltmc.quilt-loader","version":"0.28.0"}]}""")]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.minecraft","version":"1.21.1"}]}""")]
    [InlineData("mcbbs.packmeta", """{"addons":[{"id":"game","version":"1.20.1"},{"id":"quilt","version":"0.28.0"},{"id":"quilt","version":"0.28.0"}]}""")]
    [InlineData("manifest.json", """{"addons":{"game":"1.20.1","forge":"47.2.18","neoforge":"21.1.80"}}""")]
    [InlineData("manifest.json", """{"addons":{"game":"1.20.1","game":"1.21.1"}}""")]
    [InlineData("manifest.json", """{"addons":{"game":"1.20.1","quilt":""}}""")]
    public void RejectsConflictingOrMissingComponentVersions(string name, string metadata)
    {
        using var archive = Archive((name, metadata));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Parse(archive, LocalModpackInstaller.Detect(archive)));
    }

    [Theory]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.12.2"},{"uid":"com.mumfrey.liteloader","version":"1.12.2"}]}""", "com.mumfrey.liteloader")]
    [InlineData("mcbbs.packmeta", """{"addons":{"game":"1.12.2","optifine":"HD_U_G5"}}""", "optifine")]
    [InlineData("mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.12.2"},{"uid":"net.minecraftforge","version":"0.3.0"}]}""", "Cleanroom")]
    public void UnsupportedComponentsAreReportedInsteadOfIgnored(string name, string metadata, string component)
    {
        using var archive = Archive((name, metadata));
        var error = Assert.Throws<NotSupportedException>(() => LocalModpackInstaller.Parse(archive, LocalModpackInstaller.Detect(archive)));
        Assert.Contains(component, error.Message);
    }

    [Fact]
    public void MetadataAndSettingsComeFromTheSameNearestInstance()
    {
        using var archive = Archive(
            ("other/instance.cfg", "name=Wrong Instance"),
            ("wrapped/instance.cfg", "name=Right Instance"),
            ("wrapped/mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"org.lwjgl3","version":"3.3.1"}]}"""),
            ("wrapped/.minecraft/config/test.txt", "dotted"),
            ("wrapped/minecraft/config/test.txt", "preferred"),
            ("wrapped/minecraft/config/modpack.json", "{}"));
        var info = LocalModpackInstaller.Parse(archive, LocalModpackInstaller.Detect(archive));
        Assert.Equal(LocalModpackKind.Mmc, info.Kind);
        Assert.Equal("Right Instance", info.Name);
        Assert.Equal("wrapped/", info.BaseFolder);
        Assert.Equal("wrapped/minecraft/", info.OverridesFolder);
    }

    [Theory]
    [InlineData("one/mmc-pack.json", "two/mmc-pack.json")]
    [InlineData("one/mmc-pack.json", "two/mcbbs.packmeta")]
    [InlineData("mmc-pack.json", "MMC-PACK.JSON")]
    [InlineData("mmc-pack.json", "modpack.json")]
    public void RejectsAmbiguousPackRoots(string first, string second)
    {
        using var archive = Archive((first, "{}"), (second, "{}"));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Detect(archive));
    }

    [Theory]
    [InlineData("""{"jar":"1.20.1","patches":[{"id":"game","version":"1.20.1"},{"id":"quilt","version":"0.28.0"}]}""", "quilt", "0.28.0")]
    [InlineData("""{"patches":[{"id":"game","version":"1.20.1"},{"id":"fabric","version":"0.16.10"}]}""", "fabric", "0.16.10")]
    [InlineData("""{"jar":"1.20.1","libraries":[{"name":"org.quiltmc:quilt-loader:0.28.0"}]}""", "quilt", "0.28.0")]
    [InlineData("""{"jar":"1.20.1","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.18"},{"name":"net.minecraftforge:fmlloader:1.20.1-47.2.18"}]}""", "forge", "47.2.18")]
    [InlineData("""{"jar":"1.20.1","arguments":null,"libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.18"}]}""", "forge", "47.2.18")]
    [InlineData("""{"jar":"1.21.1","libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.35"},{"name":"net.neoforged:neoforge:21.1.80"}]}""", "neoforge", "21.1.80")]
    [InlineData("""{"jar":"1.21.1","libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.35"}],"arguments":{"game":["--fml.neoForgeVersion","21.1.80"]}}""", "neoforge", "21.1.80")]
    [InlineData("""{"jar":"1.21.1","patches":[{"id":"game","version":"1.21.1"},{"id":"neoforge","version":"21.1.80","libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.35"}]}]}""", "neoforge", "21.1.80")]
    public void ReadsHmclPatchedAndFlattenedComponents(string profile, string loader, string version)
    {
        using var archive = Archive(("modpack.json", """{"name":"HMCL Pack","gameVersion":"old-manifest-value"}"""), ("minecraft/pack.json", profile));
        var info = LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl);
        Assert.Equal(loader == "neoforge" ? "1.21.1" : "1.20.1", info.Minecraft);
        Assert.Equal(version, loader switch { "quilt" => info.QuiltLoader, "fabric" => info.FabricLoader, "forge" => info.Forge, _ => info.NeoForge });
    }

    [Theory]
    [InlineData("""{"jar":"1.20.1","patches":[{"id":"game","version":"1.21.1"}]}""")]
    [InlineData("""{"jar":"1.20.1","patches":[{"id":"quilt","version":"0.28.0"},{"id":"fabric","version":"0.16.10"}]}""")]
    [InlineData("""{"jar":"1.20.1","patches":[{"id":"quilt","version":"0.28.0","libraries":[{"name":"org.quiltmc:quilt-loader:0.27.0"}]}]}""")]
    [InlineData("""{"jar":"1.20.1","libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.35"}]}""")]
    public void RejectsInconsistentHmclMetadata(string profile)
    {
        using var archive = Archive(("modpack.json", "{}"), ("minecraft/pack.json", profile));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl));
    }

    [Theory]
    [InlineData("""{"jar":"1.20.1","patches":[{"id":"custom-loader","version":"1"}]}""")]
    [InlineData("""{"jar":"1.20.1","inheritsFrom":"outside-profile"}""")]
    [InlineData("""{"jar":"1.20.1","mainClass":"custom.loader.Main"}""")]
    [InlineData("""{"jar":"1.20.1","libraries":[{"name":"optifine:OptiFine:1.20.1_HD_U_I6"}]}""")]
    public void RejectsUnsupportedHmclComponents(string profile)
    {
        using var archive = Archive(("modpack.json", "{}"), ("minecraft/pack.json", profile));
        Assert.Throws<NotSupportedException>(() => LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl));
    }

    [Theory]
    [InlineData("{\"jar\":\"1.20.1\",\"libraries\":[{\"name\":\"custom:one:1\",\"downloads\":{\"artifact\":{\"path\":\"../escape.jar\"}}}]}")]
    [InlineData("{\"jar\":\"1.20.1\",\"libraries\":[{\"name\":\"custom:one:1\",\"downloads\":{\"artifact\":{\"url\":\"file:///tmp/one.jar\"}}}]}")]
    [InlineData("{\"jar\":\"1.20.1\",\"libraries\":[{\"name\":\"custom:one:1\",\"downloads\":{\"artifact\":{\"sha1\":\"bad\"}}}]}")]
    public void RejectsUnsafeHmclCustomLibraryDeclarations(string profile)
    {
        using var archive = Archive(("modpack.json", "{}"), ("minecraft/pack.json", profile));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl));
    }

    [Fact]
    public void HmclProfileMustBelongToTheSelectedPack()
    {
        using var archive = Archive(("selected/modpack.json", """{"gameVersion":"1.20.1"}"""), ("other/minecraft/pack.json", """{"jar":"1.20.1"}"""));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl));
    }

    [Fact]
    public void ExplicitHmclRootRebuildsFromPatchesInsteadOfItsCachedLaunchFields()
    {
        using var archive = Archive(("modpack.json", "{}"), ("minecraft/pack.json", """
            {"jar":"1.20.1","root":true,"libraries":[{"name":"net.fabricmc:fabric-loader:0.1.0"}],
             "patches":[{"id":"game","version":"1.20.1"},{"id":"quilt","version":"0.28.0"}]}
            """));
        var info = LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl);
        Assert.Equal("0.28.0", info.QuiltLoader);
        Assert.Null(info.FabricLoader);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"root\":false,")]
    public void HmclRootFlagDefaultsToFalseAndRetainsStandaloneComponents(string flag)
    {
        var profile = "{\"jar\":\"1.20.1\"," + flag + "\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.16.10\"}],\"patches\":[{\"id\":\"game\",\"version\":\"1.20.1\"},{\"id\":\"quilt\",\"version\":\"0.28.0\"}]}";
        using var archive = Archive(("modpack.json", "{}"), ("minecraft/pack.json", profile));
        Assert.Throws<InvalidDataException>(() => LocalModpackInstaller.Parse(archive, LocalModpackKind.Hmcl));
    }

    private static ZipArchive Archive(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
                writer.Write(entry.Content);
            }
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }
}
