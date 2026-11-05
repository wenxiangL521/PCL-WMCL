using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class CommunitySearchResponsiveTests
{
    [AvaloniaTheory]
    [InlineData("mods", 420, "light")]
    [InlineData("mods", 660, "light")]
    [InlineData("mods", 420, "dark")]
    [InlineData("mods", 660, "dark")]
    [InlineData("modpack", 420, "light")]
    [InlineData("modpack", 660, "light")]
    [InlineData("modpack", 420, "dark")]
    [InlineData("modpack", 660, "dark")]
    [InlineData("content-resource", 420, "light")]
    [InlineData("content-resource", 660, "light")]
    [InlineData("content-resource", 420, "dark")]
    [InlineData("content-resource", 660, "dark")]
    public async Task SearchPairsWrapWithoutSqueezingNameAndVersion(string page, double width, string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        using var model = Model();
        model.NavigateTo(page);
        UserControl view = page switch
        {
            "mods" => new ModsView { DataContext = model.Mods },
            "modpack" => new ModpackView { DataContext = model.Modpack },
            _ => new InstanceContentView { DataContext = model.Content }
        };
        var window = new Window { Content = view, Width = width, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var conditions = Assert.Single(view.GetVisualDescendants().OfType<WrapPanel>(), control => control.Name == "SearchConditions");
            var name = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "NameSearchTextBox");
            var version = Assert.Single(view.GetVisualDescendants().OfType<WMCL.App.Controls.VersionPicker>(), control => control.Name == "VersionSearchTextBox");
            var source = Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(), control => control.Name == "SourceSearchComboBox");
            Assert.True(name.Bounds.Width >= 170);
            Assert.True(version.Bounds.Width >= 170);
            Assert.True(source.Bounds.Width >= 170);
            var nameTop = name.TranslatePoint(default, conditions)!.Value.Y;
            var sourceTop = source.TranslatePoint(default, conditions)!.Value.Y;
            if (width < 500) Assert.True(sourceTop > nameTop + name.Bounds.Height);
            else Assert.Equal(nameTop, sourceTop, 2);
            foreach (var field in new Control[] { name, version, source })
                Assert.True(field.TranslatePoint(new Point(field.Bounds.Width, 0), view)!.Value.X <= view.Bounds.Width + .01);
            // 版本选择已改为不可输入的下拉：直接写绑定值即可
            model.Mods.SearchGameVersion = "1.21.11";
            model.Content.SearchGameVersion = "1.21.11";
            model.Modpack.SearchGameVersion = "1.21.11";
            await PumpAsync();
            var boundVersion = page switch { "mods" => model.Mods.SearchGameVersion, "modpack" => model.Modpack.SearchGameVersion, _ => model.Content.SearchGameVersion };
            Assert.Equal("1.21.11", boundVersion);
            Capture(window, $"{theme}-{page}-search-width-{width}");
        }
        finally { window.Close(); Restore(previous); }
    }

    [AvaloniaTheory]
    [InlineData("mods", "light")]
    [InlineData("mods", "dark")]
    [InlineData("modpack", "light")]
    [InlineData("modpack", "dark")]
    [InlineData("content-resource", "light")]
    [InlineData("content-resource", "dark")]
    public async Task ActualMainWindowAtOneAndAHalfScaleKeepsUsefulSearchInputWidths(string page, string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        using var model = Model(scale: 1.5);
        model.NavigateTo(page);
        var window = new MainWindow { DataContext = model, Width = 850, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var name = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), field => field.Name == "NameSearchTextBox" && field.IsEffectivelyVisible);
            var version = Assert.Single(window.GetVisualDescendants().OfType<WMCL.App.Controls.VersionPicker>(), field => field.Name == "VersionSearchTextBox" && field.IsEffectivelyVisible);
            Assert.True(name.Bounds.Width >= 170);
            Assert.True(version.Bounds.Width >= 170);
            foreach (var field in new Control[] { name, version })
            {
                var topLeft = field.TranslatePoint(default, window)!.Value;
                var bottomRight = field.TranslatePoint(new Point(field.Bounds.Width, field.Bounds.Height), window)!.Value;
                Assert.True(topLeft.X >= 0);
                Assert.True(bottomRight.X <= window.ClientSize.Width + 1);
                Assert.Equal(field.Bounds.Width * 1.5, bottomRight.X - topLeft.X, 4);
            }
            Capture(window, $"{theme}-{page}-search-scale-1.5");
        }
        finally { window.Close(); Restore(previous); }
    }

    private static MainWindowViewModel Model(double scale = 1) => new(new AppServices(new AppSettings
    {
        AccountType = "offline",
        FontScale = scale,
        GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "search-layout-" + Guid.NewGuid().ToString("N"))
    }, saveSettings: _ => { }));

    private static void Send(Window window, string method, params object?[] arguments) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(window.PlatformImpl, arguments);
    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
        }
    }
    private static void Restore(ThemeVariant? previous)
    {
        ThemeService.Apply(previous == ThemeVariant.Dark ? "dark" : "light");
        Application.Current!.RequestedThemeVariant = previous;
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
