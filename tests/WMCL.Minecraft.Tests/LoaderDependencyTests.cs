using System.IO.Compression;
using System.Text.Json;
using WMCL.Modding.Fabric;

namespace WMCL.Minecraft.Tests;

public sealed class LoaderDependencyTests
{
    [Theory]
    [InlineData("0.16.10", ">=0.16.10", true)]
    [InlineData("0.15.11", ">=0.16.10", false)]
    [InlineData("0.16.10-beta.1", ">=0.16.10", false)]
    [InlineData("0.16.10+local", "=0.16.10", true)]
    [InlineData("0.16.10", ">=0.16 <0.17", true)]
    [InlineData("0.17", ">=0.16 <0.17", false)]
    [InlineData("0.16", "0.16.0", true)]
    [InlineData("0.16.10", "0.16.x", true)]
    [InlineData("0.16.0-beta.1", "0.16.*", true)]
    [InlineData("0.17.0-beta.1", "0.16.*", false)]
    [InlineData("0.19.5", "^0.16.10", true)]
    [InlineData("1.0.0-beta.1", "^0.16.10", false)]
    [InlineData("0.17", "~0.16.10", false)]
    [InlineData("0.16.11", "~0.16.10", true)]
    [InlineData("0.16.10-beta.10", ">0.16.10-beta.2", true)]
    [InlineData("0.16.10-1", ">0.16.10-", true)]
    [InlineData("0.16.10-1", "<0.16.10-", false)]
    [InlineData("0.16.10", "*", true)]
    public void FabricPredicatesFollowLoaderComparisonRules(string version, string requirement, bool expected) =>
        Assert.Equal(expected, FabricVersionPredicate.Matches(version, requirement));

    [Theory]
    [InlineData("0.26.0", "\"0.25.0\"", true)]
    [InlineData("0.26.0", "\"=0.25.0\"", false)]
    [InlineData("0.25.0-beta.1", "\"0.25.x\"", false)]
    [InlineData("0.26.0-beta.1", "\"0.25.x\"", true)]
    [InlineData("0.25.2", "{\"any\":[\"=0.25.2\",\">=0.26\"]}", true)]
    [InlineData("0.25.1", "{\"any\":[\"=0.25.2\",\">=0.26\"]}", false)]
    [InlineData("0.25.2", "{\"all\":[\">=0.25\",\"<0.26\"]}", true)]
    public void QuiltUsesItsOwnBareVersionAndCompositeRangeSemantics(string version, string requirement, bool expected)
    {
        using var document = JsonDocument.Parse(requirement);
        Assert.Equal(expected, FabricVersionPredicate.Matches(version, document.RootElement, true));
    }

    [Fact]
    public void FabricArraysAreAlternativesAndInvalidRangesAreReported()
    {
        using var alternatives = JsonDocument.Parse("[\"=0.15.11\",\">=0.16.10\"]");
        Assert.True(FabricVersionPredicate.Matches("0.15.11", alternatives.RootElement));
        Assert.False(FabricVersionPredicate.Matches("0.16.0", alternatives.RootElement));
        Assert.Throws<InvalidDataException>(() => FabricVersionPredicate.Matches("0.16.10", ">=0.16.x"));
        Assert.Throws<InvalidDataException>(() => FabricVersionPredicate.Matches("0.16.10", ">= 0.16.10"));
    }

