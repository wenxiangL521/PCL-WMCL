using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class ResponsiveFormAndLinkStyleTests
{
    [AvaloniaTheory]
    [InlineData("light", 300, true)]
    [InlineData("dark", 300, true)]
    [InlineData("light", 600, false)]
    [InlineData("dark", 600, false)]
    public void FormRowsGiveNarrowInputsTheirFullWidthAndKeepWideRowsHorizontal(string theme, double width, bool stacked)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var text = new TextBox { Watermark = "可输入完整路径或快捷键", HorizontalAlignment = HorizontalAlignment.Stretch };
        var row = new WmclFormRow { Label = "快捷键与路径", Content = text, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        var window = new Window { Width = 850, Height = 500, Content = new Border { Padding = new Thickness(15), Child = row } };
        try
        {
            window.Show();
            Pump();
            Assert.Equal(stacked, row.IsStacked);
            var label = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), part => part.Name == "PART_FormLabel");
            var presenter = Assert.Single(row.GetVisualDescendants().OfType<ContentPresenter>(), part => part.Name == "PART_FormContent");
            Assert.Equal(stacked ? 1 : 0, Grid.GetRow(presenter));
            Assert.Equal(stacked ? 0 : 1, Grid.GetColumn(presenter));
            Assert.True(text.Bounds.Width >= (stacked ? width - 1 : width - row.LabelWidth - 13));
            Assert.True(label.Bounds.Right <= row.Bounds.Width);
            Assert.True(text.TranslatePoint(new Point(text.Bounds.Width, 0), row)!.Value.X <= row.Bounds.Width + .01);
            row.Width = stacked ? 600 : 300;
            Pump();
            Assert.Equal(!stacked, row.IsStacked);
        }
        finally { window.Close(); Restore(previous); }
    }

    [AvaloniaFact]
    public void UnconstrainedHorizontalHostsDoNotUsePreviousBoundsToForceAStackedLayout()
    {
        var row = new WmclFormRow { Label = "自然宽度", Content = new TextBox { Width = 280 } };
        row.Measure(new Size(300, double.PositiveInfinity));
        Assert.True(row.IsStacked);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.False(row.IsStacked);
        row.Measure(new Size(300, double.PositiveInfinity));
        Assert.True(row.IsStacked);
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void SingleLineInputCanScrollToTheCaretAndWatermarkFitsInsideTheBorder(string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var path = "/home/player/很长的路径/" + new string('a', 180) + "/minecraft";
        var text = new TextBox { Width = 260, Height = 30, Watermark = "快捷键 / 路径 / 输入条件", Text = path };
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Hidden);
        var window = new Window { Content = text, Width = 850, Height = 500 };
        try
        {
            window.Show();
            Pump();
            text.Focus();
            PressKey(window, Key.End);
            Pump();
            var scroll = Assert.Single(text.GetVisualDescendants().OfType<ScrollViewer>(), item => item.Name == "PART_ScrollViewer");
            Assert.Equal(path.Length, text.CaretIndex);
            Assert.True(scroll.Offset.X > 0);
            Input(window, "TextInput", "尾");
            Pump();
            Assert.EndsWith("尾", text.Text);
            PressKey(window, Key.Home);
            Pump();
            Assert.Equal(0, text.CaretIndex);
            Assert.InRange(scroll.Offset.X, 0, 1);
            text.Text = "";
            Pump();
            var watermark = Assert.Single(text.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "PART_Watermark");
            Assert.True(watermark.IsVisible);
            Assert.True(watermark.Bounds.Height <= text.Bounds.Height - 2);
            Assert.Equal(Brush(text, "WmclText"), Assert.IsAssignableFrom<ISolidColorBrush>(text.CaretBrush).Color);
            Capture(window, theme + "-single-line-input");
        }
        finally { window.Close(); Restore(previous); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ComboPopupUsesTheSharedCardPaletteAndDoesNotGrowToTheLongestPath(string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var combo = new ComboBox { Width = 240, ItemsSource = new[] { "短选项", "/home/player/" + new string('a', 120) }, SelectedIndex = 0 };
        var window = new Window { Width = 850, Height = 500, Content = combo };
        try
        {
            window.Show();
            Pump();
            combo.Focus();
            combo.IsDropDownOpen = true;
            Pump();
            var popup = Assert.Single(combo.GetVisualDescendants().OfType<Popup>(), part => part.Name == "PART_Popup");
            var border = Assert.IsType<Border>(popup.Child);
            Assert.Equal("PopupBorder", border.Name);
            Assert.Equal(Brush(combo, "WmclCard"), Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            Assert.InRange(border.MaxWidth, 1, combo.Bounds.Width + .01);
            PressKey(window, Key.Down);
            PressKey(window, Key.Enter);
            Pump();
            Assert.Equal(1, combo.SelectedIndex);
        }
        finally { combo.IsDropDownOpen = false; window.Close(); Restore(previous); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DisabledComboTemplateUsesTheSharedInputPaletteRatherThanAFluentGrayPlate(string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var combo = new ComboBox { Width = 240, ItemsSource = new[] { "Java 25", "Java 11" }, SelectedIndex = 0, IsEnabled = false };
        var window = new Window { Width = 850, Height = 500, Content = combo };
        try
        {
            window.Show();
            Pump();
            var background = Assert.Single(combo.GetVisualDescendants().OfType<Border>(), part => part.Name == "Background" && part.TemplatedParent == combo);
            Assert.Equal(Brush(combo, "WmclInput"), Assert.IsAssignableFrom<ISolidColorBrush>(background.Background).Color);
            Assert.Equal(Brush(combo, "WmclBorder"), Assert.IsAssignableFrom<ISolidColorBrush>(background.BorderBrush).Color);
            var content = Assert.Single(combo.GetVisualDescendants().OfType<ContentControl>(), part => part.Name == "ContentPresenter" && part.TemplatedParent == combo);
            Assert.Equal(Brush(combo, "WmclTextDisabled"), Assert.IsAssignableFrom<ISolidColorBrush>(content.Foreground).Color);
        }
        finally { window.Close(); Restore(previous); }
    }

    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 850)]
    [InlineData("light", 620)]
    [InlineData("dark", 620)]
    public void LinkAdvancedSettingsReuseTheSharedCardExpanderAndWrapActions(string theme, double width)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        using var connection = new LinkViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        var view = new LinkSettingsView { DataContext = new LinkSettingsViewModel(connection) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        var host = new Border { Padding = new Thickness(20), Child = view };
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        var window = new Window { Content = grid, Width = width, Height = 500 };
        try
        {
            window.Show();
            Pump();
            var section = view.FindControl<Expander>("AdvancedSettingsSection")!;
            Assert.Contains("cardSection", section.Classes);
            var header = Assert.Single(section.GetVisualDescendants().OfType<ToggleButton>(), control => control.Name == "TaskSectionHeader");
            header.Focus();
            PressKey(window, Key.Space);
            Pump();
            Assert.True(section.IsExpanded);
            var surface = Assert.Single(section.GetVisualDescendants().OfType<Border>(), control => control.Name == "TaskSectionSurface");
            Assert.Equal(Brush(section, "WmclCard"), Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
            var body = Assert.Single(section.GetVisualDescendants().OfType<ContentPresenter>(), control => control.Name == "PART_ContentPresenter" && control.TemplatedParent == section);
            Assert.True(body.IsVisible);
            Assert.True(body.Background is null || body.Background is ISolidColorBrush { Color.A: 0 });
            var peer = view.FindControl<TextBox>("PeerTextBox")!;
            Assert.True(peer.AcceptsReturn);
            Assert.Equal(TextWrapping.Wrap, peer.TextWrapping);
            // 只看页面里手写的按钮；ComboBox 等控件模板内部的按钮不属于样式约定。
            foreach (var button in body.GetVisualDescendants().OfType<Button>()
                         .Where(control => control is not ToggleButton && control.TemplatedParent is null && control.IsEffectivelyVisible))
            {
                Assert.Contains("taskAction", button.Classes);
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), view)!.Value.X;
                Assert.True(right <= view.Bounds.Width + .01);
            }
            connection.SelectedBackend = "陶瓦（HMCL）";
            connection.TerracottaExecutablePath = "/home/player/" + new string('a', 120) + "/terracotta";
            Pump();
            var path = view.FindControl<TextBox>("TerracottaPathTextBox")!;
            Assert.True(path.IsEffectivelyVisible);
            Assert.Equal(connection.TerracottaExecutablePath, ToolTip.GetTip(path));
            Assert.True(path.TranslatePoint(new Point(path.Bounds.Width, 0), view)!.Value.X <= view.Bounds.Width + .01);
            Capture(window, theme + "-link-advanced-" + width);
            header.Focus();
            PressKey(window, Key.Space);
            Assert.False(section.IsExpanded);
        }
        finally { window.Close(); Restore(previous); }
    }

    [Fact]
    public void LinkAndFormSourcesOptIntoSharedThemesRatherThanDuplicatingControlTemplates()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WMCL.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var link = XDocument.Load(Path.Combine(root!.FullName, "src/WMCL.App/Features/Link/Views/LinkSettingsView.axaml"));
        var expander = Assert.Single(link.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.Equal("cardSection", expander.Attribute("Classes")?.Value);
        Assert.DoesNotContain(link.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        var theme = File.ReadAllText(Path.Combine(root.FullName, "src/WMCL.UI/Styles/WmclTaskControls.axaml"));
        Assert.Contains("Expander.taskSection, Expander.cardSection", theme);
        Assert.Contains("WmclTaskSectionTheme", theme);
        var instance = XDocument.Load(Path.Combine(root.FullName, "src/WMCL.App/Features/Instances/Views/InstanceView.axaml"));
        Assert.All(instance.Descendants().Where(element => element.Name.LocalName == "Expander"), element =>
            Assert.Equal("cardSection", element.Attribute("Classes")?.Value));
    }

    private static Color Brush(Control control, string key) => Assert.IsAssignableFrom<ISolidColorBrush>(control.FindResource(control.ActualThemeVariant, key)).Color;
    private static void PressKey(Window window, Key key)
    {
        Input(window, "KeyPress", key, RawInputModifiers.None, PhysicalKey.None, null);
        Input(window, "KeyRelease", key, RawInputModifiers.None, PhysicalKey.None, null);
    }
    private static void Input(Window window, string method, params object?[] args) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(window.PlatformImpl, args);
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Restore(ThemeVariant? previous)
    {
        ThemeService.Apply(previous == ThemeVariant.Dark ? "dark" : "light");
        Application.Current!.RequestedThemeVariant = previous;
    }
    private static void Capture(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        Pump();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(path, name + ".png"));
    }
}
