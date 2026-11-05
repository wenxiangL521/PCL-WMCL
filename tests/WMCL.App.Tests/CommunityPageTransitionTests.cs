using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class CommunityPageTransitionTests
{
    [AvaloniaFact]
    public void SwitchingModToModpackChangesPageInstance()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "nav-anim-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var main = new MainWindowViewModel(services);

        main.NavigateTo("mods");
        var modsPage = main.CurrentPage;
        main.NavigateTo("modpack");
        var modpackPage = main.CurrentPage;

        // 页面实例变化才会触发 TransitioningContentControl 的 CrossFade
        Assert.NotNull(modsPage);
        Assert.NotNull(modpackPage);
        Assert.NotSame(modsPage, modpackPage);
    }
}
