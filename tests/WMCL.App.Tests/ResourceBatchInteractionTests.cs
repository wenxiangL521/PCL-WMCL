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

public sealed class ResourceBatchInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light", 850, false)]
    [InlineData("light", 850, true)]
    [InlineData("dark", 520, false)]
    [InlineData("dark", 520, true)]
    public async Task MouseCanReviewMixedResourcesAndInstallOrCancelTheFrozenTarget(string theme, int width, bool cancel)
    {
        ThemeService.Apply(theme);
        using var fixture = new ResourceBatchFixture { DelayDownloads = true };
        foreach (var resource in fixture.MixedResources) fixture.Services.ResourceFavorites.SetFavorite("default", resource, true);
        using var main = new MainWindowViewModel(fixture.Services);
        var view = new ResourceFavoritesView { DataContext = main.Favorites };
        var window = new Window { Content = view, Width = width, Height = 500 };
        window.Show();
        try
        {
            await PumpAsync();
            Click(window, Button(view, "全选"));
            await PumpAsync();
            Click(window, Button(view, "批量安装"));
            var batch = main.Favorites.Batch!;
            await batch.OpenCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.True(main.Favorites.IsBatchOpen);
            var batchView = view.GetVisualDescendants().OfType<ResourceFavoriteBatchView>().Single();
            Assert.True(batchView.IsEffectivelyVisible);
            var instanceBox = batchView.FindControl<ComboBox>("BatchInstanceBox")!;
            batch.SelectedInstance = "first";
            await PumpAsync();
            instanceBox.Focus();
            Key(window, batch.Instances.IndexOf("second") > instanceBox.SelectedIndex ? Avalonia.Input.Key.Down : Avalonia.Input.Key.Up);
            await PumpAsync();
            Assert.Equal("second", batch.SelectedInstance);
            var saveBox = batchView.FindControl<ComboBox>("BatchSaveBox")!;
            saveBox.Focus();
            Key(window, Avalonia.Input.Key.Down);
            await PumpAsync();
            Assert.Equal("Save Two", batch.SelectedSave);
            Assert.False(Button(view, "开始安装").IsEffectivelyEnabled);
            Click(window, Button(view, "检查安装"));
            await batch.CheckCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.True(batch.CanInstall);
            Assert.Equal(10, batch.Rows.Count);
            Assert.Contains(batch.Rows, row => row.State == "跳过" && row.Detail.Contains("独立实例"));
            Assert.Empty(fixture.BinaryRequests);
            Click(window, Button(view, "开始安装"));
            await fixture.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.True(main.IsTaskActive);
            Assert.True(Assert.Single(main.Tasks.Tasks, task => task.Title == "批量安装收藏").HasCancel);
            Assert.False(instanceBox.IsEffectivelyEnabled);
            Assert.False(Button(view, "返回收藏").IsEffectivelyEnabled);
            if (cancel) Click(window, Button(view, "取消"));
            else
            {
                batch.SelectedInstance = "first";
                fixture.ReleaseDownloads.TrySetResult();
            }
            await batch.InstallCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            Assert.False(batch.IsInstalling);
            Assert.DoesNotContain(main.Tasks.Tasks, task => task.Title == "批量安装收藏");
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "first", "mods")));
            if (cancel)
            {
                Assert.Contains("已取消 9", batch.Status);
                Assert.False(Directory.Exists(Path.Combine(fixture.Root, "versions", "second", "mods")));
            }
            else
            {
                Assert.Contains("已安装 9", batch.Status);
                Assert.True(File.Exists(Path.Combine(fixture.Root, "versions", "second", "mods", "main-v.jar")));
                Assert.True(File.Exists(Path.Combine(fixture.Root, "versions", "second", "saves", "Save Two", "datapacks", "data-v.zip")));
                Assert.False(batch.CanInstall);
            }
            if (cancel) Key(window, Avalonia.Input.Key.Escape);
            else Click(window, Button(view, "返回收藏"));
            await PumpAsync();
            Assert.False(main.Favorites.IsBatchOpen);
            Assert.True(view.FindControl<Button>("BatchInstallButton")!.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("selection")]
    [InlineData("instance")]
    [InlineData("save")]
    public async Task DelayedPreviewCannotEnableAnOldSelectionOrTarget(string change)
    {
        using var fixture = new ResourceBatchFixture { DelayMetadata = true };
        using var main = new MainWindowViewModel(fixture.Services);
        fixture.Services.ResourceFavorites.SetFavorite("default", fixture.Resource("data", "datapack"), true);
        main.Favorites.SelectAllCommand.Execute(null);
        var batch = main.Favorites.Batch!;
        await batch.OpenCommand.ExecuteAsync(null);
        var checking = batch.CheckCommand.ExecuteAsync(null);
        await fixture.MetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "selection") main.Favorites.ClearSelectionCommand.Execute(null);
        else if (change == "instance") batch.SelectedInstance = batch.SelectedInstance == "first" ? "second" : "first";
        else batch.SelectedSave = "Save Two";
        fixture.ReleaseMetadata.TrySetResult();
        await checking.WaitAsync(TimeSpan.FromSeconds(5));
        await PumpAsync();
        Assert.False(batch.IsChecking);
        Assert.False(batch.CanInstall);
        Assert.Empty(batch.Rows);
        Assert.Empty(fixture.BinaryRequests);
    }

    [AvaloniaFact]
    public async Task EmptyInstanceListHasAnExplanationAndCannotPlanOrInstall()
    {
        using var fixture = new ResourceBatchFixture();
        Directory.Delete(Path.Combine(fixture.Root, "versions"), recursive: true);
        var batch = new ResourceFavoriteBatchViewModel(fixture.Services);
        batch.SetSelection([fixture.Resource("main")]);
        await batch.OpenCommand.ExecuteAsync(null);
        Assert.Empty(batch.Instances);
        Assert.Contains("没有可用", batch.Status);
        Assert.False(batch.CanCheck);
        Assert.False(batch.CanInstall);
        Assert.Empty(fixture.Requests);
    }

    private static Button Button(Control root, string content) => root.GetVisualDescendants().OfType<Button>()
        .Single(button => button.IsEffectivelyVisible && button.Content as string == content);

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width);
        Assert.InRange(point.Y, 0, window.Bounds.Height);
        Input(window, "MouseMove", point, RawInputModifiers.None);
        Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
        Input(window, "MouseUp", point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Key(Window window, Key key)
    {
        Input(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        Input(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Input(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }
}
