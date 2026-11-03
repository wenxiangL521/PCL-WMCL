using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Local;

namespace WMCL.App.Tests;

public sealed class CompactLocalResourceListTests
{
    [AvaloniaTheory]
    [InlineData("LocalModList", "light", 1.0, 860)]
    [InlineData("LocalModList", "dark", 1.5, 660)]
    [InlineData("ResourcePackList", "light", 1.0, 420)]
    [InlineData("ResourcePackList", "dark", 1.5, 660)]
    [InlineData("ShaderPackList", "light", 1.0, 420)]
    [InlineData("ShaderPackList", "dark", 1.5, 660)]
    [InlineData("WorldList", "light", 1.0, 420)]
    [InlineData("WorldList", "dark", 1.5, 660)]
    [InlineData("WorldBackups", "light", 1.0, 420)]
    [InlineData("WorldBackups", "dark", 1.5, 660)]
    public async Task FourHundredActualItemsHave32PixelHitRowsIconsTooltipsAndNativeSelection(
        string listName, string theme, double scale, double width)
    {
        ThemeService.Apply(theme);
        using var files = new LocalIconTestFiles();
        var path = files.Archive("local.jar", ("fabric.mod.json", "{\"icon\":\"pack.png\"}"u8.ToArray()), ("pack.png", LocalIconTestFiles.Png()));
        var model = new CompactLocalResourceModel(listName, path);
        UserControl view = listName == "LocalModList" ? new ModsView() : new InstanceContentView();
        view.DataContext = model;
        var window = new Window
        {
            Content = new LayoutTransformControl { Child = view, LayoutTransform = new ScaleTransform(scale, scale) },
            Width = width,
            Height = 1200 * scale
        };
        try
        {
            window.Show();
            await PumpAsync();
            var list = view.FindControl<ListBox>(listName)!;
            Assert.NotNull(list);
            Assert.Equal(400, list.Items.Count);
            list.BringIntoView();
            var outerScroll = view.FindControl<ScrollViewer>("ResourceBrowserScroll")!;
            var listPosition = list.TranslatePoint(default, outerScroll)!.Value;
            outerScroll.Offset = new Vector(0, Math.Max(0, outerScroll.Offset.Y + listPosition.Y));
            list.ScrollIntoView(list.Items[0]!);
            await PumpAsync();
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().Where(row => row.Bounds.Height > 0).ToArray();
            Assert.InRange(rows.Length, 4, 32);
            foreach (var row in rows)
            {
                Assert.InRange(row.Bounds.Height, 31.9, 33);
                Assert.Equal(0, row.Margin.Top + row.Margin.Bottom);
                var icon = Assert.Single(row.GetVisualDescendants().OfType<LocalResourceIcon>());
                Assert.Equal(24, icon.Bounds.Width);
                Assert.Equal(24, icon.Bounds.Height);
                await icon.PendingLoad;
                Assert.NotNull(icon.Source);
                var text = row.GetVisualDescendants().OfType<TextBlock>().First(block => block.FontSize == 13);
                Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
            }
            var first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var second = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
            Assert.InRange(second.Bounds.Y - first.Bounds.Y, 31.9, 33);
            Click(window, first, RawInputModifiers.None);
            Assert.Single(list.SelectedItems!);
            if (listName != "WorldBackups")
            {
                Click(window, Assert.IsType<ListBoxItem>(list.ContainerFromIndex(2)), RawInputModifiers.Control);
                Assert.Equal(2, list.SelectedItems!.Count);
                Assert.Contains(list.Items[0], list.SelectedItems.Cast<object>());
                Assert.Contains(list.Items[2], list.SelectedItems.Cast<object>());
                Click(window, Assert.IsType<ListBoxItem>(list.ContainerFromIndex(4)), RawInputModifiers.Shift);
                Assert.Contains(list.Items[2], list.SelectedItems.Cast<object>());
                Assert.Contains(list.Items[3], list.SelectedItems.Cast<object>());
                Assert.Contains(list.Items[4], list.SelectedItems.Cast<object>());
            }
            else Assert.Equal(SelectionMode.Single, list.SelectionMode);
            var iconInFirst = Assert.Single(first.GetVisualDescendants().OfType<LocalResourceIcon>());
            var root = Assert.IsType<Grid>(iconInFirst.Parent);
            Assert.NotNull(ToolTip.GetTip(root));
            ToolTip.SetIsOpen(root, true);
            await PumpAsync();
            var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(root));
            var details = string.Join("|", tip.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("Local resource with a deliberately long readable title", details);
            Assert.Contains(path, details);
            if (listName == "LocalModList")
            {
                Assert.Contains("可更新 → 2.0", details);
                Assert.Contains("test.mod", details);
                Assert.Contains("已启用", details);
            }
            else Assert.Contains("KB", details);
            ToolTip.SetIsOpen(root, false);
            var selected = list.GetVisualDescendants().OfType<ListBoxItem>().First(row => row.IsSelected);
            var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.NotNull(presenter.Background);
            SaveOptional(window, $"{theme}-{listName}-{scale:0.0}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CompactRowsAreLessThanHalfTheLegacy66PixelPitchInTheRealTheme()
    {
        using var files = new LocalIconTestFiles();
        var model = new CompactLocalResourceModel("LocalModList", Path.Combine(files.Root, "missing.jar"));
        var baseline = new ListBox
        {
            Classes = { "resourceRows" },
            ItemsSource = model.LocalModsView,
            Height = 280,
            ItemTemplate = new FuncDataTemplate<LocalMod>((item, _) => new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = item!.DisplayName },
                    new TextBlock { Text = item.DetailText, FontSize = 11 },
                    new TextBlock { Text = item.UpdateText, FontSize = 11 }
                }
            })
        };
        var window = new Window { Content = baseline, Width = 700, Height = 700 };
        try
        {
            window.Show();
            await PumpAsync();
            var oldFirst = Assert.IsType<ListBoxItem>(baseline.ContainerFromIndex(0));
            var oldSecond = Assert.IsType<ListBoxItem>(baseline.ContainerFromIndex(1));
            var oldPitch = oldSecond.Bounds.Y - oldFirst.Bounds.Y;
            Assert.True(oldPitch >= 64, $"Legacy row pitch was {oldPitch:0.##}.");
            var view = new ModsView { DataContext = model };
            window.Content = view;
            await PumpAsync();
            var list = view.FindControl<ListBox>("LocalModList")!;
            var first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var second = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
            var compactPitch = second.Bounds.Y - first.Bounds.Y;
            Assert.InRange(compactPitch, 31.9, 32.1);
            Assert.True(compactPitch <= oldPitch / 2);
            Assert.InRange(first.Bounds.Height, 31.9, 33);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control, RawInputModifiers modifiers)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
        input.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [point, modifiers]);
        input.GetMethod("MouseDown")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, modifiers]);
        input.GetMethod("MouseUp")!.Invoke(window.PlatformImpl, [point, MouseButton.Left, modifiers]);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task PumpAsync()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(25);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void SaveOptional(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_COMPACT_RESOURCE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}