    [Theory]
    [InlineData("Fabric")]
    [InlineData("LegacyFabric")]
    public async Task EmbeddedModsCannotRequireANewerLoaderThanTheSelectedOne(string kind)
    {
        var nested = ApiArchiveTestData.Jar("""{"depends":{"fabricloader":">=0.16.10"}}""");
        var root = ApiArchiveTestData.Jar("""{"depends":{"fabricloader":">=0.14"},"jars":[{"file":"META-INF/jars/module.jar"}]}""",
            nested: new Dictionary<string, byte[]> { ["META-INF/jars/module.jar"] = nested });
        await WithJarAsync(root, async path =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoaderDependencyChecker.ValidateAsync(path, kind, "0.15.11"));
            Assert.Contains("module.jar", error.Message);
            Assert.Contains("0.16.10", error.Message);
            await LoaderDependencyChecker.ValidateAsync(path, kind, "0.16.10");
        });
    }

    [Fact]
    public async Task BreaksRejectsAnExplicitlyIncompatibleLoader()
    {
        var root = ApiArchiveTestData.Jar("""{"depends":{"fabricloader":">=0.14"},"breaks":{"fabricloader":"0.16.x"}}""");
        await WithJarAsync(root, async path =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoaderDependencyChecker.ValidateAsync(path, "Fabric", "0.16.10"));
            Assert.Contains("不兼容", error.Message);
            await LoaderDependencyChecker.ValidateAsync(path, "Fabric", "0.17.0");
        });
    }

    [Fact]
    public async Task QuiltMetadataAndNestedJarsUseTheActualQuiltLoaderVersion()
    {
        var nested = ApiArchiveTestData.Jar("""{"quilt_loader":{"depends":[{"id":"quilt_loader","versions":{"any":["=0.25.2",">=0.26"]}}]}}""", "quilt.mod.json");
        var root = ApiArchiveTestData.Jar("""{"quilt_loader":{"depends":["minecraft",{"id":"quilt_loader","versions":{"all":[">=0.25","<0.30"]}}],"jars":["module.jar"]}}""",
            "quilt.mod.json", new Dictionary<string, byte[]> { ["module.jar"] = nested });
        await WithJarAsync(root, async path =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => LoaderDependencyChecker.ValidateAsync(path, "Quilt", "0.25.1"));
            await LoaderDependencyChecker.ValidateAsync(path, "Quilt", "0.25.2");
            await Assert.ThrowsAsync<InvalidOperationException>(() => LoaderDependencyChecker.ValidateAsync(path, "Quilt", "0.30.0"));
        });
    }

    [Fact]
    public async Task MissingNestedJarsAndCancellationAreNotTreatedAsCompatible()
    {
        var root = ApiArchiveTestData.Jar("""{"jars":[{"file":"missing.jar"}]}""");
        await WithJarAsync(root, async path =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => LoaderDependencyChecker.ValidateAsync(path, "Fabric", "0.16.10"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoaderDependencyChecker.ValidateAsync(path, "Fabric", "0.16.10", cancellation.Token));
        });
    }

    [Theory]
    [InlineData("[{\"id\":\"quilt_loader\",\"versions\":\">=0.25\",\"unless\":\"alternate\"}]")]
    [InlineData("[[{\"id\":\"quilt_loader\",\"versions\":\">=0.25\"},\"alternate\"]]")]
    public async Task ConditionsNeedingOtherModStateAreReportedInsteadOfAssumedCompatible(string dependencies)
    {
        var bytes = ApiArchiveTestData.Jar("{\"quilt_loader\":{\"depends\":" + dependencies + "}}", "quilt.mod.json");
        await WithJarAsync(bytes, path => Assert.ThrowsAsync<InvalidDataException>(() =>
            LoaderDependencyChecker.ValidateAsync(path, "Quilt", "0.24.0")));
    }

    [Fact]
    public async Task OptionalLoaderDependenciesStillConstrainTheLoaderThatIsActuallyPresent()
    {
        var bytes = ApiArchiveTestData.Jar("""{"quilt_loader":{"depends":[{"id":"quilt_loader","versions":">=0.25","optional":true}]}}""", "quilt.mod.json");
        await WithJarAsync(bytes, path => Assert.ThrowsAsync<InvalidOperationException>(() =>
            LoaderDependencyChecker.ValidateAsync(path, "Quilt", "0.24.0")));
    }

    private static async Task WithJarAsync(byte[] bytes, Func<string, Task> verify)
    {
        var path = Path.Combine(Path.GetTempPath(), "wmcl-loader-dependency-" + Guid.NewGuid().ToString("N") + ".jar");
        await File.WriteAllBytesAsync(path, bytes);
        try { await verify(path); }
        finally { File.Delete(path); }
    }
}

internal static class ApiArchiveTestData
{
    public static byte[] Jar(string metadata, string metadataName = "fabric.mod.json", IReadOnlyDictionary<string, byte[]>? nested = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(metadataName).Open())) writer.Write(metadata);
            foreach (var entry in nested ?? new Dictionary<string, byte[]>())
            {
                using var stream = archive.CreateEntry(entry.Key).Open();
                stream.Write(entry.Value);
            }
        }
        return output.ToArray();
    }
}
