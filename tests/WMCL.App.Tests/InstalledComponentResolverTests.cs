using System.Text.Json;
using WMCL.App.Services;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modrinth;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class InstalledComponentResolverTests
{
    private static (InstallManifest Manifest, ModrinthVersion Api) ApiManifest(bool metadata = false)
    {
        var api = InstallTestData.Api(id: "historical");
        var manifest = new InstallManifest
        {
            Components = { ["fabric-api"] = api.VersionNumber },
            ManagedFiles = [new("mods/renamed-by-user.jar", api.Files[0].Sha1!.ToUpperInvariant(), "fabric-api", api.Files[0].Url)]
        };
        if (metadata) manifest.ComponentMetadata["fabric-api"] = JsonSerializer.SerializeToElement(api);
        return (manifest, api);
    }

    private static (InstallManifest Manifest, OptiFabricFile File) OptiFabricManifest(bool metadata = false)
    {
        var file = InstallTestData.OptiFabric();
        var manifest = new InstallManifest
        {
            Components = { ["optifabric"] = file.Version },
            ManagedFiles = [new("mods/" + file.FileName, new string('b', 40), "optifabric", file.DownloadUrl)]
        };
        if (metadata) manifest.ComponentMetadata["optifabric"] = JsonSerializer.SerializeToElement(file);
        return (manifest, file);
    }

    [Fact]
    public void ApiLegacyResolutionUsesHistoricalHashInsteadOfTheFirstMatchingVersionNumber()
    {
        var (manifest, original) = ApiManifest();
        var other = original with { Id = "different", Files = [original.Files[0] with { Sha1 = new string('b', 40) }] };
        var restored = InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [other, original]);
        Assert.Equal("historical", restored!.Id);
        Assert.Equal(original.Files, restored.Files);
    }

    [Fact]
    public void ApiSnapshotSurvivesCatalogRemovalOrReplacementOfTheSameRemoteId()
    {
        var (manifest, original) = ApiManifest(metadata: true);
        var replacement = original with { Files = [original.Files[0] with { Sha1 = new string('b', 40) }] };
        Assert.Equal(original.Files, InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [])!.Files);
        Assert.Equal(original.Files, InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [replacement])!.Files);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("project")]
    [InlineData("game")]
    [InlineData("version")]
    [InlineData("files")]
    public void InvalidApiSnapshotCannotDowngradeToVersionStringMatching(string mutation)
    {
        var (manifest, original) = ApiManifest();
        var invalid = mutation switch
        {
            "hash" => original with { Files = [original.Files[0] with { Sha1 = new string('b', 40) }] },
            "project" => original with { ProjectId = "another-project" },
            "game" => original with { GameVersions = ["1.21"] },
            "version" => original with { VersionNumber = "different-version" },
            _ => original with { Files = [] }
        };
        manifest.ComponentMetadata["fabric-api"] = JsonSerializer.SerializeToElement(invalid);
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [original]));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("component")]
    [InlineData("hash")]
    public void LegacyApiNeedsAConsistentManagedFileHash(string mutation)
    {
        var (manifest, original) = ApiManifest();
        var managed = manifest.ManagedFiles[0];
        if (mutation == "absent") manifest.ManagedFiles.Clear();
        else manifest.ManagedFiles[0] = mutation switch
        {
            "component" => managed with { Component = "some-other-mod" },
            _ => managed with { Sha1 = new string('c', 40) }
        };
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [original]));
    }

    [Fact]
    public void LegacyApiWithoutUrlCanUseAnUnambiguousHashButCannotGuessBetweenResourceIds()
    {
        var (manifest, original) = ApiManifest();
        manifest.ManagedFiles[0] = manifest.ManagedFiles[0] with { Url = null };
        Assert.Equal(original.Id, InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [original])!.Id);
        var duplicate = original with { Id = "another-id" };
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [duplicate, original]));
    }

    [Fact]
    public void OptiFabricLegacyResolutionUsesTheCurseForgeFileUrl()
    {
        var (manifest, original) = OptiFabricManifest();
        var other = original with { Id = original.Id + 1 };
        Assert.Equal(original.Id, InstalledComponentResolver.OptiFabric(manifest, "1.20.1", [other, original])!.Id);
    }

    [Fact]
    public void OptiFabricSnapshotSurvivesAnAbsentCatalog()
    {
        var (manifest, original) = OptiFabricManifest(metadata: true);
        var restored = InstalledComponentResolver.OptiFabric(manifest, "1.20.1", []);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.GameVersions.ToArray(), restored.GameVersions.ToArray());
        Assert.Equal(new string('b', 40), restored.Sha1);
        Assert.Equal(original.DownloadUrl, restored!.DownloadUrl);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("game")]
    [InlineData("name")]
    [InlineData("size")]
    [InlineData("url")]
    [InlineData("hash")]
    public void OptiFabricSnapshotAndHistoricalRecordMustAgree(string mutation)
    {
        var (manifest, original) = OptiFabricManifest();
        var invalid = mutation switch
        {
            "id" => original with { Id = original.Id + 1 },
            "game" => original with { GameVersions = ["1.21", "Fabric"] },
            "name" => original with { FileName = "../optifabric-1.14.3.jar" },
            "size" => original with { Size = 0 },
            _ => original
        };
        manifest.ComponentMetadata["optifabric"] = JsonSerializer.SerializeToElement(invalid);
        if (mutation == "url") manifest.ManagedFiles[0] = manifest.ManagedFiles[0] with { Url = null };
        if (mutation == "hash") manifest.ManagedFiles[0] = manifest.ManagedFiles[0] with { Sha1 = "invalid" };
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.OptiFabric(manifest, "1.20.1", [original]));
    }

    [Fact]
    public void OptiFabricRejectsConflictingHistoricalHashesAndConflictingSnapshotHashes()
    {
        var (manifest, original) = OptiFabricManifest(metadata: true);
        manifest.ComponentMetadata["optifabric"] = JsonSerializer.SerializeToElement(original with { Sha1 = new string('c', 40) });
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.OptiFabric(manifest, "1.20.1", []));
        manifest.ComponentMetadata.Clear();
        manifest.ManagedFiles.Add(manifest.ManagedFiles[0] with { Sha1 = new string('d', 40) });
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.OptiFabric(manifest, "1.20.1", [original]));
    }

    [Fact]
    public void OptiFineSnapshotRestoresTheOriginalFilenameAfterCatalogRemoval()
    {
        var original = InstallTestData.OptiFine();
        var manifest = new InstallManifest { Components = { ["optifine"] = original.Patch } };
        manifest.ComponentMetadata["optifine"] = JsonSerializer.SerializeToElement(original);
        Assert.Equal(original, InstalledComponentResolver.OptiFine(manifest, "1.20.1", []));
        manifest.ComponentMetadata.Clear();
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.OptiFine(manifest, "1.20.1",
            [original with { FileName = "another.jar" }, original]));
    }

    [Theory]
    [InlineData("fabric-api")]
    [InlineData("optifabric")]
    [InlineData("optifine")]
    public void MalformedMetadataIsReportedInsteadOfIgnored(string component)
    {
        var (manifest, api) = ApiManifest();
        manifest.Components[component] = "installed";
        manifest.ComponentMetadata[component] = JsonSerializer.SerializeToElement("invalid object");
        Assert.Throws<InvalidDataException>(() =>
        {
            if (component == "fabric-api") InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [api]);
            else if (component == "optifabric") InstalledComponentResolver.OptiFabric(manifest, "1.20.1", [InstallTestData.OptiFabric()]);
            else InstalledComponentResolver.OptiFine(manifest, "1.20.1", [InstallTestData.OptiFine()]);
        });
    }

    [Fact]
    public void AbsentComponentsDoNotSelectCatalogDefaults()
    {
        var manifest = new InstallManifest();
        Assert.Null(InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [InstallTestData.Api()]));
        Assert.Null(InstalledComponentResolver.OptiFabric(manifest, "1.20.1", [InstallTestData.OptiFabric()]));
        Assert.Null(InstalledComponentResolver.OptiFine(manifest, "1.20.1", [InstallTestData.OptiFine()]));
    }

    [Fact]
    public void LegacyApiUsesUrlToDisambiguateEqualHashesButAllowsAnUnambiguousMirrorChange()
    {
        var (manifest, original) = ApiManifest();
        var other = original with
        {
            Id = "another-source",
            Files = [original.Files[0] with { Url = "https://example.invalid/another-source.jar" }]
        };
        Assert.Equal(original.Id, InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [other, original])!.Id);
        Assert.Equal(other.Id, InstalledComponentResolver.Api(manifest, "1.20.1", "Fabric", [other])!.Id);
    }

    [Theory]
    [InlineData("Quilt")]
    [InlineData("LegacyFabric")]
    public void ApiSnapshotsRetainTheCorrectLoaderFamilyAndProject(string kind)
    {
        var minecraft = kind == "LegacyFabric" ? "1.8.9" : "1.20.1";
        var api = InstallTestData.Api(minecraft, kind);
        var project = ApiComponentService.Project(kind);
        var manifest = new InstallManifest
        {
            Components = { [project] = api.VersionNumber },
            ManagedFiles = [new("mods/" + api.Files[0].Filename, api.Files[0].Sha1!, project)]
        };
        manifest.ComponentMetadata[project] = JsonSerializer.SerializeToElement(api);
        Assert.Equal(api.Id, InstalledComponentResolver.Api(manifest, minecraft, kind, [])!.Id);
        manifest.ComponentMetadata[project] = JsonSerializer.SerializeToElement(api with { ProjectId = ApiComponentService.ProjectId("Fabric") });
        Assert.Throws<InvalidDataException>(() => InstalledComponentResolver.Api(manifest, minecraft, kind, []));
    }
}
