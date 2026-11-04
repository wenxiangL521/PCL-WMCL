using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class PersonalizationLayoutTests
{
    [AvaloniaFact]
    public void AppearanceSlidersLiveInACollapsedExpander()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "personalization-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new SettingsViewModel(services) { ShowPersonalization = true };
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 1700 };
        try
        {
            window.Show();
            Pump();
            var expander = view.GetVisualDescendants().OfType<Expander>()
                .Single(item => HeaderText(item) == "外观微调");
            Assert.False(expander.IsExpanded);

            // 折叠时滑块不可见
            var hueRow = view.GetVisualDescendants().OfType<TextBlock>()
                .Any(item => item.Text == "色相" && item.IsEffectivelyVisible);
            Assert.False(hueRow);

            expander.BringIntoView();
            Pump();
            Save(window, "settings-appearance-collapsed.png");

            expander.IsExpanded = true;
            Pump();
            Assert.True(expander.IsExpanded);
            expander.BringIntoView();
            Pump();
            Save(window, "settings-appearance-expanded.png");

        }
        finally
        {
            window.Close();
        }
    }

    private static void Save(Window window, string name)
    {
        var shotDir = Environment.GetEnvironmentVariable("WMCL_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(shotDir)) return;
        Directory.CreateDirectory(shotDir);
        var bitmap = new RenderTargetBitmap(new PixelSize(900, 1700), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(shotDir, name));
    }

    private static string? HeaderText(Expander expander) =>
        expander.Header is StackPanel panel
            ? panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text
            : expander.Header as string;

    private static void Pump()
    {
        for (var index = 0; index < 6; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
