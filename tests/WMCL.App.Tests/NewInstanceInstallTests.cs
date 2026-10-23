using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class NewInstanceInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-new-instance-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _stages = [];
    private readonly List<string> _installerRoots = [];

    public NewInstanceInstallTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "options.txt"), "user options");
        File.WriteAllText(Path.Combine(_root, "launcher_profiles.json"), "user profiles");
    }

    [Theory]
    [InlineData("无", "1.20.1", IsolationPolicy.Auto)]
    [InlineData("无", "Custom", IsolationPolicy.Isolated)]
    [InlineData("Fabric", "Custom", IsolationPolicy.Auto)]
    [InlineData("Fabric", "Custom", IsolationPolicy.Isolated)]
    [InlineData("Fabric", "Custom", IsolationPolicy.Global)]
    [InlineData("Forge", "Custom", IsolationPolicy.Global)]
    public async Task CompleteSelectionPublishesOnlyTheFinalInstanceAndUsesTheSelectedIsolation(string loader, string name, string isolation)
    {
        var plan = Plan(loader, name, addons: loader != "无");
        var before = Snapshot();
        var states = new Dictionary<CombinedInstallStep, CombinedInstallStepState>();
        var result = await Service(isolation).InstallAsync(plan, (step, state, _) =>
        {
            if (state == CombinedInstallStepState.Running)
                Assert.False(Directory.Exists(Target(plan)));
            states[step] = state;
        });
        Assert.Empty(result.Errors);
        Assert.Equal(plan.InstanceId, result.InstanceId);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, plan.InstanceId)).IsHealthy);
        Assert.All(states.Values, state => Assert.Equal(CombinedInstallStepState.Completed, state));
        AssertPreserved(before);
        Assert.Equal([name], Directory.GetDirectories(Path.Combine(_root, "versions")).Select(Path.GetFileName));
        if (loader != "无")
        {
            var mods = isolation == IsolationPolicy.Global ? "mods/" : $"versions/{name}/mods/";
            var manifest = InstallManifestStore.Load(_root, name)!;
            Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == mods + "OptiFine.jar");
            Assert.Equal("new OptiFine", File.ReadAllText(Path.Combine(_root, mods, "OptiFine.jar")));
            if (loader == "Fabric")
                Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == mods + plan.ApiVersion!.Files[0].Filename);
        }
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATargetAppearingAfterPlanCreationIsRejectedBeforeBackendCreation(bool file)
    {
        var plan = Plan();
        Directory.CreateDirectory(Path.GetDirectoryName(Target(plan))!);
        if (file) File.WriteAllText(Target(plan), "external file");
        else
        {
            Directory.CreateDirectory(Target(plan));
            File.WriteAllText(Path.Combine(Target(plan), "keep.txt"), "external instance");
        }
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(plan, Report));
        Assert.Equal(before, Snapshot());
        Assert.Empty(_stages);
    }

    [Theory]
    [InlineData("vanilla")]
    [InlineData("loader")]
    [InlineData("complete")]
    public async Task CoreFailureLeavesOriginalFilesAndNoPartiallyPublishedInstance(string phase)
    {
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => Service(configure: _ => new BackendOptions { FailPhase = phase })
            .InstallAsync(Plan(), Report));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData("libraries/generated.jar")]
    [InlineData("assets/indexes/fixture.json")]
    [InlineData("resources/fixture.txt")]
    [InlineData("mods/OptiFine.jar")]
    public async Task SharedConflictRejectsPublicationAndPreservesTheExistingFile(string relative)
    {
        Write(relative, "existing user bytes");
        var before = Snapshot();
        var failure = await Assert.ThrowsAsync<IOException>(() => Service(IsolationPolicy.Global)
            .InstallAsync(Plan(addons: true), Report));
        Assert.Contains("共享依赖", failure.Message);
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(IsolationPolicy.Global)]
    [InlineData(IsolationPolicy.Isolated)]
    public async Task AnAddonFailurePublishesTheCoreAndOtherSuccessfulSelectionsWithAnExplicitPartialStatus(string isolation)
    {
        var plan = Plan(addons: true);
        var result = await Service(isolation, _ => new BackendOptions { FailProject = "fabric-api" }).InstallAsync(plan, Report);
        Assert.Single(result.Errors);
        Assert.Contains("fabric-api failure", result.Status);
        Assert.DoesNotContain("安装完成：", result.Status);
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.DoesNotContain("fabric-api", manifest.Components.Keys);
        Assert.Contains("optifabric", manifest.Components.Keys);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, plan.InstanceId)).IsHealthy);
        AssertClean();
    }

    [Theory]
    [InlineData(IsolationPolicy.Global)]
    [InlineData(IsolationPolicy.Isolated)]
    public async Task FilesLeftBehindByAFailedAddonAreNotSilentlyPublished(string isolation)
    {
        var before = Snapshot();
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(isolation,
            _ => new BackendOptions { FailProject = "fabric-api", LeaveFailedMod = true }).InstallAsync(Plan(addons: true), Report));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationBeforeFinalMoveRollsBackGlobalModsAndDependencies(bool cancel)
    {
        var plan = Plan(addons: true);
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        void BeforeMove(CombinedInstallStep step, CombinedInstallStepState state, JobProgress update)
        {
            if (update.Stage != "发布实例") return;
            Assert.True(File.Exists(Path.Combine(_root, "mods/OptiFine.jar")));
            Assert.False(Directory.Exists(Target(plan)));
            if (cancel) cancellation.Cancel();
            else throw new IOException("publication failed");
        }
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(IsolationPolicy.Global).InstallAsync(plan, BeforeMove, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => Service(IsolationPolicy.Global).InstallAsync(plan, BeforeMove));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("shared")]
    public async Task FilesChangedAtTheFinalPublicationCallbackAreRechecked(string kind)
    {
        var plan = Plan();
        Write("libraries/generated.jar", "generated library");
        var before = Snapshot();
        void Mutate(CombinedInstallStep step, CombinedInstallStepState state, JobProgress update)
        {
            if (update.Stage != "发布实例") return;
            if (kind == "shared") Write("libraries/generated.jar", "external change");
            else File.WriteAllText(Path.Combine(_stages.Last(), "versions", plan.InstanceId, plan.InstanceId + ".jar"), "changed staged client");
        }
        await Assert.ThrowsAsync<IOException>(() => Service().InstallAsync(plan, Mutate));
        if (kind == "shared") before["libraries/generated.jar"] = FileUtil.Sha1(Path.Combine(_root, "libraries/generated.jar"));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Fact]
    public async Task IdenticalGlobalModsAreReusedWithoutChangingTheOriginalTimestamp()
    {
        var path = Write("mods/OptiFine.jar", "new OptiFine");
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(path);
        await Service(IsolationPolicy.Global).InstallAsync(Plan(addons: true), Report);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        AssertClean();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledCompletionPublishesNothingAndDoesNotMarkVanillaInstalled(bool cancel)
    {
        var before = Snapshot();
        var services = Services();
        var target = new VersionItem { Id = "1.20.1" };
        var download = new DownloadViewModel(services) { SelectedVersion = target };
        InstallViewModel? viewModel = null;
        var completed = false;
        viewModel = new InstallViewModel(download, InstallTestData.CreateMods(services), services, root =>
        {
            _stages.Add(root);
            return new Backend(root, _installerRoots)
            {
                AfterComplete = () =>
                {
                    if (cancel) viewModel!.CancelCombinedCommand.Execute(null);
                    else throw new IOException("completion failed");
                }
            };
        }, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        viewModel.ShowSelectedVersion();
        viewModel.SelectedBaseLoader = "Fabric";
        viewModel.InstallCompleted += (_, _) => completed = true;
        await viewModel.InstallCombinedCommand.ExecuteAsync(null);
        Assert.False(target.IsInstalled);
        Assert.Equal(before, Snapshot());
        Assert.False(completed);
        Assert.False(viewModel.IsCombinedBusy);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        Assert.Contains(cancel ? "已取消" : "completion failed", viewModel.CombinedStatus);
        if (cancel)
        {
            var stage = Assert.Single(viewModel.Stages, stage => stage.IsCancelled);
            Assert.Equal("已取消", stage.PercentText);
            Assert.False(stage.IsFailed);
            Assert.Equal("安装已取消", stage.Detail);
        }
    }

    [AvaloniaTheory]
    [InlineData("1.20.1", true)]
    [InlineData("Custom", false)]
    public async Task VanillaStatusWaitsForPublicationAndDescribesTheActualInstalledId(string name, bool installed)
    {
        var services = Services();
        var target = new VersionItem { Id = "1.20.1" };
        var download = new DownloadViewModel(services) { SelectedVersion = target };
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var viewModel = new InstallViewModel(download, InstallTestData.CreateMods(services), services,
            root => new Backend(root, _installerRoots) { CompleteGate = gate, Entered = entered },
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        var tasks = new TaskManagerViewModel(download, viewModel.Mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services), viewModel);
        viewModel.ShowSelectedVersion();
        viewModel.InstanceName = name;
        var view = new InstallView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var operation = viewModel.InstallCombinedCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.UpdateLayout();
            Assert.True(viewModel.IsCombinedBusy);
            Assert.False(target.IsInstalled);
            Assert.False(Directory.Exists(Path.Combine(_root, "versions", name)));
            Assert.True(viewModel.IsChoosingVersion);
            Assert.False(viewModel.VersionCatalog.CanCancel);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "取消安装"));
            Assert.Empty(view.FindControl<ScrollViewer>("PageScroll")!.GetVisualDescendants().OfType<ProgressBar>());
            var task = Assert.Single(tasks.Tasks);
            Assert.True(task.HasCancel);
            Assert.NotEmpty(task.Detail);
            Assert.Equal(viewModel.Stages.Count, task.Children!.Count);
        }
        finally
        {
            gate.SetResult();
            await operation;
            window.Close();
        }
        Assert.Equal(installed, target.IsInstalled);
        Assert.True(File.Exists(Path.Combine(_root, "versions", name, name + ".json")));
        Assert.True(viewModel.Stages.Last().IsDone);
        Assert.Contains(name, viewModel.CombinedStatus);
        var completed = Assert.Single(tasks.History, task => task.Title == "安装 " + name);
        Assert.Equal("安装 " + name, completed.Title);
        Assert.Equal(viewModel.CombinedStatus, completed.Detail);
        Assert.Equal("完成", completed.PercentText);
        Assert.False(completed.HasCancel);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskCenterKeepsFailureOrCancellationAfterReturningToCatalog(bool cancel)
    {
        var services = Services();
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var model = new InstallViewModel(download, InstallTestData.CreateMods(services), services,
            root => new Backend(root, _installerRoots)
            {
                Entered = entered,
                CompleteGate = gate,
                AfterComplete = () => { if (!cancel) throw new IOException("test download failure"); }
            }, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        using var instance = new InstanceViewModel(services);
        var tasks = new TaskManagerViewModel(download, model.Mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), instance, model);
        model.ShowSelectedVersion();
        model.InstanceName = "Fixed target";
        var operation = model.InstallCombinedCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsChoosingVersion);
        var active = Assert.Single(tasks.Tasks);
        Assert.True(active.HasCancel);
        if (cancel) active.Cancel!();
        gate.SetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(tasks.Tasks);
        var finished = Assert.Single(tasks.History, task => task.Title == "安装 Fixed target");
        Assert.Equal("安装 Fixed target", finished.Title);
        Assert.Equal(cancel ? "已取消" : "失败", finished.PercentText);
        Assert.Contains(cancel ? "已取消" : "test download failure", finished.Detail);
        Assert.False(finished.HasCancel);
        Assert.True(tasks.HasHistory);
        Assert.True(model.IsChoosingVersion);
        Assert.False(Directory.Exists(Path.Combine(_root, "versions/Fixed target")));
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        tasks.Refresh();
        Assert.Single(tasks.History, task => task.Title == "安装 Fixed target");
    }

    [AvaloniaFact]
    public async Task RejectedSubmissionKeepsSelectionAndRecordsTheReasonWithoutStartingDownload()
    {
        var services = Services();
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var backendCalls = 0;
        var model = new InstallViewModel(download, InstallTestData.CreateMods(services), services,
            root => { backendCalls++; return new Backend(root, _installerRoots); },
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        using var instance = new InstanceViewModel(services);
        var tasks = new TaskManagerViewModel(download, model.Mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), instance, model);
        model.ShowSelectedVersion();
        model.CombinedProgress = 100;
        using var held = services.GameOperations.Acquire(_root, "test active game");
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.False(model.IsChoosingVersion);
        Assert.False(model.IsCombinedBusy);
        Assert.Equal(0, backendCalls);
        Assert.Empty(tasks.Tasks);
        var rejected = Assert.Single(tasks.History, task => task.Title == "安装 1.20.1");
        Assert.Contains("test active game", rejected.Detail);
        Assert.Equal("失败", rejected.PercentText);
        Assert.Equal(0, rejected.Progress);
        Assert.Empty(rejected.Children!);
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
    private CombinedInstallPlan Plan(string loader = "Fabric", string name = "Custom", bool addons = false) =>
        CombinedInstallPlan.Create(_root, "1.20.1", true, loader, loader == "Forge" ? "47.2.0" : "0.16.0",
            addons ? InstallTestData.OptiFine() : null, addons, addons, name,
            InstallTestData.OptiFabric(), InstallTestData.Api());
    private NewInstanceInstallService Service(string isolation = IsolationPolicy.Auto, Func<string, BackendOptions>? configure = null) =>
        new(_root, isolation, root =>
        {
            _stages.Add(root);
            return new Backend(root, _installerRoots) { Options = configure?.Invoke(root) ?? new() };
        });
    private static void Report(CombinedInstallStep step, CombinedInstallStepState state, JobProgress update) { }
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), FileUtil.Sha1);
    private void AssertPreserved(Dictionary<string, string> before)
    {
        foreach (var (path, hash) in before) Assert.Equal(hash, FileUtil.Sha1(Path.Combine(_root, path)));
    }
    private void AssertClean()
    {
        Assert.All(_stages.Concat(_installerRoots), path => Assert.False(Directory.Exists(path)));
        Assert.Empty(Directory.GetDirectories(_root, ".wmcl-instance-install-*"));
    }
    private string Target(CombinedInstallPlan plan) => Path.Combine(_root, "versions", plan.InstanceId);
    private string Write(string relative, string contents)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed record BackendOptions
    {
        public string? FailPhase { get; init; }
        public string? FailProject { get; init; }
        public bool LeaveFailedMod { get; init; }
    }

    private sealed class Backend(string root, List<string> installerRoots) : ICombinedInstallBackend
    {
        public Action? AfterComplete { get; init; }
        public TaskCompletionSource? CompleteGate { get; init; }
        public TaskCompletionSource? Entered { get; init; }
        public BackendOptions Options { get; init; } = new();

        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(root, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"), JsonSerializer.Serialize(new
            {
                id = plan.Minecraft,
                clientVersion = plan.Minecraft,
                mainClass = "net.minecraft.client.main.Main",
                libraries = Array.Empty<object>()
            }), ct);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".jar"), "new client", ct);
            Directory.CreateDirectory(Path.Combine(root, "assets/indexes"));
            await File.WriteAllTextAsync(Path.Combine(root, "assets/indexes/fixture.json"), "{}", ct);
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            await File.WriteAllTextAsync(Path.Combine(root, "resources/fixture.txt"), "legacy resource", ct);
            if (Options.FailPhase == "vanilla") throw new IOException("vanilla failed");
            if (!plan.NeedsMerge && CompleteGate is not null)
            {
                Entered?.SetResult();
                await CompleteGate.Task.WaitAsync(ct);
            }
        }

        public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
        {
            installerRoots.Add(stagingRoot);
            var directory = Path.Combine(stagingRoot, "versions", "loader-profile");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "loader-profile.json"), JsonSerializer.Serialize(new
            {
                id = "loader-profile",
                inheritsFrom = plan.Minecraft,
                mainClass = plan.BaseLoader == "Forge" ? "net.minecraftforge.bootstrap.Main" : "net.fabricmc.loader.impl.launch.knot.KnotClient",
                libraries = Array.Empty<object>()
            }), ct);
            Directory.CreateDirectory(Path.Combine(stagingRoot, "libraries"));
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, "libraries/generated.jar"), "generated library", ct);
            if (Options.FailPhase == "loader") throw new IOException("loader failed");
            return "loader-profile";
        }

        public async Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (CompleteGate is not null)
            {
                Entered?.SetResult();
                await CompleteGate.Task.WaitAsync(ct);
            }
            AfterComplete?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Options.FailPhase == "complete") throw new IOException("completion failed");
        }

        public async Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var path = Path.Combine(root, "OptiFine.jar");
            await File.WriteAllTextAsync(path, "new OptiFine", ct);
            return path;
        }
        public async Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (project == Options.FailProject && !Options.LeaveFailedMod) throw new IOException(project + " failure");
            Directory.CreateDirectory(modsDirectory);
            var filename = project == "optifabric" ? plan.OptiFabric!.FileName : plan.ApiVersion!.Files[0].Filename;
            await File.WriteAllTextAsync(Path.Combine(modsDirectory, filename), project, ct);
            if (project == Options.FailProject) throw new IOException(project + " failure after writing");
            return [filename];
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
