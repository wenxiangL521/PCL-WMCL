using System.Formats.Tar;
using System.IO.Compression;
using PCL.Core.Update;

namespace PCL.Core.Tests;

public class UpdatePrepareTests
{
    [Fact]
    public async Task ExtractsExecutableFromTarball()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "update.tar.gz");
        try
        {
            await CreateArchiveAsync(archive);
            var prepared = await UpdateService.PrepareAsync(archive, "0.2.0");

            Assert.Equal("PCL", Path.GetFileName(prepared.ExecutablePath));
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
    public async Task RejectsPathTraversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcl-update-" + Guid.NewGuid().ToString("N"));
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

        await tar.WriteEntryAsync(Entry("PCL", "#!/bin/sh\necho updated\n"));
        await tar.WriteEntryAsync(Entry("README.md", "readme"));
        if (includeTraversal)
            await tar.WriteEntryAsync(Entry("../evil.txt", "evil"));
    }

    private static PaxTarEntry Entry(string name, string content) => new(TarEntryType.RegularFile, name)
    {
        DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
    };
}
