using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WMCL.App.Controls;

/// <summary>
/// Orthographic, nearest-texel renderer. Every sample has its own interpolated depth;
/// intersecting sleeves/capes never rely on a whole-face painter order. Transparency
/// is resolved per pixel in bounded 32x32 tiles, not a viewport-sized fragment list.
/// </summary>
internal sealed class SkinModelRasterizer
{
    internal sealed record Texture(int Width, int Height, uint[] Pixels)
    {
        internal static Texture Read(Bitmap bitmap)
        {
            var size = bitmap.PixelSize;
            if (size.Width > 4096 || size.Height > 4096) throw new InvalidDataException("Skin preview texture is too large.");
            using var copy = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var buffer = copy.Lock();
            bitmap.CopyPixels(buffer);
            var row = new int[size.Width];
            var pixels = new uint[checked(size.Width * size.Height)];
            for (var y = 0; y < size.Height; y++)
            {
                Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
                for (var x = 0; x < row.Length; x++) pixels[y * size.Width + x] = unchecked((uint)row[x]);
            }
            return new(size.Width, size.Height, pixels);
        }
    }

    private readonly record struct Surface(SkinModelGeometry.Face Face, Texture Texture,
        double X, double Y, double Ax, double Ay, double Bx, double By, double Det,
        double Depth, double DepthA, double DepthB, int Left, int Top, int Right, int Bottom);
    private const int TileSide = 32;
    private int[] _counts = [];
    private double[] _opaqueDepth = [];
    private uint[] _opaque = [];
    private double[] _depths = [];
    private uint[] _colors = [];

