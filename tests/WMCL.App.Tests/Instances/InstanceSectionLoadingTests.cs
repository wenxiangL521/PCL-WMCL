using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class InstanceSectionLoadingTests
{
    private static (MainWindowViewModel Main, string Root) Fixture(int mods)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "section-loading-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "versions", "Pack");
        var modsDirectory = Path.Combine(instance, "mods");
        Directory.CreateDirectory(modsDirectory);
        File.WriteAllText(Path.Combine(instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.21.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}],"arguments":{"game":[],"jvm":[]}}""");
        for (var index = 0; index < mods; index++)
            File.WriteAllText(Path.Combine(modsDirectory, $"mod-{index}.jar"), "fixture");
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var main = new MainWindowViewModel(services);
        main.Instance.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        return (main, root);
    }

    [AvaloniaFact]
    public async Task OpeningModManagementShowsLoadingStateBeforeContent()
    {
        var (main, _) = Fixture(60);
        using var _main = main;
        main.NavigateTo("instance");
        main.Instance.SelectedSectionIndex = 3;
        // 切换后立刻应处于“准备中”，说明读取被让出到后台而不是同步卡住。
        Assert.True(main.Instance.IsSectionPreparing);
        Assert.Null(main.Instance.EmbeddedPage);
        await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(main.Instance.IsSectionPreparing);
        Assert.Same(main.Mods, main.Instance.EmbeddedPage);
        Assert.Equal(60, main.Mods.LocalMods.Count);
    }

    [AvaloniaFact]
    public async Task OpeningModifySectionAlsoShowsLoadingState()
    {
        var (main, _) = Fixture(2);
        using var _main = main;
        main.NavigateTo("instance");
        main.Instance.SelectedSectionIndex = 2;
        Assert.True(main.Instance.IsSectionPreparing);
        await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(main.Instance.IsSectionPreparing);
        Assert.Same(main.Install, main.Instance.EmbeddedPage);
    }
}
