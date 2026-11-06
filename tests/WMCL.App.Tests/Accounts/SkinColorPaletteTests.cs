using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WMCL.App.Controls;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinColorPaletteTests
{
    [AvaloniaFact]
    public void CommonColorsAndAlphaSelectARealBrushWithoutTypingHex()
    {
        var palette = new SkinColorPalette();
        var window = new Window { Content = palette, Width = 300, Height = 560 };
        try
        {
            window.Show();
            Pump();
            var red = palette.CommonColors.Children.OfType<Button>().Single(button =>
                ((button.Content as Border)?.Background as ISolidColorBrush)?.Color == Color.FromUInt32(0xFFE53935));
            red.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Color.FromUInt32(0xFFE53935), palette.SelectedColor);
            Assert.Single(palette.RecentColors.Children);
            palette.AlphaSlider.Value = 128;
            Assert.Equal(128, palette.SelectedColor.A);
            var rgb = palette.SelectedColor;
            Assert.InRange(rgb.R, 227, 231);
            Assert.InRange(rgb.G, 55, 59);
            Assert.InRange(rgb.B, 51, 55);
            foreach (var color in new[] { Colors.Blue, Colors.Cyan, Colors.Gold, Colors.Brown, Colors.Gray, Colors.Lime, Colors.Orange, Colors.Pink, Colors.Purple, Colors.White })
                palette.RememberColor(color);
            Assert.Equal(8, palette.RecentColors.Children.Count);
            var shotDirectory = Environment.GetEnvironmentVariable("WMCL_SKIN_PALETTE_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(shotDirectory))
            {
                Directory.CreateDirectory(shotDirectory);
                Pump();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(shotDirectory, "skin-color-palette.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HsvFieldRespondsToTheMouseAndPreservesOpacity()
    {
        var palette = new SkinColorPalette();
        var window = new Window { Content = palette, Width = 300, Height = 560 };
        try
        {
            window.Show();
            Pump();
            palette.HueSlider.Value = 180;
            palette.AlphaSlider.Value = 120;
            var field = palette.SaturationField;
            var position = field.TranslatePoint(new Point(field.Bounds.Width - 1, 1), window)!.Value;
            Click(window, position);
            Assert.Equal(120, palette.SelectedColor.A);
            Assert.InRange(palette.SelectedColor.R, 0, 5);
            Assert.InRange(palette.SelectedColor.G, 250, 255);
            Assert.InRange(palette.SelectedColor.B, 250, 255);
            Assert.True(field.IsFocused);
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Assert.True(palette.SelectedColor.G < 255);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EyedropperAtZoomedPixelSynchronizesPaletteAndDoesNotPaintOrAddUndo(bool controlShortcut)
    {
        using var skin = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var expected = Color.FromArgb(170, 12, 160, 210);
        using (var pixels = skin.Lock())
            Marshal.WriteInt32(pixels.Address, 10 * pixels.RowBytes + 10 * 4, unchecked((int)expected.ToUInt32()));
        var window = new SkinEditorWindow(skin, "classic");
        try
        {
            window.Show();
            Pump();
            var canvas = window.FindControl<SkinPixelEditor>("LargePixelCanvas")!;
            var palette = Assert.IsType<SkinColorPalette>(Assert.IsType<Flyout>(window.FindControl<Button>("PaletteButton")!.Flyout).Content);
            palette.SelectedColor = Colors.Red;
            Assert.Equal(Colors.Red, window.Editor.SelectedColor);
            window.Editor.IsEraser = true;
            var scale = Math.Min(canvas.Bounds.Width / 64, canvas.Bounds.Height / 64);
            var point = new Point((canvas.Bounds.Width - 64 * scale) / 2 + 10.5 * scale,
                (canvas.Bounds.Height - 64 * scale) / 2 + 10.5 * scale);
            canvas.ZoomAt(point, 3);
            canvas.PanX += 15;
            canvas.PanY -= 10;
            point += new Vector(15, -10);
            var screen = canvas.TranslatePoint(point, window)!.Value;
            canvas.IsColorPicking = !controlShortcut;
            var version = window.Editor.PixelsVersion;
            Send(window, "MouseMove", screen, RawInputModifiers.None);
            Send(window, "MouseDown", screen, MouseButton.Left, controlShortcut ? RawInputModifiers.Control : RawInputModifiers.None);
            Send(window, "MouseUp", screen, MouseButton.Left, RawInputModifiers.None);
            Pump();
            Assert.Equal(expected, window.Editor.SelectedColor);
            Assert.Equal(expected, palette.SelectedColor);
            Assert.Equal(expected.ToString(), window.FindControl<TextBox>("ColorBox")!.Text);
            Assert.Equal(version, window.Editor.PixelsVersion);
            Assert.False(window.Editor.CanUndo);
            Assert.False(window.Editor.IsEraser);
            Assert.False(canvas.IsColorPicking);
            Assert.Single(palette.RecentColors.Children);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PaintingUsesThePaletteColorAndOpacityWithoutAnyHexEntry()
    {
        using var skin = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var window = new SkinEditorWindow(skin, "classic");
        try
        {
            window.Show();
            Pump();
            var palette = Assert.IsType<SkinColorPalette>(Assert.IsType<Flyout>(window.FindControl<Button>("PaletteButton")!.Flyout).Content);
            palette.SelectedColor = Color.FromArgb(128, 20, 160, 210);
            var canvas = window.FindControl<SkinPixelEditor>("LargePixelCanvas")!;
            var screen = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2 + 1, canvas.Bounds.Height / 2 + 1), window)!.Value;
            Click(window, screen);
            Assert.Equal(Color.FromArgb(128, 20, 160, 210), window.Editor.Pixels[32 * 64 + 32]);
            Assert.True(window.Editor.CanUndo);
            window.Editor.UndoCommand.Execute(null);
            Assert.Null(window.Editor.Pixels[32 * 64 + 32]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PaletteAndHexChangesStayInSyncAndNeverEditTexture()
    {
        using var skin = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var window = new SkinEditorWindow(skin, "classic");
        try
        {
            window.Show();
            Pump();
            var palette = Assert.IsType<SkinColorPalette>(Assert.IsType<Flyout>(window.FindControl<Button>("PaletteButton")!.Flyout).Content);
            var version = window.Editor.PixelsVersion;
            window.FindControl<TextBox>("ColorBox")!.Text = "#7F123456";
            Pump();
            Assert.Equal(Color.FromUInt32(0x7F123456), palette.SelectedColor);
            palette.SelectedColor = Colors.Lime;
            Assert.Equal(Colors.Lime, window.Editor.SelectedColor);
            Assert.Equal(Colors.Lime.ToString(), window.FindControl<TextBox>("ColorBox")!.Text);
            Assert.Equal(version, window.Editor.PixelsVersion);
            Assert.All(window.Editor.Pixels, color => Assert.Null(color));
        }
        finally { window.Close(); }
    }
    private static void Click(Window window, Point position)
    {
        Send(window, "MouseMove", position, RawInputModifiers.None);
        Send(window, "MouseDown", position, MouseButton.Left, RawInputModifiers.None);
        Send(window, "MouseUp", position, MouseButton.Left, RawInputModifiers.None);
        Pump();
    }
    private static void Send(Window window, string method, params object?[] args) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(window.PlatformImpl, args);
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
