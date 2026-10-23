using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed partial class ComponentComparisonTests
{
    private static void ChangeMinecraft(InstallViewModel model, string minecraft = "1.20.2", string type = "release")
    {
        var version = new VersionItem { Id = minecraft, Type = type, ReleaseTime = "2023-09-21T10:00:00Z" };
        model.VersionCatalog.Open([version], model.Download.SelectedVersion);
        model.VersionCatalog.Groups.SelectMany(group => group.Entries).First().SelectCommand.Execute(null);
    }

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("Fabric")]
    [InlineData("OptiFine")]
    [InlineData("无")]
    public async Task MinecraftRestoreReturnsEveryOriginalSelectionInsteadOfCatalogDefaults(string kind)
    {
        var (model, _) = await Model(kind);
        Assert.False(model.HasMinecraftComparison);
        Assert.False(model.RestoreMinecraftCommand.CanExecute(null));
        var original = model.ComponentVersion(kind);
        var api = model.ApiSelection.SelectedVersion;
        ChangeMinecraft(model);
        Assert.Equal("1.20.1", model.OriginalMinecraftVersionText);
        Assert.Equal("1.20.2", model.MinecraftVersionText);
        Assert.True(model.HasMinecraftComparison);
        Assert.True(model.RestoreMinecraftCommand.CanExecute(null));
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("1.20.1", model.Download.SelectedVersion!.Id);
        Assert.Equal(kind, model.SelectedBaseLoader);
        if (kind != "无") Assert.Equal(original, model.ComponentVersion(kind));
        if (kind == "Fabric") Assert.Equal(api!.Id, model.ApiSelection.SelectedVersion!.Id);
        Assert.False(model.HasMinecraftComparison);
        Assert.False(model.IsRestoringMinecraft);
        Assert.True(model.CanEditInstall);
        Assert.Equal("开始修改", model.InstallActionText);
        Assert.False(model.ForgeCard.IsExpanded);
        Assert.False(model.FabricCard.IsExpanded);
        Assert.False(model.ApiCard.IsExpanded);
    }

    [AvaloniaFact]
    public async Task MinecraftRestorePreservesBothCapturedSupportingFilesAndTheirParents()
    {
        var (model, _) = await Model("Fabric", addons: true);
        var api = model.ApiSelection.SelectedVersion!;
        var optifabric = model.OptiFabricSelection.SelectedVersion!;
        var optifine = model.Mods.SelectedOptiFineVersion!;
        ChangeMinecraft(model);
        model.ApiCard.ClearCommand.Execute(null);
        model.OptiFineCard.ClearCommand.Execute(null);
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("0.15.0", model.Mods.SelectedLoader);
        Assert.Equal(api.Id, model.ApiSelection.SelectedVersion!.Id);
        Assert.Equal(api.Files, model.ApiSelection.SelectedVersion.Files);
        Assert.Equal(optifabric.Id, model.OptiFabricSelection.SelectedVersion!.Id);
        Assert.Equal(optifabric.DownloadUrl, model.OptiFabricSelection.SelectedVersion.DownloadUrl);
        Assert.Same(optifine, model.Mods.SelectedOptiFineVersion);
        Assert.True(model.InstallFabricApi);
        Assert.True(model.InstallOptiFabric);
        Assert.True(model.InstallOptiFineWithBase);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task MinecraftRestoreDoesNotAddAnOriginallyAbsentApi()
    {
        var (model, _) = await Model("Fabric");
        var manifest = WMCL.Minecraft.InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components.Remove("fabric-api");
        WMCL.Minecraft.InstallManifestStore.Save(_root, "Custom", manifest);
        await model.OpenInstanceAsync(_root, "Custom");
        ChangeMinecraft(model);
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.False(model.InstallFabricApi);
        Assert.Null(model.ApiSelection.SelectedVersion);
        Assert.False(model.ApiCard.IsSelected);
        Assert.Equal("开始修改", model.InstallActionText);
    }

    [AvaloniaFact]
    public async Task UnresolvedOriginalApiCannotEnterMinecraftModification()
    {
        var (model, _) = await Model("Fabric", missingApi: true);
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.CanRestoreMinecraft);
        Assert.Null(model.Download.SelectedVersion);
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Null(model.Download.SelectedVersion);
    }

    [AvaloniaFact]
    public async Task MinecraftRestoreIgnoresLateCompletionAfterChangingRoot()
    {
        TaskCompletionSource? gate = null;
        var (model, services) = await Model("Fabric", prepareOptiFine: game =>
            game == "1.20.1" && gate is not null ? gate.Task : Task.CompletedTask);
        ChangeMinecraft(model);
        gate = new TaskCompletionSource();
        var restore = model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.True(model.IsRestoringMinecraft);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.CanEditInstall);
        Assert.False(model.RestoreMinecraftCommand.CanExecute(null));
        services.SetGameDirectory(Path.Combine(_root, "next"));
        gate.SetResult();
        await restore;
        Assert.False(model.IsExistingInstance);
        Assert.False(model.HasMinecraftComparison);
        Assert.Null(model.Download.SelectedVersion);
        Assert.False(model.IsRestoringMinecraft);
        Assert.False(model.ApiCard.IsSelected);
    }

    [AvaloniaFact]
    public async Task MinecraftRestoreRespectsBusyAndDoesNotChangeTheTargetInstanceIdentity()
    {
        var (model, _) = await Model();
        ChangeMinecraft(model);
        model.IsCombinedBusy = true;
        Assert.False(model.RestoreMinecraftCommand.CanExecute(null));
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("1.20.2", model.MinecraftVersionText);
        model.IsCombinedBusy = false;
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("Custom", model.TargetInstanceId);
        Assert.Equal("Custom", model.InstanceName);
        Assert.Equal("1.20.1", model.MinecraftVersionText);
    }

    [AvaloniaFact]
    public async Task MinecraftRestoreUsesCapturedComponentsEvenWhenTheOriginalCatalogIsOffline()
    {
        var offline = false;
        var (model, _) = await Model("OptiFine", prepareOptiFine: game =>
            game == "1.20.1" && offline ? Task.FromException(new IOException("offline")) : Task.CompletedTask);
        var optifine = model.Mods.SelectedOptiFineVersion;
        ChangeMinecraft(model);
        offline = true;
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Same(optifine, model.Mods.SelectedOptiFineVersion);
        Assert.Equal("开始修改", model.InstallActionText);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task RealInstallViewShowsMinecraftOnlyForModificationAndItsHeaderOpensTheCatalog()
    {
        var (model, _) = await Model();
        model.Download.Versions.Add(new VersionItem { Id = "1.20.2" });
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await Pump();
            var card = view.FindControl<InstallMinecraftCard>("MinecraftComponentCard")!;
            Assert.True(card.IsEffectivelyVisible);
            Assert.False(card.FindControl<Grid>("ComparisonPanel")!.IsVisible);
            Assert.True(card.FindControl<Grid>("SummaryPanel")!.IsVisible);
            var header = card.FindControl<Button>("HeaderButton")!;
            header.Focus();
            var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            input.GetMethod("KeyPress")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            input.GetMethod("KeyRelease")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            await Pump();
            Assert.True(model.IsChoosingVersion);
            model.ShowDownloadCatalog();
            ChangeMinecraft(model);
            await Pump();
            Assert.False(model.IsExistingInstance);
            Assert.False(card.IsVisible);
            Assert.False(model.RestoreMinecraftCommand.CanExecute(null));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task MinecraftCardUsesOriginalComparisonLayoutAndKeyboardRestore(string theme)
    {
        ThemeService.Apply(theme);
        var (model, _) = await Model("Fabric", addons: true);
        var originalIcon = model.OriginalMinecraftIcon;
        ChangeMinecraft(model, "24w10a", "snapshot");
        Assert.Same(originalIcon, model.OriginalMinecraftIcon);
        Assert.NotSame(model.MinecraftIcon, model.OriginalMinecraftIcon);
        var card = new InstallMinecraftCard { DataContext = model };
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
            Assert.Equal(new Thickness(132, 11, 80, 0), comparison.Margin);
            Assert.Equal(new Thickness(0, 5, 32, 0), restore.Margin);
            Assert.Equal(30, restore.Bounds.Width);
            Assert.False(card.FindControl<Grid>("SummaryPanel")!.IsVisible);
            restore.Focus();
            var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            input.GetMethod("KeyPress")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            input.GetMethod("KeyRelease")!.Invoke(window.PlatformImpl, [Key.Enter, RawInputModifiers.None, PhysicalKey.None, null]);
            await Pump();
            Assert.False(comparison.IsVisible);
            Assert.True(card.FindControl<Grid>("SummaryPanel")!.IsVisible);
            Assert.Equal("开始修改", model.InstallActionText);
            var header = card.FindControl<Button>("HeaderButton")!;
            Assert.True(header.IsFocused);
            Assert.False(model.IsChoosingVersion);
        }
        finally { window.Close(); ThemeService.Apply("light"); }
    }
}
