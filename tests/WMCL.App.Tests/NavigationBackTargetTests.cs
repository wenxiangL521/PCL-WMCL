using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class NavigationBackTargetTests
{
    [AvaloniaFact]
    public async Task BackFromDownloadReturnsToInstancePageNotLaunch()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "nav-target-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var main = new MainWindowViewModel(services);

        main.NavigateTo("instance");
        await Task.Delay(30);
        var instancePage = main.CurrentPage;
        Assert.IsType<InstanceViewModel>(instancePage);

        main.NavigateTo("content-resource");
        Assert.True(main.CanGoBack);

        main.GoBackCommand.Execute(null);
        Assert.Same(instancePage, main.CurrentPage);
    }
}
