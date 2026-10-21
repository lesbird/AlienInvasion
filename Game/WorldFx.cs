using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Gore and hit feedback: decals, flying body chunks, hit markers and kill slow-motion.</summary>
public partial class World
{
    public readonly List<Decal> Decals = new();
    public readonly List<Chunk> Chunks = new();
    public float HitMarkerT, KillMarkerT, SlowMoT, SlowMoScale = 1;
    public int ShotsFired, ShotsHit;
    bool explosiveContext;
    const int MaxDecals = 320, MaxChunks = 140;

    /// <summary>Game-time multiplier; the app scales dt by this and ticks the timers in real time.</summary>
    public float TimeScale => SlowMoT > 0 ? SlowMoScale : 1;

    public void TickRealTime(float dt)
    {
        SlowMoT -= dt;
        HitMarkerT -= dt;
        KillMarkerT -= dt;
    }

    public readonly List<Shockwave> Shockwaves = new();

    public void AddShockwave(Vector3 pos, float size, float strength)
    {
        if (Shockwaves.Count >= 12) Shockwaves.RemoveAt(0);
        Shockwaves.Add(new Shockwave { Pos = pos, Size = size, Strength = strength, Life = 0.55f });
    }

    void UpdateShockwaves(float dt)
    {
        foreach (var s in Shockwaves) s.Age += dt;
        Shockwaves.RemoveAll(s => s.Age >= s.Life);
    }

    public void AddDecal(Vector3 pos, Vector3 n, float size, int kind, Vector4 col)
    {
        if (Decals.Count >= MaxDecals) Decals.RemoveAt(0);
        Decals.Add(new Decal
        {
            Pos = pos + n * (0.01f + Decals.Count % 7 * 0.0015f),   // slight stagger so overlapping decals don't fight
            N = n, Size = size, Kind = kind, Color = col, Rot = (float)Rng.NextDouble() * MathF.Tau, Life = kind == 0 ? 120 : 160
        });
    }

    /// <summary>Blood sprayed onto the surface behind a hit.</summary>
    void SplatterBehind(Vector3 from, Vector3 dir)
    {
        float t = L.Raycast(from, dir, 3.5f, out var n);
        if (t < 3.5f) AddDecal(from + dir * t, n, 0.45f + (float)Rng.NextDouble() * 0.6f, 2, BloodColor);
    }

    void Scorch(Vector3 pos, float radius)
    {
        if (radius < 1.5f) return;
        float t = L.Raycast(pos, -Vector3.UnitY, radius * 0.8f, out var n);
        if (t < radius * 0.8f) AddDecal(pos - Vector3.UnitY * t, n, radius * 0.55f, 1, new Vector4(1, 1, 1, 0.9f));
        // the nearest wall gets a mark too
        float best = radius * 0.6f; Vector3 bn = Vector3.Zero, bp = Vector3.Zero;
        foreach (var d in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            float tw = L.Raycast(pos, d, best, out var nw);
            if (tw < best) { best = tw; bn = nw; bp = pos + d * tw; }
        }
        if (bn != Vector3.Zero) AddDecal(bp, bn, radius * 0.45f, 1, new Vector4(1, 1, 1, 0.8f));
    }

    void KillFeedback(Enemy e)
    {
        KillMarkerT = 0.35f;
        bool big = e.MaxHp >= 1500;
        // brief hit-stop on every kill, a long dramatic one for bosses
        float len = big ? 1.6f : 0.09f;
        if (len >= SlowMoT) { SlowMoT = len; SlowMoScale = big ? 0.25f : 0.3f; }
        Snd.Play("killmark", null, 0.55f, 1, 0.05f);
    }

