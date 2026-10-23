using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.App.ViewModels;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed partial class ComponentComparisonTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiChangesWithTheSameVersionNumberRemainVisibleAndRestoreTheOriginalFile(bool sameRemoteId)
    {
        var (model, _) = await Model("Fabric");
        var original = model.ApiSelection.SelectedVersion!;
        var replacement = original with
        {
            Id = sameRemoteId ? original.Id : "another-resource",
            Files = [original.Files[0] with { Sha1 = new string('c', 40), Url = "https://example.invalid/changed.jar" }]
        };
        await model.SelectComponentVersionAsync("Fabric API", new(replacement.Id, replacement.VersionNumber, "", Api: replacement));
        Assert.True(model.ApiCard.HasComparison);
        Assert.True(model.CanApplyInstall);
        await model.ApiCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(original.Id, model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal(original.Files, model.ApiSelection.SelectedVersion.Files);
        Assert.False(model.ApiCard.HasComparison);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task OptiFabricChangesWithTheSameVersionNumberRestoreTheOriginalCurseForgeId()
    {
        var (model, _) = await Model("Fabric", addons: true);
        var original = model.OptiFabricSelection.SelectedVersion!;
        var replacement = original with { Id = original.Id + 1 };
        await model.SelectComponentVersionAsync("OptiFabric", new(replacement.Id.ToString(), replacement.Version, "", OptiFabric: replacement));
        Assert.True(model.OptiFabricCard.HasComparison);
        Assert.True(model.CanApplyInstall);
        await model.OptiFabricCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(original.Id, model.OptiFabricSelection.SelectedVersion!.Id);
        Assert.Equal(original.DownloadUrl, model.OptiFabricSelection.SelectedVersion.DownloadUrl);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task OptiFineChangesWithTheSamePatchRestoreTheOriginalInstaller()
    {
        var (model, _) = await Model("OptiFine");
        var original = model.Mods.SelectedOptiFineVersion!;
        var replacement = original with { FileName = "different-installer.jar" };
        await model.SelectComponentVersionAsync("OptiFine", new(replacement.Patch, replacement.DisplayName, "", OptiFine: replacement));
        Assert.True(model.OptiFineCard.HasComparison);
        Assert.True(model.CanApplyInstall);
        await model.OptiFineCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(original, model.Mods.SelectedOptiFineVersion);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task SavedApiAndOptiFabricCanOpenAndRestoreAfterTheirCatalogEntriesDisappear()
    {
        var (model, _) = await Model("Fabric", addons: true);
        var api = model.ApiSelection.SelectedVersion!;
        var optifabric = model.OptiFabricSelection.SelectedVersion!;
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.ComponentMetadata["fabric-api"] = JsonSerializer.SerializeToElement(api);
        manifest.ComponentMetadata["optifabric"] = JsonSerializer.SerializeToElement(optifabric);
        InstallManifestStore.Save(_root, "Custom", manifest);
        model.ApiSelection.Versions.Clear();
        model.OptiFabricSelection.Versions.Clear();
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Equal(api.Id, model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal(optifabric.Id, model.OptiFabricSelection.SelectedVersion!.Id);
        model.ApiCard.ClearCommand.Execute(null);
        model.OptiFabricCard.ClearCommand.Execute(null);
        Assert.True(model.ApiCard.CanRestore);
        Assert.True(model.OptiFabricCard.CanRestore);
        await model.ApiCard.RestoreCommand.ExecuteAsync(null);
        await model.OptiFabricCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(api.Files, model.ApiSelection.SelectedVersion!.Files);
        Assert.Equal(optifabric.DownloadUrl, model.OptiFabricSelection.SelectedVersion!.DownloadUrl);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task LegacyApiResourceIdIsNormalizedWithoutCreatingAnUnrequestedModification()
    {
        var (model, _) = await Model("Fabric");
        var api = model.ApiSelection.SelectedVersion!;
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components["fabric-api"] = api.Id;
        InstallManifestStore.Save(_root, "Custom", manifest);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.False(model.ApiCard.HasComparison);
        Assert.False(model.CanApplyInstall);
        Assert.Equal(api.VersionNumber.Split('+')[0], model.ApiCard.OriginalSummary);
    }

    [AvaloniaFact]
    public async Task MissingLegacyOptiFineStopsOpeningInsteadOfKeepingTheLatestPatch()
    {
        var missing = false;
        var (model, _) = await Model("OptiFine", catalog: new(OptiFine: game =>
            Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>(missing ? [] :
                [new(game, "HD_U", "I5", "OptiFine-old.jar", "")])));
        missing = true;
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains("无法确认已安装的 OptiFine I5", model.CombinedStatus);
    }

    [AvaloniaFact]
    public async Task SelectingTheOriginalOptiFabricCatalogRowRetainsTheHistoricalHash()
    {
        var (model, _) = await Model("Fabric", addons: true);
        var original = model.OptiFabricSelection.SelectedVersion!;
        Assert.NotNull(original.Sha1);
        var catalog = model.OptiFabricSelection.Versions.Single(file => file.Id == original.Id);
        Assert.Null(catalog.Sha1);
        await model.SelectComponentVersionAsync("OptiFabric", new(catalog.Id.ToString(), catalog.Version, "", OptiFabric: catalog));
        Assert.Equal(original.Sha1, model.OptiFabricSelection.SelectedVersion!.Sha1);
        Assert.False(model.OptiFabricCard.HasComparison);
        Assert.False(model.CanApplyInstall);
    }
}
