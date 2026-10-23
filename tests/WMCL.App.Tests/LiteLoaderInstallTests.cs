using System.Text.Json;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Modpack;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Fabric;

namespace WMCL.App.Tests;

public sealed class LiteLoaderInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-lite-install-" + Guid.NewGuid().ToString("N"));
    private const string Minecraft = "1.12.2";
    private const string ForgeVersion = "14.23.5.2860";
    private const string TweakClass = "com.mumfrey.liteloader.launch.LiteLoaderTweaker";

    private static LiteLoaderVersion Version => new(Minecraft, "1.12.2-SNAPSHOT", TweakClass, true,
        DateTimeOffset.FromUnixTimeSeconds(1500000000), null,
        JsonSerializer.SerializeToElement(new[] { new { name = "net.minecraft:launchwrapper:1.12" } }));

    private CombinedInstallPlan Plan(string loader = "无", bool optifine = false, string? existing = null) =>
        CombinedInstallPlan.Create(_root, Minecraft, false, loader, loader == "Forge" ? ForgeVersion : null,
            optifine ? new OptiFineVersion(Minecraft, "HD_U", "G5", "OptiFine.jar", ForgeVersion) : null,
            false, false, "lite-instance", existingInstanceId: existing, liteLoader: Version);

    [AvaloniaFact]
    public async Task DownloadCommandPublishesSelectedLiteLoaderAndReopensItsActualMetadata()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = new VersionItem { Id = Minecraft } };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]),
            getLiteLoaderVersions: _ => Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([Version]));
        var model = new InstallViewModel(download, mods, services, root => new Backend(root),
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        model.ShowSelectedVersion();
        await ((IAsyncRelayCommand)Assert.Single(model.LiteLoaderCard.Entries).SelectCommand).ExecuteAsync(null);
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.StartsWith("安装完成：", model.CombinedStatus);
        const string id = "1.12.2-LiteLoader";
        Assert.Equal(Version.Version, InstallManifestStore.Load(_root, id)!.Components["liteloader"]);
        await model.OpenInstanceAsync(_root, id);
        Assert.Equal(Version.Version, model.SelectedLiteLoader!.Version);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [Theory]
    [InlineData("无", false)]
    [InlineData("Forge", false)]
    [InlineData("OptiFine", true)]
    [InlineData("Forge", true)]
    public async Task NewInstanceStagesAndPublishesOriginalSupportedCombinations(string loader, bool optifine)
    {
        var plan = Plan(loader, optifine);
        var backendCalls = 0;
        var completed = new List<CombinedInstallStep>();
        var service = new NewInstanceInstallService(_root, IsolationPolicy.Isolated, root =>
            new Backend(root, () => backendCalls++));
        var result = await service.InstallAsync(plan, (step, state, _) =>
        {
            if (state == CombinedInstallStepState.Completed) completed.Add(step);
        });
        Assert.Empty(result.Errors);
        Assert.Equal(loader == "Forge" ? 1 : 0, backendCalls);
        Assert.Contains(CombinedInstallStep.LiteLoader, completed);
        Assert.Equal(CombinedInstallStep.Apply, completed[^1]);
        var installed = await new VersionRepository(_root).GetLocalVersionAsync(plan.InstanceId);
        Assert.Equal(Minecraft, installed.BaseGameVersion);
        Assert.Null(installed.InheritsFrom);
        Assert.Equal(loader == "Forge" ? "forge" : "liteloader", MinecraftVersionUtil.GetLoader(installed));
        Assert.True(MinecraftVersionUtil.IsModable(installed));
        Assert.Equal("net.minecraft.launchwrapper.Launch", installed.MainClass);
        Assert.Equal("2017-09-18T08:39:46Z", installed.Time);
        Assert.Equal("2017-09-18T08:39:46Z", installed.ReleaseTime);
        var arguments = installed.Arguments!.Game.SelectMany(argument => argument.Values).ToArray();
        Assert.Contains("${auth_player_name}", arguments);
        Assert.Contains(TweakClass, arguments);
        if (loader == "Forge") Assert.Contains("net.minecraftforge.fml.common.launcher.FMLTweaker", arguments);
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.Equal(Version.Version, manifest.Components["liteloader"]);
        var saved = manifest.ComponentMetadata["liteloader"].Deserialize<LiteLoaderVersion>()!;
        Assert.Equal(Version.Version, saved.Version);
        Assert.Equal(TweakClass, saved.TweakClass);
        Assert.True(JsonElement.DeepEquals(Version.Libraries, saved.Libraries));
        Assert.Equal(optifine, manifest.Components.ContainsKey("optifine"));
        if (optifine)
        {
            var relative = $"versions/{plan.InstanceId}/mods/{Minecraft}/OptiFine.jar";
            Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == relative && file.Component == "optifine");
            Assert.True(File.Exists(Path.Combine(_root, relative)));
            Assert.False(File.Exists(Path.Combine(_root, "versions", plan.InstanceId, "mods", "OptiFine.jar")));
        }
        Assert.True((await InstallManifestStore.VerifyAsync(_root, plan.InstanceId)).IsHealthy);
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-instance-install-*"));
        var export = Path.Combine(_root, "unsupported.mrpack");
        await Assert.ThrowsAsync<NotSupportedException>(() => ModpackExporter.ExportAsync(_root, plan.InstanceId, export,
            isolationMode: IsolationPolicy.Isolated));
        Assert.False(File.Exists(export));
    }

    [Fact]
    public void PlanRejectsWrongMinecraftAndUnsupportedLoaderCombinations()
    {
        Assert.Throws<InvalidOperationException>(() => CombinedInstallPlan.Create(_root, "1.20.1", false,
            "无", null, null, false, false, "lite-instance", liteLoader: Version));
        foreach (var loader in new[] { "Fabric", "Quilt", "LegacyFabric", "NeoForge" })
            Assert.Throws<InvalidOperationException>(() => CombinedInstallPlan.Create(_root, Minecraft, false,
                loader, "1.0.0", null, false, false, "lite-instance", liteLoader: Version));
    }

    [Fact]
    public async Task CancellationBeforeLiteLoaderDoesNotPublishOrTouchExistingData()
    {
        Directory.CreateDirectory(_root);
        var sentinel = Path.Combine(_root, "launcher_profiles.json");
        await File.WriteAllTextAsync(sentinel, "preserve");
        using var cancellation = new CancellationTokenSource();
        var plan = Plan();
        var service = new NewInstanceInstallService(_root, IsolationPolicy.Isolated, root => new Backend(root));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(plan, (step, state, _) =>
        {
            if (step == CombinedInstallStep.LiteLoader && state == CombinedInstallStepState.Running) cancellation.Cancel();
        }, cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", plan.InstanceId)));
        Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-instance-install-*"));
    }

    [Fact]
    public async Task CompletionFailureDoesNotPublishPartialProfile()
    {
        var plan = Plan();
        var service = new NewInstanceInstallService(_root, IsolationPolicy.Isolated, root => new Backend(root, failCompletion: true));
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(plan, (_, _, _) => { }));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", plan.InstanceId)));
    }

    [Fact]
    public async Task RemovingLiteLoaderAndOptiFineUpdatesManifestAndPreservesUserFiles()
    {
        var plan = Plan("OptiFine", true);
        await new NewInstanceInstallService(_root, IsolationPolicy.Isolated, root => new Backend(root))
            .InstallAsync(plan, (_, _, _) => { });
        var instance = Path.Combine(_root, "versions", plan.InstanceId);
        var userFile = Path.Combine(instance, "options.txt");
        await File.WriteAllTextAsync(userFile, "preserve options");
        var previous = InstallManifestStore.Load(_root, plan.InstanceId)!;
        previous.ComponentMetadata["custom"] = JsonSerializer.SerializeToElement(new { value = "preserve" });
        InstallManifestStore.Save(_root, plan.InstanceId, previous);
        var reset = CombinedInstallPlan.Create(_root, Minecraft, false, "无", null, null, false, false,
            plan.InstanceId, existingInstanceId: plan.InstanceId);
        await new ExistingInstanceInstallService(_root, IsolationPolicy.Isolated, root => new Backend(root))
            .InstallAsync(reset, (_, _, _) => { });
        Assert.Equal("preserve options", await File.ReadAllTextAsync(userFile));
        var manifest = InstallManifestStore.Load(_root, plan.InstanceId)!;
        Assert.False(manifest.Components.ContainsKey("liteloader"));
        Assert.False(manifest.ComponentMetadata.ContainsKey("liteloader"));
        Assert.Equal("preserve", manifest.ComponentMetadata["custom"].GetProperty("value").GetString());
        Assert.False(manifest.Components.ContainsKey("optifine"));
        Assert.False(File.Exists(Path.Combine(instance, "mods", Minecraft, "OptiFine.jar")));
        Assert.Equal("vanilla", MinecraftVersionUtil.GetLoader(await new VersionRepository(_root).GetLocalVersionAsync(plan.InstanceId)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Backend(string root, Action? installedLoader = null, bool failCompletion = false) : ICombinedInstallBackend
    {
        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var directory = Path.Combine(root, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"), JsonSerializer.Serialize(new
            {
                id = plan.Minecraft,
                mainClass = "net.minecraft.client.main.Main",
                time = "2017-09-18T08:39:46Z",
                releaseTime = "2017-09-18T08:39:46Z",
                minecraftArguments = "--username ${auth_player_name} --version ${version_name}",
                libraries = Array.Empty<object>()
            }), ct);
            await File.WriteAllBytesAsync(Path.Combine(directory, plan.Minecraft + ".jar"), [1, 2, 3], ct);
        }

        public async Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
        {
            installedLoader?.Invoke();
            Assert.Equal("Forge", plan.BaseLoader);
            const string id = "forge-test";
            var directory = Path.Combine(stagingRoot, "versions", id);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new
            {
                id,
                inheritsFrom = plan.Minecraft,
                mainClass = "net.minecraft.launchwrapper.Launch",
                minecraftArguments = "--username ${auth_player_name} --version ${version_name} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker",
                libraries = new[] { new { name = "net.minecraftforge:forge:" + ForgeVersion } }
            }), ct);
            return id;
        }

        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct) =>
            failCompletion ? Task.FromException(new IOException("Fixture completion failure")) : Task.CompletedTask;

        public async Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var path = Path.Combine(root, "OptiFine.jar");
            await File.WriteAllBytesAsync(path, [4, 5, 6], ct);
            return path;
        }

        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory,
            IProgress<JobProgress> progress, CancellationToken ct) => throw new InvalidOperationException("Unexpected API installation");
    }
}
