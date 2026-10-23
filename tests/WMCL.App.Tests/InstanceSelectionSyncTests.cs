using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;

namespace WMCL.App.Tests;

public sealed class InstanceSelectionSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-selection-sync-" + Guid.NewGuid().ToString("N"));

    public InstanceSelectionSyncTests()
    {
        foreach (var id in new[] { "AFirst", "Source", "Other" })
        {
            var directory = Path.Combine(_root, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"),
                $$"""{"id":"{{id}}","clientVersion":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
            InstanceSettingsStore.Save(directory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });
        }
    }

    [AvaloniaTheory]
    [InlineData("rename", false)]
    [InlineData("rename", true)]
    [InlineData("clone", false)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task ManagementEventRefreshesPagesAndPreservesTheirOwnSelection(string action, bool unrelatedSelection)
    {
        var services = Services();
        services.GetFavoriteVersions(_root).Add("Source");
        services.GetHiddenVersions(_root).Add("Source");
        var otherFolder = Path.Combine(_root, "different-folder");
        services.GetFavoriteVersions(otherFolder).Add("Source");
        using var main = new MainWindowViewModel(services, loadVersions: LoadLocalAsync,
            instancePrompt: (_, _, _) => Task.FromResult<string?>("Target"), instanceConfirm: (_, _) => Task.FromResult(true));
        await main.Launch.LoadAsync();
        await main.Mods.LoadAsync();
        await main.Content.LoadAsync();
        var selected = unrelatedSelection ? "Other" : "Source";
        main.Launch.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == selected);
        main.VersionSelect.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == selected);
        main.Mods.SelectedModInstance = selected;
        main.Content.SelectedInstance = selected;
        main.Instance.SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true };
        var command = action switch
        {
            "clone" => main.Instance.CloneInstanceCommand,
            "delete" => main.Instance.DeleteInstanceCommand,
            _ => main.Instance.RenameInstanceCommand
        };
        await command.ExecuteAsync(null);
        await main.InstanceRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = unrelatedSelection ? "Other" : action == "rename" ? "Target" : action == "clone" ? "Source" : null;
        if (expected is not null)
        {
            Assert.Equal(expected, main.Launch.SelectedVersion!.Id);
            Assert.Equal(expected, main.VersionSelect.SelectedVersion!.Id);
            Assert.Equal(expected, main.Mods.SelectedModInstance);
            Assert.Equal(expected, main.Content.SelectedInstance);
        }
        else
        {
            Assert.DoesNotContain(main.Launch.InstalledVersions, version => version.Id == "Source");
            Assert.NotEqual("Source", main.Launch.SelectedVersion?.Id);
            Assert.NotEqual("Source", main.VersionSelect.SelectedVersion?.Id);
            Assert.NotEqual("Source", main.Mods.SelectedModInstance);
            Assert.NotEqual("Source", main.Content.SelectedInstance);
        }
        if (action == "rename")
        {
            Assert.Contains("Target", services.GetFavoriteVersions(_root));
            Assert.Contains("Target", services.GetHiddenVersions(_root));
            var target = main.Launch.InstalledVersions.Single(version => version.Id == "Target");
            Assert.True(target.IsFavorite);
            Assert.True(target.IsHidden);
        }
        if (action != "clone")
        {
            Assert.DoesNotContain("Source", services.GetFavoriteVersions(_root));
            Assert.DoesNotContain("Source", services.GetHiddenVersions(_root));
        }
        Assert.Equal(["Source"], services.GetFavoriteVersions(otherFolder));
        Assert.Contains("AFirst", main.Mods.ModInstances);
        Assert.Contains("AFirst", main.Content.Instances);
    }

    [AvaloniaFact]
    public async Task OrdinaryRefreshRebindsSelectionsByIdentityInsteadOfChoosingTheFirstRow()
    {
        using var main = new MainWindowViewModel(Services(), loadVersions: LoadLocalAsync);
        await main.Launch.LoadAsync();
        await main.Mods.LoadAsync();
        main.Launch.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == "Source");
        main.VersionSelect.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == "Other");
        main.Mods.SelectedModInstance = "Other";
        var previous = main.Launch.SelectedVersion;
        await main.Launch.LoadAsync();
        await main.Mods.LoadAsync();
        Assert.Equal("Source", main.Launch.SelectedVersion!.Id);
        Assert.NotSame(previous, main.Launch.SelectedVersion);
        Assert.Contains(main.Launch.SelectedVersion, main.Launch.InstalledVersions);
        Assert.Equal("Other", main.VersionSelect.SelectedVersion!.Id);
        Assert.Contains(main.VersionSelect.SelectedVersion, main.VersionSelect.Versions);
        Assert.Equal("Other", main.Mods.SelectedModInstance);
    }

    [AvaloniaFact]
    public async Task FolderChangeDiscardsTheOldCatalogAndRunsTheQueuedRefresh()
    {
        var services = Services();
        var oldResult = new TaskCompletionSource<IReadOnlyList<VersionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRoot = Path.Combine(_root, "next");
        var calls = new List<string>();
        var launch = new LaunchViewModel(services, loadVersions: root =>
        {
            calls.Add(root);
            return root == _root ? oldResult.Task : Task.FromResult<IReadOnlyList<VersionItem>>([new() { Id = "Next", IsInstalled = true }]);
        });
        var old = launch.LoadAsync();
        services.SetGameDirectory(nextRoot);
        var next = launch.LoadAsync("Next");
        oldResult.SetResult([new VersionItem { Id = "Old", IsInstalled = true }]);
        await Task.WhenAll(old, next).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([_root, nextRoot], calls);
        Assert.Equal("Next", launch.SelectedVersion!.Id);
        Assert.DoesNotContain(launch.Versions, version => version.Id == "Old");
        Assert.False(launch.IsBusy);
    }

    [AvaloniaTheory]
    [InlineData("mods")]
    [InlineData("content")]
    [InlineData("instance")]
    public async Task BoundInstancePickerRetainsItsSelectionAfterCollectionRebuild(string page)
    {
        using var main = new MainWindowViewModel(Services(), loadVersions: LoadLocalAsync);
        await main.Mods.LoadAsync();
        await main.Content.LoadAsync();
        await main.Instance.LoadAsync();
        main.Mods.SelectedModInstance = "Other";
        main.Content.SelectedInstance = "Other";
        main.Instance.SelectedInstance = main.Instance.Instances.Single(version => version.Id == "Other");
        main.Mods.SetMode("mods");
        UserControl view = page switch
        {
            "mods" => new ModsView { DataContext = main.Mods },
            "content" => new InstanceContentView { DataContext = main.Content },
            _ => new InstanceView { DataContext = main.Instance }
        };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            var picker = view.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                ReferenceEquals(control.ItemsSource, page switch
                {
                    "mods" => (object)main.Mods.ModInstances,
                    "content" => main.Content.Instances,
                    _ => main.Instance.Instances
                }));
            Assert.Equal("Other", (picker.SelectedItem as VersionItem)?.Id ?? picker.SelectedItem);
            await (page switch
            {
                "mods" => main.Mods.LoadAsync(),
                "content" => main.Content.LoadAsync(),
                _ => main.Instance.LoadAsync()
            });
            Assert.Equal("Other", (picker.SelectedItem as VersionItem)?.Id ?? picker.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BrowsingDuringManagementRefreshKeepsTheNewUserSelection()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<VersionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var main = new MainWindowViewModel(Services(), loadVersions: root =>
                ++calls == 1 ? LoadLocalAsync(root) : pending.Task,
            instancePrompt: (_, _, _) => Task.FromResult<string?>("Target"));
        await main.Launch.LoadAsync();
        main.Launch.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == "Source");
        main.VersionSelect.SelectedVersion = main.Launch.SelectedVersion;
        main.Instance.SelectedInstance = main.Launch.SelectedVersion;
        try
        {
            await main.Instance.RenameInstanceCommand.ExecuteAsync(null);
            Assert.True(main.Launch.IsBusy);
            main.VersionSelect.SelectedVersion = main.VersionSelect.Versions.Single(version => version.Id == "Other");
            pending.SetResult(await LoadLocalAsync(_root));
            await main.InstanceRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Target", main.Launch.SelectedVersion!.Id);
            Assert.Equal("Other", main.VersionSelect.SelectedVersion!.Id);
            Assert.Contains(main.VersionSelect.SelectedVersion, main.VersionSelect.Versions);
        }
        finally
        {
            pending.TrySetResult(await LoadLocalAsync(_root));
            await main.InstanceRefreshTask;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowMetadataDoesNotReplaceANewerScanOrUserTarget(bool scanAgain)
    {
        File.WriteAllText(Path.Combine(_root, "versions/AFirst/AFirst.json"),
            """{"id":"AFirst","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var mods = new ModsViewModel(Services(), (_, _) =>
        {
            if (++calls != 1) return Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]);
            entered.SetResult();
            return pending.Task;
        }, (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>([]));
        var first = mods.LoadAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (scanAgain)
            {
                Directory.Delete(Path.Combine(_root, "versions/Other"), true);
                await mods.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else mods.SelectedModInstance = "Other";
            pending.SetResult([]);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (scanAgain) Assert.DoesNotContain("Other", mods.ModInstances);
            else Assert.Equal("Other", mods.SelectedModInstance);
        }
        finally
        {
            pending.TrySetResult([]);
            await first;
        }
    }

    [AvaloniaFact]
    public async Task QueuedRenameRefreshIsNotDroppedWhileAnotherCatalogLoadIsRunning()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<VersionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var launch = new LaunchViewModel(Services(), loadVersions: _ => ++calls == 1 ? pending.Task :
            Task.FromResult<IReadOnlyList<VersionItem>>([new() { Id = "AFirst", IsInstalled = true }, new() { Id = "Target", IsInstalled = true }]));
        var first = launch.LoadAsync();
        var renamed = launch.LoadAsync("Target");
        pending.SetResult([new() { Id = "Source", IsInstalled = true }]);
        await Task.WhenAll(first, renamed).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal("Target", launch.SelectedVersion!.Id);
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });

    private static Task<IReadOnlyList<VersionItem>> LoadLocalAsync(string root) =>
        Task.FromResult<IReadOnlyList<VersionItem>>(new VersionRepository(root).ListInstalled()
            .Select(version => new VersionItem { Id = version.Id, IsInstalled = true }).ToArray());

    public void Dispose() => Directory.Delete(_root, true);
}