    internal uint[] Render(Texture skin, Texture? cape, IReadOnlyList<SkinModelGeometry.Face> faces,
        int width, int height, double yaw, double pitch, double zoom = 1, double panX = 0, double panY = 0)
    {
        if (width is < 1 or > 768 || height is < 1 or > 768) throw new ArgumentOutOfRangeException(nameof(width));
        var radians = pitch * Math.PI / 180;
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        var scale = Math.Min(width / 25d, height / 37d) * zoom;
        (double X, double Y, double Z) Project(SkinModelGeometry.Vertex vertex)
        {
            var rotated = SkinModelGeometry.Rotate(vertex, yaw);
            return (width / 2d + rotated.X * scale + panX,
                height / 2d + ((rotated.Y - 16) * cos - rotated.Z * sin) * scale + panY,
                rotated.Z * cos + (rotated.Y - 16) * sin);
        }
        var surfaces = new List<Surface>(faces.Count);
        foreach (var face in faces)
        {
            var texture = face.Cape ? cape : skin;
            if (texture is null) continue;
            var normal = SkinModelGeometry.Rotate(face.Normal, yaw);
            var facing = normal.Z * cos + normal.Y * sin;
            // Clothing and capes have two visible sides. A transparent texel must not
            // erase the far side; the opaque player's geometry provides occlusion.
            if (!face.Overlay && !face.Cape && facing >= -1e-8) continue;
            var origin = Project(face.Origin);
            var a = Project(face.Origin + face.Across);
            var b = Project(face.Origin + face.Down);
            var ax = a.X - origin.X;
            var ay = a.Y - origin.Y;
            var bx = b.X - origin.X;
            var by = b.Y - origin.Y;
            var determinant = ax * by - ay * bx;
            if (Math.Abs(determinant) < 1e-8) continue;
            var xs = new[] { origin.X, a.X, b.X, a.X + b.X - origin.X };
            var ys = new[] { origin.Y, a.Y, b.Y, a.Y + b.Y - origin.Y };
            surfaces.Add(new(face, texture, origin.X, origin.Y, ax, ay, bx, by, determinant,
                origin.Z, a.Z - origin.Z, b.Z - origin.Z,
                Math.Clamp((int)Math.Floor(xs.Min()), 0, width), Math.Clamp((int)Math.Floor(ys.Min()), 0, height),
                Math.Clamp((int)Math.Ceiling(xs.Max()), 0, width), Math.Clamp((int)Math.Ceiling(ys.Max()), 0, height)));
        }
        var stride = Math.Max(1, surfaces.Count);
        EnsureCapacity(stride);
        var output = new uint[width * height];
        for (var tileY = 0; tileY < height; tileY += TileSide)
            for (var tileX = 0; tileX < width; tileX += TileSide)
            {
                Array.Fill(_opaqueDepth, double.PositiveInfinity);
                Array.Clear(_opaque);
                Array.Clear(_counts);
                foreach (var surface in surfaces) Raster(surface, false);
                foreach (var surface in surfaces) Raster(surface, true);
                for (var y = tileY; y < Math.Min(height, tileY + TileSide); y++)
                    for (var x = tileX; x < Math.Min(width, tileX + TileSide); x++)
                    {
                        var index = (y - tileY) * TileSide + x - tileX;
                        var start = index * stride;
                        var count = _counts[index];
                        // Insert small per-pixel fragment sets far-to-near.
                        for (var i = 1; i < count; i++)
                        {
                            var d = _depths[start + i];
                            var color = _colors[start + i];
                            var j = i - 1;
                            while (j >= 0 && _depths[start + j] < d - 1e-7)
                            {
                                _depths[start + j + 1] = _depths[start + j];
                                _colors[start + j + 1] = _colors[start + j];
                                j--;
                            }
                            _depths[start + j + 1] = d;
                            _colors[start + j + 1] = color;
                        }
                        var result = Premultiply(_opaque[index]);
                        for (var i = 0; i < count; i++) result = Over(_colors[start + i], result);
                        output[y * width + x] = result;
                    }

                void Raster(Surface surface, bool transparent)
                {
                    var left = Math.Max(tileX, surface.Left);
                    var top = Math.Max(tileY, surface.Top);
                    var right = Math.Min(tileX + TileSide, surface.Right);
                    var bottom = Math.Min(tileY + TileSide, surface.Bottom);
                    var uv = surface.Face.Texture;
                    var textureScale = surface.Texture.Width / 64d;
                    var minX = Math.Max(0, (int)Math.Round(uv.X * textureScale));
                    var minY = Math.Max(0, (int)Math.Round(uv.Y * textureScale));
                    var maxX = Math.Min(surface.Texture.Width - 1, (int)Math.Round(uv.Right * textureScale) - 1);
                    var maxY = Math.Min(surface.Texture.Height - 1, (int)Math.Round(uv.Bottom * textureScale) - 1);
                    for (var y = top; y < bottom; y++)
                        for (var x = left; x < right; x++)
                        {
                            var dx = x + 0.5 - surface.X;
                            var dy = y + 0.5 - surface.Y;
                            var u = (dx * surface.By - dy * surface.Bx) / surface.Det;
                            var v = (dy * surface.Ax - dx * surface.Ay) / surface.Det;
                            if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
                            var tx = Math.Clamp((int)Math.Floor((uv.X + uv.Width * u) * textureScale), minX, maxX);
                            var ty = Math.Clamp((int)Math.Floor((uv.Y + uv.Height * v) * textureScale), minY, maxY);
                            var color = surface.Texture.Pixels[ty * surface.Texture.Width + tx];
                            var alpha = color >> 24;
                            if (alpha == 0 || (transparent ? alpha == 255 : alpha != 255)) continue;
                            var depth = surface.Depth + surface.DepthA * u + surface.DepthB * v;
                            var index = (y - tileY) * TileSide + x - tileX;
                            // Coplanar/intersecting inflated parts: opaque beats alpha at
                            // equal depth; equal opaque samples retain original face order.
                            // This tolerance is in model units, independent of zoom/DPI.
                            if (depth >= _opaqueDepth[index] - 1e-7) continue;
                            if (!transparent)
                            {
                                _opaqueDepth[index] = depth;
                                _opaque[index] = color;
                            }
                            else
                            {
                                var slot = index * stride + _counts[index]++;
                                _depths[slot] = depth;
                                _colors[slot] = color;
                            }
                        }
                }
            }
        return output;
    }

    private void EnsureCapacity(int faces)
    {
        const int pixels = TileSide * TileSide;
        if (_counts.Length == 0)
        {
            _counts = new int[pixels];
            _opaqueDepth = new double[pixels];
            _opaque = new uint[pixels];
        }
        if (_depths.Length < pixels * faces)
        {
            _depths = new double[pixels * faces];
            _colors = new uint[pixels * faces];
        }
    }
    private static uint Premultiply(uint color)
    {
        var alpha = color >> 24;
        return alpha << 24 | ((color >> 16 & 255) * alpha / 255) << 16 |
            ((color >> 8 & 255) * alpha / 255) << 8 | (color & 255) * alpha / 255;
    }
    private static uint Over(uint source, uint destination)
    {
        var alpha = source >> 24;
        var inverse = 255 - alpha;
        uint Channel(int shift) => (((source >> shift) & 255) * alpha + ((destination >> shift) & 255) * inverse + 127) / 255;
        var outAlpha = alpha + ((destination >> 24) * inverse + 127) / 255;
        return outAlpha << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
}
