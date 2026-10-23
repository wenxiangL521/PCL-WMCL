using System.Net;
using System.Text.Json;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Launch.Tests;

public sealed class JavaRequirementTests
{
    [Theory]
    [InlineData("1.6.4", "net.minecraftforge:forge:1.6.4-9.11.1.965", "7.0.80", true)]
    [InlineData("1.6.4", "net.minecraftforge:forge:1.6.4-9.11.1.965", "8.0.51", false)]
    [InlineData("1.7.10", "net.minecraftforge:forge:1.7.10-10.13.4.1614", "8.0.451", true)]
    [InlineData("1.7.10", "net.minecraftforge:forge:1.7.10-10.13.4.1614", "17.0.1", false)]
    [InlineData("1.14.4", "net.minecraftforge:forge:1.14.4-28.2.26", "10.0.2", true)]
    [InlineData("1.14.4", "net.minecraftforge:forge:1.14.4-28.2.26", "11.0.1", false)]
    [InlineData("1.15.2", "net.minecraftforge:forge:1.15.2-31.2.57", "15.0.2", true)]
    [InlineData("1.15.2", "net.minecraftforge:forge:1.15.2-31.2.57", "16.0.1", false)]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.25", "8.0.320", true)]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.25", "8.0.321", false)]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.25", "17.0.1", false)]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.26", "23.0.1", true)]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.26", "24.0.1", false)]
    [InlineData("1.17.1", "net.minecraftforge:forge:1.17.1-37.0.79", "16.0.2", true)]
    [InlineData("1.17.1", "net.minecraftforge:forge:1.17.1-37.0.79", "17.0.1", false)]
    [InlineData("1.17.1", "net.minecraftforge:forge:1.17.1-37.0.80", "17.0.1", true)]
    [InlineData("1.19.4", "net.minecraftforge:forge:1.19.4-45.0.65", "20.0.1", false)]
    [InlineData("1.20.1", "net.minecraftforge:forge:1.20.1-47.4.8", "21.0.1", true)]
    [InlineData("1.20.1", "net.minecraftforge:forge:1.20.1-47.4.8", "22.0.1", false)]
    [InlineData("1.20.1", "net.neoforged:forge:1.20.1-47.1.99", "22.0.1", false)]
    [InlineData("1.20.2", "net.neoforged:neoforge:20.2.62-beta", "22.0.1", false)]
    [InlineData("1.20.2", "net.neoforged:neoforge:20.2.63-beta", "22.0.1", true)]
    [InlineData("1.20.1", "net.fabricmc:fabric-loader:0.16.14", "24.0.1", true)]
    [InlineData("1.20.1", "net.fabricmc:fabric-loader:0.16.14", "25.0.1", false)]
    [InlineData("1.20.1", "net.fabricmc:fabric-loader:0.17.0", "25.0.1", true)]
    [InlineData("1.12.2", "com.mumfrey:liteloader:1.12.2", "17.0.1", false)]
    [InlineData("1.8.9", "optifine:OptiFine:1.8.9_HD_U_M5", "7.0.80", false)]
    [InlineData("1.8.9", "optifine:OptiFine:1.8.9_HD_U_M5", "8.0.451", true)]
    [InlineData("1.8.9", "optifine:OptiFine:1.8.9_HD_U_M5", "9.0.1", false)]
    public void ActualLibraryCoordinatesApplyOriginalLoaderBounds(string minecraft, string library, string java, bool allowed)
    {
        var version = new VersionJson { Id = "Renamed", BaseGameVersion = minecraft, Libraries = [new() { Name = library }] };
        Assert.Equal(allowed, JavaRequirement.ForVersion(version).Contains(Version.Parse(java)));
    }

    [Theory]
    [InlineData("1.12.2", "Forge", "14.23.5.2859", 8, 9)]
    [InlineData("1.16.5", "Forge", "36.2.39", 8, 24)]
    [InlineData("1.20.1", "Forge", "47.2.0", 17, 22)]
    [InlineData("1.20.1", "Fabric", "0.15.11", 17, 25)]
    [InlineData("1.12.2", "OptiFine", "unknown", 8, 9)]
    [InlineData("1.12.2", "LiteLoader", "1.12.2-RC1", 8, 9)]
    public void InstallStageRequirementsCarryLoaderBounds(string minecraft, string loader,
        string version, int minimum, int maximum)
    {
        var requirement = JavaRequirement.ForInstall(minecraft, loader, version);
        Assert.Equal(minimum, requirement.Minimum.Major);
        Assert.Equal(maximum, requirement.Maximum?.Major);
    }

    [Theory]
    [InlineData("1.17.1", null, 16)]
    [InlineData("21w19a", "2021-05-12T12:00:00Z", 16)]
    [InlineData("1.18.2", null, 17)]
    [InlineData("1.20.5", null, 21)]
    [InlineData("24w14a", "2024-04-03T12:00:00Z", 21)]
    public void BaseGameAndSnapshotUseOriginalReleaseBoundaries(string minecraft, string? released, int minimum)
    {
        var requirement = JavaRequirement.ForVersion(new VersionJson { Id = minecraft, ReleaseTime = released });
        Assert.Equal(minimum, requirement.Minimum.Major);
        Assert.False(requirement.Contains(new Version(minimum - 1, 0, 0)));
    }

    [Fact]
    public void ExplicitJavaOutsideRequirementProducesAWarningInsteadOfSilentUse()
    {
        var requirement = JavaRequirement.ForVersion(new VersionJson { Id = "1.20.1" });
        var java8 = new JavaRuntime("/usr/bin/java", new Version(8, 0, 451), 8, "x86_64", "/usr", null);
        var java17 = new JavaRuntime("/usr/bin/java", new Version(17, 0, 9), 17, "x86_64", "/usr", null);
        Assert.Contains("不满足版本要求", requirement.WarningFor(java8));
        Assert.Null(requirement.WarningFor(java17));
    }

    [Fact]
    public void CombinedLibrariesAndPackSupportAreIntersectedUsingCapturedBaseIdentity()
    {
        var version = new VersionJson
        {
            Id = "Custom",
            BaseGameVersion = "1.18.2",
            Libraries = [new() { Name = "net.minecraftforge:forge:1.18.2-40.2.0" }, new() { Name = "optifine:OptiFine:1.18.2_HD_U_H9" }]
        };
        var requirement = JavaRequirement.ForVersion(version, new InstallManifest { McbbsSupportJava = [8, 17, 21] });
        Assert.True(requirement.Contains(new Version(17, 0, 1)));
        Assert.False(requirement.Contains(new Version(18, 0, 1)));
        Assert.False(requirement.Contains(new Version(21, 0, 1)));
        Assert.False(requirement.Contains(new Version(8, 0, 451)));
    }

    [Fact]
    public void MojangNewMajorRequirementAndManifestOnlyLoaderArePreserved()
    {
        var requirement = JavaRequirement.ForVersion(new VersionJson { Id = "Renamed", JavaVersion = new() { MajorVersion = 25 } },
            new InstallManifest { Components = new() { ["minecraft"] = "26.1" } });
        Assert.False(requirement.Contains(new Version(24, 0, 1)));
        Assert.True(requirement.Contains(new Version(25, 0, 1)));
        var legacy = JavaRequirement.ForVersion(new VersionJson { Id = "Custom" },
            new InstallManifest { Components = new() { ["minecraft"] = "1.16.5", ["forge"] = "36.2.25" } });
        Assert.False(legacy.Contains(new Version(8, 0, 321)));
    }

    [Fact]
    public void DiscoverySelectionRejectsHigherMajorsAndOverLimitUpdates()
    {
        var requirement = new JavaRequirement(new Version(8, 0, 0), new Version(8, 0, 320), true);
        JavaRuntime Runtime(int major, int patch) => new("fixture", new Version(major, 0, patch), major, "x86_64", "fixture", null);
        Assert.Equal(new Version(8, 0, 320), requirement.Select([Runtime(21, 1), Runtime(8, 451), Runtime(8, 320), Runtime(8, 51)])!.Version);
        Assert.Null(requirement.Select([Runtime(21, 1), Runtime(8, 451)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisioningFiltersFullMetadataBeforeRequestingManifest(bool compatible)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-java-range-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        var entries = new Dictionary<string, object>
        {
            ["new-eight"] = new[] { new { version = new { name = "8u321" }, manifest = new { url = "https://fixture.test/rejected" } } },
            ["modern"] = new[] { new { version = new { name = "21.0.1" }, manifest = new { url = "https://fixture.test/rejected" } } }
        };
        if (compatible) entries["old-eight"] = new[] { new { version = new { name = "8u320" }, manifest = new { url = "https://fixture.test/allowed" } } };
        var json = JsonSerializer.Serialize(new[] { "linux", "linux-i386", "windows-x64", "windows-x86", "mac-os", "mac-os-arm64" }
            .ToDictionary(platform => platform, _ => entries));
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "fixture.test")
            {
                Assert.Equal("/allowed", request.RequestUri.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"files\":{}}") };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        var service = new JavaProvisioner(http: http, runtimeDirectory: root);
        var requirement = new JavaRequirement(new Version(8, 0, 0), new Version(8, 0, 320), true);
        if (compatible) await Assert.ThrowsAsync<InvalidDataException>(() => service.ProvisionAsync(8, force: true, requirement: requirement));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvisionAsync(8, force: true, requirement: requirement));
        Assert.Equal(compatible ? 2 : 1, requests.Count);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("1.6.4", "net.minecraftforge:forge:1.6.4-9.11.1.965", 7, "7.0.80", "8.0.1")]
    [InlineData("1.8.9", "optifine:OptiFine:1.8.9_HD_U_M5", 8, "8.0.320", "9.0.1")]
    [InlineData("1.16.5", "net.minecraftforge:forge:1.16.5-36.2.25", 1, "8.0.320", "8.0.321")]
    public void ConflictingGameAndLoaderRangesWarnAndUseTheWholeNewConstraint(string minecraft, string library,
        int minimum, string allowed, string rejected)
    {
        var warnings = new List<string>();
        var version = new VersionJson
        {
            Id = "Captured Instance",
            BaseGameVersion = minecraft,
            JavaVersion = new() { MajorVersion = 25 },
            Libraries = [new() { Name = library }]
        };
        var requirement = JavaRequirement.ForVersion(version, warn: warnings.Add);
        Assert.Contains("Captured Instance", Assert.Single(warnings));
        Assert.Equal(minimum, requirement.Minimum.Major);
        Assert.True(requirement.Contains(Version.Parse(allowed)));
        Assert.False(requirement.Contains(Version.Parse(rejected)));
    }

    [Fact]
    public void PackSupportedMajorsAreNotDiscardedByOriginalLoaderConflictRecovery()
    {
        var version = new VersionJson
        {
            Id = "Captured",
            BaseGameVersion = "1.16.5",
            JavaVersion = new() { MajorVersion = 25 },
            Libraries = [new() { Name = "net.minecraftforge:forge:1.16.5-36.2.25" }]
        };
        var requirement = JavaRequirement.ForVersion(version, new InstallManifest { McbbsSupportJava = [21] }, _ => { });
        Assert.False(requirement.Contains(new Version(8, 0, 320)));
        Assert.False(requirement.Contains(new Version(21, 0, 1)));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
