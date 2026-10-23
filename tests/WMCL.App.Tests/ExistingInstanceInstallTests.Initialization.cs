using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Controls;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [AvaloniaFact]
    public async Task InitializationFailureStaysInTheModificationPageAndRetriesTheOriginalTarget()
    {
        CreateInstance();
        var profile = Path.Combine(DirectoryFor(), "Custom.json");
        var original = File.ReadAllText(profile);
        File.WriteAllText(profile, "{");
        var services = Services();
        var popupCount = 0;
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            showInstanceError: (_, _) => { popupCount++; return Task.CompletedTask; });
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.HasInstanceInitializationError);
        Assert.True(model.IsInstanceInitializationVisible);
        Assert.False(model.IsChoosingVersion);
        Assert.False(model.IsPreparingInstance);
        Assert.False(model.CanEditInstall);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.ShowInstallAction);
        Assert.True(model.RetryInstanceInitializationCommand.CanExecute(null));
        Assert.Equal(0, popupCount);
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Border>("InstanceInitializationPanel")!.IsVisible);
            Assert.False(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            Assert.False(view.FindControl<ScrollViewer>("CatalogScroll")!.IsVisible);
            Assert.False(view.FindControl<DownloadActionButton>("StartDownloadButton")!.Show);
            var indicator = view.FindControl<Border>("InstanceInitializationPanel")!.Child as PclLoadingIndicator;
            Assert.NotNull(indicator);
            Assert.Equal(model.InstanceInitializationError, indicator.Error);
            var hiddenCard = view.FindControl<InstallComponentCard>("OptiFabricComponentCard")!;
            Assert.False(hiddenCard.IsVisible);
            hiddenCard.Opacity = 0;
            await model.RetryInstanceInitializationCommand.ExecuteAsync(null);
            Assert.True(model.HasInstanceInitializationError);
            File.WriteAllText(profile, original);
            await model.RetryInstanceInitializationCommand.ExecuteAsync(null);
            await Task.Delay(1250);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsExistingInstance);
            Assert.Equal("Custom", model.TargetInstanceId);
            Assert.False(model.IsInstanceInitializationVisible);
            Assert.False(model.HasInstanceInitializationError);
            Assert.False(model.RetryInstanceInitializationCommand.CanExecute(null));
            Assert.True(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            Assert.False(view.FindControl<Border>("InstanceInitializationPanel")!.IsVisible);
            Assert.Equal(1, hiddenCard.Opacity);
            Assert.Equal(0, popupCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SwitchingRootClearsTheFailureAndPreventsRetryingTheOldDirectory()
    {
        var services = Services();
        var model = ViewModel(services);
        await model.OpenInstanceAsync(_root, "Missing");
        Assert.True(model.HasInstanceInitializationError);
        services.SetGameDirectory(Path.Combine(_root, "other"));
        Assert.False(model.HasInstanceInitializationError);
        Assert.False(model.IsInstanceInitializationVisible);
        Assert.True(model.IsChoosingVersion);
        Assert.False(model.RetryInstanceInitializationCommand.CanExecute(null));
        await model.RetryInstanceInitializationCommand.ExecuteAsync(null);
        Assert.False(model.IsExistingInstance);
        Assert.False(model.HasInstanceInitializationError);
    }

    [AvaloniaFact]
    public async Task ANewTargetReplacesTheFailedTargetForSubsequentRetries()
    {
        CreateInstance();
        var model = ViewModel(Services());
        await model.OpenInstanceAsync(_root, "Missing");
        Assert.True(model.HasInstanceInitializationError);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.HasInstanceInitializationError);
        Assert.Equal("Custom", model.TargetInstanceId);
        Assert.False(model.RetryInstanceInitializationCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData("complete")]
    [InlineData("root")]
    [InlineData("detach")]
    public async Task LoadingWaitsBeforeShowingAndLateResultsCannotReactivateAnAbandonedTarget(string action)
    {
        CreateInstance();
        var services = Services();
        var gate = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            null, new OptiFabricSelectionViewModel((_, _) => gate.Task), InstallTestData.CreateApiSelection());
        var opening = model.OpenInstanceAsync(_root, "Custom");
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            Assert.True(model.IsPreparingInstance);
            Assert.False(model.RetryInstanceInitializationCommand.CanExecute(null));
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Border>("InstanceInitializationPanel")!.IsVisible);
            await Task.Delay(420);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Border>("InstanceInitializationPanel")!.IsVisible);
            Assert.False(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            Assert.False(view.FindControl<DownloadActionButton>("StartDownloadButton")!.Show);
            if (action == "root") services.SetGameDirectory(Path.Combine(_root, "other"));
            else if (action == "detach") window.Content = null;
            gate.SetResult([InstallTestData.OptiFabric()]);
            await opening;
            await Task.Delay(1250);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsPreparingInstance);
            Assert.False(model.HasInstanceInitializationError);
            Assert.False(model.IsInstanceInitializationVisible);
            Assert.Equal(action == "complete", model.IsExistingInstance);
            if (action == "complete")
                Assert.True(view.FindControl<ScrollViewer>("PageScroll")!.IsVisible);
            else
            {
                Assert.Null(model.Download.SelectedVersion);
                Assert.False(model.CanApplyInstall);
            }
        }
        finally
        {
            gate.TrySetResult([]);
            window.Close();
        }
    }
}
