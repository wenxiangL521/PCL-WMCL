using WMCL.App.Services.Infrastructure;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class SkinCameraDiagnosisTests
{
    [AvaloniaFact]
    public void ExtremeTopViewReproducesTheReportedFlatteningWithoutLosingBodyGeometry()
    {
        var source = Environment.GetEnvironmentVariable("WMCL_SKIN_REAL_TEXTURE_PATH");
        using var stream = string.IsNullOrWhiteSpace(source)
            ? AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png")) : File.OpenRead(source);
        using var skin = new Bitmap(stream);
        var texture = SkinModelRasterizer.Texture.Read(skin);
        var renderer = new SkinModelRasterizer();
        var faces = SkinModelGeometry.Build(WMCL.App.Services.Infrastructure.SkinModelDetector.Detect(skin) == "slim", skin.PixelSize.Height * 2 == skin.PixelSize.Width, false);
        var normal = renderer.Render(texture, null, faces, 207, 308, -20, 10);
        var extreme = renderer.Render(texture, null, faces, 207, 308, -20, 89);
        var extremeBottom = renderer.Render(texture, null, faces, 207, 308, -20, -89);
        Assert.True(Height(extremeBottom, 207, 308) < 120);
        var oblique = renderer.Render(texture, null, faces, 207, 308, -20, 60);
        var normalHeight = Height(normal, 207, 308);
        var extremeHeight = Height(extreme, 207, 308);
        var obliqueHeight = Height(oblique, 207, 308);
        Assert.True(normalHeight > 220, $"Normal height={normalHeight}");
        Assert.True(extremeHeight < 120, $"Extreme height={extremeHeight}");
        Assert.True(obliqueHeight > extremeHeight * 1.5, $"oblique={obliqueHeight}, extreme={extremeHeight}");
        // The mesh is not deleted: when the occluding head is removed, the body still
        // rasterizes from precisely the same camera pose.
        var bodyOnly = faces.Where(face => face.Part is "Body" or "Jacket" or "RightLeg" or "LeftLeg" or "RightPants" or "LeftPants").ToArray();
        var body = renderer.Render(texture, null, bodyOnly, 207, 308, -20, 89);
        Assert.Contains(body, pixel => pixel >> 24 > 0);
        Save(normal, 207, 308, "camera-normal");
        Save(extreme, 207, 308, "camera-extreme-top-reproduction");
        Save(extremeBottom, 207, 308, "camera-extreme-bottom-reproduction");
        Save(oblique, 207, 308, "camera-60-degree-oblique");
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FreeOrbitCrossesBothPolesAndDraggingBackIsImmediate(int direction)
    {
        var preview = new SkinModelPreview();
        var window = new Window { Content = preview, Width = 207, Height = 308 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var origin = new Point(100, 150);
            var beyondPole = origin + new Vector(0, -200 * direction);
            Send(window, "MouseMove", origin, Avalonia.Input.RawInputModifiers.None);
            Send(window, "MouseDown", origin, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Send(window, "MouseMove", beyondPole, Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Assert.Equal(Normalize(10 + 100 * direction), preview.Pitch);
            Send(window, "MouseMove", beyondPole + new Vector(0, -20 * direction), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Assert.Equal(Normalize(10 + 110 * direction), preview.Pitch);
            Send(window, "MouseMove", beyondPole, Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Assert.Equal(Normalize(10 + 100 * direction), preview.Pitch);
            Send(window, "MouseUp", beyondPole, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            preview.Pitch = 300 * direction;
            Assert.Equal(Normalize(300 * direction), preview.Pitch);
            preview.ResetView();
            Assert.Equal(10, preview.Pitch);
            Assert.Equal(-25, preview.Yaw);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FullVerticalRotationsStayRenderableAndMatchTheProjectionHelper()
    {
        using var input = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
        using var skin = new Bitmap(input);
        var texture = SkinModelRasterizer.Texture.Read(skin);
        var faces = SkinModelGeometry.Build(false, false, true);
        var preview = new SkinModelPreview();
        foreach (var angle in new[] { -720d, -450, -270, -180, -95, -90, 90, 95, 180, 270, 450, 720 })
        {
            preview.Pitch = angle;
            Assert.Equal(Normalize(angle), preview.Pitch);
            var projected = SkinModelGeometry.Project(faces, -25, new Size(207, 308), angle);
            var equivalent = SkinModelGeometry.Project(faces, -25, new Size(207, 308), preview.Pitch);
            Assert.Equal(projected.Count, equivalent.Count);
            foreach (var face in projected)
            {
                var sameFace = Assert.Single(equivalent, candidate => candidate.Face.Part == face.Face.Part && candidate.Face.Side == face.Face.Side);
                Assert.Equal(face.Origin.X, sameFace.Origin.X, 7);
                Assert.Equal(face.Origin.Y, sameFace.Origin.Y, 7);
                Assert.Equal(face.Across.X, sameFace.Across.X, 7);
                Assert.Equal(face.Across.Y, sameFace.Across.Y, 7);
                Assert.Equal(face.Down.X, sameFace.Down.X, 7);
                Assert.Equal(face.Down.Y, sameFace.Down.Y, 7);
            }
            var pixels = new SkinModelRasterizer().Render(texture, null, faces, 207, 308, -25, preview.Pitch);
            Assert.Contains(pixels, pixel => pixel >> 24 > 0);
        }
    }

    private static double Normalize(double angle) => ((angle % 360) + 540) % 360 - 180;

    private static void Send(Window window, string method, params object?[] args) => typeof(AvaloniaHeadlessPlatform).Assembly
        .GetType("Avalonia.Headless.IHeadlessWindow")!.GetMethod(method)!.Invoke(window.PlatformImpl, args);

    private static int Height(uint[] pixels, int width, int height)
    {
        var first = height;
        var last = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (pixels[y * width + x] >> 24 > 0) { first = Math.Min(first, y); last = Math.Max(last, y); }
        return last < first ? 0 : last - first + 1;
    }
    private static void Save(uint[] pixels, int width, int height, string name)
    {
        var output = Environment.GetEnvironmentVariable("WMCL_SKIN_CAMERA_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels.AsSpan(y * width, width).ToArray().Select(pixel => unchecked((int)pixel)).ToArray(), 0,
                    buffer.Address + y * buffer.RowBytes, width);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
}
