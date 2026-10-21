using System.Numerics;

namespace AlienInvasion.Engine;

/// <summary>Procedural texture generation. All textures are RGBA8, tileable.</summary>
public static class TexGen
{
    public const int Size = 256;

    // ---------- noise ----------
    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

    /// <summary>Tileable value noise; u,v in [0,1), period = lattice cells across the texture.</summary>
    public static float Noise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int xa = Mod(x0, period), xb = Mod(x0 + 1, period), ya = Mod(y0, period), yb = Mod(y0 + 1, period);
        float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    public static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0;
        int p = basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(u, v, p, seed + o * 131) * amp;
            norm += amp;
            amp *= gain;
            p *= 2;
        }
        return sum / norm;
    }

    /// <summary>Voronoi cell distance (F1), tileable.</summary>
    public static float Cells(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int cx = (int)MathF.Floor(x), cy = (int)MathF.Floor(y);
        float best = 9;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                float px = gx + Hash(Mod(gx, period), Mod(gy, period), seed);
                float py = gy + Hash(Mod(gx, period), Mod(gy, period), seed + 7);
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < best) best = d;
            }
        return MathF.Sqrt(best);
    }

    static float Sat(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    static float Smooth(float a, float b, float x) { float t = Sat((x - a) / (b - a)); return t * t * (3 - 2 * t); }

    static byte[] Make(int w, int h, Func<float, float, Vector4> f)
    {
        var data = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                var c = f((x + 0.5f) / w, (y + 0.5f) / h);
                int i = (y * w + x) * 4;
                data[i] = (byte)(Sat(c.X) * 255);
                data[i + 1] = (byte)(Sat(c.Y) * 255);
                data[i + 2] = (byte)(Sat(c.Z) * 255);
                data[i + 3] = (byte)(Sat(c.W) * 255);
            }
        });
        return data;
    }

    // ---------- alien infestation overlay ----------
    // Alpha < 1 encodes self-illumination (glowing veins) used by the level shader.
    static Vector4 Infest(Vector4 baseCol, float u, float v, float amount, int seed, Vector3 fleshCol, Vector3 veinCol)
    {
        if (amount <= 0.001f) return baseCol;
        float n = Fbm(u, v, 4, 5, seed + 900);
        float edge = 1 - amount * 0.85f;
        float cover = Smooth(edge - 0.06f, edge + 0.04f, n);
        if (cover <= 0) return baseCol;
        float cell = Cells(u, v, 10, seed + 77);
        float bumps = 0.6f + 0.4f * Sat(cell * 1.4f);
        var flesh = fleshCol * bumps * (0.7f + 0.5f * Fbm(u, v, 16, 3, seed + 31));
        float vein = 1 - Smooth(0.0f, 0.025f, MathF.Abs(Fbm(u, v, 6, 4, seed + 500) - 0.5f));
        vein *= cover * Smooth(0.2f, 0.9f, amount + 0.3f) * Smooth(0.45f, 0.75f, Fbm(u, v, 3, 3, seed + 600));
        var col = Vector3.Lerp(new Vector3(baseCol.X, baseCol.Y, baseCol.Z), flesh, cover);
        col = Vector3.Lerp(col, veinCol, vein * 0.8f);
        float glow = vein * 0.45f;
        return new Vector4(col, 1 - glow);
    }

    public struct Theme
    {
        public Vector3 Wall, WallAccent, Floor, Ceiling, Stripe, Flesh, Vein;
        public float Rust, Damage, Infest;
    }

    public static Theme ThemeFor(int level)
    {
        float d = (level - 1) / 29f;
        var t = new Theme();
        if (level <= 7)
        {
            t.Wall = new(0.55f, 0.57f, 0.58f); t.WallAccent = new(0.28f, 0.36f, 0.30f);
            t.Floor = new(0.42f, 0.43f, 0.45f); t.Ceiling = new(0.62f, 0.63f, 0.64f); t.Stripe = new(0.85f, 0.7f, 0.15f);
        }
        else if (level <= 15)
        {
            t.Wall = new(0.48f, 0.45f, 0.40f); t.WallAccent = new(0.45f, 0.30f, 0.18f);
            t.Floor = new(0.36f, 0.34f, 0.32f); t.Ceiling = new(0.45f, 0.44f, 0.42f); t.Stripe = new(0.9f, 0.55f, 0.1f);
        }
        else if (level <= 23)
        {
            t.Wall = new(0.50f, 0.50f, 0.54f); t.WallAccent = new(0.45f, 0.12f, 0.12f);
            t.Floor = new(0.30f, 0.30f, 0.33f); t.Ceiling = new(0.40f, 0.40f, 0.44f); t.Stripe = new(0.8f, 0.15f, 0.12f);
        }
        else
        {
            t.Wall = new(0.36f, 0.33f, 0.38f); t.WallAccent = new(0.30f, 0.12f, 0.35f);
            t.Floor = new(0.26f, 0.24f, 0.28f); t.Ceiling = new(0.30f, 0.27f, 0.32f); t.Stripe = new(0.5f, 0.15f, 0.6f);
        }
        t.Flesh = Vector3.Lerp(new(0.35f, 0.30f, 0.22f), new(0.38f, 0.12f, 0.36f), d);
        t.Vein = Vector3.Lerp(new(0.5f, 0.9f, 0.3f), new(0.3f, 1.0f, 0.7f), d);
        t.Rust = level <= 7 ? 0.1f : level <= 15 ? 0.6f : 0.35f;
        t.Damage = d;
        t.Infest = level < 4 ? 0 : MathF.Pow((level - 3) / 27f, 1.1f) * 0.95f;
        return t;
    }

    public static byte[] Wall(Theme t, int seed) => Make(Size, Size, (u, v) =>
    {
        // Panels: 2 across, 2 high
        float pu = (u * 2) % 1, pv = (v * 2) % 1;
        float seam = MathF.Min(MathF.Min(pu, 1 - pu), MathF.Min(pv, 1 - pv));
        float seamDark = Smooth(0.0f, 0.015f, seam);
        float bevel = Smooth(0.012f, 0.03f, seam);
        float grime = Fbm(u, v, 8, 5, seed);
        float concrete = 0.75f + 0.35f * Fbm(u, v, 32, 3, seed + 5);
        var col = t.Wall * concrete * (0.8f + 0.4f * grime);
        // accent band near bottom (v from 0 = top to 1 = bottom)
        if (v > 0.72f && v < 0.88f) col = t.WallAccent * (0.8f + 0.3f * grime);
        if (v > 0.70f && v < 0.72f || v > 0.88f && v < 0.90f) col *= 0.5f;
        // hazard stripe at very bottom
        if (v > 0.93f)
        {
            float s = ((u + v) * 16) % 1;
            col = s < 0.5f ? t.Stripe * (0.8f + 0.2f * grime) : new Vector3(0.08f);
        }
        // rivets
        float ru = (u * 8) % 1, rv = (v * 8) % 1;
        float rd = MathF.Sqrt((ru - 0.5f) * (ru - 0.5f) + (rv - 0.5f) * (rv - 0.5f));
        if ((pv < 0.08f || pv > 0.92f) && rd < 0.12f) col *= 1.25f - rd * 2;
        col *= 0.55f + 0.45f * seamDark;
        col *= 0.9f + 0.1f * bevel;
        // rust / drips
        float rust = Fbm(u, v * 0.25f, 12, 4, seed + 40);
        float drip = Smooth(0.6f - t.Rust * 0.2f, 0.75f, Fbm(u, 0, 64, 2, seed + 41) + v * 0.3f) * t.Rust;
        col = Vector3.Lerp(col, new Vector3(0.35f, 0.18f, 0.08f) * (0.6f + rust), Sat(drip * 0.7f + (rust - 0.7f) * t.Rust * 2));
        // scorch / damage
        float dmg = Fbm(u, v, 3, 4, seed + 70);
        col *= 1 - Smooth(0.75f - t.Damage * 0.2f, 0.9f, dmg) * 0.7f;
        return Infest(new Vector4(col, 1), u, v, t.Infest, seed, t.Flesh, t.Vein);
    });

    public static byte[] Floor(Theme t, int seed) => Make(Size, Size, (u, v) =>
    {
        // diamond tread plate in 2x2 tiles
        float pu = (u * 2) % 1, pv = (v * 2) % 1;
        float seam = MathF.Min(MathF.Min(pu, 1 - pu), MathF.Min(pv, 1 - pv));
        float du = (u * 24) % 1, dv = (v * 24) % 1;
        int row = (int)(v * 24);
        float tread;
        if (row % 2 == 0) tread = MathF.Abs(du - dv);
        else tread = MathF.Abs(du + dv - 1);
        float bump = 1 - Smooth(0.05f, 0.18f, tread);
        float grime = Fbm(u, v, 8, 5, seed);
        var col = t.Floor * (0.75f + 0.35f * grime) * (0.9f + 0.25f * bump);
        col *= 0.45f + 0.55f * Smooth(0.0f, 0.02f, seam);
        float wear = Fbm(u, v, 16, 3, seed + 3);
        col = Vector3.Lerp(col, col * 1.3f, Smooth(0.6f, 0.8f, wear) * 0.5f);
        float rust = Fbm(u, v, 10, 4, seed + 9);
        col = Vector3.Lerp(col, new Vector3(0.3f, 0.16f, 0.07f), Smooth(0.65f, 0.85f, rust) * t.Rust);
        float blood = Fbm(u, v, 5, 4, seed + 99);
        col = Vector3.Lerp(col, new Vector3(0.2f, 0.35f, 0.05f), Smooth(0.78f - t.Damage * 0.12f, 0.82f, blood) * 0.6f * t.Damage);
        return Infest(new Vector4(col, 1), u, v, t.Infest * 0.8f, seed + 3, t.Flesh, t.Vein);
    });

    public static byte[] Ceiling(Theme t, int seed) => Make(Size, Size, (u, v) =>
    {
        float pu = (u * 3) % 1, pv = (v * 3) % 1;
        float seam = MathF.Min(MathF.Min(pu, 1 - pu), MathF.Min(pv, 1 - pv));
        float grime = Fbm(u, v, 8, 4, seed);
        var col = t.Ceiling * (0.7f + 0.4f * grime);
        col *= 0.3f + 0.7f * Smooth(0.01f, 0.04f, seam);
        float holes = (u * 48) % 1, holesV = (v * 48) % 1;
        if (MathF.Abs(holes - 0.5f) < 0.15f && MathF.Abs(holesV - 0.5f) < 0.15f) col *= 0.8f;
        float stain = Fbm(u, v, 4, 4, seed + 13);
        col *= 1 - Smooth(0.6f, 0.85f, stain) * 0.5f;
        return Infest(new Vector4(col, 1), u, v, t.Infest, seed + 7, t.Flesh, t.Vein);
    });

    public static byte[] Crate(int seed) => Make(Size, Size, (u, v) =>
    {
        float edge = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        float grime = Fbm(u, v, 8, 4, seed);
        var green = new Vector3(0.25f, 0.30f, 0.18f) * (0.8f + 0.4f * grime);
        var col = green;
        float planks = (v * 6) % 1;
        col *= 0.85f + 0.15f * Smooth(0.0f, 0.06f, MathF.Min(planks, 1 - planks));
        if (edge < 0.09f) col = new Vector3(0.18f, 0.2f, 0.14f) * (0.8f + 0.4f * grime);
        float diag = MathF.Abs(u - v);
        if (diag < 0.05f && edge >= 0.09f) col = new Vector3(0.2f, 0.23f, 0.15f);
        // stencil block
        if (u > 0.3f && u < 0.7f && v > 0.42f && v < 0.58f)
        {
            float su = (u - 0.3f) / 0.4f * 5;
            if ((su % 1) < 0.65f && Fbm(u, v, 64, 1, seed + 3) > 0.3f) col = Vector3.Lerp(col, new Vector3(0.85f, 0.85f, 0.75f), 0.8f);
        }
        float bolt = MathF.Sqrt(MathF.Pow((u * 4 % 1) - 0.5f, 2) + MathF.Pow((v * 4 % 1) - 0.5f, 2));
        if (edge < 0.09f && bolt < 0.1f) col *= 1.5f;
        return new Vector4(col, 1);
    });

    /// <summary>Mostly light grey so it can be tinted by key colour; hazard stripes at the edges.</summary>
    public static byte[] Door(int seed) => Make(Size, Size, (u, v) =>
    {
        float grime = Fbm(u, v, 8, 4, seed);
        var col = new Vector3(0.75f) * (0.85f + 0.25f * grime);
        float hu = MathF.Abs(u - 0.5f);
        if (v < 0.12f || v > 0.88f)
        {
            float s = ((u + v) * 10) % 1;
            col = s < 0.5f ? new Vector3(0.95f, 0.8f, 0.1f) : new Vector3(0.1f);
        }
        else if (hu < 0.012f) col = new Vector3(0.1f);
        else
        {
            float pv = (v * 5) % 1;
            col *= 0.8f + 0.2f * Smooth(0, 0.05f, MathF.Min(pv, 1 - pv));
            if (v > 0.45f && v < 0.55f && hu > 0.05f && hu < 0.4f) return new Vector4(1, 1, 1, 0.2f); // glowing band
        }
        return new Vector4(col, 1);
    });

    public static byte[] Elevator(int seed) => Make(Size, Size, (u, v) =>
    {
        float edge = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        float grime = Fbm(u, v, 8, 4, seed);
        var col = new Vector3(0.3f, 0.32f, 0.35f) * (0.8f + 0.3f * grime);
        float gu = (u * 20) % 1, gv = (v * 20) % 1;
        if (gu < 0.2f || gv < 0.2f) col *= 0.5f;
        if (edge < 0.1f)
        {
            float s = ((u + v) * 12) % 1;
            col = s < 0.5f ? new Vector3(0.95f, 0.75f, 0.1f) : new Vector3(0.08f);
        }
        if (edge > 0.1f && edge < 0.13f) return new Vector4(0.2f, 1.0f, 0.4f, 0.1f);
        return new Vector4(col, 1);
    });

    public static byte[] AlienSkin(int seed) => Make(Size, Size, (u, v) =>
    {
        float c = Cells(u, v, 12, seed);
        float n = Fbm(u, v, 8, 4, seed + 1);
        float g = 0.65f + 0.35f * Smooth(0.05f, 0.5f, c);
        g *= 0.8f + 0.35f * n;
        float spots = Smooth(0.62f, 0.7f, Fbm(u, v, 6, 3, seed + 2));
        return new Vector4(g * (1 - spots * 0.35f), g * (1 - spots * 0.2f), g * (1 - spots * 0.3f), 1);
    });

    public static byte[] Metal(int seed) => Make(Size, Size, (u, v) =>
    {
        float brushed = Fbm(u * 0.1f, v, 64, 3, seed);
        float n = Fbm(u, v, 8, 4, seed + 1);
        float g = 0.75f + 0.2f * brushed + 0.15f * n;
        return new Vector4(g, g, g, 1);
    });

    public static byte[] Particle(int size = 64)
    {
        var d = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float a = Sat(1 - r);
                a = a * a * (3 - 2 * a);
                int i = (y * size + x) * 4;
                d[i] = d[i + 1] = d[i + 2] = 255;
                d[i + 3] = (byte)(a * 255);
            }
        return d;
    }

    public static byte[] Smoke(int seed, int size = 64)
    {
        var d = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float dx = u * 2 - 1, dy = v * 2 - 1;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float n = Fbm(u, v, 4, 4, seed);
                float a = Sat(1 - r * (1.2f - n * 0.6f));
                int i = (y * size + x) * 4;
                d[i] = d[i + 1] = d[i + 2] = 255;
                d[i + 3] = (byte)(a * a * 255);
            }
        return d;
    }

    public static byte[] Portal(int seed) => Make(Size, Size, (u, v) =>
    {
        float dx = u * 2 - 1, dy = v * 2 - 1;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        float a = MathF.Atan2(dy, dx) / MathF.Tau + 0.5f;
        float swirl = Fbm((a + r * 0.8f) % 1, r * 0.999f, 6, 4, seed);
        float alpha = Sat(1 - Smooth(0.85f, 1.0f, r));
        var col = Vector3.Lerp(new Vector3(0.4f, 0.05f, 0.7f), new Vector3(0.2f, 1f, 0.7f), Smooth(0.4f, 0.8f, swirl));
        col = Vector3.Lerp(col, new Vector3(1f, 0.9f, 1f), Smooth(0.25f, 0f, r) * 0.9f);
        return new Vector4(col * (0.6f + swirl * 0.8f), alpha);
    });

    /// <summary>
    /// Tangent-space normal map derived from a colour texture's luminance (dark = recessed).
    /// Alpha stores the height. Tileable (wraps at the edges).
    /// </summary>
    public static byte[] NormalMap(byte[] rgba, int size, float strength)
    {
        var lum = new float[size * size];
        for (int i = 0; i < lum.Length; i++)
            lum[i] = (rgba[i * 4] * 0.299f + rgba[i * 4 + 1] * 0.587f + rgba[i * 4 + 2] * 0.114f) / 255f;
        // light blur so per-pixel noise doesn't turn into glitter
        var h = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        sum += lum[Mod(y + oy, size) * size + Mod(x + ox, size)];
                h[y * size + x] = sum / 9f;
            }
        var outp = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float H(int dx, int dy) => h[Mod(y + dy, size) * size + Mod(x + dx, size)];
                float gx = (H(1, -1) + 2 * H(1, 0) + H(1, 1)) - (H(-1, -1) + 2 * H(-1, 0) + H(-1, 1));
                float gy = (H(-1, 1) + 2 * H(0, 1) + H(1, 1)) - (H(-1, -1) + 2 * H(0, -1) + H(1, -1));
                var n = Vector3.Normalize(new Vector3(-gx * strength, -gy * strength, 1));
                int i = (y * size + x) * 4;
                outp[i] = (byte)((n.X * 0.5f + 0.5f) * 255);
                outp[i + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255);
                outp[i + 2] = (byte)((n.Z * 0.5f + 0.5f) * 255);
                outp[i + 3] = (byte)(Sat(h[y * size + x]) * 255);
            }
        });
        return outp;
    }

    public static byte[] White() => new byte[] { 255, 255, 255, 255 };
}
