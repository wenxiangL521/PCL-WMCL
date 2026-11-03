using Avalonia;

namespace WMCL.App.Controls;

/// <summary>Minecraft's public skin/cape box UV layout. Coordinates are model pixels, not screen pixels.</summary>
internal static class SkinModelGeometry
{
    internal readonly record struct Vertex(double X, double Y, double Z)
    {
        public static Vertex operator +(Vertex a, Vertex b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vertex operator *(Vertex value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    }

    internal sealed record Face(string Part, string Side, Vertex Origin, Vertex Across, Vertex Down,
        Vertex Normal, Rect Texture, bool Overlay = false, bool Cape = false, bool Mirror = false)
    {
        public Vertex Center => Origin + Across * 0.5 + Down * 0.5;
    }

    internal readonly record struct ProjectedFace(Face Face, Point Origin, Point Across, Point Down, double Depth);

    internal static IReadOnlyList<Face> Build(bool slim, bool legacy, bool cape)
    {
        var result = new List<Face>(78);
        Box("Head", -4, 0, -4, 8, 8, 8, 0, 0);
        Box("Body", -4, 8, -2, 8, 12, 4, 16, 16);
        var arm = slim && !legacy ? 3 : 4;
        Box("RightArm", -4 - arm, 8, -2, arm, 12, 4, 40, 16);
        Box("LeftArm", 4, 8, -2, arm, 12, 4, legacy ? 40 : 32, legacy ? 16 : 48, mirror: legacy);
        Box("RightLeg", -4, 20, -2, 4, 12, 4, 0, 16);
        Box("LeftLeg", 0, 20, -2, 4, 12, 4, legacy ? 0 : 16, legacy ? 16 : 48, mirror: legacy);
        Box("Hat", -4, 0, -4, 8, 8, 8, 32, 0, inflation: 0.5);
        if (!legacy)
        {
            Box("Jacket", -4, 8, -2, 8, 12, 4, 16, 32, inflation: 0.25);
            Box("RightSleeve", -4 - arm, 8, -2, arm, 12, 4, 40, 32, inflation: 0.25);
            Box("LeftSleeve", 4, 8, -2, arm, 12, 4, 48, 48, inflation: 0.25);
            Box("RightPants", -4, 20, -2, 4, 12, 4, 0, 32, inflation: 0.25);
            Box("LeftPants", 0, 20, -2, 4, 12, 4, 0, 48, inflation: 0.25);
        }
        if (cape) Box("Cape", -5, 8, 2.6, 10, 16, 1, 0, 0, isCape: true);
        return result;

        void Box(string part, double x, double y, double z, int w, int h, int d, int u, int v,
            double inflation = 0, bool mirror = false, bool isCape = false)
        {
            var left = x - inflation;
            var top = y - inflation;
            var front = z - inflation;
            var right = x + w + inflation;
            var bottom = y + h + inflation;
            var back = z + d + inflation;
            var width = right - left;
            var height = bottom - top;
            var depth = back - front;
            // The cape hangs behind the body: its +Z face is the outer decorated face.
            // Minecraft's cape atlas puts that face at (1,1), and its body-facing lining at (12,1).
            Add("Front", new(left, top, front), new(width, 0, 0), new(0, height, 0), new(0, 0, -1),
                new(isCape ? 12 : u + d, v + d, w, h));
            Add("Back", new(right, top, back), new(-width, 0, 0), new(0, height, 0), new(0, 0, 1),
                new(isCape ? 1 : u + 2 * d + w, v + d, w, h));
            Add("Right", new(left, top, back), new(0, 0, -depth), new(0, height, 0), new(-1, 0, 0), new(isCape ? 11 : u, v + d, d, h));
            Add("Left", new(right, top, front), new(0, 0, depth), new(0, height, 0), new(1, 0, 0), new(isCape ? 0 : u + d + w, v + d, d, h));
            Add("Top", isCape ? new(right, top, front) : new(left, top, back),
                new(isCape ? -width : width, 0, 0), new(0, 0, isCape ? depth : -depth), new(0, -1, 0), new(u + d, v, w, d));
            Add("Bottom", isCape ? new(right, bottom, front) : new(left, bottom, back),
                new(isCape ? -width : width, 0, 0), new(0, 0, isCape ? depth : -depth), new(0, 1, 0), new(u + d + w, v, w, d));

            void Add(string side, Vertex origin, Vertex across, Vertex down, Vertex normal, Rect uv)
            {
                // Old skins reuse the right limb with its geometry reflected, not merely its front face flipped.
                if (mirror)
                {
                    var center = x + w / 2;
                    origin = new Vertex(2 * center - origin.X, origin.Y, origin.Z);
                    across = new Vertex(-across.X, across.Y, across.Z);
                    down = new Vertex(-down.X, down.Y, down.Z);
                    normal = new Vertex(-normal.X, normal.Y, normal.Z);
                }
                result.Add(new Face(part, side, origin, across, down, normal, uv, inflation > 0, isCape));
            }
        }
    }

    internal static Vertex Rotate(Vertex point, double yaw)
    {
        var radians = yaw * Math.PI / 180;
        return new(point.X * Math.Cos(radians) + point.Z * Math.Sin(radians), point.Y,
            -point.X * Math.Sin(radians) + point.Z * Math.Cos(radians));
    }

    internal static IReadOnlyList<ProjectedFace> Project(IReadOnlyList<Face> faces, double yaw, Size size,
        double pitch = 10, double zoom = 1, double panX = 0, double panY = 0)
    {
        yaw = double.IsFinite(yaw) ? yaw : -25;
        var radians = (double.IsFinite(pitch) ? pitch % 360 : 10) * Math.PI / 180;
        zoom = Math.Clamp(double.IsFinite(zoom) ? zoom : 1, 0.5, 3);
        panX = double.IsFinite(panX) ? panX : 0;
        panY = double.IsFinite(panY) ? panY : 0;
        var scale = Math.Max(0, Math.Min(size.Width / 25, size.Height / 37)) * zoom;
        Point Screen(Vertex value)
        {
            var rotated = Rotate(value, yaw);
            return new Point(size.Width / 2 + rotated.X * scale + panX,
                size.Height / 2 + ((rotated.Y - 16) * Math.Cos(radians) - rotated.Z * Math.Sin(radians)) * scale + panY);
        }
        double FacingDepth(Vertex normal)
        {
            var rotated = Rotate(normal, yaw);
            return rotated.Z * Math.Cos(radians) + rotated.Y * Math.Sin(radians);
        }
        double Depth(Vertex value)
        {
            var rotated = Rotate(value, yaw);
            return rotated.Z * Math.Cos(radians) + (rotated.Y - 16) * Math.Sin(radians);
        }
        return faces.Where(face => FacingDepth(face.Normal) < -0.00001)
            .Select(face => new ProjectedFace(face, Screen(face.Origin),
                Screen(face.Origin + face.Across), Screen(face.Origin + face.Down), Depth(face.Center)))
            .OrderByDescending(face => face.Depth).ToArray();
    }
}
