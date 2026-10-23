using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class StandaloneLoaderIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-standalone-loader-" + Guid.NewGuid().ToString("N"));

    [AvaloniaTheory]
    [InlineData("profile")]
    [InlineData("manifest")]
    [InlineData("client")]
    [InlineData("inheritance")]
    public async Task LocalVanillaSelectionUsesItsMinecraftIdentityForAllComponents(string source)
    {
        CreateInstance("Custom", "1.20.1", source);
        var before = Snapshot();
        var calls = new List<string>();
        var mods = CreateMods(Services(), calls);
        var view = new ModsView { DataContext = mods };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await mods.LoadAsync();
            var selector = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, mods.GameVersions));
            selector.SelectedItem = "Custom";
            Assert.Equal("Custom", selector.SelectedItem);
            Assert.Equal("1.20.1", mods.SelectedGameVersion);
            Assert.NotEmpty(calls);
            Assert.All(calls, game => Assert.Equal("1.20.1", game));
            Assert.True(mods.CanInstall);
            Assert.True(mods.CanInstallForgelike);
            Assert.True(mods.CanInstallOptiFine);
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OfficialVanillaWithoutExtraMetadataIsCheckedAgainstTheCatalog()
    {
        CreateInstance("1.12.2", "1.12.2", "unknown");
        var calls = new List<string>();
        var checkedCatalog = 0;
        var mods = CreateMods(Services(), calls, () =>
        {
            checkedCatalog++;
            return Task.FromResult<IReadOnlyList<string>>(["1.12.2"]);
        });
        await mods.LoadAsync();
        Assert.Equal(1, checkedCatalog);
        Assert.Equal("1.12.2", mods.SelectedVanillaInstance);
        Assert.Equal("1.12.2", mods.SelectedGameVersion);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, game => Assert.Equal("1.12.2", game));
    }

    [AvaloniaTheory]
    [InlineData("profile-id")]
    [InlineData("folder")]
    [InlineData("profile-text")]
    public async Task UnknownLocalNameNeverReachesTheComponentEndpoints(string source)
    {
        var id = source == "profile-text" ? "Friendly" : "1.20.1-custom";
        CreateInstance(id, "1.20.1", "unknown");
        if (source != "profile-id")
            File.WriteAllText(Path.Combine(_root, "versions", id, id + ".json"),
                """{"id":"Friendly","description":"Minecraft 1.20.1","libraries":[]}""");
        var calls = new List<string>();
        var mods = CreateMods(Services(), calls, () => Task.FromResult<IReadOnlyList<string>>(["1.20.1"]));
        var before = Snapshot();
        await mods.LoadAsync();
        Assert.Equal(id, mods.SelectedVanillaInstance);
        Assert.Null(mods.SelectedGameVersion);
        Assert.Empty(calls);
        Assert.Contains("无法识别", mods.ComponentTargetInfo);
        Assert.False(mods.CanInstall);
        Assert.False(mods.CanInstallForgelike);
        Assert.False(mods.CanInstallOptiFine);
        await mods.InstallFabricCommand.ExecuteAsync(null);
        await mods.InstallForgelikeCommand.ExecuteAsync(null);
        await mods.InstallOptiFineCommand.ExecuteAsync(null);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaFact]
    public async Task AnUnconfirmedMinecraftGuessCannotBeUsedForOnlineModOperations()
    {
        CreateInstance("Friendly", "1.20.1", "unknown");
        File.WriteAllText(Path.Combine(_root, "versions/Friendly/Friendly.json"),
            """{"id":"Friendly","description":"Minecraft 1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.15.0"}]}""");
        var mods = CreateMods(Services(), []);
        await mods.LoadAsync();
        await mods.SelectModInstanceAsync("Friendly");
        Assert.Contains("无法精准识别", mods.ModTargetInfo);
        Assert.False(mods.CanInstallMod);
        Assert.Empty(mods.ModVersions);
        await mods.CheckModUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("请选择", mods.UpdateStatus);
    }

    [AvaloniaFact]
    public async Task AFailedOfficialCatalogCanBeRetriedWithoutLosingTheLocalSelection()
    {
        CreateInstance("1.12.2", "1.12.2", "unknown");
        var calls = new List<string>();
        var attempts = 0;
        var mods = CreateMods(Services(), calls, () => ++attempts == 1
            ? Task.FromException<IReadOnlyList<string>>(new IOException("catalog unavailable"))
            : Task.FromResult<IReadOnlyList<string>>(["1.12.2"]));
        await mods.LoadAsync();
        Assert.Empty(calls);
        Assert.Null(mods.SelectedGameVersion);
        Assert.Contains("catalog unavailable", mods.Status);
        await mods.LoadAsync();
        Assert.Equal("1.12.2", mods.SelectedVanillaInstance);
        Assert.Equal("1.12.2", mods.SelectedGameVersion);
        Assert.Equal(3, calls.Count);
        Assert.True(mods.CanInstall);
    }

    [AvaloniaFact]
    public async Task RefreshPreservesTheSelectedLocalAliasWhenSeveralHaveTheSameBase()
    {
        CreateInstance("First", "1.20.1");
        CreateInstance("Second", "1.20.1");
        var mods = CreateMods(Services(), []);
        var view = new ModsView { DataContext = mods };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await mods.LoadAsync();
            var selector = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, mods.GameVersions));
            selector.SelectedItem = "Second";
            await mods.LoadAsync();
            Assert.Equal("Second", selector.SelectedItem);
            Assert.Equal("Second", mods.SelectedVanillaInstance);
            Assert.Equal("1.20.1", mods.SelectedGameVersion);
            Assert.Contains("Second · Minecraft 1.20.1", mods.ComponentTargetInfo);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("rename")]
    [InlineData("clone")]
    [InlineData("delete")]
    public async Task ManagementEventsRemapTheLocalSelectionAndPreserveItsMinecraftIdentity(string action)
    {
        CreateInstance("Source", "1.20.1");
        CreateInstance("Other", "1.21.1");
        var calls = new List<string>();
        using var main = new MainWindowViewModel(Services(),
            loadVersions: root => Task.FromResult<IReadOnlyList<VersionItem>>(new VersionRepository(root).ListInstalled()
                .Select(version => new VersionItem { Id = version.Id, IsInstalled = true }).ToArray()),
            instancePrompt: (_, _, _) => Task.FromResult<string?>("Target"),
            instanceConfirm: (_, _) => Task.FromResult(true), createMods: services => CreateMods(services, calls));
        await main.Mods.LoadAsync();
        main.Mods.SelectedVanillaInstance = "Source";
        main.Instance.SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true };
        var command = action switch
        {
            "clone" => main.Instance.CloneInstanceCommand,
            "delete" => main.Instance.DeleteInstanceCommand,
            _ => main.Instance.RenameInstanceCommand
        };
        await command.ExecuteAsync(null);
        await main.InstanceRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(action == "rename" ? "Target" : action == "clone" ? "Source" : "Other", main.Mods.SelectedVanillaInstance);
        Assert.Equal(action == "delete" ? "1.21.1" : "1.20.1", main.Mods.SelectedGameVersion);
        Assert.All(calls, game => Assert.Contains(game, new[] { "1.20.1", "1.21.1" }));
    }

    [AvaloniaFact]
    public async Task ExplicitCatalogPreparationDoesNotResolveAnIdenticallyNamedLocalInstance()
    {
        CreateInstance("1.20.1", "1.21.1");
        var calls = new List<string>();
        var mods = CreateMods(Services(), calls);
        var before = Snapshot();
        await mods.LoadAsync();
        Assert.Equal("1.21.1", mods.SelectedGameVersion);
        calls.Clear();
        await mods.PrepareGameVersionAsync("1.20.1");
        Assert.Null(mods.SelectedVanillaInstance);
        Assert.Equal("1.20.1", mods.SelectedGameVersion);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, game => Assert.Equal("1.20.1", game));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameGameInAnotherRootDoesNotReuseOrAcceptOldMetadata(bool oldFails)
    {
        var services = Services();
        var loaders = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var forge = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var optifine = new TaskCompletionSource<IReadOnlyList<OptiFineVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new int[3];
        var mods = new ModsViewModel(services,
            (_, _) => ++calls[0] == 1 ? loaders.Task : Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("new-loader", true)]),
            (_, _) => ++calls[1] == 1 ? forge.Task : Task.FromResult<IReadOnlyList<string>>(["new-forge"]),
            game => ++calls[2] == 1 ? optifine.Task : Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine(game)]));
        var old = mods.PrepareGameVersionAsync("1.20.1");
        services.Settings.GameDirectory = Path.Combine(_root, "other-root");
        try
        {
            await mods.PrepareGameVersionAsync("1.20.1").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal([2, 2, 2], calls);
            var statuses = (mods.Status, mods.ForgelikeStatus, mods.OptiFineStatus);
            CompleteOld();
            await old;
            Assert.Equal(["new-loader"], mods.Loaders);
            Assert.Equal(["new-forge"], mods.ForgelikeVersions);
            Assert.Equal(statuses, (mods.Status, mods.ForgelikeStatus, mods.OptiFineStatus));
        }
        finally
        {
            CompleteOld();
            await old;
        }

        void CompleteOld()
        {
            if (oldFails)
            {
                loaders.TrySetException(new IOException("old metadata failure"));
                forge.TrySetException(new IOException("old metadata failure"));
                optifine.TrySetException(new IOException("old metadata failure"));
            }
            else
            {
                loaders.TrySetResult([new("old-loader", true)]);
                forge.TrySetResult(["old-forge"]);
                optifine.TrySetResult([InstallTestData.OptiFine()]);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("Fabric")]
    [InlineData("Quilt")]
    [InlineData("LegacyFabric")]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("OptiFine")]
    public async Task InstallationKeepsItsOfficialRequestAndBusyStateAcrossSelectionAndRootChanges(string kind)
    {
        CreateInstance("Custom", "1.20.1");
        CreateInstance("Other", "1.21.1");
        CreateInstance("Unknown", "", "unknown");
        var services = Services();
        var entered = new TaskCompletionSource<StandaloneLoaderInstallRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var mods = CreateMods(services, [], install: (request, _, _, ct) =>
        {
            requests++;
            entered.TrySetResult(request);
            return gate.Task.WaitAsync(ct);
        });
        await mods.LoadAsync();
        mods.SelectedVanillaInstance = "Custom";
        if (kind is "Quilt" or "LegacyFabric") mods.SelectedFabricKind = kind;
        if (kind == "NeoForge") mods.SelectedForgelikeKind = kind;
        var command = kind switch
        {
            "Forge" or "NeoForge" => mods.InstallForgelikeCommand,
            "OptiFine" => mods.InstallOptiFineCommand,
            _ => mods.InstallFabricCommand
        };
        var before = Snapshot();
        var pending = command.ExecuteAsync(null);
        try
        {
            var request = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(_root, request.GameRoot);
            Assert.Equal("1.20.1", request.Minecraft);
            Assert.Equal(kind, request.Kind);
            Assert.Equal(kind == "OptiFine" ? "I6" : kind is "Forge" or "NeoForge" ? "47.2.0" : "0.16.0", request.Version);
            var installationStatus = kind is "Forge" or "NeoForge" ? mods.ForgelikeStatus : kind == "OptiFine" ? mods.OptiFineStatus : mods.Status;
            mods.SelectedVanillaInstance = "Other";
            Assert.Equal("1.21.1", mods.SelectedGameVersion);
            var busy = kind is "Forge" or "NeoForge" ? mods.IsForgelikeBusy : kind == "OptiFine" ? mods.IsOptiFineBusy : mods.IsBusy;
            var canInstall = kind is "Forge" or "NeoForge" ? mods.CanInstallForgelike : kind == "OptiFine" ? mods.CanInstallOptiFine : mods.CanInstall;
            Assert.True(busy);
            Assert.False(canInstall);
            Assert.Equal(installationStatus, kind is "Forge" or "NeoForge" ? mods.ForgelikeStatus : kind == "OptiFine" ? mods.OptiFineStatus : mods.Status);
            mods.SelectedVanillaInstance = "Unknown";
            Assert.Null(mods.SelectedGameVersion);
            Assert.Equal(installationStatus, kind is "Forge" or "NeoForge" ? mods.ForgelikeStatus : kind == "OptiFine" ? mods.OptiFineStatus : mods.Status);
            await command.ExecuteAsync(null);
            Assert.Equal(1, requests);
            Assert.NotNull(services.GameOperations.GetActiveOperation(_root));
            services.Settings.GameDirectory = Path.Combine(_root, "other-root");
            gate.TrySetResult("Installed");
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain("Installed", mods.ModInstances);
            Assert.NotEqual("Installed", mods.SelectedModInstance);
            Assert.Null(services.GameOperations.GetActiveOperation(_root));
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        }
        finally
        {
            gate.TrySetResult("Installed");
            await pending;
        }
    }

    [AvaloniaTheory]
    [InlineData("Fabric")]
    [InlineData("Forge")]
    [InlineData("OptiFine")]
    public async Task RootChangesRejectPreviouslyEnabledInstallCommandsBeforeWriting(string kind)
    {
        CreateInstance("Custom", "1.20.1");
        var services = Services();
        var mods = CreateMods(services, []);
        await mods.LoadAsync();
        Assert.True(mods.CanInstall && mods.CanInstallForgelike && mods.CanInstallOptiFine);
        var before = Snapshot();
        var nextRoot = Path.Combine(_root, "other-root");
        services.Settings.GameDirectory = nextRoot;
        var command = kind switch
        {
            "Forge" => mods.InstallForgelikeCommand,
            "OptiFine" => mods.InstallOptiFineCommand,
            _ => mods.InstallFabricCommand
        };
        await command.ExecuteAsync(null);
        var status = kind == "Forge" ? mods.ForgelikeStatus : kind == "OptiFine" ? mods.OptiFineStatus : mods.Status;
        Assert.Contains("游戏目录已切换", status);
        Assert.False(Directory.Exists(nextRoot));
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        Assert.Null(services.GameOperations.GetActiveOperation(nextRoot));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });

    private static ModsViewModel CreateMods(AppServices services, List<string> calls,
        Func<Task<IReadOnlyList<string>>>? official = null,
        Func<StandaloneLoaderInstallRequest, IProgress<JobProgress>, IProgress<string>?, CancellationToken, Task<string>>? install = null) => new(services,
        (_, game) =>
        {
            calls.Add(game);
            return Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true)]);
        },
        (_, game) =>
        {
            calls.Add(game);
            return Task.FromResult<IReadOnlyList<string>>(["47.2.0"]);
        },
        game =>
        {
            calls.Add(game);
            return Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine(game)]);
        }, official ?? (() => Task.FromResult<IReadOnlyList<string>>([])), install);

    private void CreateInstance(string id, string minecraft, string source = "profile")
    {
        var directory = Path.Combine(_root, "versions", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new
        {
            id,
            clientVersion = source == "profile" ? minecraft : null,
            inheritsFrom = source == "inheritance" ? "Parent" : null,
            mainClass = "net.minecraft.client.main.Main",
            libraries = Array.Empty<object>()
        }));
        if (source == "manifest")
            InstallManifestStore.Save(_root, id, new InstallManifest { Components = new() { ["minecraft"] = minecraft } });
        if (source == "client")
        {
            using var archive = ZipFile.Open(Path.Combine(directory, id + ".jar"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("version.json").Open());
            writer.Write(JsonSerializer.Serialize(new { id = minecraft }));
        }
        if (source == "inheritance") CreateInstance("Parent", minecraft);
    }

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), WMCL.Core.IO.FileUtil.Sha1);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
