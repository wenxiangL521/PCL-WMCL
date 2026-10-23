using WMCL.Core.Update;

namespace WMCL.Core.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("v0.2.0", "0.1.9", true)]
    [InlineData("1.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("invalid", "0.1.0", false)]
    public void ComparesVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("""{"version":"0.2.0","url":"https://example.invalid/a.tar.gz"}""", UpdateCheckStatus.UpdateAvailable)]
    [InlineData("""{"version":"0.1.0","url":"https://example.invalid/a.tar.gz"}""", UpdateCheckStatus.UpToDate)]
    [InlineData("""{"version":"","url":"https://example.invalid/a.tar.gz"}""", UpdateCheckStatus.InvalidManifest)]
    [InlineData("""{"version":"0.2.0","url":""}""", UpdateCheckStatus.MissingUrl)]
    [InlineData("""{"version":"next","url":"https://example.invalid/a.tar.gz"}""", UpdateCheckStatus.IncomparableVersion)]
    [InlineData("{ broken", UpdateCheckStatus.InvalidManifest)]
    public void ClassifiesManifestProblemsWithoutCallingThemUpToDate(string json, UpdateCheckStatus expected)
    {
        var result = UpdateService.EvaluateManifest(json, "0.1.0");
        Assert.Equal(expected, result.Status);
        if (expected is UpdateCheckStatus.UpToDate or UpdateCheckStatus.UpdateAvailable)
            Assert.Null(result.Error);
        else
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task MissingUrlAndCanceledTokenAreClassifiedBeforeDownloading()
    {
        var missing = await UpdateService.CheckAsync("", "0.1.0");
        Assert.Equal(UpdateCheckStatus.MissingUrl, missing.Status);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var info = new UpdateInfo("0.2.0", "https://example.invalid/WMCL-linux-x64.tar.gz");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateService.DownloadAsync(info, ct: cancellation.Token));
    }

    [Fact]
    public void ParsesManifest()
    {
        const string json = """
            {
              "version": "0.2.0",
              "url": "https://example.invalid/WMCL-linux-x64.tar.gz",
              "sha256": "abc",
              "notes": "test"
            }
            """;

        var info = UpdateService.ParseManifest(json);

        Assert.NotNull(info);
        Assert.Equal("0.2.0", info.Version);
        Assert.Equal("https://example.invalid/WMCL-linux-x64.tar.gz", info.Url);
        Assert.Equal("abc", info.Sha256);
        Assert.Equal("test", info.Notes);
    }
}
