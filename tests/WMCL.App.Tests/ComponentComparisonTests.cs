using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ComponentComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-compare-" + Guid.NewGuid().ToString("N"));

    private static (string Old, string New) Versions(string kind) => kind switch
    {
        "Forge" => ("47.2.0", "47.3.0"),
        "NeoForge" => ("20.1.1", "20.1.2"),
        "OptiFine" => ("I5", "I6"),
        _ => ("0.15.0", "0.16.0")
    };

    private sealed record CatalogOverrides(
        Func<string, string, Task<IReadOnlyList<FabricLoaderInfo>>>? Loaders = null,
        Func<ForgelikeKind, string, Task<IReadOnlyList<string>>>? Forgelike = null,
        Func<string, Task<IReadOnlyList<OptiFineVersion>>>? OptiFine = null,
        Func<string, string, CancellationToken, Task<IReadOnlyList<WMCL.Modding.Modrinth.ModrinthVersion>>>? Api = null,
        Func<string, CancellationToken, Task<IReadOnlyList<OptiFabricFile>>>? OptiFabric = null);

    private async Task<(InstallViewModel Model, AppServices Services)> Model(string kind = "Forge", bool missingApi = false, bool addons = false,
        Func<string, Task>? prepareOptiFine = null, CatalogOverrides? catalog = null,
        Func<string, string, Task>? showInstanceError = null)
    {
        var folder = Path.Combine(_root, "versions/Custom");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Custom.json"),
            """{"id":"Custom","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        File.WriteAllText(Path.Combine(folder, "Custom.jar"), "client");
        var components = new Dictionary<string, string> { ["minecraft"] = "1.20.1" };
        if (kind != "无") components[kind.ToLowerInvariant()] = Versions(kind).Old;
        if (kind == "Fabric") components["fabric-api"] = missingApi ? "missing-api+1.20.1" : "0.90.0+1.20.1";
        if (addons)
        {
            components["optifine"] = "I5";
            components["optifabric"] = "1.14.3";
        }
        var manifest = new InstallManifest { Components = components };
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var mods = new ModsViewModel(services,
            catalog?.Loaders ?? ((_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true), new("0.15.0", true)])),
            catalog?.Forgelike ?? ((family, _) => Task.FromResult<IReadOnlyList<string>>(family == ForgelikeKind.Forge
                ? ["47.3.0", "47.2.0"] : ["20.1.2", "20.1.1"])),
            catalog?.OptiFine ?? (async game =>
            {
                if (prepareOptiFine is not null) await prepareOptiFine(game);
                return [new(game, "HD_U", "I6", "OptiFine-new.jar", "47.3.0"),
                    new(game, "HD_U", "I5", "OptiFine-old.jar", "")];
            }));
        var api = new ApiSelectionViewModel(catalog?.Api ?? ((game, family, _) => Task.FromResult<IReadOnlyList<WMCL.Modding.Modrinth.ModrinthVersion>>([
            InstallTestData.Api(game, family, "0.92.0", "new-api"),
            InstallTestData.Api(game, family, "0.90.0", "old-api")
        ])));
        var optifabric = catalog?.OptiFabric is { } files ? new OptiFabricSelectionViewModel(files) : InstallTestData.CreateOptiFabricSelection();
        if (kind == "Fabric" && !missingApi)
        {
            await api.PrepareAsync("1.20.1", kind);
            var installed = api.Versions.Single(version => version.VersionNumber == components["fabric-api"]);
            var file = installed.Files.First();
            manifest.ManagedFiles.Add(new("mods/" + file.Filename, file.Sha1!, "fabric-api", file.Url));
        }
        if (addons)
        {
            await optifabric.PrepareAsync("1.20.1");
            var file = optifabric.Versions.Single(version => version.Version == components["optifabric"]);
            manifest.ManagedFiles.Add(new("mods/" + file.FileName, new string('b', 40), "optifabric", file.DownloadUrl));
        }
        InstallManifestStore.Save(_root, "Custom", manifest);
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), mods, services, null, optifabric, api,
            showInstanceError: showInstanceError);
        await model.OpenInstanceAsync(_root, "Custom");
        return (model, services);
    }

    private static InstallComponentCardViewModel Card(InstallViewModel model, string kind) => kind switch
    {
        "Forge" => model.ForgeCard,
        "NeoForge" => model.NeoForgeCard,
        "OptiFine" => model.OptiFineCard,
        _ => model.FabricCard
    };

    private static Task Select(InstallComponentCardViewModel card, string version) =>
        ((IAsyncRelayCommand)card.Entries.Single(entry => entry.Choice.Version == version).SelectCommand).ExecuteAsync(null);

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    public async Task VersionChangesCompareAgainstTheInstalledValueAndRestoreExactly(string kind)
    {
        var (model, _) = await Model(kind);
        var card = Card(model, kind);
        Assert.False(card.HasComparison);
        Assert.Equal("开始修改", model.InstallActionText);
        var original = card.Summary;
        await Select(card, Versions(kind).New);
        Assert.True(card.HasComparison);
        Assert.Equal(original, card.OriginalSummary);
        Assert.NotEqual(card.OriginalSummary, card.ComparisonSummary);
        Assert.True(card.CanRestore);
        Assert.Equal("移除", card.ClearText);
        Assert.Equal("开始修改", model.InstallActionText);
        await card.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(Versions(kind).Old, model.ComponentVersion(kind));
        Assert.False(card.HasComparison);
        Assert.False(card.CanRestore);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    public async Task RemovalRetainsOriginalSideAndRestoresItInsteadOfChoosingLatest(string kind)
    {
        var (model, _) = await Model(kind);
        var card = Card(model, kind);
        card.ClearCommand.Execute(null);
        Assert.False(card.IsSelected);
        Assert.True(card.HasComparison);
        Assert.Equal("移除", card.ComparisonSummary);
        Assert.True(card.CanRestore);
        Assert.Equal(new Thickness(0, 5, 32, 0), card.RestoreMargin);
        await card.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(Versions(kind).Old, model.ComponentVersion(kind));
        Assert.False(card.HasComparison);
        if (kind == "Fabric")
        {
            Assert.True(model.ApiCard.HasComparison);
            await model.ApiCard.RestoreCommand.ExecuteAsync(null);
        }
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task ApiRestorePreservesTheCapturedFileIdentityAndOtherSelections()
    {
        var (model, _) = await Model("Fabric");
        var original = model.ApiSelection.SelectedVersion!;
        model.ApiCard.ClearCommand.Execute(null);
        Assert.True(model.ApiCard.HasComparison);
        Assert.True(model.ApiCard.CanRestore);
        await model.ApiCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(original.Id, model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal(original.Files, model.ApiSelection.SelectedVersion.Files);
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task MissingInstalledApiMetadataStopsOpeningInsteadOfSelectingLatest()
    {
        string? error = null;
        var (model, _) = await Model("Fabric", missingApi: true,
            showInstanceError: (title, message) =>
            {
                Assert.Equal("读取实例失败", title);
                error = message;
                return Task.CompletedTask;
            });
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Null(model.ApiSelection.SelectedVersion);
        Assert.Contains("无法确认已安装的 Fabric API missing-api", model.CombinedStatus);
        Assert.Null(error);
        Assert.True(model.HasInstanceInitializationError);
        Assert.Contains("无法确认已安装的 Fabric API missing-api", model.InstanceInitializationError);
    }

    [AvaloniaFact]
    public async Task OptiFabricRestoreUsesTheOriginalFileAndRequiresItsParentComponents()
    {
        var (model, _) = await Model("Fabric", addons: true);
        var original = model.OptiFabricSelection.SelectedVersion!;
        model.OptiFabricCard.ClearCommand.Execute(null);
        Assert.True(model.OptiFabricCard.HasComparison);
        Assert.True(model.OptiFabricCard.CanRestore);
        await model.OptiFabricCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal(original.Id, model.OptiFabricSelection.SelectedVersion!.Id);
        Assert.Equal(original.DownloadUrl, model.OptiFabricSelection.SelectedVersion.DownloadUrl);
        Assert.Equal("开始修改", model.InstallActionText);
        model.OptiFineCard.ClearCommand.Execute(null);
        Assert.True(model.OptiFabricCard.IsVisible);
        Assert.False(model.OptiFabricCard.CanExpand);
        Assert.False(model.OptiFabricCard.CanRestore);
        await model.OptiFabricCard.RestoreCommand.ExecuteAsync(null);
        Assert.False(model.InstallOptiFineWithBase);
    }

    [AvaloniaFact]
    public async Task ChangingMinecraftDoesNotPermitRestoringAnOldForgeBuildIntoTheNewGame()
    {
        var (model, _) = await Model();
        model.ForgeCard.ClearCommand.Execute(null);
        model.Download.SelectedVersion = new VersionItem { Id = "1.20.2" };
        await model.ForgeCard.PrepareAsync(_root, "1.20.2");
        Assert.True(model.ForgeCard.HasComparison);
        Assert.False(model.ForgeCard.CanRestore);
        await model.ForgeCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("无", model.SelectedBaseLoader);
        Assert.Equal("1.20.2", model.Download.SelectedVersion.Id);
    }

    [AvaloniaFact]
    public async Task ForgeLibraryCoordinateRestoresTheLoaderVersionWithoutTheMinecraftPrefix()
    {
        var (model, _) = await Model();
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components.Remove("forge");
        InstallManifestStore.Save(_root, "Custom", manifest);
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.json"),
            """{"id":"Custom","clientVersion":"1.20.1","mainClass":"net.minecraft.launchwrapper.Launch","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.0"}]}""");
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.Equal("47.2.0", model.Mods.SelectedForgelikeVersion);
        Assert.Equal("开始修改", model.InstallActionText);
        await Select(model.ForgeCard, "47.3.0");
        Assert.Equal("47.2.0", model.ForgeCard.OriginalSummary);
        await model.ForgeCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("47.2.0", model.Mods.SelectedForgelikeVersion);
    }

    [AvaloniaFact]
    public async Task AnUnknownInstalledLoaderVersionStopsOpeningInsteadOfKeepingTheDefaultCatalogVersion()
    {
        var (model, _) = await Model("Fabric");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components.Remove("fabric");
        InstallManifestStore.Save(_root, "Custom", manifest);
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.json"),
            """{"id":"Custom","clientVersion":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.FabricCard.HasComparison);
        Assert.Contains("无法确定 Fabric 的版本", model.CombinedStatus);
        Assert.Equal("开始下载", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task AnEmptyOrFailedCatalogCannotOfferRestoreAsAnAvailableAction()
    {
        var (model, _) = await Model();
        model.ForgeCard.ClearCommand.Execute(null);
        Assert.True(model.ForgeCard.CanRestore);
        model.ForgeCard.UpdateExternalChoices(_root, "1.20.1", [], false, "");
        Assert.True(model.ForgeCard.HasComparison);
        Assert.False(model.ForgeCard.CanRestore);
        model.ForgeCard.UpdateExternalChoices(_root, "1.20.1", [], false, "test metadata failure");
        Assert.False(model.ForgeCard.HasComparison);
        Assert.False(model.ForgeCard.CanRestore);
        Assert.StartsWith("获取失败：", model.ForgeCard.Summary);
    }

    [AvaloniaFact]
    public async Task IncompatibleForgeCannotBeRestoredOverTheSelectedOptiFine()
    {
        var (model, _) = await Model();
        await Select(model.ForgeCard, "47.3.0");
        await Select(model.OptiFineCard, "I6");
        Assert.True(model.ForgeCard.HasComparison);
        Assert.False(model.ForgeCard.CanRestore);
        await model.ForgeCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("47.3.0", model.Mods.SelectedForgelikeVersion);
        Assert.Equal("I6", model.Mods.SelectedOptiFineVersion!.Patch);
    }

    [AvaloniaFact]
    public async Task BusyAndChangedRootCannotExecuteAnOldRestoreCommand()
    {
        var (model, services) = await Model();
        await Select(model.ForgeCard, "47.3.0");
        var restore = model.ForgeCard.RestoreCommand;
        model.IsCombinedBusy = true;
        Assert.False(restore.CanExecute(null));
        await restore.ExecuteAsync(null);
        Assert.Equal("47.3.0", model.Mods.SelectedForgelikeVersion);
        model.IsCombinedBusy = false;
        services.SetGameDirectory(Path.Combine(_root, "next"));
        Assert.False(model.ForgeCard.HasComparison);
        Assert.False(restore.CanExecute(null));
        await restore.ExecuteAsync(null);
        Assert.False(model.IsExistingInstance);
    }

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    public async Task AddedComponentCanRestoreItsOriginalAbsence(string kind)
    {
        var (model, _) = await Model("无");
        var card = Card(model, kind);
        await Select(card, Versions(kind).New);
        card.IsExpanded = true;
        Assert.True(card.HasComparison);
        Assert.False(card.HasOriginalComponent);
        Assert.Equal("无", card.OriginalSummary);
        Assert.Equal("取消安装", card.ClearText);
        Assert.True(card.CanRestore);
        Assert.Equal(new Thickness(132, 11, 110, 0), card.ComparisonMargin);
        await card.RestoreCommand.ExecuteAsync(null);
        Assert.False(card.HasComparison);
        Assert.False(card.IsSelected);
        Assert.False(card.IsExpanded);
        Assert.False(card.RestoreCommand.CanExecute(null));
        Assert.False(model.InstallFabricApi);
        Assert.False(model.InstallOptiFabric);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaTheory]
    [InlineData("Fabric API", "fabric-api")]
    [InlineData("OptiFabric", "optifabric")]
    public async Task AddedSupportingComponentRestoresAbsenceWithoutRemovingItsParents(string kind, string project)
    {
        var (model, _) = await Model("Fabric", addons: true);
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components.Remove(project);
        InstallManifestStore.Save(_root, "Custom", manifest);
        await model.OpenInstanceAsync(_root, "Custom");
        var card = kind == "Fabric API" ? model.ApiCard : model.OptiFabricCard;
        await ((IAsyncRelayCommand)card.Entries.First().SelectCommand).ExecuteAsync(null);
        Assert.True(card.CanRestore);
        Assert.Equal("无", card.OriginalSummary);
        await card.RestoreCommand.ExecuteAsync(null);
        Assert.False(card.IsSelected);
        Assert.False(card.HasComparison);
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.Equal("I5", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task AddedComponentRestoreRemainsAvailableWithoutACatalogButRespectsBusyAndRootChanges()
    {
        var (model, services) = await Model("无");
        await Select(model.ForgeCard, "47.3.0");
        model.ForgeCard.UpdateExternalChoices(_root, "1.20.1", [], false, "catalog unavailable");
        Assert.True(model.ForgeCard.CanRestore);
        model.IsCombinedBusy = true;
        Assert.False(model.ForgeCard.RestoreCommand.CanExecute(null));
        await model.ForgeCard.RestoreCommand.ExecuteAsync(null);
        Assert.True(model.ForgeCard.IsSelected);
        model.IsCombinedBusy = false;
        Assert.True(model.ForgeCard.CanRestore);
        await model.ForgeCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("开始修改", model.InstallActionText);
        services.SetGameDirectory(Path.Combine(_root, "next"));
        Assert.False(model.ForgeCard.RestoreCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("light", false)]
    [InlineData("dark", false)]
    public async Task ComparisonUsesOriginalLayoutAndKeyboardRestoreReturnsFocusToHeader(string theme, bool originallyInstalled)
    {
        ThemeService.Apply(theme);
        var (model, _) = await Model(originallyInstalled ? "Forge" : "无");
        await Select(model.ForgeCard, "47.3.0");
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await Pump();
            var comparison = card.FindControl<Grid>("ComparisonPanel")!;
            var restore = card.FindControl<Button>("RestoreButton")!;
            Assert.True(comparison.IsEffectivelyVisible);
            Assert.Equal(18, comparison.Bounds.Height);
            Assert.Equal(180, comparison.ColumnDefinitions[0].Width.Value);
            Assert.Equal(new Thickness(132, 11, 110, 0), comparison.Margin);
            Assert.Equal(new Thickness(0, 5, 60, 0), restore.Margin);
            Assert.True(restore.IsEffectivelyVisible);
            Assert.False(card.FindControl<Grid>("SummaryPanel")!.IsVisible);
            model.ForgeCard.ToggleCommand.Execute(null);
            await Pump();
            Assert.Equal(0, comparison.Opacity);
            model.ForgeCard.ToggleCommand.Execute(null);
            await Pump();
            Assert.Equal(1, comparison.Opacity);
            restore.Focus();
            var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            input.GetMethod("KeyPress")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            input.GetMethod("KeyRelease")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            await Pump();
            if (originallyInstalled) Assert.Equal("47.2.0", model.Mods.SelectedForgelikeVersion);
            else Assert.False(model.ForgeCard.IsSelected);
            Assert.False(comparison.IsVisible);
            Assert.Equal("开始修改", model.InstallActionText);
            Assert.True(card.FindControl<Button>("HeaderButton")!.IsFocused);
        }
        finally { window.Close(); ThemeService.Apply("light"); }
    }

    private static async Task Pump()
    {
        for (var frame = 0; frame < 35; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
