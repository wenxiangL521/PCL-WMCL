using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class ComponentMetadataTests
{
    [Fact]
    public async Task OptiFineChoicesShowOfficialDatesAndKeepForgeCompatibilityStates()
    {
        OptiFineVersion[] versions =
        [
            new("1.20.1", "HD_U", "I6", "OptiFine_1.20.1_HD_U_I6.jar", "47.2.18", new DateOnly(2024, 3, 12)),
            new("1.20.1", "HD_U", "I5", "OptiFine_1.20.1_HD_U_I5.jar", null),
            new("1.20.1", "HD_U", "I4", "OptiFine_1.20.1_HD_U_I4.jar", "")
        ];
        var mods = new ModsViewModel(Services(),
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>(versions));
        var choices = await mods.GetInstallComponentChoicesAsync("OptiFine", "1.20.1");
        Assert.Equal("正式版，发布于 2024/03/12，兼容 Forge 47.2.18", choices[0].Info);
        Assert.Equal("正式版，不兼容 Forge", choices[1].Info);
        Assert.Equal("正式版", choices[2].Info);
        Assert.Equal("1.20.1 I6", choices[0].Title);
        Assert.Same(versions[0], choices[0].OptiFine);
    }

    [Fact]
    public async Task ForgeChoicesShowLocalPublicationDatesAndKeepExactInstallVersions()
    {
        var published = new DateTimeOffset(2025, 7, 1, 3, 4, 0, TimeSpan.FromHours(8));
        var mods = new ModsViewModel(Services(),
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]),
            getForgeVersionDetails: game =>
            {
                Assert.Equal("1.20.1", game);
                return Task.FromResult<IReadOnlyList<ForgeVersionInfo>>([new("47.4.28", published), new("47.3.0", null)]);
            });
        var choices = await mods.GetInstallComponentChoicesAsync("Forge", "1.20.1");
        Assert.Equal(["47.4.28", "47.3.0"], choices.Select(choice => choice.Version));
        Assert.Equal($"发布于 {published.LocalDateTime:yyyy/MM/dd HH:mm}", choices[0].Info);
        Assert.Equal("47.4.28", choices[0].Title);
        Assert.Equal("", choices[1].Info);
    }

    private static AppServices Services() => new(new AppSettings
    {
        GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-component-metadata-tests-" + Guid.NewGuid().ToString("N"))
    });

    [Fact]
    public async Task ConcurrentPreparationLoadsEachListOnceAndKeepsTheChosenVersion()
    {
        var calls = new int[3];
        var loaders = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var forge = new TaskCompletionSource<IReadOnlyList<string>>();
        var optifine = new TaskCompletionSource<IReadOnlyList<OptiFineVersion>>();
        var mods = new ModsViewModel(Services(),
            (_, _) => { calls[0]++; return loaders.Task; },
            (_, _) => { calls[1]++; return forge.Task; },
            _ => { calls[2]++; return optifine.Task; });
        var first = mods.PrepareGameVersionAsync("1.20.1");
        var second = mods.PrepareGameVersionAsync("1.20.1");
        Assert.Equal([1, 1, 1], calls);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        loaders.SetResult([new("0.16.0", true), new("0.15.0", true)]);
        forge.SetResult(["47.2.0", "47.1.0"]);
        optifine.SetResult([InstallTestData.OptiFine()]);
        await Task.WhenAll(first, second);
        mods.SelectedLoader = "0.15.0";
        mods.SelectedForgelikeVersion = "47.1.0";
        await mods.PrepareGameVersionAsync("1.20.1");
        Assert.Equal("0.15.0", mods.SelectedLoader);
        Assert.Equal("47.1.0", mods.SelectedForgelikeVersion);
        Assert.Equal([1, 1, 1], calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleSuccessOrFailureCannotReplaceNewGameListsOrStatus(bool oldRequestFails)
    {
        var loaders = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var forge = new TaskCompletionSource<IReadOnlyList<string>>();
        var optifine = new TaskCompletionSource<IReadOnlyList<OptiFineVersion>>();
        var mods = new ModsViewModel(Services(),
            (_, game) => game == "old" ? loaders.Task : Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("new-loader", true)]),
            (_, game) => game == "old" ? forge.Task : Task.FromResult<IReadOnlyList<string>>(["new-forge"]),
            game => game == "old" ? optifine.Task : Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine(game)]));
        var old = mods.PrepareGameVersionAsync("old");
        await mods.PrepareGameVersionAsync("new");
        var status = (mods.Status, mods.ForgelikeStatus, mods.OptiFineStatus);
        if (oldRequestFails)
        {
            loaders.SetException(new IOException("old failure"));
            forge.SetException(new IOException("old failure"));
            optifine.SetException(new IOException("old failure"));
        }
        else
        {
            loaders.SetResult([new("old-loader", true)]);
            forge.SetResult(["old-forge"]);
            optifine.SetResult([InstallTestData.OptiFine("old")]);
        }
        await old;
        Assert.Equal(["new-loader"], mods.Loaders);
        Assert.Equal(["new-forge"], mods.ForgelikeVersions);
        Assert.Equal("new", mods.SelectedOptiFineVersion!.Minecraft);
        Assert.Equal(status, (mods.Status, mods.ForgelikeStatus, mods.OptiFineStatus));
    }

    [Fact]
    public async Task SwitchingFabricFamilyClearsOldOptionsAndIgnoresItsLateFailure()
    {
        var fabric = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var quilt = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var mods = new ModsViewModel(Services(),
            (kind, _) => kind == "Fabric" ? fabric.Task : quilt.Task,
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]));
        var old = mods.PrepareGameVersionAsync("1.20.1");
        mods.SelectedFabricKind = "Quilt";
        Assert.Empty(mods.Loaders);
        Assert.Null(mods.SelectedLoader);
        var current = mods.PrepareGameVersionAsync("1.20.1");
        quilt.SetResult([new("0.28.0", true)]);
        await current;
        fabric.SetException(new IOException("stale Fabric failure"));
        await old;
        Assert.Equal("0.28.0", mods.SelectedLoader);
        Assert.DoesNotContain("failure", mods.Status);
    }

    [Fact]
    public async Task StableDefaultOutsideInitialThirtyIsStillAnAvailableOption()
    {
        var loaders = Enumerable.Range(0, 30).Select(index => new FabricLoaderInfo($"pre-{index}", false))
            .Append(new FabricLoaderInfo("stable", true)).ToArray();
        var mods = new ModsViewModel(Services(),
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>(loaders),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]));
        await mods.PrepareGameVersionAsync("1.20.1");
        Assert.Equal("stable", mods.SelectedLoader);
        Assert.Contains(mods.SelectedLoader, mods.Loaders);
    }

    [AvaloniaFact]
    public async Task CancellingComponentLoadAbortsTheLoaderWithoutAnErrorCard()
    {
        var services = Services();
        var download = new DownloadViewModel(services);
        var mods = InstallTestData.CreateMods(services);
        var install = new InstallViewModel(download, mods, services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var card = new InstallComponentCardViewModel(install, "Forge", async (_, _, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
            return [];
        });
        var loading = card.PrepareAsync("root", "1.20.1", forceReload: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        card.Cancel();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(card.IsLoading);
        Assert.Equal("", card.Error);
    }

    [AvaloniaFact]
    public async Task ChoosingAnUninstalledCatalogVersionPreparesItsComponents()
    {
        var services = Services();
        var download = new DownloadViewModel(services);
        download.Versions.Add(new VersionItem { Id = "1.20.1" });
        var mods = InstallTestData.CreateMods(services);
        var install = new InstallViewModel(download, mods, services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        await install.OpenVersionCatalogCommand.ExecuteAsync(null);
        var entry = install.VersionCatalog.Groups.SelectMany(group => group.Entries).First();
        entry.SelectCommand.Execute(null);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (mods.SelectedLoader is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal("1.20.1", mods.SelectedGameVersion);
        Assert.Equal("0.16.0", mods.SelectedLoader);
        Assert.False(download.SelectedVersion!.IsInstalled);
    }
}