    /// <summary>Big or explosive kills blow the alien apart; otherwise a limb sometimes comes off.</summary>
    void Dismember(Enemy e, Vector3 dir)
    {
        if (e.D.Stationary) return;
        float r = e.D.Radius, h = e.D.Height;
        bool full = (e.LastHitExplosive && e.LastHit > 25) || e.LastHit >= MathF.Max(55, e.MaxHp * 0.7f) || e.Type == EnemyDefs.Bloater;
        var col = e.D.Color;
        var push = new Vector3(dir.X, 0, dir.Z) * 3;
        void Piece(Vector3 at, Vector3 scale, int shape, float speed)
        {
            if (Chunks.Count >= MaxChunks) Chunks.RemoveAt(0);
            Chunks.Add(new Chunk
            {
                Pos = at, Vel = push + RandDir() * speed + Vector3.UnitY * (2.5f + (float)Rng.NextDouble() * 3), Scale = scale, Shape = shape,
                Axis = RandDir(), Angle = (float)Rng.NextDouble() * 6, Spin = 4 + (float)Rng.NextDouble() * 10,
                Color = col * (0.7f + (float)Rng.NextDouble() * 0.3f), Life = 30
            });
        }
        if (full)
        {
            e.Gibbed = true;
            Piece(e.Pos + new Vector3(0, h * 0.85f, 0), new Vector3(r * 0.7f), 0, 4);           // head
            int limbs = 2 + (int)(r * 4);
            for (int i = 0; i < limbs; i++) Piece(e.Mid + RandDir() * r * 0.4f, new Vector3(r * 0.22f, h * 0.35f, r * 0.22f), 1, 6);
            int lumps = 2 + (int)(r * 5);
            for (int i = 0; i < lumps; i++) Piece(e.Mid + RandDir() * r * 0.5f, new Vector3(r * (0.3f + (float)Rng.NextDouble() * 0.35f)), 0, 5);
            Blood(e.Mid, Vector3.UnitY * 3, 30);
            AddDecal(new Vector3(e.Pos.X, 0, e.Pos.Z), Vector3.UnitY, 1.2f + r * 1.5f, 2, BloodColor);
            Snd.Play("splat", e.Mid, 1f, 0.8f);
        }
        else
        {
            if (Rng.NextDouble() < 0.45) Piece(e.Mid + RandDir() * r * 0.4f, new Vector3(r * 0.2f, h * 0.32f, r * 0.2f), 1, 4);
            AddDecal(new Vector3(e.Pos.X, 0, e.Pos.Z) + RandDir() * 0.3f * new Vector3(1, 0, 1), Vector3.UnitY, 0.8f + r, 2, BloodColor);
        }
    }

    void UpdateChunks(float dt)
    {
        foreach (var c in Chunks)
        {
            c.Life -= dt;
            if (c.Resting) continue;
            c.Vel.Y -= 18 * dt;
            var next = c.Pos + c.Vel * dt;
            float rad = MathF.Min(c.Scale.X, c.Scale.Y) * 0.5f;
            if (L.Blocked(next.X, c.Pos.Z, 0.08f)) { c.Vel.X *= -0.4f; next.X = c.Pos.X; }
            if (L.Blocked(next.X, next.Z, 0.08f)) { c.Vel.Z *= -0.4f; next.Z = c.Pos.Z; }
            if (next.Y < rad)
            {
                next.Y = rad;
                if (!c.Bled) { c.Bled = true; AddDecal(new Vector3(next.X, 0, next.Z), Vector3.UnitY, 0.35f + rad * 2, 2, BloodColor); }
                c.Vel.Y *= -0.3f;
                c.Vel.X *= 0.55f; c.Vel.Z *= 0.55f;
                c.Spin *= 0.5f;
                if (MathF.Abs(c.Vel.Y) < 0.8f && new Vector2(c.Vel.X, c.Vel.Z).Length() < 0.4f) c.Resting = true;
            }
            if (next.Y > Level.WH - 0.2f) { next.Y = Level.WH - 0.2f; c.Vel.Y = -MathF.Abs(c.Vel.Y) * 0.3f; }
            c.Pos = next;
            c.Angle += c.Spin * dt;
        }
        Chunks.RemoveAll(c => c.Life <= 0);
        // decals fade out eventually
        var ds = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Decals);
        for (int i = 0; i < ds.Length; i++) ds[i].Life -= dt;
        Decals.RemoveAll(d => d.Life <= 0);
    }
}
