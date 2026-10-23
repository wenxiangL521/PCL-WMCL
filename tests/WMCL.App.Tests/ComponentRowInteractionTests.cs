using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Fabric;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed class ComponentRowInteractionTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task HoverActionsUseOriginalDimensionsReserveTextAndNeverSelectTheVersion(string theme)
    {
        ThemeService.Apply(theme);
        var opened = new List<string>();
        var model = Model(opened: opened);
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 350 };
        window.Show();
        try
        {
            model.ForgeCard.ToggleCommand.Execute(null);
            await Pump(300);
            var row = Rows(card)[0];
            var actions = row.FindControl<ItemsControl>("Actions")!;
            var layout = row.FindControl<Grid>("TextLayout")!;
            Assert.Equal(0, actions.Opacity);
            Assert.False(actions.IsHitTestVisible);
            Assert.Equal(4, layout.ColumnDefinitions[3].Width.Value);
            var buttons = row.GetVisualDescendants().OfType<ComponentActionButton>().ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button => { Assert.Equal(25, button.Bounds.Width); Assert.Equal(25, button.Bounds.Height); });
            Assert.All(buttons, button => Assert.NotNull(button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data));
            Move(window, Center(window, row));
            await Pump(65);
            Assert.InRange(actions.Opacity, 0.01, 0.99);
            await Pump(130);
            Assert.Equal(1, actions.Opacity);
            Assert.Equal(55, layout.ColumnDefinitions[3].Width.Value);
            Assert.Equal(42, row.Bounds.Height);
            Assert.All(buttons, button => Assert.DoesNotContain(row.FindControl<Button>("SelectButton")!, button.GetVisualAncestors()));
            Click(window, buttons[1]);
            await Pump(30);
            Assert.EndsWith("/1.20.1-47.2.0/forge-1.20.1-47.2.0-changelog.txt", Assert.Single(opened));
            Assert.Equal("无", model.SelectedBaseLoader);
            Assert.True(model.ForgeCard.IsExpanded);
            Assert.Same(InstallComponentAction.SaveIcon, ((InstallComponentAction)buttons[0].DataContext!).IconFactory);
            Assert.Same(InstallComponentAction.InfoIcon, ((InstallComponentAction)buttons[1].DataContext!).IconFactory);
            Assert.Equal(1.05, ((InstallComponentAction)buttons[1].DataContext!).IconScale);
            Click(window, row.FindControl<Button>("SelectButton")!);
            await Pump(300);
            Assert.Equal("Forge", model.SelectedBaseLoader);
            Assert.False(model.ForgeCard.IsExpanded);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ActionKeyboardAndDragCancellationRemainSeparateFromRowSelection()
    {
        var opened = new List<string>();
        var model = Model(opened: opened);
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 350 };
        window.Show();
        try
        {
            model.ForgeCard.ToggleCommand.Execute(null);
            await Pump(300);
            var row = Rows(card)[0];
            var button = row.GetVisualDescendants().OfType<ComponentActionButton>().Last();
            Move(window, Center(window, row));
            await Pump(140);
            var point = Center(window, button);
            Input(window, "MouseDown", point, MouseButton.Left, RawInputModifiers.None);
            await Pump(70);
            var panel = button.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PressPanel");
            Assert.InRange(((ScaleTransform)panel.RenderTransform!).ScaleX, 0.8, 0.99);
            Assert.Equal(25, button.Bounds.Width);
            Move(window, new Point(1, 1));
            Input(window, "MouseUp", new Point(1, 1), MouseButton.Left, RawInputModifiers.None);
            await Pump(300);
            Assert.Empty(opened);
            Assert.Equal(1, ((ScaleTransform)panel.RenderTransform!).ScaleX);
            button.Focus(NavigationMethod.Tab);
            await Pump(140);
            Press(window, Key.Enter);
            await Pump(30);
            Assert.Single(opened);
            Assert.Equal("无", model.SelectedBaseLoader);
            Press(window, Key.Down);
            await Pump(50);
            Assert.True(Rows(card)[1].FindControl<Button>("SelectButton")!.IsFocused);
            Assert.Equal("无", model.SelectedBaseLoader);
            Move(window, Center(window, button));
            Click(window, button, MouseButton.Right);
            Click(window, button, MouseButton.Middle);
            Assert.Single(opened);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OriginalActionSetsAndSuggestedNamesAreComponentSpecific()
    {
        var names = new List<string>();
        var model = Model(pick: (name, _) => { names.Add(name); return Task.FromResult<string?>(null); });
        var forge = model.ForgeCard.Entries[0];
        Assert.Equal(["另存为", "更新日志"], forge.Actions.Select(action => action.Title));
        Assert.Equal(["另存为", "更新日志"], model.NeoForgeCard.Entries[0].Actions.Select(action => action.Title));
        Assert.Equal(["另存为", "更新日志"], model.OptiFineCard.Entries[0].Actions.Select(action => action.Title));
        Assert.Empty(model.FabricCard.Entries[0].Actions);
        await forge.Actions[0].Command.ExecuteAsync(null);
        Assert.Equal(["Forge-1.20.1-47.2.0.jar"], names);
        Assert.Equal("无", model.SelectedBaseLoader);
        var modernLite = Model("1.12.2").LiteLoaderCard.Entries[0];
        Assert.Equal(["保存安装器", "查看全部版本"], modernLite.Actions.Select(action => action.Title));
        var legacyLite = Model("1.7.10").LiteLoaderCard.Entries[0];
        Assert.Equal(["保存安装器"], legacyLite.Actions.Select(action => action.Title));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusyOrReplacedCatalogCannotExecuteOldRowActions(bool replace)
    {
        var opened = new List<string>();
        var model = Model(opened: opened);
        var action = model.ForgeCard.Entries[0].Actions[1];
        if (replace) model.Download.SelectedVersion = new VersionItem { Id = "1.21.1" };
        else model.IsCombinedBusy = true;
        Assert.False(action.Command.CanExecute(null));
        await action.Command.ExecuteAsync(null);
        Assert.Empty(opened);
    }

    [AvaloniaFact]
    public async Task PickerResultAfterVersionChangeCannotDownloadOrOverwriteNewPageStatus()
    {
        var picker = new TaskCompletionSource<string?>();
        var model = Model(pick: (_, _) => picker.Task);
        var pending = model.ForgeCard.Entries[0].Actions[0].Command.ExecuteAsync(null);
        model.Download.SelectedVersion = new VersionItem { Id = "1.21.1" };
        var status = model.CombinedStatus;
        picker.SetResult(Path.Combine(Path.GetTempPath(), "must-not-save.jar"));
        await pending;
        Assert.Equal(status, model.CombinedStatus);
    }

    [AvaloniaFact]
    public async Task SaveCompletionCannotReplaceAnotherOperationsFeedback()
    {
        var picker = new TaskCompletionSource<string?>();
        var model = Model(pick: (_, _) => picker.Task);
        var pending = model.ForgeCard.Entries[0].Actions[0].Command.ExecuteAsync(null);
        model.CombinedStatus = "另一操作已完成";
        picker.SetResult(null);
        await pending;
        Assert.Equal("另一操作已完成", model.CombinedStatus);
    }

    [AvaloniaFact]
    public async Task ASecondSaveDoesNotOpenAnotherPickerWhileTheFirstOneIsPending()
    {
        var picker = new TaskCompletionSource<string?>();
        var calls = 0;
        var model = Model(pick: (_, _) => { calls++; return picker.Task; });
        var first = model.ForgeCard.Entries[0].Actions[0].Command.ExecuteAsync(null);
        await model.OptiFineCard.Entries[0].Actions[0].Command.ExecuteAsync(null);
        Assert.Equal(1, calls);
        picker.SetResult(null);
        await first;
    }

    [AvaloniaFact]
    public async Task InstallerSaveUsesTheExistingTaskCenterAndItsCancelAction()
    {
        var picker = new TaskCompletionSource<string?>();
        var model = Model(pick: (_, _) => picker.Task);
        var services = new AppServices(new AppSettings
        { GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-row-tasks-" + Guid.NewGuid().ToString("N")) }, saveSettings: _ => { });
        var tasks = new TaskManagerViewModel(model.Download, model.Mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services), model);
        var pending = model.ForgeCard.Entries[0].Actions[0].Command.ExecuteAsync(null);
        Assert.True(model.IsSavingComponentInstaller);
        var task = Assert.Single(tasks.Tasks);
        Assert.Contains("Forge-1.20.1-47.2.0.jar", task.Title);
        Assert.True(task.HasCancel);
        task.Cancel!();
        picker.SetResult(null);
        await pending;
        Assert.False(model.IsSavingComponentInstaller);
        Assert.Empty(tasks.Tasks);
        Assert.Equal("已取消保存安装器", Assert.Single(tasks.History).Detail);
        Assert.Equal("无", model.SelectedBaseLoader);
    }

    [AvaloniaFact]
    public async Task ReducedMotionAndReattachmentSnapActionVisibilityAndPadding()
    {
        var previous = Environment.GetEnvironmentVariable("WMCL_REDUCE_MOTION");
        Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", "1");
        var model = Model();
        var card = new InstallComponentCard { DataContext = model.ForgeCard };
        var window = new Window { Content = card, Width = 600, Height = 350 };
        window.Show();
        try
        {
            model.ForgeCard.ToggleCommand.Execute(null);
            await Pump(30);
            var row = Rows(card)[0];
            Move(window, Center(window, row));
            Assert.Equal(1, row.FindControl<ItemsControl>("Actions")!.Opacity);
            Assert.Equal(55, row.FindControl<Grid>("TextLayout")!.ColumnDefinitions[3].Width.Value);
            window.Content = null;
            Assert.False(row.FindControl<ItemsControl>("Actions")!.IsHitTestVisible);
            window.Content = card;
            await Pump(30);
            Move(window, new Point(1, 1));
            Assert.Equal(0, row.FindControl<ItemsControl>("Actions")!.Opacity);
            Assert.Equal(4, row.FindControl<Grid>("TextLayout")!.ColumnDefinitions[3].Width.Value);
        }
        finally { window.Close(); Environment.SetEnvironmentVariable("WMCL_REDUCE_MOTION", previous); }
    }

    private static InstallViewModel Model(string minecraft = "1.20.1", List<string>? opened = null,
        Func<string, CancellationToken, Task<string?>>? pick = null)
    {
        var services = new AppServices(new AppSettings
        { GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-row-ui-" + Guid.NewGuid().ToString("N")) }, saveSettings: _ => { });
        var download = new DownloadViewModel(services) { SelectedVersion = new VersionItem { Id = minecraft } };
        var mods = new ModsViewModel(services,
            (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true)]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>(["47.2.0", "47.1.0"]),
            game => Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine(game)]),
            getLiteLoaderVersions: game => Task.FromResult<IReadOnlyList<LiteLoaderVersion>>([new(game, game + "-SNAPSHOT",
                "com.mumfrey.liteloader.launch.LiteLoaderTweaker", true, null, null, JsonSerializer.SerializeToElement(Array.Empty<object>()))]));
        var actions = new ComponentRowActionService(services.Optifine, pick: pick ?? ((_, _) => Task.FromResult<string?>(null)),
            open: url => opened?.Add(url));
        var model = new InstallViewModel(download, mods, services, null,
            InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(), rowActions: actions);
        model.ShowSelectedVersion();
        return model;
    }

    private static InstallComponentRow[] Rows(Control card) => card.GetVisualDescendants().OfType<InstallComponentRow>().ToArray();
    private static Point Center(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Move(Window window, Point point) => Input(window, "MouseMove", point, RawInputModifiers.None);
    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        var point = Center(window, control);
        Move(window, point);
        Input(window, "MouseDown", point, button, RawInputModifiers.None);
        Input(window, "MouseUp", point, button, RawInputModifiers.None);
    }
    private static void Press(Window window, Key key)
    {
        Input(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        Input(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }
    private static void Input(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);
    private static async Task Pump(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        } while (DateTime.UtcNow < deadline);
    }
}
