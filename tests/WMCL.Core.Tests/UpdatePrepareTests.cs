using System.Formats.Tar;
using System.IO.Compression;
using WMCL.Core.Update;

namespace WMCL.Core.Tests;

public class UpdatePrepareTests
{
    [Fact]
    public async Task ExtractsExecutableFromTarball()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "update.tar.gz");
        try
        {
            await CreateArchiveAsync(archive);
            var prepared = await UpdateService.PrepareAsync(archive, "0.2.0");

            Assert.Equal("WMCL", Path.GetFileName(prepared.ExecutablePath));
            Assert.True(File.Exists(prepared.ExecutablePath));
            Assert.Equal("#!/bin/sh\necho updated\n", File.ReadAllText(prepared.ExecutablePath));
            Assert.True(File.Exists(Path.Combine(prepared.StagingDirectory, "README.md")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractsWindowsExecutableFromZip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-update-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "update.zip");
        try
        {
            await using (var file = File.Create(archive))
            await using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                await WriteZipEntryAsync(zip, "WMCL.exe", "MZ");
                await WriteZipEntryAsync(zip, "README.md", "readme");
            }
            var prepared = await UpdateService.PrepareAsync(archive, "0.2.0");
            Assert.Equal("WMCL.exe", Path.GetFileName(prepared.ExecutablePath));
            Assert.True(File.Exists(Path.Combine(prepared.StagingDirectory, "README.md")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RendersWindowsAndMacApplyScripts()
    {
        var windows = UpdateService.BuildWindowsApplyScript(
            "/tmp/staging", "C:\\Program Files\\WMCL", "C:\\Program Files\\WMCL\\WMCL.exe");
        Assert.Contains("tasklist", windows);
        Assert.Contains("xcopy", windows);
        Assert.Contains("start \"\"", windows);

        var mac = UpdateService.BuildMacApplyScript(
            "/tmp/staging/WMCL.app", "/Applications/WMCL.app", "/tmp/update.log");
        Assert.Contains("ditto", mac);
        Assert.Contains("open '/Applications/WMCL.app'", mac);
        Assert.Contains("kill -0", mac);
    }

    [Fact]
    public async Task RejectsPathTraversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "bad.tar.gz");
        try
        {
            await CreateArchiveAsync(archive, includeTraversal: true);
            await Assert.ThrowsAsync<InvalidDataException>(
                () => UpdateService.PrepareAsync(archive, "0.2.0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CreateArchiveAsync(string path, bool includeTraversal = false)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Pax);

        await tar.WriteEntryAsync(Entry("WMCL", "#!/bin/sh\necho updated\n"));
        await tar.WriteEntryAsync(Entry("README.md", "readme"));
        if (includeTraversal)
            await tar.WriteEntryAsync(Entry("../evil.txt", "evil"));
    }

    private static async Task WriteZipEntryAsync(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private static PaxTarEntry Entry(string name, string content) => new(TarEntryType.RegularFile, name)
    {
        DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
    };
}
