using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

/// <summary>
/// WMCL follows PCL-ME: the slider maps to whole-window translucency with a 40% floor,
/// so the blurred desktop shows through while text stays readable.
/// </summary>
[Trait("Kind", "Motion")]
public sealed class ReadableOpacityTests
{
    [AvaloniaTheory]
    [InlineData(0, 0.4, 0.4)]
    [InlineData(20, 0.52, 0.4)]
    [InlineData(50, 0.7, 0.4)]
    [InlineData(100, 1.0, 0.4)]
    [InlineData(0, 0.75, 0.75)]
    [InlineData(100, 1.0, 0.75)]
    public async Task SliderMapsToWindowOpacityFloor(double percent, double expected, double minimum)
    {
        var window = new Window { Content = new Border { Child = new TextBlock { Text = "readable" } }, Width = 400, Height = 200 };
        try
        {
            window.Show();
            await PumpAsync();
            WindowPresentationManager.Apply(window, new WindowPresentation(1, (int)percent, true, minimum));
            Assert.Equal(expected, WindowPresentationManager.GetOpacity(window), 6);
            Assert.Equal(expected, WindowPresentationManager.GetBackgroundOpacity(window), 6);
            Assert.Contains(WindowTransparencyLevel.Blur, window.TransparencyLevelHint!);
            // Never dim below the readability floor, no matter how low the slider is dragged.
            Assert.True(WindowPresentationManager.GetOpacity(window) >= minimum - 1e-9);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task LowestTransparencyStillRendersReadableTextAndControls(string theme)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var settings = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "opacity-" + Guid.NewGuid().ToString("N")),
            Theme = theme,
            ApplicationOpacity = 0,
            WindowBackdropBlur = true
        }));
        using var model = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
        model.NavigateTo("settings-ui");
        var window = new MainWindow { DataContext = model, Width = 1000, Height = 760 };
        try
        {
            window.Show();
            await PumpAsync();
            // Light theme without a backdrop keeps a higher floor so dark text stays legible.
            Assert.Equal(theme == "dark" ? 0.4 : 0.75, WindowPresentationManager.GetOpacity(window), 6);
            var title = window.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == "个性化" && text.Name == "TitleText" && text.IsEffectivelyVisible);
            title.BringIntoView();
            await PumpAsync();
            // PCL-ME translucency lives on the window surface only; widgets stay at 100%.
            Assert.Equal(1, title.Opacity, 3);
            Assert.Equal(1, Assert.IsAssignableFrom<Border>(window.Content).Child!.Opacity, 3);
            Assert.Equal((byte)255, Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color.A);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var painted = CountTextPixels(frame, title, window);
            Assert.True(painted >= 5, $"Only {painted} glyph pixels painted for {theme} at 0%.");
            // 透明度滑块已收进「外观微调」折叠组，先展开
            var appearance = window.GetVisualDescendants().OfType<Expander>()
                .First(item => (item.Header as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "外观微调");
            appearance.IsExpanded = true;
            await PumpAsync();
            var slider = window.GetVisualDescendants().OfType<Slider>().First(control => control.Name == "ApplicationOpacitySlider");
            Assert.True(slider.Bounds.Width > 100);
            Save(frame, $"{theme}-pclme-opacity-0");
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previous == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaFact]
    public async Task StandaloneCardShowsDesktopThroughBlurredBackdropWhileTextStaysOpaque()
    {
        ThemeService.Apply("dark");
        var text = new TextBlock { Text = "MMMM 8888 可读正文", FontSize = 28, FontWeight = FontWeight.Bold };
        var card = new Border { Margin = new Thickness(24), Child = text };
        card.Classes.Add("card");
        var window = new Window { Content = new Border { Child = card }, Width = 620, Height = 240 };
        try
        {
            window.Show();
            await PumpAsync();
            WindowPresentationManager.Apply(window, new WindowPresentation(1, 0, true));
            await PumpAsync();
            // PCL-ME keeps the normal theme surfaces; the window itself is 40% opaque so
            // the desktop shows through instead of the app dimming individual widgets.
            Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
            Assert.Equal((byte)255, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color.A);
            Assert.Equal(1, text.Opacity, 3);
            Assert.Equal(1, Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Opacity, 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TransparencySettingSurvivesRoundTripAndToleratesLegacyLowValues()
    {
        foreach (var percent in new[] { 0, 20, 55, 100 })
        {
            var settings = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(new AppSettings { ApplicationOpacity = percent }));
            Assert.Equal(percent, settings.ApplicationOpacity);
        }
        Assert.Equal(0, AppSettingsSerializer.Deserialize("{\"ApplicationOpacity\":-40}").ApplicationOpacity);
    }

    private static int CountTextPixels(WriteableBitmap frame, TextBlock text, Window window)
    {
        var origin = text.TranslatePoint(default, window)!.Value;
        var size = text.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return 0;
        using var pixels = new WriteableBitmap(frame.PixelSize, frame.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        frame.CopyPixels(buffer);
        var painted = 0;
        var top = Math.Clamp((int)origin.Y, 0, frame.PixelSize.Height - 1);
        var bottom = Math.Clamp((int)(origin.Y + size.Height), 0, frame.PixelSize.Height);
        var left = Math.Clamp((int)origin.X, 0, frame.PixelSize.Width - 1);
        var right = Math.Clamp((int)(origin.X + size.Width), 0, frame.PixelSize.Width);
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * buffer.RowBytes + x * 4;
                var b = Marshal.ReadByte(buffer.Address, offset);
                var g = Marshal.ReadByte(buffer.Address, offset + 1);
                var r = Marshal.ReadByte(buffer.Address, offset + 2);
                if (r + g + b > 3 * 90) painted++;
            }
        return painted;
    }

    private static void Save(WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_READABLE_OPACITY_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }
    }
}
