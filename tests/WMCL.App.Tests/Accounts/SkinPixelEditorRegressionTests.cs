using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WMCL.App.Controls;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinPixelEditorRegressionTests
{
    [AvaloniaTheory]
    [InlineData(32)]
    [InlineData(64)]
    public void CenteredCanvasHasANaturalSizeAndRendersTheLoadedSkin(int height)
    {
        using var source = BitmapWithColors(64, height);
        var model = new SkinEditorViewModel();
        model.LoadSkin(source);
        var editor = new SkinPixelEditor { ViewModel = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Content = new Border { Child = editor, Height = 320 }, Width = 450, Height = 400 };
        try
        {
            window.Show();
            Pump();
            Assert.True(editor.Bounds.Width > 0);
            Assert.True(editor.Bounds.Height > 0);
            Assert.Equal(64d / height, editor.Bounds.Width / editor.Bounds.Height, 6);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using (var captured = new WriteableBitmap(frame.PixelSize, frame.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul))
            using (var buffer = captured.Lock())
            {
                frame.CopyPixels(buffer);
                var skinPixels = 0;
                for (var row = 0; row < frame.PixelSize.Height; row++)
                    for (var column = 0; column < frame.PixelSize.Width; column++)
                    {
                        var offset = row * buffer.RowBytes + column * 4;
                        if (Marshal.ReadByte(buffer.Address, offset + 2) == 220 &&
                            Marshal.ReadByte(buffer.Address, offset + 1) == 30 &&
                            Marshal.ReadByte(buffer.Address, offset) == 60) skinPixels++;
                    }
                Assert.True(skinPixels > 100);
            }
            var path = Environment.GetEnvironmentVariable("WMCL_SKIN_EDITOR_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
                frame.Save(Path.Combine(path, "editor-64x" + height + ".png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PngRoundTripKeepsRgbAndAlphaInTheirOriginalChannels()
    {
        using var source = BitmapWithColors(64, 64);
        var model = new SkinEditorViewModel();
        model.LoadSkin(source);
        Assert.Equal(Color.FromArgb(255, 220, 30, 60), model.Pixels[0]);
        Assert.Equal(Color.FromArgb(100, 20, 190, 230), model.Pixels[1]);
        Assert.Null(model.Pixels[2]);
        using var output = model.ToBitmap();
        Assert.NotNull(output);
        using var bytes = new MemoryStream();
        output.Save(bytes, PngBitmapEncoderOptions.Default);
        bytes.Position = 0;
        using var reloaded = new Bitmap(bytes);
        var result = new SkinEditorViewModel();
        result.LoadSkin(reloaded);
        Assert.Equal(model.Pixels[0], result.Pixels[0]);
        // Skia decodes PNG through premultiplied storage. At alpha=100 this
        // fixture can lose one RGB level when converted back to straight BGRA.
        // Keep alpha exact and channels separate, so swapped ARGB cannot pass.
        var semiTransparent = Assert.IsType<Color>(result.Pixels[1]);
        Assert.Equal(100, semiTransparent.A);
        Assert.InRange((int)semiTransparent.R, 19, 21);
        Assert.InRange((int)semiTransparent.G, 189, 191);
        Assert.InRange((int)semiTransparent.B, 229, 231);
        Assert.Null(result.Pixels[2]);
    }

    [AvaloniaFact]
    public void BrushNotifiesOncePerChangedPaintAndUndoRestoresTheWholeStroke()
    {
        using var source = BitmapWithColors(64, 64);
        var model = new SkinEditorViewModel();
        model.LoadSkin(source);
        var initial = model.Pixels.ToArray();
        var version = model.PixelsVersion;
        var notifications = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.PixelsVersion)) notifications++; };
        model.SelectedColor = Color.FromArgb(200, 80, 90, 100);
        model.BrushSize = 2;
        model.BeginStroke();
        model.PaintPixel(10, 10);
        model.PaintPixel(12, 10);
        Assert.Equal(version + 2, model.PixelsVersion);
        Assert.Equal(2, notifications);
        model.PaintPixel(12, 10);
        Assert.Equal(2, notifications);
        model.UndoCommand.Execute(null);
        Assert.Equal(initial, model.Pixels);
        Assert.False(model.CanUndo);
        Assert.True(model.CanRedo);
        model.RedoCommand.Execute(null);
        Assert.Equal(model.SelectedColor, model.Pixels[10 * 64 + 10]);
        Assert.Equal(model.SelectedColor, model.Pixels[10 * 64 + 13]);
    }

    [AvaloniaFact]
    public void PointerOutsideTheAtlasDoesNotPaintAndReleasedPointerCannotContinuePainting()
    {
        using var source = BitmapWithColors(64, 32);
        var model = new SkinEditorViewModel();
        model.LoadSkin(source);
        model.SelectedColor = Colors.Red;
        var editor = new SkinPixelEditor { ViewModel = model, Width = 320, Height = 320 };
        var window = new Window { Content = editor, Width = 320, Height = 320 };
        try
        {
            window.Show();
            Pump();
            var initialVersion = model.PixelsVersion;
            Click(window, new Point(10, 79.9), MouseButton.Left);
            Assert.Equal(initialVersion, model.PixelsVersion);
            Assert.False(model.CanUndo);
            Click(window, new Point(10, 90), MouseButton.Right);
            Assert.Equal(initialVersion, model.PixelsVersion);
            Click(window, new Point(10, 90), MouseButton.Left);
            Assert.Equal(initialVersion + 1, model.PixelsVersion);
            Assert.True(model.CanUndo);
            Send(window, "MouseMove", new Point(50, 100), RawInputModifiers.None);
            Assert.Equal(initialVersion + 1, model.PixelsVersion);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RebindingAndReattachingCanvasTracksOnlyTheCurrentModelsDimensions()
    {
        using var square = BitmapWithColors(64, 64);
        using var legacy = BitmapWithColors(64, 32);
        var old = new SkinEditorViewModel();
        old.LoadSkin(square);
        var current = new SkinEditorViewModel();
        current.LoadSkin(legacy);
        var editor = new SkinPixelEditor { ViewModel = old, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Content = editor, Width = 400, Height = 400 };
        try
        {
            window.Show();
            Pump();
            Assert.Equal(editor.Bounds.Width, editor.Bounds.Height);
            editor.ViewModel = current;
            Pump();
            window.UpdateLayout();
            Assert.Equal(2, editor.Bounds.Width / editor.Bounds.Height, 6);
            window.Content = null;
            current.LoadSkin(square);
            window.Content = editor;
            Pump();
            window.UpdateLayout();
            Assert.Equal(editor.Bounds.Width, editor.Bounds.Height);
            old.LoadSkin(null);
            Pump();
            Assert.Same(current, editor.ViewModel);
            Assert.Equal(editor.Bounds.Width, editor.Bounds.Height);
            current.LoadSkin(legacy);
            Pump();
            window.UpdateLayout();
            Assert.Equal(2, editor.Bounds.Width / editor.Bounds.Height, 6);
        }
        finally { window.Close(); }
    }

    private static Bitmap BitmapWithColors(int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var pixel = row * buffer.RowBytes + column * 4;
                Marshal.WriteByte(buffer.Address, pixel, 60);
                Marshal.WriteByte(buffer.Address, pixel + 1, 30);
                Marshal.WriteByte(buffer.Address, pixel + 2, 220);
                Marshal.WriteByte(buffer.Address, pixel + 3, 255);
            }
        Marshal.WriteByte(buffer.Address, 4, 230);
        Marshal.WriteByte(buffer.Address, 5, 190);
        Marshal.WriteByte(buffer.Address, 6, 20);
        Marshal.WriteByte(buffer.Address, 7, 100);
        Marshal.WriteByte(buffer.Address, 11, 0);
        return bitmap;
    }

    private static void Click(Window window, Point target, MouseButton button)
    {
        Send(window, "MouseMove", target, RawInputModifiers.None);
        Send(window, "MouseDown", target, button, RawInputModifiers.None);
        Send(window, "MouseUp", target, button, RawInputModifiers.None);
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
