using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.Launch.Tests;

public sealed class LaunchPreparationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-prepare-" + Guid.NewGuid().ToString("N"));
    private const string VersionId = "1.21.1";
    private static readonly JavaRuntime Runtime = new("test-java", new Version(21, 0, 1), 21, "x86_64", "test-home", "Test");

    public LaunchPreparationServiceTests()
    {
        Directory.CreateDirectory(VersionDirectory);
        File.WriteAllText(Path.Combine(VersionDirectory, VersionId + ".json"),
            """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","libraries":[],"javaVersion":{"majorVersion":21}}""");
    }

    private string VersionDirectory => Path.Combine(_root, "versions", VersionId);

    [Theory]
    [InlineData("override-java", "instance-java", "global-java", "override-java")]
    [InlineData(null, "instance-java", "global-java", "instance-java")]
    [InlineData(" ", "", "global-java", "global-java")]
    [InlineData(null, null, null, null)]
    public async Task ResolvesJavaUsingOverrideThenInstanceThenDefaults(
        string? overridePath, string? instancePath, string? defaultPath, string? expected)
    {
        InstanceSettingsStore.Save(VersionDirectory, new InstanceSettings { JavaPath = instancePath });
        string? probed = null;
        var provisioned = false;
        var service = new LaunchPreparationService((path, _) =>
        {
            probed = path;
            return Task.FromResult<JavaRuntime?>(Runtime);
        }, (requirement, _, _) =>
        {
            provisioned = true;
            Assert.Equal(21, requirement.Minimum.Major);
            return Task.FromResult(Runtime);
        });

        var prepared = await service.PrepareAsync(_root, VersionId,
            new LaunchPreferences { JavaPath = defaultPath, IsolationMode = "isolated" },
            new LaunchOverrides { JavaPath = overridePath });

        Assert.Equal(expected, probed);
        Assert.Equal(expected is null, provisioned);
        Assert.Equal(VersionDirectory, prepared.GameDirectory);
        Assert.Equal(VersionId, prepared.Version.Id);
        Assert.Null(prepared.JavaWarning);
    }

    [Fact]
    public async Task InvalidConfiguredJavaDoesNotSilentlyProvisionAnotherRuntime()
    {
        var service = new LaunchPreparationService((_, _) => Task.FromResult<JavaRuntime?>(null),
            (_, _, _) => throw new InvalidOperationException("must not provision"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(
            _root, VersionId, new LaunchPreferences { JavaPath = "missing-java" }));
        Assert.Contains("无法识别指定的 Java：missing-java", error.Message);
    }

    [Fact]
    public async Task RejectsRuntimeOutsideModpackSupportedMajors()
    {
        InstallManifestStore.Save(_root, VersionId, new InstallManifest { McbbsSupportJava = [17] });
        var service = CreateService();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(
            _root, VersionId, new LaunchPreferences { JavaPath = Runtime.Path }));
        Assert.Contains("该整合包不支持 Java 21", error.Message);
    }

    [Fact]
    public async Task CancellationStopsBeforeResolvingJava()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new LaunchPreparationService((_, _) => throw new InvalidOperationException("must not probe"),
            (_, _, _) => throw new InvalidOperationException("must not provision"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(
            _root, VersionId, new LaunchPreferences(), ct: cancellation.Token));
    }

    [Fact]
    public async Task RequestCombinesGlobalAndInstanceSettingsWithoutChangingTheirValues()
    {
        InstanceSettingsStore.Save(VersionDirectory, new InstanceSettings
        {
            MemoryMb = 6144,
            Width = 1200,
            Height = 700,
            ExtraJvmArgs = "-Dinstance=true",
            ExtraGameArgs = "--instance \"two words\"",
            Server = "example.invalid",
            Port = 25566,
            InitialHeapMb = 1024,
            GcStrategy = "G1"
        });
        InstallManifestStore.Save(_root, VersionId, new InstallManifest { McbbsMinMemoryMb = 8192 });
        var preferences = new LaunchPreferences
        {
            MemoryMb = 4096,
            Width = 850,
            Height = 500,
            ExtraJvmArgs = "-Dglobal=true",
            ExtraGameArgs = "--global",
            ProcessPriority = "high",
            HighPerformanceGpu = true,
            GpuIndex = "2"
        };
        var prepared = await CreateService().PrepareAsync(_root, VersionId, preferences);
        var account = AccountInfo.Offline("Steve");
        var request = prepared.CreateRequest(account, preferences, jvmPrefix: ["-javaagent:test"]);

        Assert.Same(account, request.Account);
        Assert.Equal(_root, request.MinecraftDirectory);
        Assert.Equal(8192, request.MemoryMb);
        Assert.Equal(1200, request.Width);
        Assert.Equal(700, request.Height);
        Assert.Equal(["-javaagent:test", "-Dglobal=true", "-Dinstance=true"], request.ExtraJvmArguments);
        Assert.Equal(["--global", "--instance", "two words", "--server", "example.invalid", "--port", "25566"], request.ExtraGameArguments);
        Assert.Equal("high", request.ProcessPriority);
        Assert.True(request.HighPerformanceGpu);
        Assert.Equal("2", request.GpuIndex);
        Assert.Equal(1024, request.InitialHeapMb);
        Assert.Equal("G1", request.GcStrategy);
        Assert.Equal(6144, prepared.InstanceSettings.MemoryMb);
    }

    [Fact]
    public async Task ExplicitCliOptionsOverrideInstanceSettingsAndPreserveCliJvmDefaults()
    {
        InstanceSettingsStore.Save(VersionDirectory, new InstanceSettings
        {
            MemoryMb = 6144,
            Width = 1200,
            Height = 700,
            Server = "instance.invalid",
            Port = 25566,
            InitialHeapMb = 1024,
            GcStrategy = "G1"
        });
        var preferences = new LaunchPreferences { IncludeInstanceJvmTuning = false };
        var overrides = new LaunchOverrides
        {
            MemoryMb = 2048,
            Width = 854,
            Height = 480,
            Server = "cli.invalid",
            Port = 25567
        };
        var prepared = await CreateService().PrepareAsync(_root, VersionId, preferences, overrides);
        var request = prepared.CreateRequest(AccountInfo.Offline("Steve"), preferences, overrides);

        Assert.Equal(2048, request.MemoryMb);
        Assert.Equal(854, request.Width);
        Assert.Equal(480, request.Height);
        Assert.Equal(["--server", "cli.invalid", "--port", "25567"], request.ExtraGameArguments);
        Assert.Equal(0, request.InitialHeapMb);
        Assert.Equal("", request.GcStrategy);
    }

    [Fact]
    public async Task UnconfiguredResolutionRemainsOptional()
    {
        var preferences = new LaunchPreferences();
        var prepared = await CreateService().PrepareAsync(_root, VersionId, preferences);
        var request = prepared.CreateRequest(AccountInfo.Offline("Steve"), preferences);
        Assert.Null(request.Width);
        Assert.Null(request.Height);
        Assert.Equal(4096, request.MemoryMb);
        Assert.Empty(request.ExtraGameArguments);
    }

    private static LaunchPreparationService CreateService() => new(
        (_, _) => Task.FromResult<JavaRuntime?>(Runtime), (_, _, _) => Task.FromResult(Runtime));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
