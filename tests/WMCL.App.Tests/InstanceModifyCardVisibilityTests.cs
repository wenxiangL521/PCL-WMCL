using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class InstanceModifyCardVisibilityTests
{
    [AvaloniaFact]
    public async Task OpeningModifyPageShowsAllComponentRows()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "modify-rows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "versions", "Custom"));
        File.WriteAllText(Path.Combine(root, "versions", "Custom", "Custom.json"),
            """{"id":"Custom","inheritsFrom":"1.20.1"}""");
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions);
        var model = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());

        await model.OpenInstanceAsync(root, "Custom");

        Assert.True(model.IsExistingInstance);
        Assert.True(model.ForgeCard.IsVisible, "Forge 行应可见");
        Assert.True(model.FabricCard.IsVisible, "Fabric 行应可见");
        Assert.True(model.OptiFineCard.IsVisible, "OptiFine 行应可见");
    }
}
