using System.Text.Json;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class CombinedInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-combined-tests-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task InstalledResourceSnapshotsSurviveStagingCatalogChangesRestorationAndRename()
    {
        var plan = Plan("Fabric", optifine: true, api: true, optifabric: true);
        var hash = Convert.ToHexStringLower(SHA1.HashData([10, 11]));
        plan = plan with { ApiVersion = plan.ApiVersion! with { Files = [plan.ApiVersion!.Files[0] with { Sha1 = hash }] } };
        var result = await new NewInstanceInstallService(_root, IsolationPolicy.Isolated, root => new Backend(root))
            .InstallAsync(plan, (_, _, _) => { });
        Assert.Empty(result.Errors);
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            root => new Backend(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            confirmModification: (_, _) => Task.FromResult(true));
        await model.OpenInstanceAsync(_root, plan.InstanceId);
        Assert.True(model.IsExistingInstance);
        Assert.Equal(hash, model.ApiSelection.SelectedVersion!.Files[0].Sha1);
        Assert.Equal(hash, model.OptiFabricSelection.SelectedVersion!.Sha1);
        Assert.NotEqual(hash, model.ApiSelection.Versions.Single().Files[0].Sha1);
        Assert.False(model.CanApplyInstall);
        model.ApiCard.ClearCommand.Execute(null);
        model.OptiFabricCard.ClearCommand.Execute(null);
        await model.ApiCard.RestoreCommand.ExecuteAsync(null);
        await model.OptiFabricCard.RestoreCommand.ExecuteAsync(null);
        Assert.False(model.CanApplyInstall);
        model.InstanceName = "Renamed";
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Equal("Renamed", model.TargetInstanceId);
        Assert.False(model.CanApplyInstall);
        await model.OpenInstanceAsync(_root, "Renamed");
        Assert.True(model.IsExistingInstance);
        Assert.Equal(hash, model.ApiSelection.SelectedVersion!.Files[0].Sha1);
        Assert.Equal(hash, model.OptiFabricSelection.SelectedVersion!.Sha1);
        Assert.False(model.CanApplyInstall);
    }

    [Theory]
    [InlineData("Fabric", true, true, true, "fabric-api")]
    [InlineData("Fabric", true, false, false, "fabric-api")]
    [InlineData("Fabric", false, true, false, "fabric-api")]
    [InlineData("Forge", true, true, true, "fabric-api")]
    [InlineData("Quilt", false, true, true, "qsl")]
    [InlineData("LegacyFabric", false, true, true, "legacy-fabric-api")]
    [InlineData("OptiFine", true, true, true, "fabric-api")]
    [InlineData("NeoForge", false, true, true, "fabric-api")]
    [InlineData("无", false, true, true, "fabric-api")]
    public void PlanUsesIndependentSelectionsAndIncludesOnlyExecutedSteps(
        string loader, bool optifine, bool api, bool optifabric, string apiProject)
    {
        var minecraft = loader == "LegacyFabric" ? "1.12.2" : "1.20.1";
        var plan = CombinedInstallPlan.Create(_root, minecraft, false, loader,
            loader is "Forge" or "NeoForge" ? "47.2.0" : "0.16.0",
            optifine ? InstallTestData.OptiFine(minecraft) : null, api, optifabric, "", InstallTestData.OptiFabric(minecraft),
            InstallTestData.Api(minecraft, loader));
        Assert.Equal(loader, plan.BaseLoader);
        Assert.Equal(apiProject, plan.ApiProject);
        Assert.Equal(loader != "无", plan.Steps.Contains(CombinedInstallStep.Loader));
        Assert.Equal(optifine && loader != "OptiFine", plan.Steps.Contains(CombinedInstallStep.OptiFineMod));
        Assert.Equal(api && loader is "Fabric" or "Quilt" or "LegacyFabric", plan.Steps.Contains(CombinedInstallStep.Api));
        Assert.Equal(optifabric && optifine && loader == "Fabric", plan.Steps.Contains(CombinedInstallStep.OptiFabric));
        Assert.Equal(CombinedInstallStep.Vanilla, plan.Steps[0]);
        Assert.Equal(CombinedInstallStep.Manifest, plan.Steps[^1]);
        Assert.Equal(plan.Steps.Count, plan.Steps.Distinct().Count());
    }

    [Theory]
    [InlineData("Fabric")]
    [InlineData("Forge")]
    public async Task CombinedOptiFineInstallsBaseOnceAndSavesVerifiedManifestLast(string loader)
    {
        var backend = new Backend(_root);
        var plan = Plan(loader, optifine: true, api: true, optifabric: true);
        var completed = new List<CombinedInstallStep>();
        var service = new CombinedInstallService(_root, IsolationPolicy.Isolated, backend);
        var result = await service.InstallAsync(plan, (step, state, _) =>
        {
            if (state == CombinedInstallStepState.Completed) completed.Add(step);
            if (step != CombinedInstallStep.Manifest)
                Assert.False(File.Exists(InstallManifestStore.GetPath(_root, plan.InstanceId)));
        });
        Assert.Empty(result.Errors);
        Assert.Equal(plan.Steps, completed);
        Assert.Equal(1, backend.LoaderCalls);
        Assert.Equal(loader, backend.InstalledPlan!.BaseLoader);
        Assert.Equal(loader == "Fabric" ? new[] { "fabric-api", "optifabric" } : [], backend.ModCalls);
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.Equal("I6", manifest.Components["optifine"]);
        Assert.Equal(plan.OptiFine, manifest.ComponentMetadata["optifine"].Deserialize<WMCL.Modding.OptiFine.OptiFineVersion>());
        if (loader == "Fabric")
        {
            Assert.Equal("1.14.3", manifest.Components["optifabric"]);
            Assert.Equal(plan.OptiFabric!.Id, manifest.ComponentMetadata["optifabric"].Deserialize<WMCL.Modding.OptiFine.OptiFabricFile>()!.Id);
            Assert.Equal(manifest.ManagedFiles.Single(file => file.Component == "optifabric").Sha1,
                manifest.ComponentMetadata["optifabric"].Deserialize<WMCL.Modding.OptiFine.OptiFabricFile>()!.Sha1);
            Assert.Equal(result.InstalledOptiFabric!.Sha1,
                manifest.ComponentMetadata["optifabric"].Deserialize<WMCL.Modding.OptiFine.OptiFabricFile>()!.Sha1);
            var savedApi = manifest.ComponentMetadata[plan.ApiProject].Deserialize<WMCL.Modding.Modrinth.ModrinthVersion>()!;
            Assert.Equal(plan.ApiVersion!.Id, savedApi.Id);
            Assert.Equal(plan.ApiVersion.Files, savedApi.Files);
            Assert.Contains(manifest.ManagedFiles, file => file.Component == "optifabric" && file.Url == plan.OptiFabric!.DownloadUrl);
        }
        Assert.Contains(loader.ToLowerInvariant(), manifest.Components.Keys);
        var installedVersion = await new VersionRepository(_root).GetVersionAsync(plan.InstanceId);
        Assert.Equal(plan.Minecraft, MinecraftVersionUtil.GetBaseGameVersion(plan.InstanceId, installedVersion));
        Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == $"versions/{plan.InstanceId}/mods/OptiFine.jar");
        Assert.All(manifest.ManagedFiles, file => Assert.StartsWith($"versions/{plan.InstanceId}/", file.RelativePath));
        var verification = await InstallManifestStore.VerifyAsync(_root, plan.InstanceId);
        Assert.Equal(0, verification.Missing);
        Assert.Equal(0, verification.Corrupted);
        Assert.True(File.Exists(Path.Combine(_root, "libraries", "generated.jar")));
        Assert.False(Directory.Exists(backend.StagingRoot));
    }

    [Fact]
    public async Task DisabledAddonsAreNotInstalledAndOldLoaderInstanceIsPreserved()
    {
        var backend = new Backend(_root);
        var existing = Path.Combine(_root, "versions", Backend.LoaderId);
        Directory.CreateDirectory(existing);
        var originalJson = Path.Combine(existing, Backend.LoaderId + ".json");
        await File.WriteAllTextAsync(originalJson, "user profile");
        await File.WriteAllTextAsync(Path.Combine(existing, "options.txt"), "user settings");
        var plan = Plan("Fabric", true, false, false);
        var service = new CombinedInstallService(_root, IsolationPolicy.Isolated, backend);
        var result = await service.InstallAsync(plan, (_, _, _) => { });
        Assert.Empty(result.Errors);
        Assert.Empty(backend.ModCalls);
        Assert.Equal("user profile", await File.ReadAllTextAsync(originalJson));
        Assert.Equal("user settings", await File.ReadAllTextAsync(Path.Combine(existing, "options.txt")));
        Assert.DoesNotContain("fabric-api", InstallManifestStore.Load(_root, plan.InstanceId)!.Components.Keys);
        Assert.DoesNotContain("optifabric", InstallManifestStore.Load(_root, plan.InstanceId)!.Components.Keys);
    }

    [Fact]
    public async Task AddonFailureStaysVisibleAndIsNotRecordedAsInstalled()
    {
        var backend = new Backend(_root) { FailProject = "fabric-api" };
        var plan = Plan("Fabric", true, true, true);
        var states = new Dictionary<CombinedInstallStep, CombinedInstallStepState>();
        var result = await new CombinedInstallService(_root, IsolationPolicy.Global, backend)
            .InstallAsync(plan, (step, state, _) => states[step] = state);
        Assert.Single(result.Errors);
        Assert.Contains("fabric-api test failure", result.Status);
        Assert.DoesNotContain("安装完成：", result.Status);
        Assert.Equal(CombinedInstallStepState.Failed, states[CombinedInstallStep.Api]);
        Assert.Equal(CombinedInstallStepState.Completed, states[CombinedInstallStep.OptiFabric]);
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.DoesNotContain("fabric-api", manifest.Components.Keys);
        Assert.Contains("optifabric", manifest.Components.Keys);
        Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == "mods/OptiFine.jar");
        var verification = await InstallManifestStore.VerifyAsync(_root, plan.InstanceId);
        Assert.Equal(0, verification.Missing + verification.Corrupted);
    }

    [Theory]
    [InlineData("Quilt", "qsl")]
    [InlineData("LegacyFabric", "legacy-fabric-api")]
    public async Task FabricFamilyApiIsRecordedWithItsActualFiles(string loader, string project)
    {
        var plan = Plan(loader, false, true, false);
        var backend = new Backend(_root);
        await new CombinedInstallService(_root, IsolationPolicy.Isolated, backend).InstallAsync(plan, (_, _, _) => { });
        Assert.Equal([project], backend.ModCalls);
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.Contains(project, manifest.Components.Keys);
        Assert.Equal(plan.ApiVersion!.VersionNumber, manifest.Components[project]);
        Assert.Contains(manifest.ManagedFiles, file => file.RelativePath.EndsWith(plan.ApiVersion.Files[0].Filename) &&
            file.Url == plan.ApiVersion.Files[0].Url);
    }

    [Fact]
    public async Task CancellationDoesNotBecomePartialSuccessOrWriteACompleteManifest()
    {
        using var cancellation = new CancellationTokenSource();
        var plan = Plan("Fabric", false, true, false);
        var backend = new Backend(_root);
        var service = new CombinedInstallService(_root, IsolationPolicy.Isolated, backend);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(plan, (step, state, _) =>
        {
            if (step == CombinedInstallStep.Api && state == CombinedInstallStepState.Running) cancellation.Cancel();
        }, cancellation.Token));
        Assert.False(File.Exists(InstallManifestStore.GetPath(_root, plan.InstanceId)));
        Assert.False(Directory.Exists(backend.StagingRoot));
    }

    [Fact]
    public async Task ViewModelCapturesChosenVersionsAndBuildsStagesBeforeVanillaDownload()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root, IsolationMode = IsolationPolicy.Isolated });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var mods = InstallTestData.CreateMods(services);
        var backend = new Backend(_root) { VanillaGate = new TaskCompletionSource() };
        var viewModel = new InstallViewModel(download, mods, services, root => backend.WithRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        viewModel.ShowSelectedVersion();
        viewModel.SelectedBaseLoader = "Fabric";
        mods.SelectedLoader = "0.15.0";
        viewModel.InstallOptiFineWithBase = true;
        viewModel.InstallFabricApi = true;
        var chosenOptiFabric = InstallTestData.OptiFabric(version: "1.13.25", id: 4642832);
        viewModel.OptiFabricSelection.SelectedVersion = chosenOptiFabric;
        var chosenApi = InstallTestData.Api(version: "0.91.0", id: "chosen-api");
        viewModel.ApiSelection.SelectedVersion = chosenApi;
        Assert.Equal("1.20.1-Fabric 0.15.0-OptiFine_I6", viewModel.InstanceName);
        viewModel.InstanceName = "My Minecraft";
        var installing = viewModel.InstallCombinedCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsCombinedBusy);
        Assert.True(viewModel.Stages[0].IsActive);
        Assert.Contains(viewModel.Stages, stage => stage.Title == "Fabric API");
        viewModel.SelectedBaseLoader = "Forge";
        viewModel.InstallFabricApi = false;
        viewModel.OptiFabricSelection.SelectedVersion = InstallTestData.OptiFabric();
        viewModel.ApiSelection.SelectedVersion = InstallTestData.Api();
        backend.VanillaGate.SetResult();
        await installing;
        Assert.Equal("Fabric", backend.InstalledPlan!.BaseLoader);
        Assert.Equal("My Minecraft", backend.InstalledPlan.InstanceId);
        Assert.Equal("0.15.0", backend.InstalledPlan.LoaderVersion);
        Assert.Same(chosenOptiFabric, backend.InstalledPlan.OptiFabric);
        Assert.Equal(chosenApi.Id, backend.InstalledPlan.ApiVersion!.Id);
        Assert.Contains("fabric-api", backend.ModCalls);
        Assert.All(viewModel.Stages, stage => Assert.True(stage.IsDone));
        Assert.StartsWith("安装完成：", viewModel.CombinedStatus);
        Assert.False(viewModel.IsCombinedBusy);
    }

    [Fact]
    public void ExplicitDuplicateAndInvalidNameAndIncompatibleForgeFailBeforeExecution()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "existing"));
        Assert.Throws<IOException>(() => CombinedInstallPlan.Create(_root, "1.20.1", false, "Fabric", "0.16.0", null, false, false, "existing"));
        Assert.ThrowsAny<Exception>(() => CombinedInstallPlan.Create(_root, "1.20.1", false, "Fabric", "0.16.0", null, false, false, "../bad"));
        Assert.Throws<InvalidOperationException>(() => CombinedInstallPlan.Create(_root, "1.20.1", false, "Forge", "47.1.0", InstallTestData.OptiFine(), false, false, ""));
    }

    [Fact]
    public async Task ViewModelDoesNotReplaceAddonFailureWithSuccess()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var backend = new Backend(_root) { FailProject = "fabric-api" };
        var viewModel = new InstallViewModel(download, InstallTestData.CreateMods(services), services, root => backend.WithRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        viewModel.ShowSelectedVersion();
        viewModel.SelectedBaseLoader = "Fabric";
        viewModel.InstallFabricApi = true;
        await viewModel.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Contains("fabric-api test failure", viewModel.CombinedStatus);
        Assert.DoesNotContain("安装完成：", viewModel.CombinedStatus);
        Assert.True(viewModel.Stages.Single(stage => stage.Title == "Fabric API").IsFailed);
        Assert.True(viewModel.Stages.Last().IsDone);
        Assert.False(download.SelectedVersion.IsInstalled);
        Assert.True(File.Exists(Path.Combine(_root, "versions", "1.20.1-Fabric 0.16.0", "1.20.1-Fabric 0.16.0.json")));
    }

    [Fact]
    public async Task ViewModelCancellationStopsDownloadAndNeverReportsCompletion()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var backend = new Backend(_root) { VanillaGate = new TaskCompletionSource() };
        var viewModel = new InstallViewModel(download, InstallTestData.CreateMods(services), services, root => backend.WithRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        var completed = false;
        viewModel.InstallCompleted += (_, _) => completed = true;
        var installing = viewModel.InstallCombinedCommand.ExecuteAsync(null);
        viewModel.CancelCombinedCommand.Execute(null);
        await installing;
        Assert.StartsWith("已取消组合安装", viewModel.CombinedStatus);
        Assert.False(viewModel.IsCombinedBusy);
        Assert.False(completed);
        Assert.False(download.SelectedVersion.IsInstalled);
    }

    [Fact]
    public async Task StandaloneOptiFineCreatesOneInstanceWithoutModStages()
    {
        var backend = new Backend(_root);
        var plan = Plan("OptiFine", true, true, true);
        var result = await new CombinedInstallService(_root, IsolationPolicy.Isolated, backend)
            .InstallAsync(plan, (_, _, _) => { });
        Assert.Empty(result.Errors);
        Assert.Equal(1, backend.LoaderCalls);
        Assert.Empty(backend.ModCalls);
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", plan.InstanceId, "mods")));
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.Equal("I6", manifest.Components["optifine"]);
        Assert.Equal(2, manifest.ManagedFiles.Count);
    }

    [Fact]
    public void StagePercentTextNotifiesForProgressAndTerminalState()
    {
        var stage = new InstallStage("Fabric API");
        var notifications = new List<string?>();
        stage.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        stage.Progress = 37;
        Assert.Equal("37%", stage.PercentText);
        stage.IsFailed = true;
        Assert.Equal("失败", stage.PercentText);
        stage.IsFailed = false;
        stage.IsDone = true;
        Assert.Equal("完成", stage.PercentText);
        Assert.Equal(4, notifications.Count(name => name == nameof(InstallStage.PercentText)));
    }

    private CombinedInstallPlan Plan(string loader, bool optifine = false, bool api = false, bool optifabric = false)
    {
        var minecraft = loader == "LegacyFabric" ? "1.12.2" : "1.20.1";
        return CombinedInstallPlan.Create(_root, minecraft, false, loader,
            loader is "Forge" or "NeoForge" ? "47.2.0" : "0.16.0",
            optifine ? InstallTestData.OptiFine(minecraft) : null, api, optifabric, "test-instance", InstallTestData.OptiFabric(minecraft),
            InstallTestData.Api(minecraft, loader));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Backend(string gameRoot) : ICombinedInstallBackend
    {
        public const string LoaderId = "fabric-loader-0.16.0-1.20.1";
        public TaskCompletionSource? VanillaGate { get; init; }
        public string? FailProject { get; init; }
        public int LoaderCalls { get; private set; }
        public string? StagingRoot { get; private set; }
        public CombinedInstallPlan? InstalledPlan { get; private set; }
        public List<string> ModCalls { get; } = [];

        public Backend WithRoot(string root)
        {
            gameRoot = root;
            return this;
        }

        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (VanillaGate is not null) await VanillaGate.Task.WaitAsync(ct);
            var directory = Path.Combine(gameRoot, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"),
                JsonSerializer.Serialize(new { id = plan.Minecraft, type = "release", mainClass = "net.minecraft.client.main.Main", libraries = Array.Empty<object>() }), ct);
            await File.WriteAllBytesAsync(Path.Combine(directory, plan.Minecraft + ".jar"), [1, 2, 3], ct);
        }

        public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
        {
            LoaderCalls++;
            InstalledPlan = plan;
            StagingRoot = stagingRoot;
            var directory = Path.Combine(stagingRoot, "versions", LoaderId);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, LoaderId + ".json"), JsonSerializer.Serialize(new
            {
                id = LoaderId,
                inheritsFrom = plan.Minecraft,
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                libraries = Array.Empty<object>()
            }), ct);
            Directory.CreateDirectory(Path.Combine(stagingRoot, "libraries"));
            await File.WriteAllBytesAsync(Path.Combine(stagingRoot, "libraries", "generated.jar"), [4, 5, 6], ct);
            return LoaderId;
        }

        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.True(File.Exists(Path.Combine(gameRoot, "versions", instanceId, instanceId + ".jar")));
            return Task.CompletedTask;
        }

        public async Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(gameRoot, "test-cache");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "OptiFine.jar");
            await File.WriteAllBytesAsync(path, [7, 8, 9], ct);
            return path;
        }

        public async Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ModCalls.Add(project);
            if (project == FailProject) throw new IOException(project + " test failure");
            Directory.CreateDirectory(modsDirectory);
            var filename = project == "optifabric" ? plan.OptiFabric!.FileName : plan.ApiVersion!.Files[0].Filename;
            await File.WriteAllBytesAsync(Path.Combine(modsDirectory, filename), [10, 11], ct);
            return [filename];
        }
    }
}
