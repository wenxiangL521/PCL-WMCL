using System.IO.Compression;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ExportSelectionFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "export-selection-" + Guid.NewGuid().ToString("N"));
    private string Instance => Path.Combine(_root, "versions", "Pack");

    public ExportSelectionFlowTests()
    {
        Directory.CreateDirectory(Instance);
        File.WriteAllText(Path.Combine(Instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.21.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}],"arguments":{"game":[],"jvm":[]}}""");
        foreach (var relative in new[]
                 {
                     "mods/alpha.jar", "mods/beta.jar", "config/pack.cfg", "saves/World/level.dat",
                     "logs/latest.log", "options.txt"
                 })
        {
            var path = Path.Combine(Instance, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
        }
    }

    private InstanceViewModel Model()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        return new InstanceViewModel(services)
        {
            SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true },
            ExportMode = "整合包 (.mrpack)"
        };
    }

    [AvaloniaFact]
    public async Task ListingShowsOnlyTopLevelFilesAndFoldedFolders()
    {
        using var model = Model();
        model.UseExportFileSelection = true;
        await model.ExportFilesPreparationTask;
        var names = model.ExportFiles.Select(file => file.RelativePath).ToArray();
        Assert.Contains("mods", names);
        Assert.Contains("config", names);
        Assert.Contains("saves", names);
        Assert.Contains("logs", names);
        Assert.Contains("options.txt", names);
        Assert.DoesNotContain("mods/alpha.jar", names);
        Assert.DoesNotContain("saves/World", names);
        Assert.All(model.ExportFiles.Where(file => file.IsDirectory), file => Assert.Equal("文件夹", file.SizeText));
        Assert.Equal("已选 " + model.ExportFiles.Count + "/" + model.ExportFiles.Count + " 项", model.ExportSelectionCount);
    }

    [AvaloniaFact]
    public async Task SelectingOneFolderExportsOnlyThatFolderAndCategoriesNoLongerBlockIt()
    {
        using var model = Model();
        model.UseExportFileSelection = true;
        await model.ExportFilesPreparationTask;
        foreach (var file in model.ExportFiles) file.IsSelected = false;
        // logs 不属于任何导出类别；显式勾选后仍应导出。
        model.ExportFiles.Single(file => file.RelativePath == "logs").IsSelected = true;
        var output = Path.Combine(_root, "logs-only.mrpack");
        model.ExportPath = output;
        await model.ExportCommand.ExecuteAsync(null);
        Assert.Contains("已导出 1 个文件", model.Status);
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal(["overrides/logs/latest.log"], archive.Entries
            .Where(entry => entry.FullName.StartsWith("overrides/", StringComparison.Ordinal))
            .Select(entry => entry.FullName).ToArray());
    }

    [AvaloniaFact]
    public async Task SelectingAFolderIncludesItsWholeTreeAndExcludesOthers()
    {
        using var model = Model();
        model.UseExportFileSelection = true;
        await model.ExportFilesPreparationTask;
        foreach (var file in model.ExportFiles) file.IsSelected = false;
        model.ExportFiles.Single(file => file.RelativePath == "saves").IsSelected = true;
        var output = Path.Combine(_root, "saves-only.mrpack");
        model.ExportPath = output;
        await model.ExportCommand.ExecuteAsync(null);
        using var archive = ZipFile.OpenRead(output);
        Assert.Equal(["overrides/saves/World/level.dat"], archive.Entries
            .Where(entry => entry.FullName.StartsWith("overrides/", StringComparison.Ordinal))
            .Select(entry => entry.FullName).ToArray());
        using var index = JsonDocument.Parse(archive.GetEntry("modrinth.index.json")!.Open());
        Assert.Equal("fabric-loader", index.RootElement.GetProperty("dependencies")
            .EnumerateObject().Select(property => property.Name).Single(name => name != "minecraft"));
    }

    [AvaloniaFact]
    public async Task ExportWithoutAnySelectionFailsInsteadOfSilentlyWritingEverything()
    {
        using var model = Model();
        model.UseExportFileSelection = true;
        await model.ExportFilesPreparationTask;
        foreach (var file in model.ExportFiles) file.IsSelected = false;
        var output = Path.Combine(_root, "empty.mrpack");
        model.ExportPath = output;
        await model.ExportCommand.ExecuteAsync(null);
        Assert.Contains("没有勾选任何文件", model.Status);
        Assert.False(File.Exists(output));
    }

    [AvaloniaFact]
    public async Task SwitchingInstanceReplacesTheListingInsteadOfReusingTheOldOne()
    {
        using var model = Model();
        model.UseExportFileSelection = true;
        await model.ExportFilesPreparationTask;
        Assert.Contains(model.ExportFiles, file => file.RelativePath == "mods");
        var other = Path.Combine(_root, "versions", "Other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Other.json"),
            """{"id":"Other","clientVersion":"1.21.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}],"arguments":{"game":[],"jvm":[]}}""");
        model.SelectedInstance = new VersionItem { Id = "Other", IsInstalled = true };
        Assert.Empty(model.ExportFiles);
        await model.ExportFilesPreparationTask;
        Assert.Equal(["Other.json"], model.ExportFiles.Select(file => file.RelativePath).ToArray());
    }

    [AvaloniaFact]
    public async Task ExportCardShowsFoldedFolderRowsAndCollapsedMaintenance()
    {
        using var model = Model();
        var view = new InstanceView { DataContext = model };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            Pump();
            view.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>()
                .Single(list => list.Name == "SectionList")!.SelectedIndex = 4;
            Pump();
            view.GetVisualDescendants().OfType<Avalonia.Controls.Expander>()
                .Single(section => section.Name == "ExportContentsSection").IsExpanded = true;
            Pump();
            var toggle = view.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>()
                .SingleOrDefault(box => box.Name == "UseExportFileSelectionToggle");
            Assert.NotNull(toggle);
            toggle!.IsChecked = true;
            await model.ExportFilesPreparationTask;
            Pump();
            var choices = view.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>()
                .SingleOrDefault(item => item.Name == "ExportFileChoices");
            Assert.NotNull(choices);
            Assert.True(choices!.IsEffectivelyVisible);
            Assert.Equal(model.ExportFiles.Count, choices.ItemCount);
            Assert.Contains(model.ExportFiles, file => file.IsDirectory && file.IconKind == LocalResourceKind.Folder);
            var maintenance = view.GetVisualDescendants().OfType<Avalonia.Controls.Expander>()
                .SingleOrDefault(section => section.Name == "MaintenanceToolsSection");
            Assert.NotNull(maintenance);
            Assert.False(maintenance!.IsExpanded);
        }
        finally { window.Close(); }
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
