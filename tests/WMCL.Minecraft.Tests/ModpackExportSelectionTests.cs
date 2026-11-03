using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WMCL.Modding.Modpack;
using Xunit.Abstractions;

namespace WMCL.Minecraft.Tests;

public sealed class ModpackExportSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-pack-selection-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;
    private string Instance => Path.Combine(_root, "versions", "Pack");

    public ModpackExportSelectionTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(Instance);
        WriteProfile("net.fabricmc:fabric-loader:0.16.0", "[]");
        foreach (var relative in new[]
                 {
                     "mods/a.jar", "mods/b.jar", "config/nested/a.txt", "defaultconfigs/a.txt",
                     "resourcepacks/a.zip", "shaderpacks/a.zip", "options.txt", "optionsof.txt",
                     "saves/World/level.dat", "WMCL/custom.txt", "logs/latest.log"
                 })
        {
            var path = Path.Combine(Instance, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
        }
    }

    [Theory]
    [InlineData(ModpackExporter.ExportFormat.Modrinth)]
    [InlineData(ModpackExporter.ExportFormat.Mcbbs)]
    public async Task SelectionIsOrdinalNormalizedAndAuthoritative(ModpackExporter.ExportFormat format)
    {
        // 显式清单即完整选择：类别开关不再二次过滤，用户勾选的日志等目录也能导出。
        var options = new ModpackExportOptions(IncludeResourcePacks: false, IncludedFiles:
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mods\\a.jar", "config/nested/a.txt", "resourcepacks/a.zip", "mods/B.jar", "Pack.json"
            });
        var output = Path.Combine(_root, "selection.pack");
        var result = await ModpackExporter.ExportAsync(_root, "Pack", output, format: format, options: options);
        // 显式清单中的 Pack.json 也会导出；大小写不同的 mods/B.jar 因实际不存在而被跳过。
        Assert.Equal(4, result.FileCount);
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal(new[] { "overrides/Pack.json", "overrides/config/nested/a.txt", "overrides/mods/a.jar", "overrides/resourcepacks/a.zip" },
            archive.Entries.Where(entry => entry.FullName.StartsWith("overrides/", StringComparison.Ordinal))
                .Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray());
        AssertMetadata(archive, format, new[] { "Pack.json", "mods/a.jar", "config/nested/a.txt", "resourcepacks/a.zip" });
    }

    [Theory]
    [InlineData(ModpackExporter.ExportFormat.Modrinth)]
    [InlineData(ModpackExporter.ExportFormat.Mcbbs)]
    public async Task DefaultsAndOptInPreserveScopeAndMandatoryMetadata(ModpackExporter.ExportFormat format)
    {
        var output = Path.Combine(_root, "default.pack");
        var result = await ModpackExporter.ExportAsync(_root, "Pack", output, format: format);
        Assert.Equal(8, result.FileCount);
        using (var archive = ZipFile.OpenRead(output))
        {
            Assert.Null(archive.GetEntry("overrides/saves/World/level.dat"));
            Assert.Null(archive.GetEntry("overrides/WMCL/custom.txt"));
            Assert.Null(archive.GetEntry("overrides/logs/latest.log"));
            AssertMetadata(archive, format, archive.Entries.Where(entry => entry.FullName.StartsWith("overrides/", StringComparison.Ordinal))
                .Select(entry => entry.FullName["overrides/".Length..]).ToArray());
        }
        output = Path.Combine(_root, "opt-in.pack");
        result = await ModpackExporter.ExportAsync(_root, "Pack", output, format: format,
            options: new ModpackExportOptions(IncludeSaves: true, IncludeWmcl: true, IncludedFiles:
                new HashSet<string> { "saves/World/level.dat", "WMCL/custom.txt" }));
        Assert.Equal(2, result.FileCount);
        using (var archive = ZipFile.OpenRead(output))
        {
            Assert.NotNull(archive.GetEntry("overrides/saves/World/level.dat"));
            Assert.NotNull(archive.GetEntry("overrides/WMCL/custom.txt"));
        }
        output = Path.Combine(_root, "empty.pack");
        result = await ModpackExporter.ExportAsync(_root, "Pack", output, format: format,
            options: new ModpackExportOptions(IncludedFiles: new HashSet<string>()));
        Assert.Equal(0, result.FileCount);
        using var empty = ZipFile.OpenRead(output);
        Assert.Single(empty.Entries);
        AssertMetadata(empty, format, []);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/mods/a.jar")]
    [InlineData("C:\\mods\\a.jar")]
    [InlineData("C:mods/a.jar")]
    [InlineData("../mods/a.jar")]
    [InlineData("mods/../a.jar")]
    [InlineData("mods/./a.jar")]
    [InlineData("mods//a.jar")]
    [InlineData("mods/a.jar/")]
    [InlineData("\\\\server\\mods\\a.jar")]
    public async Task RejectsMaliciousPathsBeforePublishing(string relative)
    {
        var output = Path.Combine(_root, "malicious.pack");
        await Assert.ThrowsAsync<ArgumentException>(() => ModpackExporter.ExportAsync(_root, "Pack", output,
            options: new ModpackExportOptions(IncludedFiles: new HashSet<string> { relative })));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
    }

    [Theory]
    [InlineData(ModpackExporter.ExportFormat.Modrinth)]
    [InlineData(ModpackExporter.ExportFormat.Mcbbs)]
    public async Task ExistingArchiveIsNeverOverwritten(ModpackExporter.ExportFormat format)
    {
        var output = Path.Combine(_root, "existing.pack");
        await File.WriteAllTextAsync(output, "existing");
        await Assert.ThrowsAnyAsync<IOException>(() => ModpackExporter.ExportAsync(_root, "Pack", output, format: format));
        Assert.Equal("existing", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
    }

    [Theory]
    [InlineData(ModpackExporter.ExportFormat.Modrinth)]
    [InlineData(ModpackExporter.ExportFormat.Mcbbs)]
    public async Task SelectedSymlinkIsRejectedAndUnselectedSymlinkIsNotRead(ModpackExporter.ExportFormat format)
    {
        var target = Path.Combine(_root, "secret.txt");
        var link = Path.Combine(Instance, "mods", "link.jar");
        File.WriteAllText(target, "secret");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception error) when (OperatingSystem.IsWindows() &&
                                      (error is UnauthorizedAccessException ||
                                       error is IOException && (error.HResult & 0xffff) is 5 or 1314))
        {
            _output.WriteLine("当前 Windows 用户无法创建符号链接：" + error.Message);
            return;
        }
        try
        {
            var output = Path.Combine(_root, "linked.pack");
            await Assert.ThrowsAsync<InvalidDataException>(() => ModpackExporter.ExportAsync(_root, "Pack", output,
                format: format, options: new ModpackExportOptions(IncludedFiles: new HashSet<string> { "mods/link.jar" })));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
            await ModpackExporter.ExportAsync(_root, "Pack", output, format: format,
                options: new ModpackExportOptions(IncludedFiles: new HashSet<string> { "mods/a.jar" }));
            Assert.Equal("secret", File.ReadAllText(target));
        }
        finally { File.Delete(link); }
    }

    [Theory]
    [InlineData(ModpackExporter.ExportFormat.Modrinth, "[\"--fml.neoForgeVersion\",\"21.1.262\"]")]
    [InlineData(ModpackExporter.ExportFormat.Mcbbs, "[\"--fml.neoForgeVersion=21.1.262\"]")]
    public async Task NeoForgeUsesExplicitGameFlagNotFancyLoader(ModpackExporter.ExportFormat format, string arguments)
    {
        WriteProfile("net.neoforged.fancymodloader:loader:4.0.45", arguments);
        var output = Path.Combine(_root, "neo.pack");
        await ModpackExporter.ExportAsync(_root, "Pack", output, format: format);
        using var archive = ZipFile.OpenRead(output);
        using var stream = archive.GetEntry(format == ModpackExporter.ExportFormat.Modrinth ? "modrinth.index.json" : "manifest.json")!.Open();
        using var document = JsonDocument.Parse(stream);
        if (format == ModpackExporter.ExportFormat.Modrinth)
            Assert.Equal("21.1.262", document.RootElement.GetProperty("dependencies").GetProperty("neoforge").GetString());
        else
            Assert.Contains(document.RootElement.GetProperty("addons").EnumerateArray(), addon =>
                addon.GetProperty("id").GetString() == "neoforge" && addon.GetProperty("version").GetString() == "21.1.262");
    }

    [Theory]
    [InlineData("net.neoforged:neoforge:21.1.261", "[\"--fml.neoForgeVersion\",\"21.1.262\"]")]
    [InlineData("net.neoforged.fancymodloader:loader:4.0.45", "[]")]
    [InlineData("net.neoforged.fancymodloader:loader:4.0.45", "[\"--fml.neoForgeVersion\"]")]
    [InlineData("net.neoforged.fancymodloader:loader:4.0.45", "[\"--fml.neoForgeVersion=21.1.262\",\"--fml.neoForgeVersion=21.1.263\"]")]
    [InlineData("net.neoforged.fancymodloader:loader:4.0.45", "[\"--fml.neoForgeVersion=${version}\"]")]
    public async Task LoaderAmbiguityAndMissingLiteralAreRejected(string library, string arguments)
    {
        WriteProfile(library, arguments);
        var output = Path.Combine(_root, "invalid.pack");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ModpackExporter.ExportAsync(_root, "Pack", output));
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("[\"--fml.forgeVersion\",\"1.21.1-52.0.0\"]")]
    [InlineData("[\"--fml.forgeVersion=52.0.0\"]")]
    public async Task ForgeFlagsNormalizeMinecraftPrefix(string arguments)
    {
        WriteProfile("net.minecraftforge:forge:1.21.1-52.0.0", arguments);
        var output = Path.Combine(_root, "forge.pack");
        await ModpackExporter.ExportAsync(_root, "Pack", output);
        using var archive = ZipFile.OpenRead(output);
        using var stream = archive.GetEntry("modrinth.index.json")!.Open();
        using var document = JsonDocument.Parse(stream);
        Assert.Equal("52.0.0", document.RootElement.GetProperty("dependencies").GetProperty("forge").GetString());
    }

    private void WriteProfile(string library, string arguments) => File.WriteAllText(Path.Combine(Instance, "Pack.json"),
        "{\"id\":\"Pack\",\"clientVersion\":\"1.21.1\",\"libraries\":[{\"name\":\"" + library +
        "\"}],\"arguments\":{\"game\":" + arguments + ",\"jvm\":[]}}");

    private static void AssertMetadata(ZipArchive archive, ModpackExporter.ExportFormat format, string[] paths)
    {
        using var stream = archive.GetEntry(format == ModpackExporter.ExportFormat.Modrinth ? "modrinth.index.json" : "manifest.json")!.Open();
        using var document = JsonDocument.Parse(stream);
        var files = document.RootElement.GetProperty("files");
        if (format == ModpackExporter.ExportFormat.Modrinth)
        {
            Assert.Empty(files.EnumerateArray());
            Assert.Equal("1.21.1", document.RootElement.GetProperty("dependencies").GetProperty("minecraft").GetString());
            return;
        }
        Assert.Equal(paths.Order(StringComparer.Ordinal), files.EnumerateArray().Select(file => file.GetProperty("path").GetString()!).Order(StringComparer.Ordinal));
        foreach (var file in files.EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            using var input = archive.GetEntry("overrides/" + path)!.Open();
            Assert.Equal(Convert.ToHexString(SHA1.HashData(input)).ToLowerInvariant(), file.GetProperty("hash").GetString());
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
