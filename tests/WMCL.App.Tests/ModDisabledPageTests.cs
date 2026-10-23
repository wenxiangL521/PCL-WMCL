using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class ModDisabledPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-mod-disabled-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task VanillaInstanceShowsDisabledPageAndNavigationEvents()
    {
        var directory = Path.Combine(_root, "versions", "Vanilla");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Vanilla.json"), """
            {
              "id": "Vanilla",
              "type": "release",
              "clientVersion": "1.20.1",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": []
            }
            """);
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var model = new ModsViewModel(services,
            getLoaders: (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([]),
            getForgelikeVersions: (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            getOptiFineVersions: _ => Task.FromResult<IReadOnlyList<OptiFineVersion>>([]),
            getOfficialVersions: () => Task.FromResult<IReadOnlyList<string>>(["1.20.1"]));
        try
        {
            await model.LoadAsync();
            Assert.True(model.IsModDisabled);
            var view = new ModsView { DataContext = model };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();
            try
            {
                Assert.True(view.FindControl<Border>("ModDisabledPanel")!.IsEffectivelyVisible);
            }
            finally { window.Close(); }
            var download = 0;
            var select = 0;
            model.DownloadRequested += (_, _) => download++;
            model.VersionSelectRequested += (_, _) => select++;
            model.GoDownloadCommand.Execute(null);
            model.GoVersionSelectCommand.Execute(null);
            Assert.Equal(1, download);
            Assert.Equal(1, select);
        }
        finally
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
