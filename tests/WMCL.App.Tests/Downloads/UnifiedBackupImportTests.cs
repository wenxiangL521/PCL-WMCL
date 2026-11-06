using System.IO.Compression;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modpack;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class UnifiedBackupImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-unified-backup-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new(new RejectNetwork());
    private int _calls;

    public UnifiedBackupImportTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("wmcl-export.json")]
    [InlineData("pcl-export.json")]
    [InlineData("WMCL-EXPORT.JSON")]
    public async Task SingleRootMarkerRoutesBackupWithoutTryingPackInstallers(string marker)
    {
        var path = Archive(marker, "manifest.json", "modrinth.index.json");
        var workflow = Workflow((actual, name, _, ct) =>
        {
            _calls++;
            Assert.Equal(path, actual);
            Assert.Equal("Chosen", name);
            Assert.False(ct.IsCancellationRequested);
            return Task.FromResult("Restored");
        });
        Assert.Equal("Restored", await workflow.InstallLocalAsync(path, "Chosen", true));
        Assert.Equal(1, _calls);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Theory]
    [InlineData("wmcl-export.json", "wmcl-export.json")]
    [InlineData("wmcl-export.json", "WMCL-EXPORT.JSON")]
    [InlineData("wmcl-export.json", "pcl-export.json")]
    public async Task DuplicateOrAmbiguousMarkersRejectBeforeCallingBackup(string first, string second)
    {
        var workflow = Workflow((_, _, _, _) => { _calls++; return Task.FromResult("unexpected"); });
        await Assert.ThrowsAsync<InvalidDataException>(() => workflow.InstallLocalAsync(Archive(first, second), null, false));
        Assert.Equal(0, _calls);
    }

    [Fact]
    public async Task PreCancelledImportDoesNotReadArchiveOrCallDelegate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var workflow = Workflow((_, _, _, _) => { _calls++; return Task.FromResult("unexpected"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.InstallLocalAsync(Path.Combine(_root, "not-created.zip"), null, true, ct: cancellation.Token));
        Assert.Equal(0, _calls);
    }

    [Fact]
    public async Task BackupFailurePropagatesWithoutFallingThroughToOtherFormats()
    {
        var failure = new IOException("backup fixture rejected");
        var workflow = Workflow((_, _, _, _) => { _calls++; return Task.FromException<string>(failure); });
        var actual = await Assert.ThrowsAsync<IOException>(() => workflow.InstallLocalAsync(Archive("wmcl-export.json", "manifest.json", "modrinth.index.json"), null, false));
        Assert.Same(failure, actual);
        Assert.Equal(1, _calls);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [AvaloniaFact]
    public async Task StandaloneModpackUiRoutesSelectedWmclBackupThroughBackupDelegate()
    {
        var path = Archive("wmcl-export.json", "manifest.json", "modrinth.index.json");
        var workflow = Workflow((actual, name, _, _) =>
        {
            _calls++;
            Assert.Equal(path, actual);
            Assert.Equal("Backup name", name);
            return Task.FromResult("Restored backup");
        });
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var model = new ModpackViewModel(services, workflow) { LocalPath = path, InstanceName = "Backup name", IncludeOptionalFiles = true };
        var installed = 0;
        model.InstanceInstalled += (_, _) => installed++;
        await model.InstallLocalCommand.ExecuteAsync(null);
        Assert.Equal(1, _calls);
        Assert.Equal(1, installed);
        Assert.Equal("整合包安装完成：Restored backup", model.Status);
        Assert.False(model.IsBusy);
    }

    private ModpackImportWorkflow Workflow(Func<string, string?, IProgress<JobProgress>?, CancellationToken, Task<string>> backup)
    {
        var repository = new VersionRepository(_root);
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(repository, downloads);
        var curseForge = new CurseForgeClient("fixture-key", _http);
        return new(new ModpackInstaller(repository, installer, downloads: downloads),
            new CurseForgeModpackInstaller(repository, installer, client: curseForge, downloads: downloads),
            new LocalModpackInstaller(repository, installer, curseForge: curseForge, downloads: downloads),
            (_, _, _) => throw new InvalidOperationException("Backup must not prompt for pack optional files"), backup);
    }
    private string Archive(params string[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("{}");
        }
        return path;
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected network request: " + request.RequestUri);
    }
    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_root, true);
    }
}
