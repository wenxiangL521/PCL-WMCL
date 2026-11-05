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
        // 现在整体替换集合：只发一次 View 变更通知，不再逐条 Add
        var resets = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.LocalModsView)) resets++; };
        model.RefreshLocalModsCommand.Execute(null);
        Assert.Equal(300, model.LocalModsView.Count);
        Assert.InRange(resets, 1, 3);
        try { Directory.Delete(root, true); } catch { }
    }
}
