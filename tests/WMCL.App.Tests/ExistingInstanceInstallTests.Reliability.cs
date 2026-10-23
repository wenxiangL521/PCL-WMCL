using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [AvaloniaFact]
    public async Task AnUnreliableUnchangedInstanceCanRequestModificationButCancellationPreservesEverything()
    {
        CreateUnreliableInstance();
        var services = Services();
        services.Settings.HintModifyInstanceWarningAcknowledged = true;
        var backend = new Backend();
        string? warning = null;
        var model = ReliabilityModel(services, backend, (_, message) =>
        {
            warning = message;
            return Task.FromResult(false);
        });
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.False(model.IsOriginalMinecraftReliable);
        Assert.False(model.HasMinecraftComparison);
        Assert.Equal("1.20.1", model.MinecraftVersionText);
        Assert.Equal("开始修改", model.InstallActionText);
        Assert.True(model.CanApplyInstall);
        var original = Snapshot();
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Contains("WMCL 未能精准识别该版本", warning);
        Assert.Contains("Minecraft 1.20.1", warning);
        Assert.Equal(0, backend.VanillaCalls);
        Assert.True(model.CanApplyInstall);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmingAnUnreliableIdentityInstallsTheChosenBaseEvenWhenOnlyTheNameChanged(bool rename)
    {
        CreateUnreliableInstance();
        var services = Services();
        services.Settings.HintModifyInstanceWarningAcknowledged = true;
        var backend = new Backend();
        string? warning = null;
        var model = ReliabilityModel(services, backend, (_, message) =>
        {
            warning = message;
            return Task.FromResult(true);
        });
        await model.OpenInstanceAsync(_root, "Custom");
        if (rename) model.InstanceName = "Renamed";
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Contains("未能精准识别", warning);
        Assert.Equal(1, backend.VanillaCalls);
        var target = rename ? "Renamed" : "Custom";
        Assert.Equal(target, model.TargetInstanceId);
        Assert.Equal("new client", File.ReadAllText(Path.Combine(DirectoryFor(target), target + ".jar")));
        Assert.Equal("1.20.1", InstallManifestStore.Load(_root, target)!.Components["minecraft"]);
        Assert.True(model.IsOriginalMinecraftReliable);
        Assert.False(model.CanApplyInstall);
        Assert.Equal("progress", File.ReadAllText(Path.Combine(_root, "saves/World/level.dat")));
        Assert.Equal("user mod", File.ReadAllText(Path.Combine(_root, "mods/user.jar")));
        await model.OpenInstanceAsync(_root, target);
        Assert.True(model.IsOriginalMinecraftReliable);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task ChoosingAnotherMinecraftVersionDoesNotWarnAboutTheDiscardedGuess()
    {
        CreateUnreliableInstance();
        var services = Services();
        services.Settings.HintModifyInstanceWarningAcknowledged = true;
        var backend = new Backend();
        var confirmations = 0;
        var model = ReliabilityModel(services, backend, (_, _) =>
        {
            confirmations++;
            return Task.FromResult(false);
        });
        await model.OpenInstanceAsync(_root, "Custom");
        await ChooseReliableMinecraftAsync(model);
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Equal(0, confirmations);
        Assert.Equal(1, backend.VanillaCalls);
        Assert.Equal("1.21.1", InstallManifestStore.Load(_root, "Custom")!.Components["minecraft"]);
        Assert.True(model.IsOriginalMinecraftReliable);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task RestoringTheGuessedMinecraftRetainsItsWarningAndEnabledAction()
    {
        CreateUnreliableInstance();
        var services = Services();
        services.Settings.HintModifyInstanceWarningAcknowledged = true;
        var backend = new Backend();
        string? warning = null;
        var model = ReliabilityModel(services, backend, (_, message) =>
        {
            warning = message;
            return Task.FromResult(false);
        });
        await model.OpenInstanceAsync(_root, "Custom");
        await ChooseReliableMinecraftAsync(model);
        Assert.True(model.CanRestoreMinecraft);
        await model.RestoreMinecraftCommand.ExecuteAsync(null);
        Assert.Equal("1.20.1", model.MinecraftVersionText);
        Assert.False(model.IsOriginalMinecraftReliable);
        Assert.True(model.CanApplyInstall);
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Contains("未能精准识别", warning);
        Assert.Equal(0, backend.VanillaCalls);
    }

    [AvaloniaFact]
    public async Task OpeningAReliableInstanceDoesNotRetainThePreviousUnreliableState()
    {
        CreateUnreliableInstance();
        var model = ReliabilityModel(Services(), new Backend(), (_, _) => Task.FromResult(false));
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsOriginalMinecraftReliable);
        var directory = DirectoryFor("Reliable");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Reliable.json"),
            """{"id":"Reliable","clientVersion":"1.20.1","libraries":[]}""");
        await model.OpenInstanceAsync(_root, "Reliable");
        Assert.True(model.IsOriginalMinecraftReliable);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task AnOfficialVersionWithoutNumericPatternCanStillBeRecognizedReliably()
    {
        CreateInstance();
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        var official = DirectoryFor("20w14infinite");
        Directory.CreateDirectory(official);
        File.WriteAllText(Path.Combine(official, "20w14infinite.json"), """{"id":"20w14infinite","libraries":[]}""");
        var services = Services();
        var download = new DownloadViewModel(services,
            () => Task.FromResult<IReadOnlyList<VersionItem>>([new() { Id = "20w14infinite", Type = "snapshot" }]));
        await download.LoadAsync();
        var model = ViewModel(services, download);
        await model.OpenInstanceAsync(_root, "20w14infinite");
        Assert.True(model.IsExistingInstance);
        Assert.True(model.IsOriginalMinecraftReliable);
        Assert.False(model.CanApplyInstall);
    }

    private void CreateUnreliableInstance()
    {
        CreateInstance();
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        File.WriteAllText(Path.Combine(DirectoryFor(), "Custom.json"),
            """{"id":"Friendly","description":"Minecraft 1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
    }

    private InstallViewModel ReliabilityModel(AppServices services, Backend backend, Func<string, string, Task<bool>> confirm) =>
        new(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services, backend.ForRoot,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(), confirmModification: confirm);

    private static async Task ChooseReliableMinecraftAsync(InstallViewModel model)
    {
        model.Download.Versions.Add(new VersionItem { Id = "1.21.1" });
        await model.OpenVersionCatalogCommand.ExecuteAsync(null);
        model.VersionCatalog.Groups.SelectMany(group => group.Entries)
            .First(entry => entry.Version.Id == "1.21.1").SelectCommand.Execute(null);
        await model.PendingComponentSelectionTask;
    }
}
