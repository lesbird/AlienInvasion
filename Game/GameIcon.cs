using System.Numerics;
using AlienInvasion.Engine;

namespace AlienInvasion.Game;

/// <summary>The app icon, drawn procedurally like everything else: an alien head caught in a
/// targeting reticle on a dark rounded tile. Rendered with signed distance fields, so it is
/// crisp at any size.</summary>
public static class GameIcon
{
    static float Cov(float d, float px) => Math.Clamp(0.5f - d / px, 0, 1);
    static float Circle(Vector2 p, Vector2 c, float r) => (p - c).Length() - r;
    static float RoundBox(Vector2 p, Vector2 b, float r)
    {
        var q = Vector2.Abs(p) - b + new Vector2(r);
        return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) - r;
    }
    static float SMin(float a, float b, float k)
    {
        float h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
    }
    static float Ellipse(Vector2 p, Vector2 r) => (new Vector2(p.X / r.X, p.Y / r.Y).Length() - 1) * MathF.Min(r.X, r.Y);

    /// <summary>Head silhouette: a big cranium tapering into a narrow chin.</summary>
    static float Head(Vector2 p) => SMin(Circle(p, new(0, -0.14f), 0.4f), Circle(p, new(0, 0.33f), 0.1f), 0.32f);

    /// <summary>Top-down RGBA pixels of the icon at the given size.</summary>
    public static byte[] Render(int size)
    {
        var px = new byte[size * size * 4];
        float aa = 2f / size;
        float line = MathF.Max(0.022f, aa * 0.6f);   // keep the reticle visible at 16 px
        // eye frame: tilted so the outer corners sweep up
        float ea = 0.5f;
        var axis = new Vector2(MathF.Cos(ea), -MathF.Sin(ea));
        var perp = new Vector2(-axis.Y, axis.X);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1);
                Vector3 c = Vector3.Zero;   // premultiplied
                float a = 0;
                void Over(Vector3 col, float k) { c = col * k + c * (1 - k); a = k + a * (1 - k); }

                // tile: dark green-black gradient with a glow behind the head
                float dTile = RoundBox(p, new(0.8f), 0.36f);
                float tile = Cov(dTile, aa);
                if (tile <= 0) continue;
                var bg = Vector3.Lerp(new(0.07f, 0.15f, 0.11f), new(0.015f, 0.04f, 0.03f), (p.Y + 1) / 2);
                bg += new Vector3(0.15f, 0.65f, 0.3f) * 0.5f * MathF.Exp(-(p - new Vector2(0, -0.05f)).LengthSquared() * 3.2f);
                // faint radar rings
                float rr = p.Length();
                for (int i = 1; i <= 3; i++) bg += new Vector3(0.2f, 0.6f, 0.35f) * 0.06f * Cov(MathF.Abs(rr - i * 0.24f) - line * 0.4f, aa);
                Over(bg, tile);
                // HUD-style inner border
                Over(new(0.35f, 0.85f, 0.45f), 0.55f * Cov(MathF.Abs(dTile + 0.035f) - line * 0.45f, aa) * tile);

                // green halo around the head
                float dHead = Head(p);
                if (dHead > 0) { c += new Vector3(0.3f, 1f, 0.45f) * 0.55f * MathF.Exp(-dHead * 11) * tile; }

                // reticle: ring plus four ticks, drawn behind the head
                float ring = MathF.Abs(rr - 0.62f) - line;
                var ap = Vector2.Abs(p);
                float ticks = MathF.Min(RoundBox(ap - new Vector2(0, 0.62f), new(line, 0.13f), line * 0.5f),
                                        RoundBox(ap - new Vector2(0.62f, 0), new(0.13f, line), line * 0.5f));
                Over(new(1f, 0.26f, 0.18f), Cov(MathF.Min(ring, ticks), aa) * tile);

                // head: lit from the top, shaded towards the edges and chin
                float head = Cov(dHead, aa);
                if (head > 0)
                {
                    var skin = Vector3.Lerp(new(0.66f, 0.92f, 0.62f), new(0.2f, 0.45f, 0.26f), Math.Clamp((p.Y + 0.55f) / 1.0f, 0, 1));
                    float inner = Math.Clamp(-dHead / 0.14f, 0, 1);
                    skin = Vector3.Lerp(skin * 0.45f, skin, MathF.Sqrt(inner));
                    skin += new Vector3(0.9f, 1f, 0.9f) * 0.3f * MathF.Exp(-(p - new Vector2(-0.14f, -0.36f)).LengthSquared() * 28);
                    // mouth slit
                    skin = Vector3.Lerp(skin, new(0.08f, 0.18f, 0.1f), 0.8f * Cov(RoundBox(p - new Vector2(0, 0.3f), new(0.045f, 0.005f), 0.005f), aa));
                    Over(skin, head);

                    // eyes (mirrored): glossy black almonds with a highlight
                    var q = new Vector2(MathF.Abs(p.X), p.Y) - new Vector2(0.175f, 0.03f);
                    var l = new Vector2(Vector2.Dot(q, axis), Vector2.Dot(q, perp));
                    float eye = Cov(Ellipse(l, new(0.165f, 0.08f)), aa) * head;
                    var eyeCol = Vector3.Lerp(new(0.02f, 0.03f, 0.03f), new(0.05f, 0.16f, 0.1f), Math.Clamp(l.Y / 0.08f, 0, 1));
                    Over(eyeCol, eye);
                    // highlights share the head's upper-left light, so they aren't mirrored
                    var hc = new Vector2(MathF.Sign(p.X) * 0.175f - 0.05f, 0);
                    Over(Vector3.One, 0.85f * eye * Cov(Circle(p, hc, MathF.Max(0.018f, aa * 0.6f)), aa));
                    Over(Vector3.One, 0.35f * eye * Cov(Circle(p, hc + new Vector2(0.09f, 0.035f), 0.009f), aa));
                }

                int o = (y * size + x) * 4;
                var rgb = a > 0 ? c / a : Vector3.Zero;
                px[o] = (byte)(Math.Clamp(rgb.X, 0, 1) * 255 + 0.5f);
                px[o + 1] = (byte)(Math.Clamp(rgb.Y, 0, 1) * 255 + 0.5f);
                px[o + 2] = (byte)(Math.Clamp(rgb.Z, 0, 1) * 255 + 0.5f);
                px[o + 3] = (byte)(Math.Clamp(a, 0, 1) * 255 + 0.5f);
            }
        return px;
    }

    /// <summary>Writes icon.png (1024), a macOS .iconset folder and a Windows .ico into dir.</summary>
    public static void MakeAll(string dir)
    {
        Directory.CreateDirectory(dir);
        Screenshot.WritePng(Path.Combine(dir, "icon.png"), Render(1024), 1024, 1024);
        var set = Path.Combine(dir, "AlienInvasion.iconset");
        Directory.CreateDirectory(set);
        foreach (int s in new[] { 16, 32, 128, 256, 512 })
        {
            Screenshot.WritePng(Path.Combine(set, $"icon_{s}x{s}.png"), Render(s), s, s);
            Screenshot.WritePng(Path.Combine(set, $"icon_{s}x{s}@2x.png"), Render(s * 2), s * 2, s * 2);
        }
        // .ico with PNG-compressed entries (Vista and later)
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = sizes.Select(s => { var m = new MemoryStream(); Screenshot.WritePng(m, Render(s), s, s); return m.ToArray(); }).ToList();
        using var ico = new BinaryWriter(File.Create(Path.Combine(dir, "AlienInvasion.ico")));
        ico.Write((short)0); ico.Write((short)1); ico.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            ico.Write((byte)0); ico.Write((byte)0);
            ico.Write((short)1); ico.Write((short)32);
            ico.Write(pngs[i].Length); ico.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) ico.Write(p);
        Console.WriteLine($"Wrote icons to {dir}");
    }
}
