using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class OptiFabricSelectionTests
{
    [Fact]
    public async Task SharedPreparationWaitsForOneRequestAndKeepsTheChosenFile()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        var calls = 0;
        var selection = new OptiFabricSelectionViewModel((_, _) => { calls++; return pending.Task; });
        var first = selection.PrepareAsync("1.20.1");
        var second = selection.PrepareAsync("1.20.1");
        Assert.Same(first, second);
        Assert.True(selection.IsBusy);
        var latest = InstallTestData.OptiFabric();
        var older = InstallTestData.OptiFabric(version: "1.13.25", id: 4642832);
        pending.SetResult([latest, older]);
        await first;
        Assert.Same(latest, selection.SelectedVersion);
        selection.SelectedVersion = older;
        await selection.PrepareAsync("1.20.1");
        Assert.Same(older, selection.SelectedVersion);
        Assert.Equal(1, calls);
        Assert.False(selection.IsBusy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchingGameClearsOldFilesAndIgnoresLateResults(bool oldFails)
    {
        var old = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        var current = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        CancellationToken oldToken = default;
        var selection = new OptiFabricSelectionViewModel((game, ct) =>
        {
            if (game == "1.20.1") { oldToken = ct; return old.Task; }
            return current.Task;
        });
        var oldLoading = selection.PrepareAsync("1.20.1");
        selection.Versions.Add(InstallTestData.OptiFabric());
        selection.SelectedVersion = selection.Versions[0];
        var currentLoading = selection.PrepareAsync("1.20.4");
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Empty(selection.Versions);
        Assert.Null(selection.SelectedVersion);
        current.SetResult([InstallTestData.OptiFabric("1.20.4")]);
        await currentLoading;
        var status = selection.Status;
        if (oldFails) old.SetException(new IOException("old failure"));
        else old.SetResult([InstallTestData.OptiFabric()]);
        await oldLoading;
        Assert.Contains("1.20.4", selection.SelectedVersion!.GameVersions);
        Assert.Equal(status, selection.Status);
        Assert.False(selection.HasError);
    }

    [Fact]
    public async Task FailedAndEmptyListsCanRetryAndCompletedRequestsCanSwitchGame()
    {
        var calls = 0;
        var selection = new OptiFabricSelectionViewModel((game, _) => ++calls switch
        {
            1 => Task.FromException<IReadOnlyList<OptiFabricFile>>(new IOException("offline")),
            2 => Task.FromResult<IReadOnlyList<OptiFabricFile>>([]),
            _ => Task.FromResult<IReadOnlyList<OptiFabricFile>>([InstallTestData.OptiFabric(game)])
        });
        await selection.PrepareAsync("1.20.1");
        Assert.True(selection.HasError);
        Assert.Contains("offline", selection.Status);
        await selection.RetryCommand.ExecuteAsync(null);
        Assert.False(selection.HasError);
        Assert.Null(selection.SelectedVersion);
        Assert.Contains("没有支持", selection.Status);
        await selection.PrepareAsync("1.20.4");
        Assert.NotNull(selection.SelectedVersion);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task CombinedPlanCapturesExactOptiFabricFileAndRejectsUnloadedOrWrongGameSelections()
    {
        var services = Services();
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var files = new[] { InstallTestData.OptiFabric(), InstallTestData.OptiFabric(version: "1.13.25", id: 4642832) };
        var selection = new OptiFabricSelectionViewModel((_, _) => Task.FromResult<IReadOnlyList<OptiFabricFile>>(files));
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, selection, InstallTestData.CreateApiSelection());
        install.ShowSelectedVersion();
        await selection.PrepareAsync("1.20.1");
        selection.SelectedVersion = files[1];
        var plan = CombinedInstallPlan.Create(services.Settings.GameDirectory, "1.20.1", false,
            "Fabric", "0.16.0", InstallTestData.OptiFine(), false, true, "", selection.SelectedVersion);
        selection.SelectedVersion = files[0];
        Assert.Same(files[1], plan.OptiFabric);
        Assert.Throws<InvalidOperationException>(() => CombinedInstallPlan.Create(services.Settings.GameDirectory, "1.20.1", false,
            "Fabric", "0.16.0", InstallTestData.OptiFine(), false, true, ""));
        Assert.Throws<InvalidOperationException>(() => CombinedInstallPlan.Create(services.Settings.GameDirectory, "1.20.1", false,
            "Fabric", "0.16.0", InstallTestData.OptiFine(), false, true, "", InstallTestData.OptiFabric("1.20.4")));
    }

    [AvaloniaFact]
    public void PclHintsFollowSelectionsWithoutForcingOptionalApiOrBridge()
    {
        var services = Services();
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        install.ShowSelectedVersion();
        install.SelectedBaseLoader = "Fabric";
        install.InstallOptiFineWithBase = true;
        install.InstallOptiFabric = false;
        install.InstallFabricApi = false;
        Assert.True(install.ShowOptiFabricOptions);
        Assert.Contains("部分 Mod 可能无法运行", install.InstallHint);
        Assert.Contains("OptiFine 无法工作", install.InstallHint);
        install.InstallFabricApi = true;
        install.InstallOptiFabric = true;
        Assert.DoesNotContain("无法工作", install.InstallHint);
        Assert.DoesNotContain("未选择", install.InstallHint);
        Assert.Contains("OptiFine 与一部分 Mod 的兼容性不佳", install.InstallHint);
        install.SelectedBaseLoader = "NeoForge";
        Assert.False(install.InstallOptiFineWithBase);
        Assert.False(install.ShowOptiFabricOptions);
        Assert.False(install.HasInstallHint);
    }

    [AvaloniaFact]
    public void OldFabricVersionsExplainOriginsAndDoNotAutoSelectTheRegularBridge()
    {
        var services = Services();
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.14.4" } };
        var selection = new OptiFabricSelectionViewModel((_, _) => Task.FromResult<IReadOnlyList<OptiFabricFile>>([]));
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, selection, InstallTestData.CreateApiSelection());
        install.ShowSelectedVersion();
        install.SelectedBaseLoader = "Fabric";
        install.InstallOptiFineWithBase = true;
        Assert.False(install.InstallOptiFabric);
        Assert.Contains("OptiFabric Origins", install.InstallHint);
        Assert.Contains("OptiFabric Origins", selection.Status);
    }

    private static AppServices Services() => new(new AppSettings
    {
        GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-optifabric-selection-tests-" + Guid.NewGuid().ToString("N"))
    });
}
