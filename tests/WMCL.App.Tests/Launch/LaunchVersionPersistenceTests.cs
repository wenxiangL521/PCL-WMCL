using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LaunchVersionPersistenceTests
{
    private static LaunchViewModel Create(AppServices services, IReadOnlyList<VersionItem> versions) =>
        new(services, null, root => Task.FromResult(versions), Path.Combine(services.Settings.GameDirectory, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));

    private static VersionItem Version(string id) => new() { Id = id, IsInstalled = true };

    [AvaloniaFact]
    public async Task SelectedVersionSurvivesRestart()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "ver-persist-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { GameDirectory = root };
        var services = new AppServices(settings, saveSettings: _ => { });
        var versions = new[] { Version("1.20.1"), Version("1.7.10") };

        var first = Create(services, versions);
        await first.LoadAsync();
        first.SelectedVersion = first.InstalledVersions.First(item => item.Id == "1.7.10");
        Assert.Equal("1.7.10", settings.LastVersionId);
        Assert.Equal("1.7.10", settings.LastVersionByFolder[root]);

        // 模拟重启：新视图模型应恢复到上次选中的版本
        var second = Create(services, versions);
        await second.LoadAsync();
        Assert.Equal("1.7.10", second.SelectedVersion?.Id);
    }

    [AvaloniaFact]
    public async Task FallsBackToFirstWhenSavedVersionMissing()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "ver-persist2-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { GameDirectory = root };
        settings.LastVersionId = "gone";
        settings.LastVersionByFolder[root] = "gone";
        var services = new AppServices(settings, saveSettings: _ => { });
        var model = Create(services, [Version("1.20.1")]);
        await model.LoadAsync();
        Assert.Equal("1.20.1", model.SelectedVersion?.Id);
    }
}
