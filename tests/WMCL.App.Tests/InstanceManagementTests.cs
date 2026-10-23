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
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class InstanceManagementTests
{
    [AvaloniaTheory]
    [InlineData("rename", false)]
    [InlineData("clone", false)]
    [InlineData("delete", false)]
    [InlineData("rename", true)]
    [InlineData("clone", true)]
    [InlineData("delete", true)]
    public async Task ManagementKeepsTheTargetShownByItsDialog(string action, bool changeRoot)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-manage-ui-" + Guid.NewGuid().ToString("N"));
        var nextRoot = Path.Combine(root, "next");
        foreach (var (game, id) in new[] { (root, "Source"), (nextRoot, "Other") })
        {
            var directory = Path.Combine(game, "versions", id);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"),
                $$"""{"id":"{{id}}","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        }
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        using var viewModel = new InstanceViewModel(services, prompt: (_, _, _) => answer.Task,
            confirm: (_, _) => confirmation.Task);
        viewModel.SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true };
        var command = action switch
        {
            "clone" => viewModel.CloneInstanceCommand,
            "delete" => viewModel.DeleteInstanceCommand,
            _ => viewModel.RenameInstanceCommand
        };
        Task? operation = null;
        try
        {
            operation = command.ExecuteAsync(null);
            Assert.True(viewModel.IsManagingInstance);
            Assert.False(viewModel.CanRepairManifest);
            Assert.False(viewModel.CloneInstanceCommand.CanExecute(null));
            Assert.False(viewModel.RenameInstanceCommand.CanExecute(null));
            Assert.False(viewModel.DeleteInstanceCommand.CanExecute(null));
            if (changeRoot)
            {
                services.SetGameDirectory(nextRoot);
                viewModel.SelectedInstance = new VersionItem { Id = "Other", IsInstalled = true };
                viewModel.Status = "other instance selected";
            }
            answer.SetResult("Target");
            confirmation.SetResult(true);
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(action == "clone", Directory.Exists(Path.Combine(root, "versions", "Source")));
            Assert.Equal(action != "delete", File.Exists(Path.Combine(root, "versions", "Target", "Target.json")));
            Assert.True(File.Exists(Path.Combine(nextRoot, "versions", "Other", "Other.json")));
            Assert.False(Directory.Exists(Path.Combine(nextRoot, "versions", "Target")));
            Assert.False(viewModel.IsManagingInstance);
            Assert.True(viewModel.CanRepairManifest);
            if (changeRoot)
            {
                Assert.Equal("Other", viewModel.SelectedInstance!.Id);
                Assert.Equal("other instance selected", viewModel.Status);
            }
            else if (action != "delete") Assert.Equal("Target", viewModel.SelectedInstance!.Id);
        }
        finally
        {
            answer.TrySetResult(null);
            confirmation.TrySetResult(false);
            if (operation is not null) await operation;
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public void RepairAndUpdatePreventInstanceManagement()
    {
        using var viewModel = new InstanceViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        Assert.False(viewModel.RenameInstanceCommand.CanExecute(null));
        viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        Assert.True(viewModel.RenameInstanceCommand.CanExecute(null));
        foreach (var repair in new[] { true, false })
        {
            viewModel.IsRepairingManifest = repair;
            viewModel.IsUpdatingModpack = !repair;
            Assert.False(viewModel.RenameInstanceCommand.CanExecute(null));
            Assert.False(viewModel.CloneInstanceCommand.CanExecute(null));
            Assert.False(viewModel.DeleteInstanceCommand.CanExecute(null));
        }
        viewModel.IsUpdatingModpack = false;
        Assert.True(viewModel.RenameInstanceCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task SidebarResetActionRunsWithoutLeavingOverview()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-sidebar-action-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        InstanceSettingsStore.Save(directory, new InstanceSettings { VersionCardType = 4 });
        try
        {
            using var viewModel = new InstanceViewModel(
                new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { }));
            await viewModel.LoadAsync();
            Assert.Equal("不常用版本", viewModel.InstanceVersionCardType);
            var view = new InstanceView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1000, Height = 800 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var reset = view.FindControl<Button>("SettingsResetButton")!;
                reset.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var point = reset.TranslatePoint(
                    new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2), window)!.Value;
                foreach (var (method, args) in new (string, object?[])[]
                         {
                             ("MouseMove", [point, RawInputModifiers.None]),
                             ("MouseDown", [point, MouseButton.Left, RawInputModifiers.None]),
                             ("MouseUp", [point, MouseButton.Left, RawInputModifiers.None])
                         })
                {
                    typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
                        .GetMethod(method)!.Invoke(window.PlatformImpl, args);
                }
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("自动", viewModel.InstanceVersionCardType);
                Assert.Equal(0, view.FindControl<ListBox>("SectionList")!.SelectedIndex);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void InstanceSidebarShowsOneCardAtATime()
    {
        using var viewModel = new InstanceViewModel(
            new AppServices(new AppSettings(), saveSettings: _ => { }));
        viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        var view = new InstanceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var overview = view.FindControl<Border>("OverviewCard")!;
            var server = view.FindControl<Border>("ServerCard")!;
            var export = view.FindControl<Border>("ExportCard")!;
            var settings = view.FindControl<Border>("SettingsCard")!;
            var sections = view.FindControl<ListBox>("SectionList")!;
            Assert.Equal(5, sections.ItemCount);
            Assert.True(view.FindControl<Button>("MoreContentButton")!.IsEffectivelyVisible);
            Assert.True(overview.IsVisible);
            Assert.False(server.IsVisible);
            Assert.False(export.IsVisible);
            Assert.False(settings.IsVisible);

            sections.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.False(overview.IsVisible);
            Assert.False(server.IsVisible);
            Assert.True(settings.IsVisible);

            var moreServer = view.FindControl<Button>("MoreServerButton")!;
            moreServer.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.IsVisible);
            Assert.True(server.IsVisible);

            var moreScreenshots = view.FindControl<Button>("MoreScreenshotsButton")!;
            moreScreenshots.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(server.IsVisible);
            Assert.True(view.FindControl<ScreenshotView>("ScreenshotCard")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OverviewExposesPclVersionCardTypeOptions()
    {
        using var viewModel = new InstanceViewModel(
            new AppServices(new AppSettings(), saveSettings: _ => { }));
        viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        var view = new InstanceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var combo = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(box => ReferenceEquals(box.ItemsSource, viewModel.VersionCardTypeOptions));
            Assert.Equal(6, viewModel.VersionCardTypeOptions.Count);
            Assert.Equal("自动", combo.SelectedItem);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "版本分类" && text.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ManagementDisablesTheExistingPageAndCancelRestoresIt()
    {
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new InstanceViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }),
            prompt: (_, _, _) => answer.Task);
        viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        var view = new InstanceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        var operation = viewModel.RenameInstanceCommand.ExecuteAsync(null);
        try
        {
            var grid = view.GetVisualDescendants().OfType<Grid>().First();
            Assert.False(grid.IsEffectivelyEnabled);
            Assert.True(viewModel.IsManagingInstance);
            answer.SetResult(null);
            await operation;
            Assert.True(grid.IsEffectivelyEnabled);
            Assert.False(viewModel.IsManagingInstance);
        }
        finally
        {
            answer.TrySetResult(null);
            await operation;
            window.Close();
        }
    }
}
