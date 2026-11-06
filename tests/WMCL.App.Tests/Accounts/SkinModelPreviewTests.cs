using System.Runtime.InteropServices;
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

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinModelPreviewTests
{
    private static readonly Size ViewSize = new(300, 400);

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public void ModernGeometryUsesAllSixBodyPartsAndTheCorrectArmWidth(bool slim, int armWidth)
    {
        var faces = SkinModelGeometry.Build(slim, false, false);
        foreach (var part in new[] { "Head", "Body", "RightArm", "LeftArm", "RightLeg", "LeftLeg" })
            Assert.Equal(6, faces.Count(face => face.Part == part));
        foreach (var part in new[] { "Hat", "Jacket", "RightSleeve", "LeftSleeve", "RightPants", "LeftPants" })
            Assert.All(faces.Where(face => face.Part == part), face => Assert.True(face.Overlay));
        var front = Assert.Single(faces, face => face.Part == "RightArm" && face.Side == "Front");
        Assert.Equal(armWidth, front.Texture.Width);
        Assert.Equal(armWidth, Math.Abs(front.Across.X));
        Assert.Equal(new Rect(44, 20, armWidth, 12), front.Texture);
        Assert.All(faces, face =>
        {
            Assert.InRange(face.Texture.X, 0, 63);
            Assert.InRange(face.Texture.Y, 0, 63);
            Assert.True(face.Texture.Right <= 64);
            Assert.True(face.Texture.Bottom <= 64);
        });
        Assert.DoesNotContain(faces, face => face.Cape);
    }

    [Fact]
    public void LegacyLeftLimbsReflectTheRightLimbGeometryAndReuseItsAtlas()
    {
        var faces = SkinModelGeometry.Build(true, true, false);
        Assert.DoesNotContain(faces, face => face.Part == "Jacket" || face.Part == "LeftSleeve");
        foreach (var pair in new[] { ("RightArm", "LeftArm"), ("RightLeg", "LeftLeg") })
        {
            foreach (var right in faces.Where(face => face.Part == pair.Item1))
            {
                var left = Assert.Single(faces, face => face.Part == pair.Item2 && face.Side == right.Side);
                Assert.Equal(right.Texture, left.Texture);
                Assert.Equal(-right.Across.X, left.Across.X);
                Assert.Equal(-right.Normal.X, left.Normal.X);
            }
        }
        Assert.Equal(4, Assert.Single(faces, face => face.Part == "LeftArm" && face.Side == "Front").Texture.Width);
    }

    [Fact]
    public void CapeUsesIndependentFrontAndBackUvsAndBackfaceProjection()
    {
        var faces = SkinModelGeometry.Build(false, false, true);
        Assert.Equal(new Rect(12, 1, 10, 16), Assert.Single(faces, face => face.Cape && face.Side == "Front").Texture);
        Assert.Equal(new Rect(1, 1, 10, 16), Assert.Single(faces, face => face.Cape && face.Side == "Back").Texture);
        var front = SkinModelGeometry.Project(faces, 0, ViewSize);
        var back = SkinModelGeometry.Project(faces, 180, ViewSize);
        Assert.Contains(front, face => face.Face.Part == "Head" && face.Face.Side == "Front");
        Assert.DoesNotContain(front, face => face.Face.Part == "Head" && face.Face.Side == "Back");
        Assert.Contains(back, face => face.Face.Cape && face.Face.Side == "Back");
        Assert.DoesNotContain(back, face => face.Face.Cape && face.Face.Side == "Front");
        Assert.Contains(front, face => face.Face.Part == "Head" && face.Face.Side == "Top");
        Assert.All(front.Zip(front.Skip(1)), pair => Assert.True(pair.First.Depth >= pair.Second.Depth));
    }

    [AvaloniaTheory]
    [InlineData(64, 64, 96, "classic")]
    [InlineData(64, 64, 96, "slim")]
    [InlineData(128, 128, 192, "classic")]
    [InlineData(128, 128, 96, "slim")]
    [InlineData(64, 32, 96, "classic")]
    [InlineData(128, 64, 192, "classic")]
    public void FrontRenderSamplesEachBodyPartFromTheSkinAtlas(int width, int height, int dpi, string variant)
    {
        var legacy = height * 2 == width;
        var slim = variant == "slim" && !legacy;
        using var skin = CreateSkin(width, height, dpi, slim);
        var control = new SkinModelPreview { Skin = skin, Variant = variant, Yaw = 0 };
        using var rendered = Render(control);
        foreach (var part in new[] { "Head", "Body", "RightArm", "LeftArm", "RightLeg", "LeftLeg" })
        {
            var expectedPart = legacy ? part.Replace("Left", "Right", StringComparison.Ordinal) : part;
            Assert.Equal(PartColor(expectedPart), PixelAtPart(rendered, part, "Front", 0, slim, legacy));
        }
        Capture(rendered, $"front-{width}x{height}-{dpi}-{variant}");
        control.Yaw = -35;
        using var rotated = Render(control);
        Assert.True(CountPixels(rotated, PartColor("Head")) > 100);
        Capture(rotated, $"rotated-{width}x{height}-{variant}");
    }

    [AvaloniaFact]
    public void BackRenderUsesTheBackAtlasAndSelectedCapeInsteadOfAFlatFrontImage()
    {
        using var skin = CreateSkin(64, 64, 96, false);
        using var cape = CreateCape(64, 32, 96);
        var control = new SkinModelPreview { Skin = skin, Cape = cape, Yaw = 180 };
        using var back = Render(control);
        Assert.Equal(0xffeeaa22u, PixelAtPart(back, "Cape", "Back", 180, false, false, true));
        Assert.Equal(BackColor("Head"), PixelAtPart(back, "Head", "Back", 180, false, false, true));
        Capture(back, "back-with-cape");
        control.Cape = null;
        using var noCape = Render(control);
        Assert.Equal(BackColor("Body"), PixelAtPart(noCape, "Body", "Back", 180, false, false));
        Assert.Equal(0, CountPixels(noCape, 0xffeeaa22u));
        Capture(noCape, "back-without-cape");
        control.Cape = cape;
        control.Yaw = 150;
        using var rotated = Render(control);
        Capture(rotated, "back-rotated-cape");
    }

    [AvaloniaTheory]
    [InlineData(64, 32, 96)]
    [InlineData(128, 64, 192)]
    public void ScaledCapeUvsAreSampledWithoutResizingOrDisposingTheBorrowedBitmap(int width, int height, int dpi)
    {
        using var skin = CreateSkin(64, 64, 96, false);
        using var cape = CreateCape(width, height, dpi);
        var control = new SkinModelPreview { Skin = skin, Cape = cape, Yaw = 180 };
        using var rendered = Render(control);
        Assert.Equal(0xffeeaa22u, PixelAtPart(rendered, "Cape", "Back", 180, false, false, true));
        control.Skin = null;
        control.Cape = null;
        using var fallback = Render(control);
        Assert.NotEqual(0u, Pixel(skin, 8, 8) >> 24);
        Assert.Equal(0xffaa55eeu, Pixel(cape, 12 * (width / 64), height / 16));
        Assert.True(CountOpaque(fallback) > 1000);
    }

    [AvaloniaFact]
    public void TransparentOuterLayersDoNotTurnIntoBlackBoxesAndOpaqueLayersOverlayTheBody()
    {
        using var skin = CreateSkin(64, 64, 96, false);
        var control = new SkinModelPreview { Skin = skin, Yaw = 0 };
        using var original = Render(control);
        Assert.Equal(PartColor("Head"), PixelAtPart(original, "Head", "Front", 0, false, false));
        Fill(skin, new Rect(40, 8, 8, 8), 0xff0088ffu);
        Fill(skin, new Rect(20, 36, 8, 12), 0xff11dd22u);
        // Replacing the borrowed source invalidates any preprocessing; a new source is the public refresh contract.
        using var updatedSkin = Copy(skin);
        control.Skin = updatedSkin;
        using var updated = Render(control);
        Assert.Equal(0xff0088ffu, PixelAtPart(updated, "Hat", "Front", 0, false, false));
        Assert.Equal(0xff11dd22u, PixelAtPart(updated, "Jacket", "Front", 0, false, false));
        Capture(updated, "outer-layers");
    }

    [AvaloniaFact]
    public void PartiallyTransparentHatBlendsOverTheFaceInsteadOfPaintingBlack()
    {
        using var skin = CreateSkin(64, 64, 96, false);
        Fill(skin, new Rect(40, 8, 8, 8), 0x800000ffu);
        using var rendered = Render(new SkinModelPreview { Skin = skin, Yaw = 0 });
        var pixel = PixelAtPart(rendered, "Hat", "Front", 0, false, false);
        Assert.Equal(255u, pixel >> 24);
        Assert.InRange((pixel >> 16) & 255u, 120u, 135u);
        Assert.InRange((pixel >> 8) & 255u, 35u, 50u);
        Assert.InRange(pixel & 255u, 140u, 155u);
    }

    [AvaloniaFact]
    public void LegacyOpaqueUnusedHatBackgroundIsNotRenderedOverTheFace()
    {
        using var skin = CreateSkin(64, 32, 96, false);
        Fill(skin, new Rect(0, 0, 8, 8), 0xff000000u);
        Fill(skin, new Rect(32, 0, 32, 16), 0xff000000u);
        using var rendered = Render(new SkinModelPreview { Skin = skin, Yaw = 0 });
        Assert.Equal(PartColor("Head"), PixelAtPart(rendered, "Head", "Front", 0, false, true));
    }

    [AvaloniaFact]
    public void PointerDraggingAndKeyboardRotateAndResetWithoutTakingBitmapOwnership()
    {
        using var skin = CreateSkin(64, 64, 96, false);
        var preview = new SkinModelPreview { Skin = skin, Yaw = -25 };
        var window = new Window { Content = preview, Width = 300, Height = 400 };
        try
        {
            window.Show();
            Pump();
            Send(window, "MouseDown", new Point(150, 180), MouseButton.Left, RawInputModifiers.None);
            Send(window, "MouseMove", new Point(230, 180), RawInputModifiers.LeftMouseButton);
            Send(window, "MouseUp", new Point(230, 180), MouseButton.Left, RawInputModifiers.None);
            Pump();
            Assert.Equal(35, preview.Yaw);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
            Assert.Equal(20, preview.Yaw);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.Equal(35, preview.Yaw);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
            Assert.Equal(-25, preview.Yaw);
            preview.Yaw = 1080;
            Assert.Equal(0, preview.Yaw);
            preview.Yaw = double.NaN;
            Assert.Equal(-25, preview.Yaw);
        }
        finally { window.Close(); }
        Assert.Equal(PartColor("Head"), Pixel(skin, 10, 10));
    }

    [AvaloniaFact]
    public void InvalidTextureIsSafelyIgnoredAndNullSkinUsesDefaultSteve()
    {
        using var invalid = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var control = new SkinModelPreview { Skin = invalid };
        using var blank = Render(control);
        Assert.Equal(0, CountOpaque(blank));
        control.Skin = null;
        using var fallback = Render(control);
        Assert.True(CountOpaque(fallback) > 1000);
    }

    [AvaloniaFact]
    public void AsymmetricCapeDecorationFacesOutwardAndKeepsItsLeftRightOrientation()
    {
        using var skin = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var cape = new WriteableBitmap(new PixelSize(64, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        // Literal standard cape atlas: decorated outer panel at 1..10, lining at 12..21.
        Fill(cape, new Rect(1, 1, 10, 16), 0xffffcc00u);
        Fill(cape, new Rect(1, 1, 2, 16), 0xffff2200u);
        Fill(cape, new Rect(9, 1, 2, 16), 0xff2255ffu);
        // A dark asymmetric F-like mark, confined to the outer panel.
        Fill(cape, new Rect(4, 4, 1, 10), 0xff111111u);
        Fill(cape, new Rect(4, 4, 4, 1), 0xff111111u);
        Fill(cape, new Rect(4, 8, 3, 1), 0xff111111u);
        Fill(cape, new Rect(12, 1, 10, 16), 0xff22cc55u);
        Fill(cape, new Rect(12, 1, 2, 16), 0xffffffffu);
        Fill(cape, new Rect(20, 1, 2, 16), 0xffcc22ffu);
        var control = new SkinModelPreview { Skin = skin, Cape = cape, Yaw = 180, Pitch = 0 };
        using var outside = Render(control);
        Assert.Equal(0xffff2200u, PixelAtCape(outside, "Back", 180, 0.1, 0.7));
        Assert.Equal(0xff2255ffu, PixelAtCape(outside, "Back", 180, 0.9, 0.7));
        Assert.Equal(0xff111111u, PixelAtCape(outside, "Back", 180, 0.35, 0.4));
        Assert.Equal(0xffffcc00u, PixelAtCape(outside, "Back", 180, 0.65, 0.7));
        Assert.Equal(0, CountPixels(outside, 0xff22cc55u));
        Capture(outside, "cape-asymmetric-outside");
        // Transparent body lets the test inspect the normally occluded inner surface.
        control.Yaw = 0;
        using var inside = Render(control);
        Assert.Equal(0xffffffffu, PixelAtCape(inside, "Front", 0, 0.1, 0.7));
        Assert.Equal(0xffcc22ffu, PixelAtCape(inside, "Front", 0, 0.9, 0.7));
        Assert.Equal(0xff22cc55u, PixelAtCape(inside, "Front", 0, 0.5, 0.7));
        Assert.Equal(0, CountPixels(inside, 0xffffcc00u));
        Capture(inside, "cape-asymmetric-inside");
        control.Yaw = 145;
        control.Pitch = 55;
        using var elevated = Render(control);
        Capture(elevated, "cape-elevated");
        control.Pitch = -45;
        using var below = Render(control);
        Capture(below, "cape-from-below");
    }

    [Theory]
    [InlineData(-89, 0)]
    [InlineData(89, 0)]
    [InlineData(-89, 90)]
    [InlineData(89, 180)]
    [InlineData(0, 270)]
    public void PitchZoomAndPanKeepFiniteProjectionAndStableFaceOrdering(double pitch, double yaw)
    {
        var geometry = SkinModelGeometry.Build(false, false, true);
        var original = SkinModelGeometry.Project(geometry, yaw, ViewSize, pitch);
        var moved = SkinModelGeometry.Project(geometry, yaw, ViewSize, pitch, 2, 37, -23);
        Assert.NotEmpty(original);
        Assert.Equal(original.Count, moved.Count);
        for (var index = 0; index < original.Count; index++)
        {
            Assert.Same(original[index].Face, moved[index].Face);
            Assert.Equal(original[index].Depth, moved[index].Depth, 10);
            foreach (var pair in new[] { (original[index].Origin, moved[index].Origin),
                         (original[index].Across, moved[index].Across), (original[index].Down, moved[index].Down) })
            {
                Assert.True(double.IsFinite(pair.Item2.X) && double.IsFinite(pair.Item2.Y));
                Assert.Equal(ViewSize.Width / 2 + 2 * (pair.Item1.X - ViewSize.Width / 2) + 37, pair.Item2.X, 8);
                Assert.Equal(ViewSize.Height / 2 + 2 * (pair.Item1.Y - ViewSize.Height / 2) - 23, pair.Item2.Y, 8);
            }
        }
        if (pitch > 0) Assert.Contains(original, face => face.Face.Part == "Head" && face.Face.Side == "Top");
        if (pitch < 0) Assert.Contains(original, face => face.Face.Part == "Head" && face.Face.Side == "Bottom");
        Assert.All(original.Zip(original.Skip(1)), pair => Assert.True(pair.First.Depth >= pair.Second.Depth));
    }

    [AvaloniaFact]
    public void PointerRotationPanWheelAndKeyboardOperateInAllDirectionsAndHomeResetsEverything()
    {
        var preview = new SkinModelPreview();
        var window = new Window { Content = preview, Width = 300, Height = 400 };
        try
        {
            window.Show();
            Pump();
            Send(window, "MouseDown", new Point(150, 180), MouseButton.Left, RawInputModifiers.None);
            Send(window, "MouseMove", new Point(230, 120), RawInputModifiers.LeftMouseButton);
            Send(window, "MouseUp", new Point(230, 120), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(35, preview.Yaw);
            Assert.Equal(40, preview.Pitch);
            Send(window, "MouseMove", new Point(235, 130), RawInputModifiers.None);
            Assert.Equal(40, preview.Pitch);
            Send(window, "MouseDown", new Point(180, 170), MouseButton.Right, RawInputModifiers.None);
            Send(window, "MouseMove", new Point(210, 195), RawInputModifiers.RightMouseButton);
            Send(window, "MouseUp", new Point(210, 195), MouseButton.Right, RawInputModifiers.None);
            Assert.Equal(30, preview.PanX);
            Assert.Equal(25, preview.PanY);
            Assert.Equal(35, preview.Yaw);
            Assert.Equal(40, preview.Pitch);
            Send(window, "MouseDown", new Point(180, 170), MouseButton.Middle, RawInputModifiers.None);
            Send(window, "MouseMove", new Point(170, 160), RawInputModifiers.MiddleMouseButton);
            Send(window, "MouseUp", new Point(170, 160), MouseButton.Middle, RawInputModifiers.None);
            Assert.Equal(20, preview.PanX);
            Assert.Equal(15, preview.PanY);
            window.MouseWheel(new Point(150, 180), new Vector(0, 1), RawInputModifiers.None);
            Assert.Equal(1.15, preview.Zoom, 6);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            Assert.Equal(55, preview.Pitch);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Assert.Equal(40, preview.Pitch);
            preview.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
            Assert.Equal(-25, preview.Yaw);
            Assert.Equal(10, preview.Pitch);
            Assert.Equal(1, preview.Zoom);
            Assert.Equal(0, preview.PanX);
            Assert.Equal(0, preview.PanY);
            preview.Pitch = 300;
            Assert.Equal(-60, preview.Pitch);
            preview.Pitch = -300;
            Assert.Equal(60, preview.Pitch);
            preview.Zoom = 100;
            Assert.Equal(3, preview.Zoom);
            preview.Zoom = -100;
            Assert.Equal(0.5, preview.Zoom);
            preview.Pitch = double.NaN;
            preview.Zoom = double.NaN;
            preview.PanX = double.PositiveInfinity;
            preview.PanY = double.NaN;
            Assert.Equal(10, preview.Pitch);
            Assert.Equal(1, preview.Zoom);
            Assert.Equal(0, preview.PanX);
            Assert.Equal(0, preview.PanY);
            // A reset while captured ends the drag rather than letting the next move restore the old pose.
            Send(window, "MouseDown", new Point(150, 180), MouseButton.Left, RawInputModifiers.None);
            preview.ResetView();
            Send(window, "MouseMove", new Point(240, 100), RawInputModifiers.LeftMouseButton);
            Send(window, "MouseUp", new Point(240, 100), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(-25, preview.Yaw);
            Assert.Equal(10, preview.Pitch);
        }
        finally { window.Close(); }
    }

    private static uint PixelAtCape(Bitmap bitmap, string side, double yaw, double horizontal, double vertical)
    {
        var projected = Assert.Single(SkinModelGeometry.Project(SkinModelGeometry.Build(false, false, true), yaw, ViewSize, 0),
            face => face.Face.Cape && face.Face.Side == side);
        var point = projected.Origin + (projected.Across - projected.Origin) * horizontal + (projected.Down - projected.Origin) * vertical;
        return Pixel(bitmap, (int)point.X, (int)point.Y);
    }

    private static uint PartColor(string part) => part switch
    {
        "Head" => 0xffff5522u,
        "Body" => 0xff22bb55u,
        "RightArm" => 0xff2244eeu,
        "LeftArm" => 0xffeed522u,
        "RightLeg" => 0xff9944ccu,
        "LeftLeg" => 0xff22ccccu,
        _ => 0xffffffffu
    };

    private static uint BackColor(string part) => 0xff000000u | ((PartColor(part) ^ 0x00ffffffu) & 0x00ffffffu);

    private static WriteableBitmap CreateSkin(int width, int height, int dpi, bool slim)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        foreach (var face in SkinModelGeometry.Build(slim, height * 2 == width, false)
                     .Where(face => !face.Overlay && !(height * 2 == width && face.Part.StartsWith("Left", StringComparison.Ordinal))))
            Fill(bitmap, face.Texture, face.Side == "Back" ? BackColor(face.Part) : PartColor(face.Part));
        return bitmap;
    }

    private static WriteableBitmap CreateCape(int width, int height, int dpi)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Fill(bitmap, new Rect(1, 1, 10, 16), 0xffeeaa22u);
        Fill(bitmap, new Rect(12, 1, 10, 16), 0xffaa55eeu);
        return bitmap;
    }

    private static WriteableBitmap Copy(Bitmap source)
    {
        var bitmap = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        source.CopyPixels(buffer, AlphaFormat.Unpremul);
        return bitmap;
    }

    private static void Fill(WriteableBitmap bitmap, Rect region, uint color)
    {
        var scale = bitmap.PixelSize.Width / 64;
        using var buffer = bitmap.Lock();
        for (var y = (int)region.Y * scale; y < region.Bottom * scale; y++)
            for (var x = (int)region.X * scale; x < region.Right * scale; x++)
                Marshal.WriteInt32(buffer.Address, y * buffer.RowBytes + x * 4, unchecked((int)color));
    }

    private static RenderTargetBitmap Render(SkinModelPreview control)
    {
        control.Measure(ViewSize);
        control.Arrange(new Rect(ViewSize));
        var result = new RenderTargetBitmap(new PixelSize((int)ViewSize.Width, (int)ViewSize.Height), new Vector(96, 96));
        result.Render(control);
        return result;
    }

    private static uint PixelAtPart(Bitmap bitmap, string part, string side, double yaw, bool slim, bool legacy, bool cape = false)
    {
        var face = Assert.Single(SkinModelGeometry.Project(SkinModelGeometry.Build(slim, legacy, cape), yaw, ViewSize),
            candidate => candidate.Face.Part == part && candidate.Face.Side == side);
        var center = new Point((face.Across.X + face.Down.X) / 2, (face.Across.Y + face.Down.Y) / 2);
        return Pixel(bitmap, (int)center.X, (int)center.Y);
    }

    private static uint Pixel(Bitmap bitmap, int x, int y)
    {
        using var copied = Copy(bitmap);
        using var buffer = copied.Lock();
        return unchecked((uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4));
    }

    private static int CountPixels(Bitmap bitmap, uint color)
    {
        using var copied = Copy(bitmap);
        using var buffer = copied.Lock();
        var count = 0;
        for (var y = 0; y < bitmap.PixelSize.Height; y++)
            for (var x = 0; x < bitmap.PixelSize.Width; x++)
                if (unchecked((uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4)) == color) count++;
        return count;
    }

    private static int CountOpaque(Bitmap bitmap)
    {
        using var copied = Copy(bitmap);
        using var buffer = copied.Lock();
        var count = 0;
        for (var y = 0; y < bitmap.PixelSize.Height; y++)
            for (var x = 0; x < bitmap.PixelSize.Width; x++)
                if ((unchecked((uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4)) >> 24) > 0) count++;
        return count;
    }

    private static void Capture(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_SKIN_MODEL_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"));
    }

    private static void Send(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
