using System.IO.Compression;
using WMCL.Modding.Modpack;
using Xunit.Abstractions;

namespace WMCL.Modding.Tests;

[Trait("Kind", "Unit")]
public sealed class ExportSymlinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-export-links-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Path, bool Directory)> _links = [];
    private readonly ITestOutputHelper _output;
    private string GameRoot => Path.Combine(_root, "game");
    private string Instance => Path.Combine(GameRoot, "versions", "Pack");

    public ExportSymlinkTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(Path.Combine(Instance, "mods"));
        Directory.CreateDirectory(Path.Combine(Instance, "config", "nested"));
        File.WriteAllText(Path.Combine(Instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
        File.WriteAllText(Path.Combine(Instance, "mods", "normal.jar"), "normal mod");
        File.WriteAllText(Path.Combine(Instance, "config", "nested", "normal.txt"), "normal config");
        File.WriteAllText(Path.Combine(Instance, "options.txt"), "normal options");
        Directory.CreateDirectory(Path.Combine(_root, "outside"));
        File.WriteAllText(Path.Combine(_root, "outside", "secret.txt"), "external secret");
    }

    public static IEnumerable<object[]> LinkedSources()
    {
        foreach (var format in new[] { "zip", "mrpack", "mcbbs" })
            foreach (var kind in new[]
                     {
                     "file", "internal-file", "options", "dangling", "directory", "selected-directory", "cycle",
                     "instance-root", "versions-ancestor", "game-root", "root-ancestor",
                     "profile", "jar", "metadata", "inherited-profile", "inherited-profile-case"
                 })
                yield return [format, kind];
    }

    [Theory]
    [MemberData(nameof(LinkedSources))]
    public async Task RejectsLinkedSourcesWithoutPublishingAnArchive(string format, string kind)
    {
        var gameRoot = GameRoot;
        var external = Path.Combine(_root, "outside");
        var secret = Path.Combine(external, "secret.txt");
        string link;
        string target;
        var directory = false;
        switch (kind)
        {
            case "file":
                link = Path.Combine(Instance, "mods", "secret.jar");
                target = secret;
                break;
            case "internal-file":
                link = Path.Combine(Instance, "mods", "alias.jar");
                target = Path.Combine(Instance, "mods", "normal.jar");
                break;
            case "options":
                link = Path.Combine(Instance, "options.txt");
                File.Delete(link);
                target = secret;
                break;
            case "dangling":
                link = Path.Combine(Instance, "optionsof.txt");
                target = Path.Combine(external, "missing.txt");
                break;
            case "directory":
                link = Path.Combine(Instance, "config", "nested", "external");
                target = external;
                directory = true;
                break;
            case "selected-directory":
                link = Path.Combine(Instance, "resourcepacks");
                target = external;
                directory = true;
                break;
            case "cycle":
                link = Path.Combine(Instance, "config", "nested", "cycle");
                target = Path.Combine(Instance, "config");
                directory = true;
                break;
            case "instance-root":
                link = Instance;
                target = Path.Combine(external, "Pack");
                Directory.Move(link, target);
                directory = true;
                break;
            case "versions-ancestor":
                link = Path.Combine(GameRoot, "versions");
                target = Path.Combine(external, "versions");
                Directory.Move(link, target);
                directory = true;
                break;
            case "game-root":
                link = Path.Combine(_root, "alias");
                target = GameRoot;
                gameRoot = link;
                directory = true;
                break;
            case "root-ancestor":
                link = Path.Combine(_root, "alias");
                target = _root;
                gameRoot = Path.Combine(link, "game");
                directory = true;
                break;
            case "profile":
                link = Path.Combine(Instance, "Pack.json");
                File.Delete(link);
                target = secret;
                break;
            case "jar":
                link = Path.Combine(Instance, "Pack.jar");
                target = secret;
                break;
            case "metadata":
                link = Path.Combine(Instance, "WMCL");
                target = external;
                directory = true;
                break;
            case "inherited-profile":
            case "inherited-profile-case":
                File.WriteAllText(Path.Combine(Instance, "Pack.json"),
                    """{"id":"Pack","INHERITSFROM":"Parent"}""".Replace("INHERITSFROM",
                        kind == "inherited-profile" ? "inheritsFrom" : "InheritsFrom"));
                Directory.CreateDirectory(Path.Combine(GameRoot, "versions", "Parent"));
                link = Path.Combine(GameRoot, "versions", "Parent", "Parent.json");
                target = secret;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!TryCreateLink(link, target, directory)) return;
        var output = Path.Combine(_root, "pack." + format);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ExportAsync(format, gameRoot, output));

        Assert.Contains("符号链接或重解析点", error.Message);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
        Assert.Equal("external secret", File.ReadAllText(secret));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("mrpack")]
    [InlineData("mcbbs")]
    public async Task NormalExportIncludesNestedFilesAndStandaloneOptions(string format)
    {
        var output = Path.Combine(_root, "pack." + format);
        await ExportAsync(format, GameRoot, output);

        using var archive = ZipFile.OpenRead(output);
        var prefix = format == "zip" ? "instance/" : "overrides/";
        foreach (var relative in new[] { "mods/normal.jar", "config/nested/normal.txt", "options.txt" })
            Assert.NotNull(archive.GetEntry(prefix + relative));
        var metadata = format switch
        {
            "zip" => "wmcl-export.json",
            "mrpack" => "modrinth.index.json",
            _ => "manifest.json"
        };
        Assert.NotNull(archive.GetEntry(metadata));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
    }

    [Theory]
    [InlineData("zip", false)]
    [InlineData("mrpack", false)]
    [InlineData("mcbbs", false)]
    [InlineData("zip", true)]
    [InlineData("mrpack", true)]
    [InlineData("mcbbs", true)]
    public async Task FailedExportPreservesExistingOutput(string format, bool linkedSource)
    {
        if (linkedSource && !TryCreateLink(Path.Combine(Instance, "mods", "secret.jar"),
                Path.Combine(_root, "outside", "secret.txt"), directory: false)) return;
        var output = Path.Combine(_root, "pack." + format);
        var original = new byte[] { 1, 3, 5, 7 };
        await File.WriteAllBytesAsync(output, original);

        await Assert.ThrowsAnyAsync<IOException>(() => ExportAsync(format, GameRoot, output));

        Assert.Equal(original, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-export-*.tmp"));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("mrpack")]
    [InlineData("mcbbs")]
    public async Task RejectsLinkedOutputAncestorWithoutChangingItsTarget(string format)
    {
        var alias = Path.Combine(_root, "linked-output");
        var target = Path.Combine(_root, "outside");
        if (!TryCreateLink(alias, target, directory: true)) return;
        var existing = Path.Combine(target, "pack." + format);
        await File.WriteAllTextAsync(existing, "existing output");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExportAsync(format, GameRoot, Path.Combine(alias, "pack." + format)));

        Assert.Equal("existing output", await File.ReadAllTextAsync(existing));
        Assert.Empty(Directory.GetFiles(target, ".wmcl-export-*.tmp"));
    }

    [Fact]
    public async Task InstanceExportDoesNotInspectUnselectedDataLinks()
    {
        if (!TryCreateLink(Path.Combine(Instance, "mods", "secret.jar"),
                Path.Combine(_root, "outside", "secret.txt"), directory: false)) return;
        var output = Path.Combine(_root, "pack.zip");
        await InstanceExporter.ExportAsync(GameRoot, "Pack", new InstanceExportOptions(IncludeMods: false), output);
        using var archive = ZipFile.OpenRead(output);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("instance/mods/", StringComparison.Ordinal));
    }

    private async Task ExportAsync(string format, string gameRoot, string output)
    {
        if (format == "zip")
            await InstanceExporter.ExportAsync(gameRoot, "Pack", output);
        else
            await ModpackExporter.ExportAsync(gameRoot, "Pack", output,
                format: format == "mrpack" ? ModpackExporter.ExportFormat.Modrinth : ModpackExporter.ExportFormat.Mcbbs);
    }

    private bool TryCreateLink(string path, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(path, target);
            else File.CreateSymbolicLink(path, target);
            _links.Add((path, directory));
            return true;
        }
        catch (Exception error) when (OperatingSystem.IsWindows() &&
                                      (error is UnauthorizedAccessException ||
                                       error is IOException && (error.HResult & 0xffff) is 5 or 1314))
        {
            _output.WriteLine("未验证符号链接场景：Windows 当前用户无创建符号链接权限。" + error.Message);
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var link in _links.AsEnumerable().Reverse())
        {
            if (link.Directory) Directory.Delete(link.Path);
            else File.Delete(link.Path);
        }
        Directory.Delete(_root, recursive: true);
    }
}
