using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class InstalledModManagementFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "installed-mod-flow-" + Guid.NewGuid().ToString("N"));
    private string DirectoryPath => Path.Combine(_root, "mods");
    public InstalledModManagementFlowTests()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "Alpha.jar"), "alpha fixture");
        File.WriteAllText(Path.Combine(DirectoryPath, "Beta.jar"), "beta fixture");
    }
    private ModsViewModel Model(Func<string, string, Task<bool>>? confirm = null)
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        return new ModsViewModel(services, null, null, null, confirmLocalDelete: confirm)
        { ModsDirectory = DirectoryPath, IsInstanceManagement = true };
    }

    [AvaloniaFact]
    public void SearchIsCaseInsensitiveAndClearRestoresTheOriginalCollectionWithoutRescanning()
    {
        var model = Model();
        model.LocalModFilter = "ALPHA";
        Assert.Single(model.LocalModsView);
        Assert.Equal("Alpha.jar", model.LocalModsView[0].FileName);
        Assert.Equal(2, model.LocalMods.Count);
        Assert.Contains("1/2", model.LocalModStatus);
        File.WriteAllText(Path.Combine(DirectoryPath, "new-not-yet-refreshed.jar"), "new");
        model.ClearLocalModFilterCommand.Execute(null);
        Assert.Equal("", model.LocalModFilter);
        Assert.Equal(2, model.LocalModsView.Count);
        model.RefreshLocalModsCommand.Execute(null);
        Assert.Equal(3, model.LocalModsView.Count);
    }

    [AvaloniaFact]
    public async Task BatchActionsOperateOnTheWholeSnapshotAndDeleteOnlyAfterConfirmation()
    {
        var count = 0;
        var model = Model((_, _) => { count++; return Task.FromResult(true); });
        var selected = model.LocalMods.ToArray();
        model.SetLocalModsEnabled(selected, false);
        Assert.All(model.LocalMods, mod => Assert.False(mod.IsEnabled));
        Assert.True(File.Exists(Path.Combine(DirectoryPath, "Alpha.jar.disabled")));
        Assert.True(File.Exists(Path.Combine(DirectoryPath, "Beta.jar.disabled")));
        await model.DeleteLocalModsAsync(model.LocalMods.ToArray());
        Assert.Equal(1, count);
        Assert.Empty(model.LocalMods);
        Assert.Empty(Directory.EnumerateFiles(DirectoryPath));
    }

    [AvaloniaFact]
    public async Task SwitchingDirectoriesWhileDeleteConfirmationIsPendingDoesNotDeleteEitherTarget()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model((_, _) => pending.Task);
        var selected = model.LocalMods.ToArray();
        var deletion = model.DeleteLocalModsAsync(selected);
        var next = Path.Combine(_root, "other-mods");
        Directory.CreateDirectory(next);
        File.WriteAllText(Path.Combine(next, "Gamma.jar"), "gamma");
        model.ModsDirectory = next;
        pending.SetResult(true);
        await deletion;
        Assert.All(selected, mod => Assert.True(File.Exists(mod.FilePath)));
        Assert.True(File.Exists(Path.Combine(next, "Gamma.jar")));
        Assert.Contains("实例已切换", model.LocalModStatus);
    }

    [AvaloniaFact]
    public async Task TheToolbarStaysOutsideTheScrollableBrowserAndBatchDeleteUsesTheMultiSelection()
    {
        var model = Model((_, _) => Task.FromResult(false));
        model.SetMode("mods");
        for (var index = 0; index < 50; index++) File.WriteAllText(Path.Combine(DirectoryPath, "fixture-" + index + ".jar"), "fake");
        model.RefreshLocalModsCommand.Execute(null);
        var view = new ModsView { DataContext = model };
        var window = new Window { Content = view, Width = 700, Height = 500 };
        try
        {
            window.Show();
            Pump();
            var toolbar = view.FindControl<Border>("ModManagementToolbar")!;
            var scroll = view.FindControl<ScrollViewer>("ResourceBrowserScroll")!;
            var before = toolbar.TranslatePoint(default, window);
            Assert.DoesNotContain(toolbar.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, scroll));
            scroll.Offset = new Vector(0, 500);
            Pump();
            Assert.Equal(before, toolbar.TranslatePoint(default, window));
            Assert.True(toolbar.IsEffectivelyVisible);
            var clear = toolbar.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "清除"));
            Assert.Same(model.ClearLocalModFilterCommand, clear.Command);
            var list = view.FindControl<ListBox>("LocalModList")!;
            list.SelectedItems!.Add(model.LocalModsView[0]);
            list.SelectedItems.Add(model.LocalModsView[1]);
            Assert.True(list.SelectedItems.Count >= 2);
            Assert.Single(toolbar.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "删除所选"));
        }
        finally { window.Close(); }
        await Task.CompletedTask;
    }

    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
