using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class HomeNewsCapacityTests
{
    private static LaunchViewModel Model()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "news-cap-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
    }

    private static void Seed(LaunchViewModel model)
    {
        for (var index = 0; index < 6; index++)
            model.HomeNews.Add(new MinecraftNewsItem("t" + index, "https://example.invalid/" + index, "", null, null, null, index));
        model.SetHomeNewsCapacity(5);
        model.SetHomeNewsCapacity(4);
    }

    [AvaloniaFact]
    public void NarrowLayoutShowsFourAndWideLayoutShowsSixAtMost()
    {
        var model = Model();
        Seed(model);
        Assert.Equal(4, model.HomeNewsDisplay.Count);
        model.SetHomeNewsCapacity(6);
        Assert.Equal(6, model.HomeNewsDisplay.Count);
        model.SetHomeNewsCapacity(12);
        Assert.Equal(6, model.HomeNewsDisplay.Count);
        model.SetHomeNewsCapacity(1);
        Assert.Equal(2, model.HomeNewsDisplay.Count);
    }
}
