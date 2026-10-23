using WMCL.Launch.Java;

namespace WMCL.Launch.Tests;

public sealed class JavaDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-java-discovery-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvironmentPathsUsePlatformExecutableSeparatorAndQuotedDirectories(bool windows)
    {
        var executable = windows ? "java.exe" : "java";
        var home = Create("Java Home", "bin", executable);
        var jdk = Create("JDK Home", "bin", executable);
        var path = Create("Path With Spaces", executable);
        Create("Path With Spaces", windows ? "java" : "java.exe");
        var options = Paths(windows) with
        {
            JavaHome = '"' + Path.GetDirectoryName(Path.GetDirectoryName(home))! + '"',
            JdkHome = Path.GetDirectoryName(Path.GetDirectoryName(jdk)),
            SearchPath = string.Join(windows ? ';' : ':', '"' + Path.GetDirectoryName(path)! + '"', Path.GetDirectoryName(path))
        };
        Assert.Equal(new[] { home, jdk, path }.Order(), JavaDiscovery.Enumerate(options, CancellationToken.None).Order());
    }

    [Fact]
    public void MacBundlesAreFoundFromSystemUserAndJavaHomeRoots()
    {
        var system = Create("System JVMs", "jdk-21.jdk", "Contents", "Home", "bin", "java");
        var user = Create("User JVMs", "jdk-17.jdk", "Contents", "Home", "bin", "java");
        var explicitBundle = Create("Explicit.jdk", "Contents", "Home", "bin", "java");
        var options = Paths(false) with
        {
            SystemRoots = [Path.Combine(_root, "System JVMs"), Path.Combine(_root, "User JVMs")],
            JavaHome = Path.Combine(_root, "Explicit.jdk")
        };
        Assert.Equal(new[] { system, user, explicitBundle }.Order(), JavaDiscovery.Enumerate(options, CancellationToken.None).Order());
    }

    [Fact]
    public void RegistryHomesAndVendorDirectoriesProduceWindowsConsoleJavaCandidates()
    {
        var registered = Create("Registry Home", "bin", "java.exe");
        var vendor = Create("Program Files", "Vendor", "jdk-21", "bin", "java.exe");
        Create("Program Files", "Vendor", "jdk-21", "bin", "javaw.exe");
        var options = Paths(true) with
        {
            RegistryHomes = [Path.Combine(_root, "Registry Home"), Path.Combine(_root, "Missing Registry Home")],
            SystemRoots = [Path.Combine(_root, "Program Files", "Vendor")]
        };
        Assert.Equal(new[] { registered, vendor }.Order(), JavaDiscovery.Enumerate(options, CancellationToken.None).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserManagersAndDownloadedRuntimeLayoutsRemainDiscoverable(bool windows)
    {
        var executable = windows ? "java.exe" : "java";
        var jdks = Create("home", ".jdks", "jdk-17", "bin", executable);
        var sdkman = Create("home", ".sdkman", "candidates", "java", "21", "bin", executable);
        var gradle = Create("home", ".gradle", "jdks", "jdk-21", "bin", executable);
        var mojang = Create("runtime", "mojang-21", "bin", executable);
        var official = Create("official", "java-runtime", "platform", "component", "bin", executable);
        var options = Paths(windows) with { SystemRoots = [Path.Combine(_root, "official")] };
        Assert.Equal(new[] { jdks, sdkman, gradle, mojang, official }.Order(), JavaDiscovery.Enumerate(options, CancellationToken.None).Order());
    }

    [Fact]
    public void BrokenOrInvalidRootsDoNotHideOtherCandidatesAndTraversalIsBounded()
    {
        var allowed = Create("system", "jdk", "bin", "java");
        Create("system", "one", "two", "three", "four", "bin", "java");
        var options = Paths(false) with { SystemRoots = ["\0invalid", Path.Combine(_root, "missing"), Path.Combine(_root, "system")] };
        Assert.Equal(allowed, Assert.Single(JavaDiscovery.Enumerate(options, CancellationToken.None)));
    }

    [Fact]
    public void CancelledEnumerationAndCachedDiscoveryStillObserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JavaDiscovery.Enumerate(Paths(false), cancellation.Token));
    }

    [Fact]
    public async Task DiscoveryCachesOnlySuccessfulResultsAndSupportsExplicitRefresh()
    {
        var calls = 0;
        var locator = new JavaLocator(_ => ["first", "duplicate", "first"], (path, _) =>
        {
            calls++;
            return Task.FromResult<JavaRuntime?>(new(path, new Version(21, 0, 2), 21, "arm64", "same-home", "fixture"));
        });
        var initial = await locator.DiscoverAsync();
        Assert.Single(initial);
        Assert.Equal(2, calls);
        Assert.Same(initial, await locator.DiscoverAsync());
        Assert.Equal(2, calls);
        Assert.Single(await locator.DiscoverAsync(forceRefresh: true));
        Assert.Equal(4, calls);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locator.DiscoverAsync(ct: cancellation.Token));
        locator.InvalidateCache();
        await locator.DiscoverAsync();
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task DiscoveryCancellationDoesNotPublishAnEmptySuccessfulCache()
    {
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        var locator = new JavaLocator(_ => ["fixture"], (_, token) =>
        {
            if (++calls == 1)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return Task.FromResult<JavaRuntime?>(new("fixture", new Version(17, 0, 2), 17, "x86_64", "fixture", null));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locator.DiscoverAsync(ct: cancellation.Token));
        Assert.Single(await locator.DiscoverAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProbeHonorsPreCancelledRequestsEvenWhenExecutableDoesNotExist()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JavaLocator().ProbePathAsync(Path.Combine(_root, "missing"), cancellation.Token));
    }

    [Fact]
    public async Task DiscoveryBoundsConcurrentJavaProcesses()
    {
        var firstFour = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var locator = new JavaLocator(_ => Enumerable.Range(0, 12).Select(index => "fixture-" + index).ToArray(), async (path, _) =>
        {
            if (Interlocked.Increment(ref started) == 4) firstFour.SetResult();
            await release.Task;
            return new(path, new Version(21, 0, 2), 21, "arm64", path, "fixture");
        });
        var discovery = locator.DiscoverAsync();
        try
        {
            await firstFour.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, started);
        }
        finally { release.SetResult(); }
        Assert.Equal(12, (await discovery).Count);
        Assert.Equal(12, started);
    }

    private JavaSearchPaths Paths(bool windows) => new(windows, Path.Combine(_root, "home"), Path.Combine(_root, "runtime"), null, null, null, [], []);

    private string Create(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture, never executed");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
