using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WMCL.App.Controls;
using static WMCL.App.Controls.SkinModelGeometry;

namespace WMCL.App.Tests;

public sealed class SkinDepthRendererTests
{
    [Fact]
    public void IntersectingQuadsUseInterpolatedPixelDepthNotTheAverageFaceDepth()
    {
        // Two crossing walls share the same centre depth. The left half of red is
        // nearer, the right half of green is nearer: no whole-face order can work.
        var red = new Face("Red", "Front", new(-6, 4, -4), new(12, 0, 8), new(0, 24, 0), new(0, 0, -1), new Rect(0, 0, 1, 1));
        var green = new Face("Green", "Front", new(-6, 4, 4), new(12, 0, -8), new(0, 24, 0), new(0, 0, -1), new Rect(1, 0, 1, 1));
        var pixels = new uint[64 * 64];
        pixels[0] = 0xffff0000;
        pixels[1] = 0xff00ff00;
        var texture = new SkinModelRasterizer.Texture(64, 64, pixels);
        var renderer = new SkinModelRasterizer();
        var frame = renderer.Render(texture, null, [red, green], 250, 370, 0, 0);
        Assert.Equal(0xffff0000u, frame[180 * 250 + 95]);
        Assert.Equal(0xff00ff00u, frame[180 * 250 + 155]);
        Assert.Equal(frame, renderer.Render(texture, null, [green, red], 250, 370, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CrossingTranslucentSurfacesBlendInPerPixelOrderAndOpaqueOccludersWin(int reverse)
    {
        var red = new Face("Red", "Front", new(-6, 4, -4), new(12, 0, 8), new(0, 24, 0), new(0, 0, -1), new Rect(0, 0, 1, 1), true);
        var blue = new Face("Blue", "Front", new(-6, 4, 4), new(12, 0, -8), new(0, 24, 0), new(0, 0, -1), new Rect(1, 0, 1, 1), true);
        var back = new Face("Back", "Front", new(-6, 4, 6), new(12, 0, 0), new(0, 24, 0), new(0, 0, -1), new Rect(2, 0, 1, 1));
        var pixels = new uint[64 * 64];
        pixels[0] = 0x80ff0000;
        pixels[1] = 0x800000ff;
        pixels[2] = 0xffffffff;
        var texture = new SkinModelRasterizer.Texture(64, 64, pixels);
        var renderer = new SkinModelRasterizer();
        Face[] faces = reverse == 0 ? [red, blue, back] : [back, blue, red];
        var frame = renderer.Render(texture, null, faces, 250, 370, 0, 0);
        Assert.Equal(0xffbf3f7fu, frame[180 * 250 + 95]);
        Assert.Equal(0xff7f3fbfu, frame[180 * 250 + 155]);
        // An opaque wall moved in front must eliminate every hidden translucent fragment.
        var foreground = back with { Origin = new(-6, 4, -8) };
        frame = renderer.Render(texture, null, [red, blue, foreground], 250, 370, 0, 0);
        Assert.Equal(0xffffffffu, frame[180 * 250 + 95]);
        Assert.Equal(0xffffffffu, frame[180 * 250 + 155]);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 0.00000001)]
    [InlineData(true, -0.00000001)]
    public void OpaqueFragmentsWinCoplanarTiesRegardlessOfAlphaFaceOrder(bool reverse, double perturbation)
    {
        var opaque = new Face("Opaque", "Front", new(-4, 4, 0), new(8, 0, 0), new(0, 24, 0), new(0, 0, -1), new Rect(0, 0, 1, 1));
        var alpha = opaque with { Part = "Alpha", Origin = new(-4, 4, perturbation), Texture = new Rect(1, 0, 1, 1), Overlay = true };
        var pixels = new uint[64 * 64];
        pixels[0] = 0xff00ff00;
        pixels[1] = 0x80ff0000;
        var frame = new SkinModelRasterizer().Render(new(64, 64, pixels), null,
            reverse ? [alpha, opaque] : [opaque, alpha], 250, 370, 0, 0);
        Assert.Equal(0xff00ff00u, frame[180 * 250 + 125]);
    }

    [Fact]
    public void EverySkinFaceUsesLiteralAtlasAndCornerDirectionsIncludingBottomAndMirroredLegacyLimbs()
    {
        // These atlas offsets are the public 64x64 skin layout, not obtained from Build().
        var atlas = new (string Part, int U, int V, int W, int H, int D)[]
        {
            ("Head",0,0,8,8,8), ("Body",16,16,8,12,4), ("RightArm",40,16,4,12,4),
            ("LeftArm",32,48,4,12,4), ("RightLeg",0,16,4,12,4), ("LeftLeg",16,48,4,12,4),
            ("Hat",32,0,8,8,8), ("Jacket",16,32,8,12,4), ("RightSleeve",40,32,4,12,4),
            ("LeftSleeve",48,48,4,12,4), ("RightPants",0,32,4,12,4), ("LeftPants",0,48,4,12,4)
        };
        foreach (var slim in new[] { false, true })
        {
            var faces = Build(slim, false, false);
            foreach (var part in atlas)
            {
                var w = slim && (part.Part.Contains("Arm") || part.Part.Contains("Sleeve")) ? 3 : part.W;
                Check("Front", new(part.U + part.D, part.V + part.D, w, part.H), 1, 0, 0, 1, 0);
                Check("Back", new(part.U + 2 * part.D + w, part.V + part.D, w, part.H), -1, 0, 0, 1, 0);
                Check("Right", new(part.U, part.V + part.D, part.D, part.H), 0, -1, 0, 1, 0);
                Check("Left", new(part.U + part.D + w, part.V + part.D, part.D, part.H), 0, 1, 0, 1, 0);
                Check("Top", new(part.U + part.D, part.V, w, part.D), 1, 0, 0, 0, -1);
                Check("Bottom", new(part.U + part.D + w, part.V, w, part.D), 1, 0, 0, 0, -1);
                void Check(string side, Rect uv, int ax, int az, int dx, int dy, int dz)
                {
                    var face = Assert.Single(faces, face => face.Part == part.Part && face.Side == side);
                    Assert.Equal(uv, face.Texture);
                    Assert.Equal(ax, Math.Sign(face.Across.X));
                    Assert.Equal(az, Math.Sign(face.Across.Z));
                    Assert.Equal(dx, Math.Sign(face.Down.X));
                    Assert.Equal(dy, Math.Sign(face.Down.Y));
                    Assert.Equal(dz, Math.Sign(face.Down.Z));
                    if (side is "Top" or "Bottom") Assert.True(face.Origin.Z > (face.Origin + face.Down).Z);
                }
            }
        }
        var legacy = Build(false, true, false);
        var leftBottom = Assert.Single(legacy, f => f.Part == "LeftLeg" && f.Side == "Bottom");
        Assert.Equal(new Rect(8, 16, 4, 4), leftBottom.Texture);
        Assert.True(leftBottom.Across.X < 0);
        Assert.True(leftBottom.Down.Z < 0);
        var cape = Build(false, false, true).Where(f => f.Cape).ToArray();
        Assert.Equal(new Rect(11, 1, 1, 16), Assert.Single(cape, f => f.Side == "Right").Texture);
        Assert.Equal(new Rect(0, 1, 1, 16), Assert.Single(cape, f => f.Side == "Left").Texture);
        foreach (var side in new[] { "Top", "Bottom" })
        {
            var face = Assert.Single(cape, f => f.Side == side);
            Assert.True(face.Across.X < 0);
            Assert.True(face.Down.Z > 0);
        }
    }

    [Fact]
    public void InvalidAndOversizedSourcesAreRejectedBeforeCachingPixels()
    {
        Assert.False(SkinModelPreview.ValidSkin(new PixelSize(8192, 8192)));
        Assert.False(SkinModelPreview.ValidSkin(new PixelSize(64, 48)));
        Assert.True(SkinModelPreview.ValidSkin(new PixelSize(256, 256)));
        Assert.True(SkinModelPreview.ValidSkin(new PixelSize(256, 128)));
    }

    [Theory]
    [InlineData(-65, 35, false, false, 64)]
    [InlineData(35, 55, false, false, 64)]
    [InlineData(145, -45, false, false, 64)]
    [InlineData(-135, 25, true, false, 128)]
    [InlineData(72, -65, true, false, 256)]
    [InlineData(-30, 18, false, true, 64)]
    [InlineData(175, 80, false, false, 64)]
    public void MultiViewPatternedSkinAndTransparentClothingMatchIndependentRayPlaneOracle(double yaw, double pitch, bool slim, bool legacy, int textureSize)
    {
        var texture = Pattern(textureSize, legacy ? textureSize / 2 : textureSize);
        var cape = Pattern(64, 32);
        var faces = Build(slim, legacy, true);
        const int width = 210, height = 280;
        var frame = new SkinModelRasterizer().Render(texture, cape, faces, width, height, yaw, pitch);
        var checkedPixels = 0;
        for (var y = 2; y < height; y += 4)
            for (var x = 2; x < width; x += 4)
            {
                var expected = RayPixel(x, y, width, height, yaw, pitch, faces, texture, cape);
                Assert.True(expected == frame[y * width + x], $"Pixel ({x},{y}) yaw={yaw}, pitch={pitch}: expected {expected:X8}, actual {frame[y * width + x]:X8}");
                checkedPixels++;
            }
        Assert.True(checkedPixels > 3000);
    }

    [AvaloniaFact]
    public void CachedSourcesRespectPhysicalPixelsAndDoNotChangeAfterAnOwnedFrameIsReleased()
    {
        using var stream = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WMCL_SKIN_REAL_TEXTURE_PATH"))
            ? AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"))
            : File.OpenRead(Environment.GetEnvironmentVariable("WMCL_SKIN_REAL_TEXTURE_PATH")!);
        using var skin = new Bitmap(stream);
        var texture = SkinModelRasterizer.Texture.Read(skin);
        Assert.Equal(skin.PixelSize.Width, texture.Width);
        var faces = Build(WMCL.App.Services.SkinModelDetector.Detect(skin) == "slim", skin.PixelSize.Height * 2 == skin.PixelSize.Width, false);
        var renderer = new SkinModelRasterizer();
        var frame = renderer.Render(texture, null, faces, 512, 768, -35, 40);
        Assert.Contains(frame, pixel => pixel >> 24 == 255);
        Assert.All(frame, pixel => Assert.True((pixel >> 24) <= 255));
        var output = Environment.GetEnvironmentVariable("WMCL_SKIN_MODEL_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var view in new[] { (-35d, 40d), (145d, 40d), (-35d, -40d), (145d, -40d) })
        {
            var pixels = renderer.Render(texture, null, faces, 384, 512, view.Item1, view.Item2);
            using var bitmap = new WriteableBitmap(new PixelSize(384, 512), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
                for (var y = 0; y < 512; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels.AsSpan(y * 384, 384).ToArray().Select(p => unchecked((int)p)).ToArray(), 0, buffer.Address + y * buffer.RowBytes, 384);
            bitmap.Save(Path.Combine(output, $"depth-steve-{view.Item1}-{view.Item2}.png"));
        }
    }

    private static SkinModelRasterizer.Texture Pattern(int width, int height)
    {
        var pixels = new uint[width * height];
        var scale = width / 64;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var u = x / scale;
                var v = y / scale;
                var alpha = (u + v) % 7 == 0 ? 0u : (u * 3 + v) % 5 == 0 ? 128u : 255u;
                pixels[y * width + x] = alpha << 24 | (uint)(u * 3 + 30) << 16 | (uint)(v * 3 + 20) << 8 | (uint)((u ^ v) * 3 + 10);
            }
        return new(width, height, pixels);
    }

    // Independent camera ray / plane intersections: no Project(), face centre order,
    // inverse screen-quad math, or renderer helpers are used for expected samples.
    private static uint RayPixel(int x, int y, int width, int height, double yaw, double pitch,
        IReadOnlyList<Face> faces, SkinModelRasterizer.Texture skin, SkinModelRasterizer.Texture cape)
    {
        var ya = yaw * Math.PI / 180;
        var pa = pitch * Math.PI / 180;
        var scale = Math.Min(width / 25d, height / 37d);
        var qx = (x + .5 - width / 2d) / scale;
        var qy = (y + .5 - height / 2d) / scale;
        var sy = 16 + qy * Math.Cos(pa);
        var sz = -qy * Math.Sin(pa);
        var start = new Vertex(qx * Math.Cos(ya) - sz * Math.Sin(ya), sy, qx * Math.Sin(ya) + sz * Math.Cos(ya));
        var direction = new Vertex(-Math.Cos(pa) * Math.Sin(ya), Math.Sin(pa), Math.Cos(pa) * Math.Cos(ya));
        var hits = new List<(double D, uint Color)>();
        foreach (var face in faces)
        {
            var denominator = Dot(face.Normal, direction);
            if (Math.Abs(denominator) < 1e-9 || !face.Overlay && !face.Cape && denominator >= 0) continue;
            var difference = new Vertex(face.Origin.X - start.X, face.Origin.Y - start.Y, face.Origin.Z - start.Z);
            var depth = Dot(difference, face.Normal) / denominator;
            var point = start + direction * depth;
            var offset = new Vertex(point.X - face.Origin.X, point.Y - face.Origin.Y, point.Z - face.Origin.Z);
            var u = Dot(offset, face.Across) / Dot(face.Across, face.Across);
            var v = Dot(offset, face.Down) / Dot(face.Down, face.Down);
            if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
            var texture = face.Cape ? cape : skin;
            var tx = Math.Clamp((int)Math.Floor((face.Texture.X + u * face.Texture.Width) * texture.Width / 64), 0, texture.Width - 1);
            var ty = Math.Clamp((int)Math.Floor((face.Texture.Y + v * face.Texture.Height) * texture.Width / 64), 0, texture.Height - 1);
            var color = texture.Pixels[ty * texture.Width + tx];
            if (color >> 24 != 0)
            {
                hits.Add((depth, color));
            }
        }
        // Opaque takes precedence at exactly coplanar intersections (1e-7 model
        // units). Transparent fragments strictly in front are composited far-to-near;
        // their original face ordinal breaks same-depth ties deterministically.
        var opaqueHits = hits.Select((hit, index) => (hit, index)).Where(pair => pair.hit.Color >> 24 == 255).ToArray();
        var nearestOpaque = opaqueHits.Length == 0 ? double.PositiveInfinity : opaqueHits.Min(pair => pair.hit.D);
        var opaque = opaqueHits.FirstOrDefault(pair => pair.hit.D <= nearestOpaque + 1e-7);
        var result = opaqueHits.Length == 0 ? 0u : opaque.hit.Color;
        var transparent = hits.Select((hit, index) => (hit, index))
            .Where(pair => pair.hit.Color >> 24 is > 0 and < 255 && pair.hit.D < nearestOpaque - 1e-7).ToList();
        transparent.Sort((a, b) => Math.Abs(a.hit.D - b.hit.D) <= 1e-7 ? a.index.CompareTo(b.index) : b.hit.D.CompareTo(a.hit.D));
        foreach (var pair in transparent)
        {
            var c = pair.hit.Color;
            var a = c >> 24;
            var inverse = 255 - a;
            uint Blend(int shift) => (((c >> shift) & 255) * a + ((result >> shift) & 255) * inverse + 127) / 255;
            result = (a + ((result >> 24) * inverse + 127) / 255) << 24 | Blend(16) << 16 | Blend(8) << 8 | Blend(0);
        }
        return result;
    }
    private static double Dot(Vertex a, Vertex b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
