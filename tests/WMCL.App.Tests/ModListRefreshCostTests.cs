using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class ModListRefreshCostTests
{
    [AvaloniaFact]
    public void RefreshingManyModsUpdatesTheFilteredViewLinearly()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "mod-refresh-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "mods");
        Directory.CreateDirectory(directory);
        for (var index = 0; index < 300; index++)
            File.WriteAllText(Path.Combine(directory, $"mod-{index:D3}.jar"), "fixture");
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new ModsViewModel(services) { ModsDirectory = directory, IsInstanceManagement = true };
        var events = 0;
        ((INotifyCollectionChanged)model.LocalModsView).CollectionChanged += (_, _) => events++;
        model.RefreshLocalModsCommand.Execute(null);
        Assert.Equal(300, model.LocalModsView.Count);
        // 旧实现每次添加都会重算过滤视图，300 个 Mod 会产生近 9 万次变更通知。
        Assert.InRange(events, 1, 300 * 3);
        try { Directory.Delete(root, true); } catch { }
    }
}
