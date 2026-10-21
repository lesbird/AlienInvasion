namespace AlienInvasion.Engine;

/// <summary>
/// Procedural action music, ported from the FPS2 project (src/Audio/Music.cs). Every level gets its own looping track, built from
/// oscillators, drums and note events into a single seamless 16-bit mono PCM buffer —
/// no audio files. The track's character (tempo, scale, root, chord progression, drum
/// groove and instrumentation) is derived from the level number so consecutive levels
/// always sound different, with the run seed adding per-playthrough melodic variation.
///
/// The buffer is designed to loop: every note is written with wrap-around indexing, so
/// a decay that runs past the end bleeds back into the start and there is no click or
/// gap at the loop point. Pure/deterministic — no OpenAL here, so it is unit-testable.
/// </summary>
public static class Music
{
    private const int SR = Synth.Rate; // 44100

    private enum Wave { Sine, Square, Saw, Tri }

    // Seven-note scales, as semitone offsets from the root. All lean dark/energetic
    // except the last (major), which shows up occasionally for a brighter level.
    private static readonly int[][] Scales =
    {
        new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor
        new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian  (metal/dread)
        new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian    (driving)
        new[] { 0, 2, 3, 5, 7, 8, 11 }, // harmonic minor (tense)
        new[] { 0, 2, 4, 5, 7, 9, 11 }, // major     (heroic)
    };

    // Bass root as a MIDI note (low register). Cycled per level.
    private static readonly int[] Roots = { 33, 31, 36, 29, 38, 34 }; // A1 G1 C2 F1 D2 Bb1

    // Chord progressions as scale-degree indices (0 = i, 5 = VI, 6 = VII, ...), one degree
    // per bar. Eight-bar phrases; a 16-bar loop plays each twice with a second-pass build.
    private static readonly int[][] Progressions =
    {
        new[] { 0, 0, 5, 5, 6, 6, 0, 4 },
        new[] { 0, 3, 4, 0, 5, 6, 0, 0 },
        new[] { 0, 6, 5, 4, 0, 6, 5, 0 },
        new[] { 0, 0, 3, 3, 6, 6, 4, 4 },
    };

    /// <summary>Builds the looping music track for a level (1-based). Deterministic in
    /// (level, seed). Returns 16-bit mono PCM sampled at <see cref="Synth.Rate"/>.</summary>
    public static short[] LevelTrack(int level, int seed)
    {
        int idx = Math.Max(0, level - 1);
        var rng = new Random(unchecked(seed * 73856093 ^ level * 19349663));

        // Distinct moduli give the combination a long period (LCM 60) so nearby levels
        // never share a full parameter set.
        int bpm = 122 + (idx % 6) * 7;                 // 122..157
        int[] scale = Scales[idx % Scales.Length];
        int rootMidi = Roots[idx % Roots.Length];
        int[] prog = Progressions[idx % Progressions.Length];
        int drumId = idx % 4;

        const int beatsPerBar = 4;
        const int bars = 16;
        double secPerBeat = 60.0 / bpm;
        int n = (int)Math.Round(bars * beatsPerBar * secPerBeat * SR);
        var b = new float[n];

        // Sample index of a musical position measured in beats from the loop start.
        double Smp(double beat) => beat * secPerBeat * SR;

        for (int bar = 0; bar < bars; bar++)
        {
            bool secondPass = bar >= bars / 2;   // build: leads/energy join halfway through
            int degree = prog[bar % prog.Length];
            double barBeat = bar * beatsPerBar;

            // --- Drums --------------------------------------------------------------
            for (int beat = 0; beat < beatsPerBar; beat++)
            {
                double t = barBeat + beat;
                Groove(b, drumId, beat, Smp, t, secPerBeat, rng);
            }

            // --- Bass: driving eighth-note root pulse -------------------------------
            float bassRoot = Hz(ScaleMidi(rootMidi, scale, degree));
            for (int e = 0; e < beatsPerBar * 2; e++)
            {
                double t = barBeat + e * 0.5;
                float accent = (e % 2 == 0) ? 0.34f : 0.24f;      // stress the downbeats
                // Occasional octave lift on the last eighth gives forward motion.
                float f = (e == beatsPerBar * 2 - 1 && (bar % 2 == 1)) ? bassRoot * 2f : bassRoot;
                Pluck(b, Smp(t), 0.42, f, accent, Wave.Saw, 0.28f, 0.02f);
            }

            // --- Pad: sustained triad, low in the mix for warmth -------------------
            float padAmp = secondPass ? 0.10f : 0.075f;
            foreach (int d in new[] { 0, 2, 4 })
                Pad(b, Smp(barBeat), secPerBeat * beatsPerBar,
                    Hz(ScaleMidi(rootMidi + 24, scale, degree + d)), padAmp, Wave.Tri, 0.06f, 0.12f);

            // --- Arp: sixteenth-note chord climb (the "action" energy) -------------
            float arpAmp = secondPass ? 0.16f : 0.13f;
            for (int s = 0; s < beatsPerBar * 4; s++)
            {
                int tone = (new[] { 0, 2, 4, 6 })[s % 4];         // root third fifth octave
                int octave = 24 + (s % 8 >= 4 ? 12 : 0);          // step up an octave mid-beat
                float f = Hz(ScaleMidi(rootMidi + octave, scale, degree + tone));
                Pluck(b, Smp(barBeat + s * 0.25), 0.16, f, arpAmp, Wave.Square, 0.09f, 0.004f);
            }

            // --- Lead: melodic phrase over the second half of the loop -------------
            if (secondPass)
            {
                for (int beat = 0; beat < beatsPerBar; beat++)
                {
                    if (rng.NextDouble() < 0.62)
                    {
                        int step = degree + 7 + rng.Next(-2, 5);  // an octave up, wandering
                        float f = Hz(ScaleMidi(rootMidi + 12, scale, step));
                        double dur = (rng.NextDouble() < 0.35 ? 1.0 : 0.5) * secPerBeat;
                        Lead(b, Smp(barBeat + beat), dur, f, 0.17f, Wave.Saw, 5.5f);
                    }
                }
            }
        }

        return Finish(b);
    }

