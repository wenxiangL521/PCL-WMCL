using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

public sealed class VersionPickerTests
{
    [AvaloniaFact]
    public void ClickingTogglesPopupAndSelectionWritesBack()
    {
        var picker = new VersionPicker
        {
            ItemsSource = new[] { "1.21.1", "1.20.1" },
            SelectedItem = "1.20.1"
        };
        var window = new Window { Content = picker, Width = 300, Height = 200 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var popup = picker.FindControl<Popup>("PickerPopup")!;
            var list = picker.FindControl<ListBox>("PickerList")!;
            var button = picker.FindControl<Button>("PickerButton")!;

            Assert.False(popup.IsOpen);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen, "点击后应展开列表");
            Assert.Equal(2, list.ItemCount);

            list.SelectedItem = "1.21.1";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1.21.1", picker.SelectedItem);
            Assert.False(popup.IsOpen, "选中后应关闭");
        }
        finally { window.Close(); }
    }
}

public sealed class ModpackVersionListTests
{
    [AvaloniaFact]
    public async Task ModpackLoadsVanillaVersionList()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "modpack-ver-" + Guid.NewGuid().ToString("N"));
        var services = new WMCL.App.Services.AppServices(
            new WMCL.App.Services.AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new WMCL.App.ViewModels.ModpackViewModel(services);
        await model.EnsureSearchGameVersionsAsync();
        Assert.NotNull(model.SearchGameVersions);
        var picker = new VersionPicker
        {
            ItemsSource = model.SearchGameVersions,
            SelectedItem = model.SearchGameVersion
        };
        // 整合包默认「全部版本」
        Assert.Equal(WMCL.App.Services.SearchVersionFilter.AllLabel, picker.SelectedItem);
    }
}

public sealed class GameVersionResolutionTests
{
    [AvaloniaFact]
    public void ResolvesInstanceInheritedGameVersion()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "resolve-" + Guid.NewGuid().ToString("N"));
        var instanceId = "Fabulously Optimized";
        var dir = Path.Combine(root, "versions", instanceId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, instanceId + ".json"),
            """{"id":"Fabulously Optimized","inheritsFrom":"1.21.1"}""");

        var resolved = WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, instanceId, root);
        Assert.Equal("1.21.1", resolved);
    }

    [AvaloniaFact]
    public void ReturnsNullWhenInstanceMissing()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "resolve-miss-" + Guid.NewGuid().ToString("N"));
        Assert.Null(WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, "不存在", root));
    }
}


public sealed class GameVersionParseTests
{
    [AvaloniaFact]
    public void ParsesInheritsFromAndJar()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "parse-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "versions", "FTB StoneBlock 4");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "FTB StoneBlock 4.json"),
            """{"id":"FTB StoneBlock 4","jar":"1.20.1"}""");
        Assert.Equal("1.20.1", WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, "FTB StoneBlock 4", root));

        var b = Path.Combine(root, "versions", "Fabulously Optimized");
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, "Fabulously Optimized.json"),
            """{"id":"Fabulously Optimized","inheritsFrom":"1.21.1"}""");
        Assert.Equal("1.21.1", WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, "Fabulously Optimized", root));
    }

    [AvaloniaFact]
    public void ReturnsNullForNonVersionInstance()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "parse-none-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "versions", "Custom Pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Custom Pack.json"), """{"id":"Custom Pack"}""");
        Assert.Null(WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, "Custom Pack", root));
    }
}

public sealed class CurseForgeManifestVersionTests
{
    [AvaloniaFact]
    public void ResolvesFromCurseForgeManifest()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "cf-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "versions", "FTB StoneBlock 4");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "FTB StoneBlock 4.json"),
            """{"id":"FTB StoneBlock 4","jar":"FTB StoneBlock 4"}""");
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            """{"manifestType":"minecraftModpack","minecraft":{"version":"1.21.1"}}""");
        Assert.Equal("1.21.1",
            WMCL.App.Services.GameVersionOptions.ResolveGameVersion(null!, "FTB StoneBlock 4", root));
    }
}

public sealed class EffectiveVersionEndToEndTests
{
    [AvaloniaFact]
    public async Task InstalledInstanceNameResolvesToRealGameVersion()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "e2e-" + Guid.NewGuid().ToString("N"));
        var settings = new WMCL.App.Services.AppSettings { GameDirectory = root, LastVersionId = "FTB StoneBlock 4" };
        var services = new WMCL.App.Services.AppServices(settings, saveSettings: _ => { });
        var gameRoot = services.Settings.GameDirectory;
        var dir = Path.Combine(gameRoot, "versions", "FTB StoneBlock 4");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "FTB StoneBlock 4.json"),
            """{"id":"FTB StoneBlock 4","jar":"FTB StoneBlock 4"}""");
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            """{"minecraft":{"version":"1.21.1"}}""");

        var model = new WMCL.App.ViewModels.ModsViewModel(services);
        model.SetMode("mods");
        await model.EnsureSearchGameVersionsAsync();

        Assert.Equal("FTB StoneBlock 4", model.SearchGameVersion);   // 界面显示实例名
        Assert.Equal("1.21.1", model.EffectiveGameVersion);          // 查询用真实版本
    }

    [AvaloniaFact]
    public void RealVersionPassesThrough()
    {
        Assert.True(WMCL.App.Services.GameVersionOptions.LooksLikeGameVersion("1.21.1"));
        Assert.True(WMCL.App.Services.GameVersionOptions.LooksLikeGameVersion("26.3"));
        Assert.False(WMCL.App.Services.GameVersionOptions.LooksLikeGameVersion("FTB StoneBlock 4"));
    }
}

public sealed class VersionFilterSelfTests
{
    [AvaloniaFact]
    public async Task FirstOptionIsAllVersions()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "all-" + Guid.NewGuid().ToString("N"));
        var services = new WMCL.App.Services.AppServices(
            new WMCL.App.Services.AppSettings { GameDirectory = root, LastVersionId = "Some Instance" },
            saveSettings: _ => { });
        var model = new WMCL.App.ViewModels.ModpackViewModel(services);
        await model.EnsureSearchGameVersionsAsync();

        Assert.Equal(WMCL.App.Services.SearchVersionFilter.AllLabel, model.SearchGameVersions[0]);
        Assert.Equal(WMCL.App.Services.SearchVersionFilter.AllLabel, model.SearchGameVersion);
        Assert.Equal("", model.EffectiveGameVersion);
    }
}
