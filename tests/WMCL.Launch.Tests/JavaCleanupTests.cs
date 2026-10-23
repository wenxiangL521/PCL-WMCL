using System.Net;
using WMCL.Launch.Java;

namespace WMCL.Launch.Tests;

public sealed class JavaCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-java-cleanup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CleanupRemovesOnlyManagedDirectoriesAndInvalidatesDiscoveryCache()
    {
        var probes = 0;
        var locator = new JavaLocator(_ => ["fixture"], (_, _) =>
        {
            probes++;
            return Task.FromResult<JavaRuntime?>(Runtime());
        });
        await locator.DiscoverAsync();
        var service = new JavaProvisioner(locator, runtimeDirectory: _root);
        var managed = Marker("mojang-21");
        var external = Marker("system-java");
        Assert.Equal(1, service.RemoveDownloaded());
        Assert.False(File.Exists(managed));
        Assert.Equal("preserve", File.ReadAllText(external));
        await locator.DiscoverAsync();
        Assert.Equal(2, probes);
        Assert.Equal(0, service.RemoveDownloaded());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupCannotRaceDiscoveryInEitherEnsureOverload(bool range)
    {
        var release = new TaskCompletionSource<JavaRuntime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var locator = new JavaLocator(_ => ["fixture"], (_, _) => release.Task);
        var service = new JavaProvisioner(locator, runtimeDirectory: _root);
        var marker = Marker("mojang-21");
        var ensure = range ? service.EnsureAsync(new JavaRequirement(new Version(21, 0, 0))) : service.EnsureAsync(21);
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.RemoveDownloaded());
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally { release.TrySetResult(Runtime()); }
        Assert.Equal(21, (await ensure).MajorVersion);
        Assert.Equal(1, service.RemoveDownloaded());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveProvisioningBlocksCleanupAndReleasesAfterFailureOrCancellation(bool cancel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        var service = new JavaProvisioner(http: http, runtimeDirectory: _root);
        var marker = Marker("mojang-21");
        var provisioning = service.ProvisionAsync(21, force: true, ct: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.RemoveDownloaded());
            Assert.True(File.Exists(marker));
        }
        finally
        {
            if (cancel) cancellation.Cancel();
            release.TrySetResult();
        }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provisioning);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => provisioning);
        Assert.Equal(1, service.RemoveDownloaded());
    }

    [Fact]
    public async Task DiscoveryFinishingAfterCleanupCannotRestoreInvalidatedCache()
    {
        var release = new TaskCompletionSource<JavaRuntime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = 0;
        var locator = new JavaLocator(_ => ["fixture"], (_, _) => ++probes == 1 ? release.Task : Task.FromResult<JavaRuntime?>(null));
        var pending = locator.DiscoverAsync();
        var service = new JavaProvisioner(locator, runtimeDirectory: _root);
        Marker("mojang-21");
        Assert.Equal(1, service.RemoveDownloaded());
        release.SetResult(Runtime());
        Assert.Single(await pending);
        Assert.Empty(await locator.DiscoverAsync());
        Assert.Equal(2, probes);
    }

    private static JavaRuntime Runtime() => new("fixture", new Version(21, 0, 2), 21, "x86_64", "fixture", "fixture");

    private string Marker(string directory)
    {
        var root = Path.Combine(_root, directory);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "keep.txt");
        File.WriteAllText(path, "preserve");
        return path;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
