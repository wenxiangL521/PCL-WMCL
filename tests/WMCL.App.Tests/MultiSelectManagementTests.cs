using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class MultiSelectManagementTests
{
    [AvaloniaFact]
    public void ResourceListsAllowMultipleSelectionAndExposeBatchActions()
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "multi-select-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        using var model = new InstanceContentViewModel(services);
        var view = new InstanceContentView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            Pump();
            foreach (var name in new[] { "ResourcePackList", "ShaderPackList", "WorldList" })
            {
                var list = view.FindControl<ListBox>(name);
                Assert.NotNull(list);
                Assert.Equal(SelectionMode.Multiple, list!.SelectionMode);
            }
            var actions = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible).Select(button => button.Content?.ToString()).ToArray();
            Assert.Contains("启用/禁用所选", actions);
            Assert.Contains("删除所选", actions);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ModManagementListAndToolbarDeclareMultiSelection()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "src/WMCL.App/Views/ModsView.axaml"));
        Assert.Contains("x:Name=\"LocalModList\"", xaml);
        Assert.Contains("SelectionMode=\"Multiple\"", xaml);
        Assert.Contains("OnToggleSelectedModsClick", xaml);
        Assert.Contains("OnDeleteSelectedModsClick", xaml);
    }

    [Fact]
    public void InstanceExportHasSingleEntryAndImportLivesWithImportModpack()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "src/WMCL.App/Views/InstanceView.axaml"));
        Assert.Contains("ExportModes", view);
        Assert.Contains("Command=\"{Binding ExportCommand}\"", view);
        Assert.DoesNotContain("ExportInstanceCommand", view);
        Assert.DoesNotContain("ExportModpackCommand", view);
        Assert.DoesNotContain("导入实例", view);
        var picker = File.ReadAllText(Path.Combine(Root(), "src/WMCL.App/Views/VersionSelectView.axaml.cs"));
        Assert.Contains("*.zip", picker);
        Assert.Contains("*.mrpack", picker);
    }

    [Fact]
    public void InstalledModListHasItsOwnSearchBox()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "src/WMCL.App/Views/ModsView.axaml"));
        Assert.Contains("LocalModSearchBox", view);
        Assert.Contains("LocalModFilter", view);
        Assert.Contains("LocalModsView", view);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WMCL.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