    /// <summary>Same track as float samples in [-1, 1] for <see cref="SoundSystem.PlayMusic"/>.</summary>
    public static float[] LevelTrackF(int level, int seed)
    {
        var pcm = LevelTrack(level, seed);
        var f = new float[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) f[i] = pcm[i] / 32000f;
        return f;
    }

    // ---- arrangement helpers -------------------------------------------------------

    /// <summary>One beat of a drum groove. Four grooves keyed by <paramref name="drumId"/>.</summary>
    private static void Groove(float[] b, int drumId, int beat,
        Func<double, double> smp, double t, double secPerBeat, Random rng)
    {
        switch (drumId)
        {
            case 0: // four-on-the-floor
                Kick(b, smp(t));
                if (beat == 1 || beat == 3) Snare(b, smp(t), rng);
                for (int h = 0; h < 2; h++) Hat(b, smp(t + h * 0.5), h == 0 ? 0.5f : 0.35f, rng);
                break;
            case 1: // rock: kick 1 & the-of-2 & 3
                if (beat == 0 || beat == 2) Kick(b, smp(t));
                if (beat == 1) Kick(b, smp(t + 0.5));
                if (beat == 1 || beat == 3) Snare(b, smp(t), rng);
                for (int h = 0; h < 2; h++) Hat(b, smp(t + h * 0.5), 0.4f, rng);
                break;
            case 2: // driving: syncopated kick, sixteenth hats
                Kick(b, smp(t));
                if (beat == 0 || beat == 2) Kick(b, smp(t + 0.75));
                if (beat == 1 || beat == 3) Snare(b, smp(t), rng);
                for (int h = 0; h < 4; h++) Hat(b, smp(t + h * 0.25), h % 2 == 0 ? 0.42f : 0.26f, rng);
                break;
            default: // half-time heavy: kick 1&3, snare on 3, eighth hats
                if (beat == 0 || beat == 2) Kick(b, smp(t));
                if (beat == 2) Snare(b, smp(t), rng);
                for (int h = 0; h < 2; h++) Hat(b, smp(t + h * 0.5), 0.4f, rng);
                break;
        }
    }

    // ---- instruments (all wrap-around, loop-safe) ----------------------------------

