using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WMCL.App.Controls;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class LargeSkinEditorTests
{
    [AvaloniaFact]
    public void ZoomAndPanKeepTheSamePixelUnderTheMouseAndPaintingUsesThatPixel()
    {
        using var skin = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var model = new SkinEditorViewModel();
        model.LoadSkin(skin);
        model.SelectedColor = Colors.Red;
        var canvas = new SkinPixelEditor
        {
            ViewModel = model,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        var window = new Window { Content = canvas, Width = 640, Height = 640 };
        try
        {
            window.Show();
            Pump();
            var target = new Point(105, 105);
            canvas.ZoomAt(target, 3);
            Assert.Equal(3, canvas.Zoom);
            canvas.PanX += 40;
            canvas.PanY -= 20;
            target += new Vector(40, -20);
            Click(window, target, MouseButton.Left);
            Assert.Equal(Colors.Red, model.Pixels[10 * 64 + 10]);
            Assert.Null(model.Pixels[10 * 64 + 9]);
            Assert.Null(model.Pixels[10 * 64 + 11]);
            var version = model.PixelsVersion;
            var panBefore = new Point(canvas.PanX, canvas.PanY);
            Send(window, "MouseDown", target, MouseButton.Right, RawInputModifiers.None);
            Send(window, "MouseMove", target + new Vector(50, 30), RawInputModifiers.RightMouseButton);
            Send(window, "MouseUp", target + new Vector(50, 30), MouseButton.Right, RawInputModifiers.None);
            Assert.Equal(panBefore.X + 50, canvas.PanX, 6);
            Assert.Equal(panBefore.Y + 30, canvas.PanY, 6);
            Assert.Equal(version, model.PixelsVersion);
            var released = new Point(canvas.PanX, canvas.PanY);
            Send(window, "MouseMove", new Point(200, 200), RawInputModifiers.None);
            Assert.Equal(released, new Point(canvas.PanX, canvas.PanY));
            Send(window, "MouseWheel", new Point(320, 320), new Vector(0, 1), RawInputModifiers.None);
            Assert.True(canvas.Zoom > 3);
            canvas.ResetView();
            Assert.Equal(1, canvas.Zoom);
            Assert.Equal(0, canvas.PanX);
            Assert.Equal(0, canvas.PanY);
            model.UndoCommand.Execute(null);
            Assert.Null(model.Pixels[10 * 64 + 10]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(760, 560)]
    [InlineData(1060, 800)]
    public void DedicatedWindowHasUsefulCanvasAndCanZoomWithoutChangingTheSkin(int width, int height)
    {
        using var source = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var window = new SkinEditorWindow(source, "classic") { Width = width, Height = height };
        try
        {
            window.Show();
            Pump();
            var canvas = window.FindControl<SkinPixelEditor>("LargePixelCanvas")!;
            Assert.True(canvas.Bounds.Width >= width - 280);
            Assert.True(canvas.Bounds.Height >= height - 240, $"Canvas bounds: {canvas.Bounds}");
            var original = window.Editor.Pixels.ToArray();
            canvas.ZoomAt(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), 4);
            canvas.PanX = 100;
            canvas.PanY = -80;
            Pump();
            Assert.Equal(original, window.Editor.Pixels);
            Assert.Equal(64, window.Editor.Width);
            Assert.Equal(64, window.Editor.Height);
            Assert.True(canvas.Zoom >= 4);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Point position, MouseButton button)
    {
        Send(window, "MouseMove", position, RawInputModifiers.None);
        Send(window, "MouseDown", position, button, RawInputModifiers.None);
        Send(window, "MouseUp", position, button, RawInputModifiers.None);
        Pump();
    }
    private static void Send(Window window, string method, params object?[] args) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(window.PlatformImpl, args);
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
