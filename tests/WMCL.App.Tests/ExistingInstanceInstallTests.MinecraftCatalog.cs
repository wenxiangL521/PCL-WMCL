using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [AvaloniaFact]
    public async Task InitializationRefreshesMinecraftAndUsesItsOfficialMetadataWithoutChangingTheInstance()
    {
        CreateInstance();
        var original = Snapshot();
        var services = Services();
        var requests = new List<bool>();
        var download = new DownloadViewModel(services, forceRefresh =>
        {
            requests.Add(forceRefresh);
            return Task.FromResult<IReadOnlyList<VersionItem>>(forceRefresh
                ? [new() { Id = "1.20.1", Type = "snapshot", ReleaseTime = "2023-06-12T13:25:51Z" }]
                : [new() { Id = "1.21.1", IsLatest = true }]);
        });
        await download.LoadAsync();
        var model = CatalogModel(services, _ => false, download: download);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal([false, true], requests);
        Assert.Equal("Custom", model.TargetInstanceId);
        Assert.Equal("1.20.1", download.SelectedVersion!.Id);
        Assert.Equal("snapshot", download.SelectedVersion.Type);
        Assert.Equal("2023-06-12T13:25:51Z", download.SelectedVersion.ReleaseTime);
        Assert.False(download.SelectedVersion.IsInstalled);
        Assert.False(model.CanApplyInstall);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaTheory]
    [InlineData("failure")]
    [InlineData("empty")]
    [InlineData("missing")]
    public async Task AStaleSuccessfulMinecraftCatalogCannotBypassRefreshFailureOrMissingBase(string result)
    {
        CreateInstance();
        var original = Snapshot();
        var services = Services();
        var broken = true;
        var requests = new List<bool>();
        var download = new DownloadViewModel(services, forceRefresh =>
        {
            requests.Add(forceRefresh);
            if (!forceRefresh || !broken) return InstallTestData.LoadMinecraftVersions();
            if (result == "failure") throw new IOException("Minecraft refresh unavailable");
            return Task.FromResult<IReadOnlyList<VersionItem>>(result == "empty" ? [] : [new() { Id = "1.21.1" }]);
        });
        await download.LoadAsync();
        var model = CatalogModel(services, _ => false, download: download);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.HasInstanceInitializationError);
        Assert.Contains("Minecraft", model.InstanceInitializationError);
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.ShowInstallAction);
        Assert.Equal(0, model.InstanceInitializationProgress);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        broken = false;
        await model.RetryInstanceInitializationCommand.ExecuteAsync(null);
        Assert.Equal([false, true, true], requests);
        Assert.True(model.IsExistingInstance);
        Assert.False(model.HasInstanceInitializationError);
        Assert.Equal(1, model.InstanceInitializationProgress);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaTheory]
    [InlineData("complete")]
    [InlineData("root")]
    [InlineData("detach")]
    public async Task PendingMinecraftIsIncludedInProgressAndCannotPublishAfterTheTargetIsAbandoned(string action)
    {
        CreateInstance();
        var services = Services();
        var gate = new TaskCompletionSource<IReadOnlyList<VersionItem>>();
        var download = new DownloadViewModel(services, () => gate.Task);
        var model = CatalogModel(services, _ => false, download: download);
        var opening = model.OpenInstanceAsync(_root, "Custom");
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (model.InstanceInitializationProgress < 0.6 && DateTime.UtcNow < until) await Task.Delay(10);
            Assert.False(opening.IsCompleted);
            Assert.True(model.IsPreparingInstance);
            Assert.InRange(model.InstanceInitializationProgress, 0.6, 0.69);
            Assert.False(model.CanApplyInstall);
            if (action == "root") services.SetGameDirectory(Path.Combine(_root, "other"));
            else if (action == "detach") window.Content = null;
            gate.SetResult([new() { Id = "1.20.1", ReleaseTime = "2023-06-12T13:25:51Z" }]);
            await opening.WaitAsync(TimeSpan.FromSeconds(2));
            if (action == "complete")
            {
                Assert.True(model.IsExistingInstance);
                Assert.Equal(1, model.InstanceInitializationProgress);
                Assert.Single(download.Versions);
            }
            else
            {
                Assert.False(model.IsExistingInstance);
                Assert.Equal(0, model.InstanceInitializationProgress);
                Assert.Null(download.SelectedVersion);
                Assert.Empty(download.Versions);
                Assert.True(model.IsChoosingVersion);
            }
        }
        finally
        {
            gate.TrySetResult([]);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AComponentFailureDoesNotWaitForOrAcceptALateMinecraftCatalog()
    {
        CreateInstance();
        var services = Services();
        var gate = new TaskCompletionSource<IReadOnlyList<VersionItem>>();
        var download = new DownloadViewModel(services, () => gate.Task);
        var model = CatalogModel(services, kind => kind == "NeoForge", download: download);
        try
        {
            await model.OpenInstanceAsync(_root, "Custom").WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(model.HasInstanceInitializationError);
            Assert.Contains("NeoForge", model.InstanceInitializationError);
            Assert.False(gate.Task.IsCompleted);
            gate.SetResult([new() { Id = "1.20.1" }]);
            await Task.Delay(30);
            Assert.False(model.IsExistingInstance);
            Assert.Empty(download.Versions);
            Assert.Null(download.SelectedVersion);
            Assert.Equal(0, model.InstanceInitializationProgress);
        }
        finally { gate.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task InitializationFindsTheBaseInTheWholeCatalogRegardlessOfTheVisibleFilter()
    {
        CreateInstance();
        var services = Services();
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedFilter = "快照" };
        var model = CatalogModel(services, _ => false, download: download);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal("1.20.1", download.SelectedVersion!.Id);
        Assert.Empty(download.Versions);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaTheory]
    [InlineData("20w14infinite", "20w14∞")]
    [InlineData("20w14∞", "20w14infinite")]
    public async Task InfinityAliasesKeepTheInstalledMinecraftIdentity(string installed, string official)
    {
        CreateInstance();
        var profile = Path.Combine(DirectoryFor(), "Custom.json");
        File.WriteAllText(profile, $$"""{"id":"Custom","clientVersion":"{{installed}}","libraries":[]}""");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components["minecraft"] = installed;
        InstallManifestStore.Save(_root, "Custom", manifest);
        var services = Services();
        var download = new DownloadViewModel(services, () => Task.FromResult<IReadOnlyList<VersionItem>>([
            new() { Id = official, Type = "snapshot", ReleaseTime = "2020-04-01T12:00:00Z" }
        ]));
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]));
        var model = new InstallViewModel(download, mods, services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance, model.InstanceInitializationError);
        Assert.Equal(installed, download.SelectedVersion!.Id);
        Assert.Equal("snapshot", download.SelectedVersion.Type);
        Assert.Equal("Custom", model.TargetInstanceId);
        Assert.False(model.CanApplyInstall);
    }
}
