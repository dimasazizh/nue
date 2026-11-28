// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// NUE adaptation: rasterize armour protection on one spoke per body part.
using System.Globalization;
using System.Numerics;

namespace Nue.Unified;

internal static class ArmourRadarImage
{
    private const int Size = 256;
    internal static readonly string[] Metrics = ["Coverage", "Cut", "Blunt"];
    internal static readonly Vector4[] Palette =
    [
        new(1, .62f, .22f, 1), new(.30f, .88f, 1, 1), new(.57f, 1, .39f, 1),
        new(1, .45f, .81f, 1), new(1, .91f, .33f, 1)
    ];
    static readonly Dictionary<string, string> s_made = new(StringComparer.Ordinal);
    static string s_dir = "";

    /// <summary>The image property value for a chart ("file:<path>"), drawing it when new; "" when it cannot be made.</summary>
    internal static Vector2 AxisDirection(int axis, int count)
    {
        float angle = MathF.Tau * axis / count;
        return new(MathF.Sin(angle), -MathF.Cos(angle));
    }

    internal static string For(Vector3[] values, int[] colours)
    {
        string key = string.Join(",", colours) + "|" +
            string.Join(";", values.Select(v => FormattableString.Invariant($"{v.X:0.000},{v.Y:0.000},{v.Z:0.000}")));
        if (s_made.TryGetValue(key, out var path)) return "file:" + path;
        try
        {
            if (s_dir.Length == 0)
            {
                s_dir = Path.Combine(Path.GetTempPath(), "NUE", "radar-" + Environment.ProcessId);
                Directory.CreateDirectory(s_dir);
            }
            string name = Fnv(key).ToString("x16", CultureInfo.InvariantCulture);
            path = Path.Combine(s_dir, "radar_" + name + ".tga");
            File.WriteAllBytes(path, Tga(Draw(values, colours)));
            s_made[key] = path;
            return "file:" + path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>A 64-bit FNV-1a hash of the chart's values (a file name; not security).</summary>
    static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    static byte[] Draw(Vector3[] values, int[] colours)
    {
        var px = new float[Size * Size * 4];   // straight RGBA, 0..1
        var web = (R: 0.62f, G: 0.56f, B: 0.46f, A: 0.55f);
        (double X, double Y) Vertex(int axis, double fraction)
        {
            var direction = AxisDirection(axis, values.Length);
            double radius = Size * .40 * fraction;
            return (Size / 2.0 + direction.X * radius, Size / 2.0 + direction.Y * radius);
        }
        for (int r = 1; r <= 3; r++)
            for (int a = 0; a < values.Length; a++)
            {
                var p = Vertex(a, r / 3.0);
                var q = Vertex(a + 1, r / 3.0);
                Stroke(px, p.X, p.Y, q.X, q.Y, 1.2f, web);
            }
        for (int a = 0; a < values.Length; a++)
        {
            var p = Vertex(a, 1.0);
            Stroke(px, Size / 2.0, Size / 2.0, p.X, p.Y, 1.2f, web);
        }
        for (int metric = 0; metric < Metrics.Length; metric++)
        {
            var colour = Palette[colours[metric]];
            for (int a = 0; a < values.Length; a++)
            {
                int next = (a + 1) % values.Length;
                var p = Vertex(a, values[a][metric]);
                var q = Vertex(next, values[next][metric]);
                Stroke(px, p.X, p.Y, q.X, q.Y, 2.8f, (colour.X, colour.Y, colour.Z, colour.W));
            }
        }
        var o = new byte[Size * Size * 4];
        for (int i = 0; i < px.Length; i++) o[i] = (byte)Math.Clamp((int)(px[i] * 255 + 0.5f), 0, 255);
        return o;
    }

    /// <summary>An anti-aliased line of width w: coverage from the distance to the segment, composited over what is there.</summary>
    static void Stroke(float[] px, double x0, double y0, double x1, double y1, float w, (float R, float G, float B, float A) c)
    {
        double half = w / 2.0;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - half - 1)), maxX = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(x0, x1) + half + 1));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - half - 1)), maxY = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(y0, y1) + half + 1));
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                double t = len2 > 0 ? Math.Clamp(((cx - x0) * dx + (cy - y0) * dy) / len2, 0, 1) : 0;
                double ex = cx - (x0 + t * dx), ey = cy - (y0 + t * dy);
                float cov = (float)Math.Clamp(half + 0.5 - Math.Sqrt(ex * ex + ey * ey), 0, 1) * c.A;
                if (cov <= 0) continue;
                int i = (y * Size + x) * 4;
                float a0 = px[i + 3], a1 = cov + a0 * (1 - cov);
                if (a1 <= 0) continue;
                px[i] = (c.R * cov + px[i] * a0 * (1 - cov)) / a1;
                px[i + 1] = (c.G * cov + px[i + 1] * a0 * (1 - cov)) / a1;
                px[i + 2] = (c.B * cov + px[i + 2] * a0 * (1 - cov)) / a1;
                px[i + 3] = a1;
            }
    }

    /// <summary>An uncompressed 32-bit TGA, top-left origin, from RGBA bytes.</summary>
    public static byte[] Tga(byte[] rgba)
    {
        var o = new byte[18 + rgba.Length];
        o[2] = 2;                                           // uncompressed true colour
        o[12] = Size & 0xFF; o[13] = Size >> 8; o[14] = Size & 0xFF; o[15] = Size >> 8;
        o[16] = 32;
        o[17] = 0x28;                                       // 8 alpha bits, top-left origin
        for (int i = 0; i < rgba.Length; i += 4)
        {
            o[18 + i] = rgba[i + 2];
            o[18 + i + 1] = rgba[i + 1];
            o[18 + i + 2] = rgba[i];
            o[18 + i + 3] = rgba[i + 3];
        }
        return o;
    }
}
