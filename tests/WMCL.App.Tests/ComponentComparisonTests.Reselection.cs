using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.ViewModels;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ComponentComparisonTests
{
    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    public async Task ChangingMinecraftKeepsTheSameCompatibleComponentVersionInsteadOfTheDefault(string kind)
    {
        var (model, _) = await Model(kind);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal(kind, model.SelectedBaseLoader);
        Assert.Equal(Versions(kind).Old, model.ComponentVersion(kind));
        Assert.False(model.IsRechoosingComponents);
        Assert.True(model.CanApplyInstall);
        if (kind == "OptiFine") Assert.Equal("1.20.2", model.Mods.SelectedOptiFineVersion!.Minecraft);
    }

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    public async Task MissingOriginalComponentFallsBackToTheCompatibleTargetCatalog(string kind)
    {
        var catalog = new CatalogOverrides(
            Loaders: (_, game) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>(game == "1.20.1"
                ? [new("0.16.0", true), new("0.15.0", true)] : [new("0.16.0", true)]),
            Forgelike: (family, game) => Task.FromResult<IReadOnlyList<string>>(family == ForgelikeKind.Forge
                ? game == "1.20.1" ? ["47.3.0", "47.2.0"] : ["47.3.0"]
                : game == "1.20.1" ? ["20.1.2", "20.1.1"] : ["20.1.2"]),
            OptiFine: game => Task.FromResult<IReadOnlyList<OptiFineVersion>>(game == "1.20.1"
                ? [new(game, "HD_U", "I6", "new.jar", ""), new(game, "HD_U", "I5", "old.jar", "")]
                : [new(game, "HD_U", "I6", "new.jar", "")]));
        var (model, _) = await Model(kind, catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal(Versions(kind).New, model.ComponentVersion(kind));
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task SupportingComponentsMatchTheTargetFilesAfterTheirParents()
    {
        var catalog = new CatalogOverrides(
            Api: (game, family, _) => Task.FromResult<IReadOnlyList<ModrinthVersion>>([
                InstallTestData.Api(game, family, "0.92.0", "new-api"),
                InstallTestData.Api(game, family, "0.90.0", "old-api-" + game) with { VersionNumber = "0.90.0+1.20.1" }
            ]),
            OptiFabric: (game, _) => Task.FromResult<IReadOnlyList<OptiFabricFile>>([
                InstallTestData.OptiFabric(game, "1.15.0", 1000),
                InstallTestData.OptiFabric(game, "1.14.3", game == "1.20.1" ? 2000 : 3000)
            ]));
        var (model, _) = await Model("Fabric", addons: true, catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.Equal("I5", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.Equal("1.20.2", model.Mods.SelectedOptiFineVersion.Minecraft);
        Assert.Equal("old-api-1.20.2", model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal(3000, model.OptiFabricSelection.SelectedVersion!.Id);
        Assert.True(model.InstallFabricApi);
        Assert.True(model.InstallOptiFabric);
    }

    [AvaloniaFact]
    public async Task SupportingFallbackUsesNewestCompatiblePublicationRatherThanListOrder()
    {
        var catalog = new CatalogOverrides(
            Api: (game, family, _) => Task.FromResult<IReadOnlyList<ModrinthVersion>>(game == "1.20.1"
                ? [InstallTestData.Api(game, family, "0.90.0", "original")]
                : [InstallTestData.Api(game, family, "0.91.0", "older"),
                    InstallTestData.Api(game, family, "0.92.0", "latest") with { DatePublished = DateTimeOffset.UnixEpoch.AddDays(1) }]),
            OptiFabric: (game, _) => Task.FromResult<IReadOnlyList<OptiFabricFile>>(game == "1.20.1"
                ? [InstallTestData.OptiFabric(game)]
                : [InstallTestData.OptiFabric(game, "1.15.0", 1000),
                    InstallTestData.OptiFabric(game, "1.16.0", 2000) with { Published = DateTimeOffset.UnixEpoch.AddDays(1) }]));
        var (model, _) = await Model("Fabric", addons: true, catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal("latest", model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal("1.16.0", model.OptiFabricSelection.SelectedVersion!.Version);
    }

    [AvaloniaFact]
    public async Task ChangingMinecraftDoesNotInstallAnApiThatWasOriginallyAbsent()
    {
        var (model, _) = await Model("Fabric");
        var manifest = WMCL.Minecraft.InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components.Remove("fabric-api");
        WMCL.Minecraft.InstallManifestStore.Save(_root, "Custom", manifest);
        await model.OpenInstanceAsync(_root, "Custom");
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.False(model.InstallFabricApi);
        Assert.False(model.ApiCard.IsSelected);
    }

    [AvaloniaFact]
    public async Task ASlowUnrelatedForgeCatalogDoesNotDelayFabricReselection()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var catalog = new CatalogOverrides(Forgelike: (family, game) => game == "1.20.2" ? gate.Task
            : Task.FromResult<IReadOnlyList<string>>(family == ForgelikeKind.Forge ? ["47.2.0"] : ["20.1.1"]));
        var (model, _) = await Model("Fabric", catalog: catalog);
        try
        {
            ChangeMinecraft(model);
            await model.PendingComponentSelectionTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("0.15.0", model.Mods.SelectedLoader);
            Assert.False(model.IsRechoosingComponents);
            Assert.False(gate.Task.IsCompleted);
        }
        finally { gate.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task FailedParentCatalogRetainsItsSelectionForRetry()
    {
        var fail = true;
        var catalog = new CatalogOverrides(Forgelike: (family, game) =>
            game == "1.20.2" && fail ? Task.FromException<IReadOnlyList<string>>(new IOException("offline"))
                : Task.FromResult<IReadOnlyList<string>>(family == ForgelikeKind.Forge ? ["47.3.0", "47.2.0"] : ["20.1.2", "20.1.1"]));
        var (model, _) = await Model(catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.False(model.ForgeCard.IsSelected);
        Assert.NotEmpty(model.ForgeCard.Error);
        Assert.False(model.CanApplyInstall);
        fail = false;
        await model.ForgeCard.RetryCommand.ExecuteAsync(null);
        await model.PendingComponentSelectionTask;
        Assert.Equal("47.2.0", model.Mods.SelectedForgelikeVersion);
        Assert.True(model.ForgeCard.IsSelected);
        Assert.True(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task ANewerMinecraftSelectionWinsOverThePreviousPendingLoad()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var catalog = new CatalogOverrides(Loaders: (_, game) => game == "1.20.2" ? gate.Task
            : Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true), new("0.15.0", true)]));
        var (model, _) = await Model("Fabric", catalog: catalog);
        ChangeMinecraft(model);
        var previous = model.PendingComponentSelectionTask;
        Assert.True(model.IsRechoosingComponents);
        Assert.False(model.CanApplyInstall);
        ChangeMinecraft(model, "1.20.3");
        await model.PendingComponentSelectionTask;
        gate.SetResult([new("0.99.0", true)]);
        await previous;
        Assert.Equal("1.20.3", model.MinecraftVersionText);
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.False(model.IsRechoosingComponents);
    }

    [AvaloniaFact]
    public async Task ManualParentSelectionWinsOverAPendingDifferentLoader()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var catalog = new CatalogOverrides(Forgelike: (family, game) => game == "1.20.2" ? gate.Task
            : Task.FromResult<IReadOnlyList<string>>(family == ForgelikeKind.Forge ? ["47.2.0"] : ["20.1.1"]));
        var (model, _) = await Model(catalog: catalog);
        ChangeMinecraft(model);
        var pending = model.PendingComponentSelectionTask;
        await Select(model.FabricCard, "0.16.0");
        gate.SetResult(["47.3.0", "47.2.0"]);
        await pending;
        Assert.Equal("Fabric", model.SelectedBaseLoader);
        Assert.Equal("0.16.0", model.Mods.SelectedLoader);
    }

    [AvaloniaFact]
    public async Task RootChangesAndRestoringTheOriginalMinecraftInvalidatePendingReselection()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        var catalog = new CatalogOverrides(Loaders: (_, game) => game == "1.20.2" ? gate.Task
            : Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true), new("0.15.0", true)]));
        var (model, services) = await Model("Fabric", catalog: catalog);
        ChangeMinecraft(model);
        var pending = model.PendingComponentSelectionTask;
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("开始修改", model.InstallActionText);
        gate.SetResult([new("0.99.0", true)]);
        await pending;
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.Equal("1.20.1", model.MinecraftVersionText);
        gate = new TaskCompletionSource<IReadOnlyList<FabricLoaderInfo>>();
        ChangeMinecraft(model);
        var stale = model.PendingComponentSelectionTask;
        Assert.True(model.IsRechoosingComponents);
        services.SetGameDirectory(Path.Combine(_root, "next"));
        gate.SetResult([new("0.99.0", true)]);
        await stale;
        Assert.False(model.IsExistingInstance);
        Assert.False(model.IsRechoosingComponents);
        Assert.Null(model.Download.SelectedVersion);
        Assert.False(model.FabricCard.IsSelected);
    }

    [AvaloniaFact]
    public async Task ForgeAndOptiFineReselectionFiltersOutIncompatibleCandidates()
    {
        var catalog = new CatalogOverrides(OptiFine: game => Task.FromResult<IReadOnlyList<OptiFineVersion>>(game == "1.20.1"
            ? [new(game, "HD_U", "I5", "old.jar", "")]
            : [new(game, "HD_U", "I5", "incompatible.jar", "47.3.0"),
                new(game, "HD_U", "I4", "compatible.jar", "47.2.0")]));
        var (model, _) = await Model("Forge", addons: true, catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal("47.2.0", model.Mods.SelectedForgelikeVersion);
        Assert.Equal("I4", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.True(model.InstallOptiFineWithBase);
    }

    [AvaloniaFact]
    public async Task AnUnsupportedTargetRemovesTheLoaderInsteadOfKeepingItsOldVersion()
    {
        var (model, _) = await Model();
        ChangeMinecraft(model, "24w10a", "snapshot");
        await model.PendingComponentSelectionTask;
        Assert.Equal("无", model.SelectedBaseLoader);
        Assert.False(model.ForgeCard.IsSelected);
        Assert.Equal("47.2.0", model.ForgeCard.OriginalSummary);
        Assert.True(model.CanRestoreMinecraft);
    }

    [AvaloniaFact]
    public async Task FailedSupportingCatalogCanRetryWithoutForgettingTheRequestedVersion()
    {
        var fail = true;
        var catalog = new CatalogOverrides(Api: (game, family, _) => game == "1.20.2" && fail
            ? Task.FromException<IReadOnlyList<ModrinthVersion>>(new IOException("offline"))
            : Task.FromResult<IReadOnlyList<ModrinthVersion>>([
                InstallTestData.Api(game, family, "0.92.0", "latest"),
                InstallTestData.Api(game, family, "0.90.0", "original") with { VersionNumber = "0.90.0+1.20.1" }
            ]));
        var (model, _) = await Model("Fabric", catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.True(model.ApiSelection.HasError);
        Assert.False(model.ApiCard.IsSelected);
        Assert.False(model.CanApplyInstall);
        fail = false;
        await model.ApiCard.RetryCommand.ExecuteAsync(null);
        await model.PendingComponentSelectionTask;
        Assert.Equal("original", model.ApiSelection.SelectedVersion!.Id);
        Assert.True(model.ApiCard.IsSelected);
        Assert.True(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task ExplicitlyUnavailableForgeDoesNotPreventOptiFineReselection()
    {
        var catalog = new CatalogOverrides(Forgelike: (_, game) => game == "1.20.2"
            ? Task.FromException<IReadOnlyList<string>>(new InvalidDataException("无"))
            : Task.FromResult<IReadOnlyList<string>>(["47.2.0"]));
        var (model, _) = await Model("Forge", addons: true, catalog: catalog);
        ChangeMinecraft(model);
        await model.PendingComponentSelectionTask;
        Assert.Equal("OptiFine", model.SelectedBaseLoader);
        Assert.Equal("I5", model.Mods.SelectedOptiFineVersion!.Patch);
    }
}
