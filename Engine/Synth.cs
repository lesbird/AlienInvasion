namespace AlienInvasion.Engine;

/// <summary>State-variable filter usable with per-sample cutoff changes.</summary>
public struct Svf
{
    float low, band;
    public float Low, Band, High;
    public void Process(float x, float cutoff, float q = 0.7f)
    {
        float f = 2 * MathF.Sin(MathF.PI * MathF.Min(cutoff, Synth.Rate / 6.5f) / Synth.Rate);
        float damp = 1f / MathF.Max(q, 0.5f);
        low += f * band;
        float high = x - low - damp * band;
        band += f * high;
        Low = low; Band = band; High = high;
    }
    public float LP(float x, float c, float q = 0.7f) { Process(x, c, q); return Low; }
    public float BP(float x, float c, float q = 2f) { Process(x, c, q); return Band; }
    public float HP(float x, float c, float q = 0.7f) { Process(x, c, q); return High; }
}

/// <summary>Procedural sound effect synthesis.</summary>
public static class Synth
{
    public const int Rate = 44100;

    public static float[] Buf(float sec) => new float[(int)(sec * Rate)];
    static float Env(float t, float a, float d) => t < a ? t / a : MathF.Exp(-(t - a) * d);

    public static void Normalize(float[] b, float peak = 0.9f)
    {
        float m = 1e-6f;
        foreach (var s in b) m = MathF.Max(m, MathF.Abs(s));
        float k = peak / m;
        for (int i = 0; i < b.Length; i++) b[i] *= k;
        // tiny fade to avoid end clicks
        int f = Math.Min(200, b.Length / 4);
        for (int i = 0; i < f; i++) b[b.Length - 1 - i] *= i / (float)f;
    }

    public static short[] ToPcm(float[] b)
    {
        var s = new short[b.Length];
        for (int i = 0; i < b.Length; i++) s[i] = (short)(Math.Clamp(b[i], -1f, 1f) * 32000);
        return s;
    }

