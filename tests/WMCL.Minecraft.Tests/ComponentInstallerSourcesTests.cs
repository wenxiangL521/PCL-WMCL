using System.Text.Json;
using WMCL.Modding.Forgelike;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Loaders;
using WMCL.Modding.OptiFine;

namespace WMCL.Minecraft.Tests;

public sealed class ComponentInstallerSourcesTests
{
    [Theory]
    [InlineData("1.20.1", "HD_U", "I6", "OptiFine_1.20.1_HD_U_I6.jar", "1.20.1/HD_U/I6")]
    [InlineData("1.8", "HD_U", "I7", "OptiFine_1.8_HD_U_I7.jar", "1.8.0/HD_U/I7")]
    [InlineData("1.20.1", "HD_U", "I6_pre1", "preview_OptiFine_1.20.1_HD_U_I6_pre1.jar", "1.20.1/HD_U_I6/pre1")]
    [InlineData("1.20.1", "HD_U_I6", "pre1", "preview_OptiFine_1.20.1_HD_U_I6_pre1.jar", "1.20.1/HD_U_I6/pre1")]
    public void OptiFineMirrorUsesExactSelectedFilenameForBothCatalogRepresentations(string minecraft, string type,
        string patch, string filename, string suffix) =>
        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/" + suffix,
            OptiFineService.GetMirrorDownloadUrl(new(minecraft, type, patch, filename, "")));

    [Theory]
    [InlineData("1.20.1", "47.2.18", null, "1.20.1-47.2.18")]
    [InlineData("1.8.9", "11.15.1.2318", null, "1.8.9-11.15.1.2318-1.8.9")]
    [InlineData("1.7.10", "10.13.4.1614", null, "1.7.10-10.13.4.1614-1.7.10")]
    [InlineData("1.7.10", "10.13.0.1208", null, "1.7.10-10.13.0.1208")]
    [InlineData("1.7.10", "10.13.0.1208", "custom", "1.7.10-10.13.0.1208-custom")]
    public void ForgeUsesOriginalBranchRulesWithoutPuttingTheBranchInSuggestedName(string minecraft, string version,
        string? branch, string coordinate)
    {
        var artifact = ComponentInstallerSources.Forge(minecraft, new(version, null, Branch: branch, InstallerHash: "fixture"));
        Assert.Equal($"Forge-{minecraft}-{version}.jar", artifact.FileName);
        Assert.All(artifact.Urls, url => Assert.EndsWith($"/{coordinate}/forge-{coordinate}-installer.jar", url));
        Assert.Equal(64 * 1024, artifact.MinimumSize);
        Assert.Equal("fixture", artifact.Hash);
        Assert.EndsWith($"/{minecraft}-{version}/forge-{minecraft}-{version}-changelog.txt",
            ComponentInstallerSources.ForgeChangelog(minecraft, version));
    }

    [Theory]
    [InlineData("1.20.1", "47.1.99", "forge/1.20.1-47.1.99/forge-1.20.1-47.1.99")]
    [InlineData("1.20.1", "1.20.1-47.1.99", "forge/1.20.1-47.1.99/forge-1.20.1-47.1.99")]
    [InlineData("1.20.4", "20.4.30-beta", "neoforge/20.4.30-beta/neoforge-20.4.30-beta")]
    public void NeoForgeKeepsLegacyApiCoordinatesAndMirrorFallback(string minecraft, string version, string suffix)
    {
        var artifact = ComponentInstallerSources.NeoForge(minecraft, version);
        Assert.Equal(2, artifact.Urls.Count);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven/", artifact.Urls[0]);
        Assert.All(artifact.Urls, url => Assert.EndsWith(suffix + "-installer.jar", url));
        Assert.EndsWith(suffix, ComponentInstallerSources.NeoForgeBase(minecraft, version));
        Assert.Equal(64 * 1024, artifact.MinimumSize);
    }

    [Theory]
    [InlineData("1.7.10", "https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar")]
    [InlineData("1.7.2", "https://dl.liteloader.com/redist/1.7.2/liteloader-installer-1.7.2-04.jar")]
    [InlineData("1.8", "http://jenkins.liteloader.com/job/LiteLoaderInstaller%201.8/lastSuccessfulBuild/artifact/ant/dist/liteloader-installer-1.8.0-00-SNAPSHOT.jar")]
    [InlineData("1.12.2", "http://jenkins.liteloader.com/job/LiteLoaderInstaller%201.12.2/lastSuccessfulBuild/artifact/build/libs/liteloader-installer-1.12.2-00-SNAPSHOT.jar")]
    public void LiteLoaderUsesInstallerNotTheLibraryUrlAndPreservesOriginalSizeLimit(string minecraft, string url)
    {
        var version = new LiteLoaderVersion(minecraft, minecraft + "-SNAPSHOT",
            "com.mumfrey.liteloader.launch.LiteLoaderTweaker", true, null, null,
            JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var artifact = ComponentInstallerSources.LiteLoader(version);
        Assert.Equal(url, Assert.Single(artifact.Urls));
        Assert.DoesNotContain("-SNAPSHOT", artifact.FileName);
        Assert.Null(artifact.Hash);
        Assert.Equal(1024 * 1024, artifact.MinimumSize);
    }

    [Fact]
    public void ForgeDetailsKeepTheInstallerHashAndBranchFromTheSameVersion()
    {
        var versions = ForgelikeInstaller.ParseForgeVersionDetails("""
            [{"version":"10.13.4.1614","branch":"1.7.10","files":[
             {"category":"universal","format":"jar","hash":"wrong"},
             {"category":"installer","format":"jar","hash":"installer"}]}]
            """, "1.7.10");
        var version = Assert.Single(versions);
        Assert.Equal("1.7.10", version.Branch);
        Assert.Equal("installer", version.InstallerHash);
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("47.2.18/other")]
    [InlineData("47.2.18?url=other")]
    public void InvalidVersionSegmentsCannotChangeTheDownloadAddress(string version) =>
        Assert.Throws<InvalidDataException>(() => ComponentInstallerSources.Forge("1.20.1", new(version, null)));
}