    /// <summary>Plucked/decaying tone (bass, arp). Two slightly detuned oscillators for width.</summary>
    private static void Pluck(float[] b, double start, double durSec, float freq, float amp,
        Wave w, float tau, float attack)
    {
        int n = b.Length, i0 = (int)start, len = (int)(durSec * SR);
        double dp = 2.0 * Math.PI * freq / SR, dp2 = 2.0 * Math.PI * freq * 1.004 / SR;
        double p1 = 0, p2 = 0;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float env = MathF.Exp(-t / tau);
            if (t < attack) env *= t / attack;
            b[Mod(i0 + k, n)] += 0.5f * (Osc(w, p1) + Osc(w, p2)) * amp * env;
            p1 += dp; p2 += dp2;
        }
    }

    /// <summary>Sustained tone with attack/release ramps (pad, held chords).</summary>
    private static void Pad(float[] b, double start, double durSec, float freq, float amp,
        Wave w, float attack, float release)
    {
        int n = b.Length, i0 = (int)start, len = (int)(durSec * SR);
        double dp = 2.0 * Math.PI * freq / SR, dp2 = 2.0 * Math.PI * freq * 1.006 / SR;
        double p1 = 0, p2 = 0;
        float dur = (float)durSec;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float env = 1f;
            if (t < attack) env = t / attack;
            else if (t > dur - release) env = MathF.Max(0f, (dur - t) / release);
            b[Mod(i0 + k, n)] += 0.5f * (Osc(w, p1) + Osc(w, p2)) * amp * env;
            p1 += dp; p2 += dp2;
        }
    }

    /// <summary>Lead voice: sustained with light vibrato and a soft attack, then a tail.</summary>
    private static void Lead(float[] b, double start, double durSec, float freq, float amp,
        Wave w, float vibratoHz)
    {
        int n = b.Length, i0 = (int)start, len = (int)(durSec * SR);
        float dur = (float)durSec;
        double phase = 0;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float f = freq * (1f + 0.006f * MathF.Sin(2f * MathF.PI * vibratoHz * t));
            phase += 2.0 * Math.PI * f / SR;
            float env = MathF.Min(1f, t / 0.012f) * MathF.Min(1f, (dur - t) / 0.06f);
            b[Mod(i0 + k, n)] += Osc(w, phase) * amp * MathF.Max(0f, env);
        }
    }

    private static void Kick(float[] b, double start)
    {
        int n = b.Length, i0 = (int)start, len = (int)(0.18 * SR);
        double phase = 0;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float f = 120f * MathF.Exp(-t * 34f) + 46f;       // pitch drop = punch
            phase += 2.0 * Math.PI * f / SR;
            float env = MathF.Exp(-t / 0.075f);
            b[Mod(i0 + k, n)] += (float)Math.Sin(phase) * 0.95f * env;
        }
    }

    private static void Snare(float[] b, double start, Random rng)
    {
        int n = b.Length, i0 = (int)start, len = (int)(0.14 * SR);
        double phase = 0; float lp = 0;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float env = MathF.Exp(-t / 0.06f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += 0.7f * (noise - lp);                         // slightly darkened noise body
            phase += 2.0 * Math.PI * 190f / SR;
            float body = (float)Math.Sin(phase) * 0.35f + lp * 0.85f;
            b[Mod(i0 + k, n)] += body * 0.5f * env;
        }
    }

    private static void Hat(float[] b, double start, float amp, Random rng)
    {
        int n = b.Length, i0 = (int)start, len = (int)(0.05 * SR);
        float prev = 0;
        for (int k = 0; k < len; k++)
        {
            float t = k / (float)SR;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp = noise - prev; prev = noise;             // crude high-pass = bright hat
            b[Mod(i0 + k, n)] += hp * 0.3f * amp * MathF.Exp(-t / 0.02f);
        }
    }

    // ---- primitives ----------------------------------------------------------------

    private static int Mod(int i, int n)
    {
        i %= n;
        return i < 0 ? i + n : i;
    }

    private static float Osc(Wave w, double phase)
    {
        switch (w)
        {
            case Wave.Sine: return MathF.Sin((float)phase);
            case Wave.Square: return Math.Sin(phase) >= 0 ? 1f : -1f;
            case Wave.Saw:
            {
                double p = (phase / (2.0 * Math.PI)) % 1.0; if (p < 0) p += 1.0;
                return (float)(2.0 * p - 1.0);
            }
            default: // Tri
            {
                double p = (phase / (2.0 * Math.PI)) % 1.0; if (p < 0) p += 1.0;
                return (float)(p < 0.5 ? 4.0 * p - 1.0 : 3.0 - 4.0 * p);
            }
        }
    }

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    /// <summary>MIDI note for a scale degree, wrapping octaves for degrees outside 0..6.</summary>
    private static int ScaleMidi(int rootMidi, int[] scale, int degree)
    {
        int len = scale.Length;
        int oct = (int)Math.Floor(degree / (double)len);
        int di = Mod(degree, len);
        return rootMidi + oct * 12 + scale[di];
    }

    /// <summary>Normalise to a safe peak, soft-clip, convert to 16-bit. No edge fades —
    /// the buffer must loop seamlessly, and wrap-around note writes already join the ends.</summary>
    private static short[] Finish(float[] b)
    {
        float peak = 1e-4f;
        for (int i = 0; i < b.Length; i++) peak = MathF.Max(peak, MathF.Abs(b[i]));
        float norm = 0.82f / peak;
        var s = new short[b.Length];
        for (int i = 0; i < b.Length; i++)
            s[i] = (short)(MathF.Tanh(b[i] * norm * 1.1f) * 30000f);
        return s;
    }
}
