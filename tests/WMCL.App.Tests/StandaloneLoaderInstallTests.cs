using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class StandaloneLoaderInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-standalone-stage-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _stages = [];
    private readonly List<string> _installerRoots = [];

    public StandaloneLoaderInstallTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "Custom", "saves"));
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.json"), """{"id":"Custom","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.jar"), "original client");
        File.WriteAllText(Path.Combine(_root, "versions/Custom/saves/level.dat"), "user progress");
        File.WriteAllText(Path.Combine(_root, "launcher_profiles.json"), "original launcher profiles");
        File.WriteAllText(Path.Combine(_root, "options.txt"), "original options");
    }

    [Theory]
    [InlineData("Fabric", "1.20.1", "0.16.0")]
    [InlineData("Quilt", "1.20.1", "0.28.0")]
    [InlineData("LegacyFabric", "1.12.2", "0.16.0")]
    [InlineData("Forge", "1.20.1", "47.2.0")]
    [InlineData("NeoForge", "1.21.1", "21.1.1")]
    [InlineData("OptiFine", "1.20.1", "I6")]
    public async Task EveryLoaderPublishesAnIndependentVerifiedInstanceAfterCompletingInStaging(string kind, string minecraft, string version)
    {
        var request = new StandaloneLoaderInstallRequest(_root, minecraft, kind, version,
            kind == "OptiFine" ? InstallTestData.OptiFine(minecraft) : null);
        var before = Snapshot();
        var id = await Service().InstallAsync(request, Progress(), null, CancellationToken.None);
        Assert.Equal(request.InstanceId, id);
        var installed = await new VersionRepository(_root).GetVersionAsync(id);
        Assert.Equal(minecraft, installed.BaseGameVersion);
        Assert.Null(installed.InheritsFrom);
        Assert.Equal(kind == "LegacyFabric" ? "fabric" : kind.ToLowerInvariant(), MinecraftVersionUtil.GetLoader(installed));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(version, InstallManifestStore.Load(_root, id)!.Components[kind.ToLowerInvariant()]);
        Assert.Equal("installed client", File.ReadAllText(Path.Combine(_root, "versions", id, id + ".jar")));
        Assert.True(File.Exists(Path.Combine(_root, "libraries/fixture/loader.jar")));
        Assert.True(File.Exists(Path.Combine(_root, "assets/indexes/fixture.json")));
        Assert.True(File.Exists(Path.Combine(_root, "resources/fixture.txt")));
        AssertPreserved(before);
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDirectoryOrFileIsRejectedBeforeCreatingABackend(bool file)
    {
        var request = Request();
        var target = Target(request);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (file) File.WriteAllText(target, "existing file");
        else
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "user.txt"), "existing user data");
        }
        var before = Snapshot();
        var failure = await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(request, Progress(), null, CancellationToken.None));
        Assert.Contains("实例已存在", failure.Message);
        Assert.Empty(_stages);
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData("loader")]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task InstallerFailureOrCancellationLeavesNoPublishedFiles(string phase)
    {
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        var service = Service(backend =>
        {
            backend.FailPhase = phase;
            backend.Cancellation = cancellation;
        });
        if (phase == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(Request(), Progress(), null, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Request(), Progress(), null, cancellation.Token));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData("libraries/fixture/loader.jar")]
    [InlineData("assets/indexes/fixture.json")]
    [InlineData("resources/fixture.txt")]
    public async Task SharedDependencyConflictsPreserveExistingBytesAndRejectTheWholePublication(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "user dependency");
        var before = Snapshot();
        var failure = await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(Request(), Progress(), null, CancellationToken.None));
        Assert.Contains("共享依赖", failure.Message);
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Fact]
    public async Task IdenticalSharedDependenciesAreReusedWithoutChangingTheirTimestamp()
    {
        var library = Path.Combine(_root, "libraries/fixture/loader.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, "generated library");
        File.SetLastWriteTimeUtc(library, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(library);
        await Service().InstallAsync(Request(), Progress(), null, CancellationToken.None);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(library));
        AssertClean();
    }

    [Fact]
    public async Task ATargetCreatedDuringInstallationIsPreservedAndPreventsPublication()
    {
        var request = Request();
        var service = Service(backend => backend.AfterComplete = () =>
        {
            Directory.CreateDirectory(Target(request));
            File.WriteAllText(Path.Combine(Target(request), "external.txt"), "external instance");
        });
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(request, Progress(), null, CancellationToken.None));
        Assert.Equal("external instance", File.ReadAllText(Path.Combine(Target(request), "external.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "libraries/fixture/loader.jar")));
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailureOrCancellationRollsBackOnlyNewSharedFiles(bool cancel)
    {
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        var progress = Progress(update =>
        {
            if (update.Stage != "发布加载器实例") return;
            if (cancel) cancellation.Cancel();
            else throw new IOException("publication rejected");
        });
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().InstallAsync(Request(), progress, null, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(Request(), progress, null, cancellation.Token));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Fact]
    public async Task AReusedDependencyChangedAfterPreflightIsRecheckedBeforePublishing()
    {
        var path = Path.Combine(_root, "libraries/fixture/loader.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "generated library");
        var progress = Progress(update =>
        {
            if (update.Stage == "应用加载器安装结果") File.WriteAllText(path, "external change");
        });
        await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(Request(), progress, null, CancellationToken.None));
        Assert.Equal("external change", File.ReadAllText(path));
        Assert.False(Directory.Exists(Target(Request())));
        AssertClean();
    }

    [Theory]
    [InlineData("target")]
    [InlineData("shared")]
    [InlineData("staged")]
    [InlineData("staged-root")]
    public async Task LinkedDestinationsAndInstallerOutputCannotPublishOutsideTheRoot(string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var external = Path.Combine(_root, "external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "keep.txt"), "external data");
        var request = Request();
        if (kind == "target") Directory.CreateSymbolicLink(Target(request), Path.Combine(external, "missing"));
        if (kind == "shared") Directory.CreateSymbolicLink(Path.Combine(_root, "libraries"), external);
        var service = Service(backend =>
        {
            if (kind == "staged") backend.AfterComplete = () =>
                Directory.CreateSymbolicLink(Path.Combine(backend.Root, "libraries/escape"), external);
            if (kind == "staged-root") backend.AfterComplete = () =>
            {
                Directory.Move(Path.Combine(backend.Root, "libraries"), Path.Combine(backend.Root, "original-libraries"));
                Directory.CreateSymbolicLink(Path.Combine(backend.Root, "libraries"), external);
            };
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(request, Progress(), null, CancellationToken.None));
        Assert.Equal(["keep.txt"], Directory.GetFiles(external).Select(Path.GetFileName));
        Assert.Equal("external data", File.ReadAllText(Path.Combine(external, "keep.txt")));
        AssertClean();
    }

    [Fact]
    public async Task ExternalChangesDuringRollbackArePreservedAndTheRecoveryLocationIsReported()
    {
        var progress = Progress(update =>
        {
            if (update.Stage != "发布加载器实例") return;
            File.WriteAllText(Path.Combine(_root, "resources/fixture.txt"), "external modified file");
            throw new IOException("publication failed");
        });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => Service().InstallAsync(Request(), progress, null, CancellationToken.None));
        Assert.Contains("暂存资料保留于", failure.Message);
        Assert.Equal("external modified file", File.ReadAllText(Path.Combine(_root, "resources/fixture.txt")));
        Assert.True(Directory.Exists(Assert.Single(_stages)));
        Assert.False(Directory.Exists(Target(Request())));
    }

    [AvaloniaTheory]
    [InlineData("Fabric", "success")]
    [InlineData("Fabric", "failure")]
    [InlineData("Fabric", "cancel")]
    [InlineData("Forge", "cancel")]
    [InlineData("OptiFine", "cancel")]
    public async Task PageAnnouncesOnlyAnActuallyPublishedInstanceAndReleasesItsLease(string kind, string outcome)
    {
        var services = Services();
        var service = Service(backend => backend.FailPhase = outcome == "failure" ? "complete" : null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<WMCL.Modding.Fabric.FabricLoaderInfo>>([new("0.16.0", true)]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>(["47.2.0"]),
            _ => Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>([InstallTestData.OptiFine()]),
            installComponent: async (request, progress, log, ct) =>
            {
                entered.SetResult();
                await gate.Task.WaitAsync(ct);
                return await service.InstallAsync(request, progress, log, ct);
            });
        await mods.LoadAsync();
        var notifications = 0;
        mods.VersionInstalled += (_, _) => notifications++;
        var request = new StandaloneLoaderInstallRequest(_root, "1.20.1", kind, kind == "Forge" ? "47.2.0" : kind == "OptiFine" ? "I6" : "0.16.0");
        var command = kind == "Forge" ? mods.InstallForgelikeCommand : kind == "OptiFine" ? mods.InstallOptiFineCommand : mods.InstallFabricCommand;
        var pending = command.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(services.GameOperations.GetActiveOperation(_root));
        if (outcome == "cancel")
        {
            var cancel = kind == "Forge" ? mods.CancelForgelikeCommand : kind == "OptiFine" ? mods.CancelOptiFineCommand : mods.CancelCommand;
            cancel.Execute(null);
        }
        gate.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(outcome == "success" ? 1 : 0, notifications);
        Assert.Equal(outcome == "success", Directory.Exists(Target(request)));
        Assert.Equal(outcome == "success", mods.ModInstances.Contains(request.InstanceId));
        var status = kind == "Forge" ? mods.ForgelikeStatus : kind == "OptiFine" ? mods.OptiFineStatus : mods.Status;
        Assert.Contains(outcome == "success" ? "安装完成" : outcome == "cancel" ? "已取消" : "失败", status);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        AssertClean();
    }

    [Fact]
    public async Task LegacyFabricUsesItsOwnTargetWhenTheSameFabricLoaderIdAlreadyExists()
    {
        var ordinary = Path.Combine(_root, "versions/fabric-loader-0.16.0-1.12.2");
        Directory.CreateDirectory(ordinary);
        File.WriteAllText(Path.Combine(ordinary, "user.txt"), "existing Fabric");
        var before = Snapshot();
        var request = new StandaloneLoaderInstallRequest(_root, "1.12.2", "LegacyFabric", "0.16.0");
        await Service().InstallAsync(request, Progress(), null, CancellationToken.None);
        Assert.True(Directory.Exists(Target(request)));
        AssertPreserved(before);
        AssertClean();
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("identity")]
    [InlineData("loader")]
    public async Task MissingManifestOrWrongFixedIdentityIsRejectedBeforePublishing(string kind)
    {
        var before = Snapshot();
        var request = Request();
        var progress = Progress(update =>
        {
            if (update.Stage != "安装清单" || update.Percent < 0.799) return;
            var root = _stages.Last();
            if (kind == "manifest") File.Delete(InstallManifestStore.GetPath(root, request.InstanceId));
            else
            {
                var profile = Path.Combine(root, "versions", request.InstanceId, request.InstanceId + ".json");
                var json = JsonNode.Parse(File.ReadAllText(profile))!;
                if (kind == "identity") json["clientVersion"] = "1.21.1";
                else json["mainClass"] = "net.minecraft.client.main.Main";
                File.WriteAllText(profile, json.ToJsonString());
                var manifest = InstallManifestStore.Load(root, request.InstanceId)!;
                manifest.ManagedFiles = manifest.ManagedFiles.Select(file => file.RelativePath.EndsWith(".json", StringComparison.Ordinal)
                    ? file with { Sha1 = FileUtil.Sha1(profile) } : file).ToList();
                InstallManifestStore.Save(root, request.InstanceId, manifest);
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().InstallAsync(request, progress, null, CancellationToken.None));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    private StandaloneLoaderInstallRequest Request() => new(_root, "1.20.1", "Fabric", "0.16.0");
    private string Target(StandaloneLoaderInstallRequest request) => Path.Combine(_root, "versions", request.InstanceId);
    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
    private StandaloneLoaderInstallService Service(Action<Backend>? configure = null) => new(Services(), root =>
    {
        _stages.Add(root);
        var backend = new Backend(root, _installerRoots);
        configure?.Invoke(backend);
        return backend;
    });
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), FileUtil.Sha1);
    private void AssertPreserved(Dictionary<string, string> before)
    {
        foreach (var (relative, hash) in before) Assert.Equal(hash, FileUtil.Sha1(Path.Combine(_root, relative)));
    }
    private void AssertClean()
    {
        Assert.All(_stages.Concat(_installerRoots), path => Assert.False(Directory.Exists(path)));
        Assert.Empty(Directory.GetDirectories(_root, ".wmcl-instance-install-*"));
    }
    private static IProgress<JobProgress> Progress(Action<JobProgress>? report = null) => new InlineProgress(report ?? (_ => { }));
    private sealed class InlineProgress(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }

    private sealed class Backend(string root, List<string> installerRoots) : ICombinedInstallBackend
    {
        public string Root => root;
        public string? FailPhase { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Action? AfterComplete { get; set; }
        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(root, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"),
                JsonSerializer.Serialize(new { id = plan.Minecraft, clientVersion = plan.Minecraft, mainClass = "net.minecraft.client.main.Main", libraries = Array.Empty<object>() }), ct);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".jar"), "installed client", ct);
            Directory.CreateDirectory(Path.Combine(root, "assets/indexes"));
            await File.WriteAllTextAsync(Path.Combine(root, "assets/indexes/fixture.json"), "{}", ct);
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            await File.WriteAllTextAsync(Path.Combine(root, "resources/fixture.txt"), "legacy resource", ct);
        }
        public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
        {
            installerRoots.Add(stagingRoot);
            var directory = Path.Combine(stagingRoot, "versions", "generated-loader");
            Directory.CreateDirectory(directory);
            var mainClass = plan.BaseLoader is "Fabric" or "LegacyFabric" ? "net.fabricmc.loader.impl.launch.knot.KnotClient"
                : plan.BaseLoader == "Quilt" ? "org.quiltmc.loader.impl.launch.knot.KnotClient"
                : plan.BaseLoader == "NeoForge" ? "net.neoforged.bootstrap.Main"
                : plan.BaseLoader == "Forge" ? "net.minecraftforge.bootstrap.Main" : "optifine.Main";
            await File.WriteAllTextAsync(Path.Combine(directory, "generated-loader.json"), JsonSerializer.Serialize(new
            {
                id = "generated-loader",
                inheritsFrom = plan.Minecraft,
                mainClass,
                libraries = Array.Empty<object>()
            }), ct);
            Directory.CreateDirectory(Path.Combine(stagingRoot, "libraries/fixture"));
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, "libraries/fixture/loader.jar"), "generated library", ct);
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, "launcher_profiles.json"), "installer changed profiles", ct);
            if (FailPhase == "loader") throw new IOException("installer failed");
            if (FailPhase == "cancel") Cancellation!.Cancel();
            ct.ThrowIfCancellationRequested();
            return "generated-loader";
        }
        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (FailPhase == "complete") throw new IOException("completion failed");
            AfterComplete?.Invoke();
            return Task.CompletedTask;
        }
        public Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
