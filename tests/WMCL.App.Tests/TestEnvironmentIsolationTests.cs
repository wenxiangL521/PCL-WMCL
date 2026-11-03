using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;

namespace WMCL.App.Tests;

public sealed class TestEnvironmentIsolationTests
{
    [Fact]
    public void AllDefaultAndLegacyApplicationPathsUseTheTestRoot()
    {
        foreach (var path in new[]
                 {
                     WmclPaths.ConfigDirectory, WmclPaths.DataDirectory, WmclPaths.CacheDirectory,
                     WmclPaths.LegacyConfigDirectory, WmclPaths.LegacyDataDirectory, WmclPaths.LegacyCacheDirectory
                 })
            Assert.StartsWith(WMCL.Tests.TestEnvironment.Root + Path.DirectorySeparatorChar, path);
    }

    [AvaloniaFact]
    public async Task AutoSaveWritesOnlyTheIsolatedConfigurationAndRestoresProxyState()
    {
        var previousConfig = File.Exists(WmclPaths.ConfigFile) ? File.ReadAllText(WmclPaths.ConfigFile) : null;
        var previousProxy = ProxyManager.Current;
        using var model = new SettingsViewModel(new AppServices(new AppSettings()));
        try
        {
            model.ProxyMode = "custom";
            model.ProxyUrl = "http://127.0.0.1:8080";
            model.PlayerName = "IsolatedTestPlayer";
            await Task.Delay(600);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith(WMCL.Tests.TestEnvironment.Root + Path.DirectorySeparatorChar, WmclPaths.ConfigFile);
            var saved = SettingsService.Load();
            Assert.Equal("IsolatedTestPlayer", saved.PlayerName);
            Assert.Equal("custom", saved.ProxyMode);
        }
        finally
        {
            ProxyManager.Apply(previousProxy);
            if (previousConfig is null) File.Delete(WmclPaths.ConfigFile);
            else File.WriteAllText(WmclPaths.ConfigFile, previousConfig);
        }
    }
}
