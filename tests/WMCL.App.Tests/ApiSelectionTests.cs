using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

public sealed class ApiSelectionTests
{
    [Fact]
    public async Task SameGameAndFamilyShareOneRequestAndPreserveTheChosenVersion()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ModrinthVersion>>();
        var calls = 0;
        var selection = new ApiSelectionViewModel((_, _, _) => { calls++; return pending.Task; });
        var first = selection.PrepareAsync("1.20.1", "Fabric");
        Assert.Same(first, selection.PrepareAsync("1.20.1", "Fabric"));
        Assert.True(selection.IsBusy);
        var latest = InstallTestData.Api();
        var older = InstallTestData.Api(version: "0.91.0", id: "older");
        pending.SetResult([latest, older]);
        await first;
        Assert.Same(latest, selection.SelectedVersion);
        selection.SelectedVersion = older;
        await selection.PrepareAsync("1.20.1", "Fabric");
        Assert.Same(older, selection.SelectedVersion);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("1.20.4", "Fabric", false)]
    [InlineData("1.20.1", "Quilt", true)]
    public async Task SwitchingGameOrFamilyCancelsOldRequestsAndIgnoresLateSuccessAndFailure(string minecraft, string kind, bool failOld)
    {
        var old = new TaskCompletionSource<IReadOnlyList<ModrinthVersion>>();
        var current = new TaskCompletionSource<IReadOnlyList<ModrinthVersion>>();
        CancellationToken oldToken = default;
        var selection = new ApiSelectionViewModel((game, family, ct) =>
        {
            if (game == "1.20.1" && family == "Fabric") { oldToken = ct; return old.Task; }
            return current.Task;
        });
        var oldLoading = selection.PrepareAsync("1.20.1", "Fabric");
        selection.Versions.Add(InstallTestData.Api());
        selection.SelectedVersion = selection.Versions[0];
        var loading = selection.PrepareAsync(minecraft, kind);
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Empty(selection.Versions);
        Assert.Null(selection.SelectedVersion);
        current.SetResult([InstallTestData.Api(minecraft, kind)]);
        await loading;
        var status = selection.Status;
        if (failOld) old.SetException(new IOException("stale failure"));
        else old.SetResult([InstallTestData.Api()]);
        await oldLoading;
        Assert.Equal(status, selection.Status);
        Assert.Contains(minecraft, selection.SelectedVersion!.GameVersions);
        Assert.False(selection.HasError);
    }

    [Fact]
    public async Task LeavingFabricFamilyClearsAndCancelsThePendingSelection()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ModrinthVersion>>();
        CancellationToken token = default;
        var selection = new ApiSelectionViewModel((_, _, ct) => { token = ct; return pending.Task; });
        var loading = selection.PrepareAsync("1.20.1", "Fabric");
        selection.Clear();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult([InstallTestData.Api()]);
        await loading;
        Assert.Empty(selection.Versions);
        Assert.Null(selection.SelectedVersion);
        Assert.False(selection.IsBusy);
    }

    [Fact]
    public async Task FailureAndEmptyStatesCanBeRetried()
    {
        var calls = 0;
        var selection = new ApiSelectionViewModel((game, kind, _) => ++calls switch
        {
            1 => Task.FromException<IReadOnlyList<ModrinthVersion>>(new IOException("offline")),
            2 => Task.FromResult<IReadOnlyList<ModrinthVersion>>([]),
            _ => Task.FromResult<IReadOnlyList<ModrinthVersion>>([InstallTestData.Api(game, kind)])
        });
        await selection.PrepareAsync("1.20.1", "Quilt");
        Assert.True(selection.HasError);
        Assert.Contains("offline", selection.Status);
        await selection.RetryCommand.ExecuteAsync(null);
        Assert.False(selection.HasError);
        Assert.Null(selection.SelectedVersion);
        Assert.Contains("没有支持", selection.Status);
        await selection.RetryCommand.ExecuteAsync(null);
        Assert.NotNull(selection.SelectedVersion);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void PlanRejectsUnloadedWrongGameWrongProjectAndWrongFamilyApiSelections()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-api-plan-" + Guid.NewGuid().ToString("N"));
        var version = InstallTestData.Api();
        CombinedInstallPlan Plan(ModrinthVersion? api) => CombinedInstallPlan.Create(root, "1.20.1", false,
            "Fabric", "0.16.10", null, true, false, "", apiVersion: api);
        Assert.Throws<InvalidOperationException>(() => Plan(null));
        Assert.Throws<InvalidOperationException>(() => Plan(InstallTestData.Api("1.20.4")));
        Assert.Throws<InvalidOperationException>(() => Plan(version with { ProjectId = "other" }));
        Assert.Throws<InvalidOperationException>(() => Plan(version with { Loaders = ["quilt"] }));
    }

    [Fact]
    public void PlanSnapshotsTheSelectedVersionAndItsMutableCollections()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-api-plan-" + Guid.NewGuid().ToString("N"));
        var files = InstallTestData.Api().Files.ToList();
        var games = new List<string> { "1.20.1" };
        var dependencies = new List<ModrinthDependency> { new("required", "required-project", "required") };
        var chosen = InstallTestData.Api() with { Files = files, GameVersions = games, Dependencies = dependencies };
        var plan = CombinedInstallPlan.Create(root, "1.20.1", false,
            "Fabric", "0.16.10", null, true, false, "", apiVersion: chosen);
        files.Clear();
        games[0] = "1.20.4";
        dependencies.Clear();
        Assert.Equal(chosen.Id, plan.ApiVersion!.Id);
        Assert.Single(plan.ApiVersion.Files);
        Assert.Equal(["1.20.1"], plan.ApiVersion.GameVersions);
        Assert.Single(plan.ApiVersion.Dependencies);
    }

    [AvaloniaFact]
    public async Task AutomaticSelectionAndExplicitClearRemainIndependentOfMetadataRefresh()
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-api-vm-" + Guid.NewGuid().ToString("N"))
        });
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = "1.20.1" } };
        var selection = InstallTestData.CreateApiSelection();
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null,
            InstallTestData.CreateOptiFabricSelection(), selection);
        install.ShowSelectedVersion();
        install.SelectedBaseLoader = "Fabric";
        await selection.PrepareAsync("1.20.1", "Fabric");
        Assert.True(install.InstallFabricApi);
        Assert.NotNull(selection.SelectedVersion);
        var chosen = selection.SelectedVersion;
        install.ClearApiCommand.Execute(null);
        await selection.PrepareAsync("1.20.1", "Fabric");
        Assert.False(install.InstallFabricApi);
        Assert.Same(chosen, selection.SelectedVersion);
        Assert.Contains("部分 Mod 可能无法运行", install.InstallHint);
        install.SelectedBaseLoader = "Quilt";
        await selection.PrepareAsync("1.20.1", "Quilt");
        Assert.False(install.InstallFabricApi);
        Assert.Contains("Quilted", install.ApiName);
        install.UseVanilla();
        Assert.Null(selection.SelectedVersion);
    }
}