    // ---------------- weapons ----------------
    public static float[] Gunshot(float dur, float thumpHz, float noiseCut, float decay, float thumpAmt, int seed)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        Svf f = new(), f2 = new();
        float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            float cut = noiseCut * MathF.Exp(-t * 6) + 300;
            float noise = f.LP(n, cut, 0.8f) * Env(t, 0.001f, decay);
            float crack = f2.HP(n, 3000) * MathF.Exp(-t * 60) * 0.6f;
            ph += MathF.Tau * (thumpHz * (1 + 2 * MathF.Exp(-t * 40))) / Rate;
            float thump = MathF.Sin(ph) * MathF.Exp(-t * decay * 0.8f) * thumpAmt;
            b[i] = noise + thump + crack;
        }
        Normalize(b);
        return b;
    }

    public static float[] Knife(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.22f);
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            float env = MathF.Sin(MathF.PI * MathF.Min(1, t / 0.2f));
            b[i] = f.BP(n, 800 + 4000 * t / 0.2f, 3) * env;
        }
        Normalize(b, 0.6f);
        return b;
    }

    public static float[] Plasma(int seed, float baseHz = 1400, float dur = 0.35f)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        float ph = 0, ph2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float fr = baseHz * MathF.Exp(-t * 10) + 180;
            ph += MathF.Tau * fr / Rate;
            ph2 += MathF.Tau * fr * 1.5f / Rate;
            float s = MathF.Sin(ph) + 0.4f * MathF.Sign(MathF.Sin(ph2)) + 0.15f * ((float)r.NextDouble() - 0.5f);
            b[i] = s * Env(t, 0.003f, 9);
        }
        Normalize(b, 0.7f);
        return b;
    }

    public static float[] Acid(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.45f);
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            float wob = 600 + 500 * MathF.Sin(t * 70) + 1200 * MathF.Exp(-t * 8);
            b[i] = f.BP(n, wob, 5) * Env(t, 0.01f, 7);
        }
        Normalize(b, 0.8f);
        return b;
    }

    public static float[] Zap(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.18f);
        float ph = 0;
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            ph += 120f / Rate;
            float saw = (ph % 1) * 2 - 1;
            float crackle = r.NextDouble() < 0.02 ? ((float)r.NextDouble() * 2 - 1) * 3 : 0;
            float n = f.HP((float)r.NextDouble() * 2 - 1 + crackle, 2000);
            b[i] = (saw * 0.6f + n) * Env(t, 0.002f, 14);
        }
        Normalize(b, 0.7f);
        return b;
    }

    public static float[] VoidCannon(int seed)
    {
        var r = new Random(seed);
        var b = Buf(1.3f);
        float ph = 0, ph2 = 0;
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            ph += MathF.Tau * (90 * MathF.Exp(-t * 2) + 28) / Rate;
            ph2 += MathF.Tau * (200 + 1400 * t) / Rate;
            float n = f.LP((float)r.NextDouble() * 2 - 1, 600 * MathF.Exp(-t * 3) + 80);
            b[i] = (MathF.Sin(ph) * 1.2f + MathF.Sin(ph2) * 0.25f * MathF.Exp(-t * 4) + n * 1.5f) * Env(t, 0.01f, 2.5f);
        }
        Normalize(b);
        return b;
    }

    /// <summary>Rail gun: a capacitor snap and a sizzling electric crack with a falling ring-out.</summary>
    public static float[] Railgun(int seed)
    {
        var r = new Random(seed);
        var b = Buf(1.1f);
        float ph = 0, ph2 = 0;
        Svf f = new(), f2 = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            float crack = f.HP(n, 2500) * MathF.Exp(-t * 18) * 1.4f;
            float sizzle = f2.BP(n, 5000 * MathF.Exp(-t * 2) + 1500, 4) * MathF.Exp(-t * 4) * 0.8f;
            ph += MathF.Tau * (2400 * MathF.Exp(-t * 5) + 120) / Rate;
            ph2 += MathF.Tau * 55 / Rate;
            float ring = MathF.Sin(ph) * MathF.Exp(-t * 3.5f) * 0.45f;
            float thump = MathF.Sin(ph2) * MathF.Exp(-t * 9) * 0.9f;
            b[i] = (crack + sizzle + ring + thump) * Env(t, 0.001f, 2.2f);
        }
        Normalize(b);
        return b;
    }

    /// <summary>Radio squelch: a crackle of band-passed static and a two-tone chirp.</summary>
    public static float[] Radio(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.22f);
        Svf f = new();
        float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = f.BP((float)r.NextDouble() * 2 - 1, 2200, 1.5f) * (t < 0.08f ? 1 : MathF.Exp(-(t - 0.08f) * 40));
            float hz = t < 0.12f ? 1500 : 2000;
            ph += MathF.Tau * hz / Rate;
            float chirp = t > 0.06f && t < 0.18f ? MathF.Sin(ph) * 0.35f : 0;
            b[i] = n * 0.8f + chirp;
        }
        Normalize(b, 0.6f);
        return b;
    }

    /// <summary>The Mind: a breathy swell of filtered noise over a slow, sour drone.</summary>
    public static float[] Whisper(int seed)
    {
        var r = new Random(seed);
        var b = Buf(1.8f);
        Svf f = new(), f2 = new();
        float ph = 0, ph2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float env = MathF.Sin(MathF.PI * t / 1.8f);
            env *= env;
            float n = (float)r.NextDouble() * 2 - 1;
            float breath = f.BP(n, 700 + 600 * MathF.Sin(t * 3.1f) + 300 * MathF.Sin(t * 11), 6) * (0.6f + 0.4f * MathF.Sin(t * 23));
            float hiss = f2.HP(n, 5000) * 0.15f;
            ph += MathF.Tau * (72 + 3 * MathF.Sin(t * 2)) / Rate;
            ph2 += MathF.Tau * 101.5f / Rate;
            float drone = (MathF.Sin(ph) + MathF.Sin(ph2) * 0.7f) * 0.25f;
            b[i] = (breath + hiss + drone) * env;
        }
        Normalize(b, 0.7f);
        return b;
    }

    public static float[] Explosion(int seed, float dur = 1.6f)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        Svf f = new();
        float brown = 0, ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            brown = brown * 0.985f + n * 0.15f;
            float cut = 2500 * MathF.Exp(-t * 3) + 120;
            float body = f.LP(brown + n * 0.3f * MathF.Exp(-t * 10), cut, 0.6f);
            ph += MathF.Tau * (60 * MathF.Exp(-t * 3) + 30) / Rate;
            b[i] = (body * 2.2f + MathF.Sin(ph) * 0.8f * MathF.Exp(-t * 4)) * Env(t, 0.004f, 2.4f);
        }
        Normalize(b);
        return b;
    }

    public static float[] Launch(int seed, bool rocket)
    {
        var r = new Random(seed);
        var b = Buf(rocket ? 0.8f : 0.35f);
        Svf f = new();
        float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            ph += MathF.Tau * (130 * MathF.Exp(-t * 15) + 50) / Rate;
            float thump = MathF.Sin(ph) * MathF.Exp(-t * 18);
            float whoosh = rocket ? f.BP(n, 400 + 2500 * t, 1.5f) * Env(t, 0.05f, 3.5f) * 1.5f : f.LP(n, 900) * MathF.Exp(-t * 25);
            b[i] = thump + whoosh;
        }
        Normalize(b);
        return b;
    }

    public static float[] Clink(int seed)
    {
        var b = Buf(0.15f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            b[i] = (MathF.Sin(MathF.Tau * 1900 * t) + 0.6f * MathF.Sin(MathF.Tau * 2730 * t)) * MathF.Exp(-t * 35);
        }
        Normalize(b, 0.5f);
        return b;
    }

    public static float[] Click(float hz = 2500, float dur = 0.05f)
    {
        var b = Buf(dur);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            b[i] = MathF.Sin(MathF.Tau * hz * t) * MathF.Exp(-t * 120) + MathF.Sin(MathF.Tau * hz * 0.37f * t) * MathF.Exp(-t * 80) * 0.5f;
        }
        Normalize(b, 0.5f);
        return b;
    }

    public static float[] WeaponSwitch()
    {
        var b = Buf(0.25f);
        var c1 = Click(1800, 0.05f);
        var c2 = Click(1200, 0.08f);
        for (int i = 0; i < c1.Length; i++) b[i] += c1[i];
        int o = (int)(0.12f * Rate);
        for (int i = 0; i < c2.Length && o + i < b.Length; i++) b[o + i] += c2[i];
        Normalize(b, 0.45f);
        return b;
    }

    // ---------------- creatures ----------------
    public static float[] Growl(int seed, float baseHz, float dur, float pitchEnd = 1f, float rasp = 0.5f, float vib = 6)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        Svf f = new(), f2 = new();
        float ph = 0;
        float jitter = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float k = t / dur;
            if (i % 400 == 0) jitter = ((float)r.NextDouble() - 0.5f) * 0.2f;
            float fr = baseHz * GMath.Lerp(1, pitchEnd, k) * (1 + 0.12f * MathF.Sin(MathF.Tau * vib * t) + jitter);
            ph += fr / Rate;
            float saw = (ph % 1) * 2 - 1;
            float n = (float)r.NextDouble() * 2 - 1;
            float src = saw + n * rasp;
            float formant = f.BP(src, 500 + 400 * MathF.Sin(t * 9), 3) + f2.BP(src, 1400 + 300 * MathF.Sin(t * 5), 4) * 0.5f;
            float env = MathF.Min(1, t / 0.04f) * MathF.Min(1, (dur - t) / 0.15f);
            b[i] = formant * env;
        }
        Normalize(b, 0.85f);
        return b;
    }

    public static float[] Screech(int seed, float carrier, float dur)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        float pc = 0, pm = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float k = t / dur;
            float mod = carrier * 0.33f;
            pm += MathF.Tau * mod / Rate;
            float idx = 4 * (1 - k) + 1;
            pc += MathF.Tau * carrier * (1 + 0.3f * MathF.Sin(t * 30)) * (1 - 0.4f * k) / Rate;
            float s = MathF.Sin(pc + idx * MathF.Sin(pm)) + ((float)r.NextDouble() - 0.5f) * 0.3f;
            b[i] = s * MathF.Min(1, t / 0.02f) * MathF.Min(1, (dur - t) / 0.1f);
        }
        Normalize(b, 0.7f);
        return b;
    }

    public static float[] Splat(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.3f);
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            b[i] = f.LP(n, 1500 * MathF.Exp(-t * 10) + 200, 2) * Env(t, 0.002f, 14);
        }
        Normalize(b, 0.8f);
        return b;
    }

    public static float[] PlayerGrunt(int seed, float dur, float hz, float fall)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        Svf f1 = new(), f2 = new();
        float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float k = t / dur;
            ph += hz * (1 - fall * k) / Rate;
            float saw = (ph % 1) * 2 - 1 + ((float)r.NextDouble() - 0.5f) * 0.3f;
            float v = f1.BP(saw, 650, 5) + f2.BP(saw, 1100, 6) * 0.7f;
            b[i] = v * MathF.Min(1, t / 0.02f) * MathF.Min(1, (dur - t) / 0.08f);
        }
        Normalize(b, 0.8f);
        return b;
    }

    public static float[] Footstep(int seed)
    {
        var r = new Random(seed);
        var b = Buf(0.12f);
        Svf f = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            b[i] = f.LP(n, 900, 1.5f) * Env(t, 0.003f, 45) + MathF.Sin(MathF.Tau * 70 * t) * MathF.Exp(-t * 50) * 0.4f;
        }
        Normalize(b, 0.4f);
        return b;
    }

    // ---------------- UI / world ----------------
    public static float[] Notes(float[] hz, float noteLen, float decay, float harmonics = 0.3f, float square = 0)
    {
        var b = Buf(noteLen * hz.Length + 0.6f);
        for (int n = 0; n < hz.Length; n++)
        {
            int start = (int)(n * noteLen * Rate);
            for (int i = 0; start + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float s = MathF.Sin(MathF.Tau * hz[n] * t) + harmonics * MathF.Sin(MathF.Tau * hz[n] * 2.01f * t)
                          + square * MathF.Sign(MathF.Sin(MathF.Tau * hz[n] * t));
                b[start + i] += s * Env(t, 0.004f, decay);
            }
        }
        Normalize(b, 0.55f);
        return b;
    }

    public static float[] Door(int seed, float dur = 1.1f)
    {
        var r = new Random(seed);
        var b = Buf(dur);
        Svf f = new(), f2 = new();
        float brown = 0, ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            brown = brown * 0.99f + n * 0.1f;
            ph += 55f / Rate;
            float hum = ((ph % 1) * 2 - 1) * 0.3f;
            float rumble = f.LP(brown, 220) * 3;
            float hiss = f2.HP(n, 4000) * 0.4f * MathF.Exp(-t * 6);
            float env = MathF.Min(1, t / 0.05f) * MathF.Min(1, (dur - t) / 0.2f);
            float clunk = t > dur - 0.18f ? MathF.Sin(MathF.Tau * 80 * t) * MathF.Exp(-(t - dur + 0.18f) * 30) * 1.5f : 0;
            b[i] = (hum + rumble + hiss) * env + clunk;
        }
        Normalize(b, 0.7f);
        return b;
    }

    public static float[] Buzzer()
    {
        var b = Buf(0.45f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            bool on = t < 0.18f || (t > 0.24f && t < 0.42f);
            float s = MathF.Sign(MathF.Sin(MathF.Tau * 140 * t)) * 0.5f + MathF.Sign(MathF.Sin(MathF.Tau * 147 * t)) * 0.5f;
            b[i] = on ? s : 0;
        }
        Normalize(b, 0.35f);
        return b;
    }

    public static float[] Elevator(int seed)
    {
        var r = new Random(seed);
        float dur = 3.5f;
        var b = Buf(dur);
        Svf f = new();
        float ph = 0, brown = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float n = (float)r.NextDouble() * 2 - 1;
            brown = brown * 0.99f + n * 0.1f;
            ph += (60 + 8 * MathF.Sin(t * 2)) / Rate;
            float motor = ((ph % 1) * 2 - 1) * 0.25f + f.LP(brown, 300) * 2;
            float env = MathF.Min(1, t / 0.5f) * MathF.Min(1, (dur - 0.6f - t) / 0.5f);
            env = MathF.Max(env, 0);
            float clunk = MathF.Sin(MathF.Tau * 70 * t) * MathF.Exp(-t * 20) * 1.5f;
            float t2 = t - (dur - 0.55f);
            float ding = t2 > 0 ? (MathF.Sin(MathF.Tau * 1318 * t2) + 0.5f * MathF.Sin(MathF.Tau * 2637 * t2)) * MathF.Exp(-t2 * 5) * 0.6f : 0;
            b[i] = motor * env + clunk + ding;
        }
        Normalize(b, 0.7f);
        return b;
    }

    /// <summary>Seamless 2 second loop.</summary>
    public static float[] PortalHum(int seed)
    {
        var r = new Random(seed);
        var b = Buf(2f);
        float[] fr = { 41, 61.5f, 82, 123, 246.5f };
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float s = 0;
            for (int k = 0; k < fr.Length; k++)
                s += MathF.Sin(MathF.Tau * fr[k] * t + k) * (1f / (k + 1)) * (0.7f + 0.3f * MathF.Sin(MathF.Tau * 0.5f * (k + 1) * t));
            b[i] = s;
        }
        Normalize(b, 0.8f);
        return b;
    }

    /// <summary>Pressurised steam blast: bright hiss with a soft attack and tail.</summary>
    public static float[] Steam(int seed)
    {
        var b = Buf(1.7f);
        var r = new Random(seed);
        Svf f = new(), g = new();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float env = MathF.Min(1, t / 0.06f) * (t < 1.35f ? 1 : MathF.Max(0, 1 - (t - 1.35f) / 0.35f));
            float n = (float)r.NextDouble() * 2 - 1;
            b[i] = (f.HP(n, 2200) * 0.7f + g.BP(n, 5200, 1.5f) * 0.5f) * env * (0.85f + 0.15f * MathF.Sin(t * 37));
        }
        Normalize(b, 0.7f);
        return b;
    }

    /// <summary>Two-tone alarm klaxon.</summary>
    public static float[] Klaxon()
    {
        var b = Buf(1.1f);
        Svf f = new();
        float ph = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float hz = (int)(t / 0.275f) % 2 == 0 ? 640 : 480;
            ph += hz / Rate;
            float sq = (ph % 1) < 0.5f ? 1 : -1;
            float env = MathF.Min(1, t / 0.02f) * MathF.Min(1, (1.1f - t) / 0.08f);
            b[i] = f.LP(sq, 2400, 1.2f) * env;
        }
        Normalize(b, 0.7f);
        return b;
    }

    /// <summary>Generator spinning down (or back up) with mains hum.</summary>
    public static float[] PowerSweep(bool down)
    {
        var b = Buf(1.6f);
        float ph = 0, ph2 = 0;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate, k = t / 1.6f;
            float hz = down ? 260 * MathF.Pow(0.12f, k) : 30 + 230 * k * k;
            ph += hz / Rate; ph2 += hz * 2.01f / Rate;
            float env = down ? (1 - k) * MathF.Min(1, t / 0.03f) : MathF.Min(1, t / 0.3f) * MathF.Min(1, (1.6f - t) / 0.2f);
            b[i] = (MathF.Sin(MathF.Tau * ph) * 0.6f + ((ph2 % 1) * 2 - 1) * 0.25f) * env;
        }
        Normalize(b, 0.75f);
        return b;
    }

    public static float[] Shatter(int seed)
    {
        var r = new Random(seed);
        var b = Buf(1.2f);
        for (int k = 0; k < 30; k++)
        {
            float hz = 1500 + (float)r.NextDouble() * 5000;
            int start = (int)(r.NextDouble() * 0.4 * Rate);
            float dec = 6 + (float)r.NextDouble() * 10;
            for (int i = 0; start + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                b[start + i] += MathF.Sin(MathF.Tau * hz * t) * MathF.Exp(-t * dec) * 0.3f;
            }
        }
        var ex = Explosion(seed, 1.2f);
        for (int i = 0; i < b.Length; i++) b[i] += ex[i] * 0.6f;
        Normalize(b);
        return b;
    }
}