public sealed class CompactLocalResourceModel
{
    public bool IsLoaderMode => false;
    public bool IsSearchMode => true;
    public bool IsInstanceManagement => true;
    public bool IsModDisabled => false;
    public bool IsDownloadPage => false;
    public bool IsEmbedded => true;
    public bool ShowDataPacks => false;
    public bool ShowResourcePacks { get; }
    public bool ShowShaders { get; }
    public bool ShowWorlds { get; }
    public LocalMod[] LocalModsView { get; }
    public InstanceContentItem[] ResourcePacks { get; }
    public InstanceContentItem[] ShaderPacks { get; }
    public InstanceContentItem[] Worlds { get; }
    public WorldBackupItem[] WorldBackups { get; }
    public LocalMod? SelectedLocalMod { get; set; }
    public InstanceContentItem? SelectedResourcePack { get; set; }
    public InstanceContentItem? SelectedShaderPack { get; set; }
    public InstanceContentItem? SelectedWorld { get; set; }
    public WorldBackupItem? SelectedWorldBackup { get; set; }

    public CompactLocalResourceModel(string list, string path)
    {
        const string title = "Local resource with a deliberately long readable title";
        ShowResourcePacks = list == "ResourcePackList";
        ShowShaders = list == "ShaderPackList";
        ShowWorlds = list is "WorldList" or "WorldBackups";
        LocalModsView = list == "LocalModList" ? Enumerable.Range(0, 400).Select(index =>
            new LocalMod(path, $"local-{index}.jar", $"local-{index}.jar", true, 2048, "2.0")
            { MetadataName = $"{title} {index}", ModId = "test.mod", Version = "1.0" }).ToArray() : [];
        ResourcePacks = Content("ResourcePackList", InstanceContentKind.ResourcePack);
        ShaderPacks = Content("ShaderPackList", InstanceContentKind.ShaderPack);
        Worlds = Content("WorldList", InstanceContentKind.World);
        WorldBackups = list == "WorldBackups" ? Enumerable.Range(0, 400).Select(index =>
            new WorldBackupItem($"{title} {index}", path, 2048, DateTime.Now)).ToArray() : [];

        InstanceContentItem[] Content(string target, InstanceContentKind kind) => list == target
            ? Enumerable.Range(0, 400).Select(index => new InstanceContentItem(kind, $"{title} {index}", path, false, 2048, DateTimeOffset.Now)).ToArray()
            : [];
    }
}
