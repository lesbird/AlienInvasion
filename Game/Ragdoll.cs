using System.Numerics;
using AlienInvasion.Engine;

namespace AlienInvasion.Game;

/// <summary>
/// Verlet ragdoll built from an alien's pose at the moment of death. Every limb segment of the model
/// becomes a stiff link between two particles; the body is a rigid tetrahedron of four "core" particles,
/// and each limb chain hangs off the core at its nearest joint. The model's other parts ride rigidly on
/// the core or on the limb segment they sit on, so any alien model collapses without a hand-made skeleton.
/// </summary>
public class Ragdoll
{
    public class Part
    {
        public Mesh Mesh = null!;
        public Matrix4x4 M0;       // world transform when captured
        public Vector3 Col;
        public float Glow;
        public int Seg = -1;       // -1: rides the core; else index into Segs
    }

    public readonly List<Part> Parts = new();
    public readonly List<(int A, int B, Vector3 Dir0, Vector3 Mid0)> Segs = new();
    readonly List<Vector3> pos = new(), prev = new();
    readonly List<float> rad = new();
    readonly List<(int A, int B, float Len, bool MinOnly)> links = new();
    readonly int[] core = new int[4];
    Matrix4x4 coreB0T;             // transpose of the captured core basis
    Vector3 coreC0;
    float stillT;
    public bool Asleep;

    const float Gravity = 16f, Step = 1 / 120f;
    float acc;

    /// <param name="limbs">limb segments in world space (end points, thickness) at capture</param>
    /// <param name="parts">every part with its world transform; IsLimb marks the parts drawn for the limbs, in the same order</param>
    public Ragdoll(List<(Vector3 A, Vector3 B, float Th)> limbs, List<(Part P, bool IsLimb)> parts, Vector3 up, Vector3 fwd)
    {
        // ---- core: centre and size of the non-limb parts
        var body = parts.Where(p => !p.IsLimb).Select(p => p.P).ToList();
        if (body.Count == 0) body = parts.Select(p => p.P).ToList();
        var c = Vector3.Zero;
        foreach (var p in body) c += p.M0.Translation;
        c /= Math.Max(1, body.Count);
        float half = 0;
        foreach (var p in body) half += MathF.Max(new Vector3(p.M0.M11, p.M0.M12, p.M0.M13).Length(), MathF.Max(new Vector3(p.M0.M21, p.M0.M22, p.M0.M23).Length(), new Vector3(p.M0.M31, p.M0.M32, p.M0.M33).Length())) * 0.5f;
        half /= Math.Max(1, body.Count);
        float s = Math.Clamp(half * 0.8f, 0.12f, 0.9f), r = Math.Clamp(half * 0.6f, 0.08f, 0.7f);
        fwd = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up) + new Vector3(1e-4f, 0, 0));
        var right = Vector3.Cross(fwd, up);
        core[0] = Add(c + fwd * s, r);
        core[1] = Add(c - fwd * s * 0.5f + right * s * 0.87f, r);
        core[2] = Add(c - fwd * s * 0.5f - right * s * 0.87f, r);
        core[3] = Add(c + up * s, r);
        for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) Link(core[i], core[j]);
        coreC0 = CoreCentre();
        coreB0T = Matrix4x4.Transpose(CoreBasis());

        // ---- limbs: merge coincident end points into joints, link each segment
        var segOf = new int[limbs.Count];
        var limbParticles = new List<int>();
        int Joint(Vector3 p, float th)
        {
            foreach (int i in limbParticles) if (Vector3.DistanceSquared(pos[i], p) < 0.05f * 0.05f) return i;
            int k = Add(p, MathF.Max(0.05f, th * 0.5f));
            limbParticles.Add(k);
            return k;
        }
        for (int i = 0; i < limbs.Count; i++)
        {
            var (a, b, th) = limbs[i];
            int ia = Joint(a, th), ib = Joint(b, th);
            if (ia == ib) { segOf[i] = -1; continue; }
            Link(ia, ib);
            segOf[i] = Segs.Count;
            Segs.Add((ia, ib, Vector3.Normalize(b - a), (a + b) * 0.5f));
        }
        // each chain (connected group of limb joints) hangs off the core at the joint nearest the body
        var adj = limbParticles.ToDictionary(i => i, _ => new List<int>());
        foreach (var sg in Segs) { adj[sg.A].Add(sg.B); adj[sg.B].Add(sg.A); }
        var seen = new HashSet<int>();
        foreach (int start in limbParticles)
        {
            if (!seen.Add(start)) continue;
            var comp = new List<int> { start };
            for (int q = 0; q < comp.Count; q++)
                foreach (int n in adj[comp[q]]) if (seen.Add(n)) comp.Add(n);
            int anchor = comp.OrderBy(i => Vector3.DistanceSquared(pos[i], c)).First();
            foreach (int k in core) Link(anchor, k);
            // keep joints from folding completely flat (two hops apart may only shrink to 60%)
            foreach (int a in comp)
                foreach (int m in adj[a])
                    foreach (int b in adj[m])
                        if (b > a) links.Add((a, b, Vector3.Distance(pos[a], pos[b]) * 0.6f, true));
        }

        // ---- parts: limbs ride their segment; the rest the core, or a segment they sit right on
        int li = 0;
        foreach (var (p, isLimb) in parts)
        {
            if (isLimb) { p.Seg = li < segOf.Length ? segOf[li] : -1; li++; }
            else
            {
                var at = p.M0.Translation;
                float best = Vector3.Distance(at, c) * 0.5f;
                if (best > 0.12f)
                    for (int k = 0; k < Segs.Count; k++)
                    {
                        float d = DistToSeg(at, pos[Segs[k].A], pos[Segs[k].B]);
                        if (d < 0.25f && d < best) { best = d; p.Seg = k; }
                    }
            }
            Parts.Add(p);
        }
    }

    int Add(Vector3 p, float r) { pos.Add(p); prev.Add(p); rad.Add(r); return pos.Count - 1; }
    void Link(int a, int b) => links.Add((a, b, Vector3.Distance(pos[a], pos[b]), false));

    static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / MathF.Max(1e-6f, ab.LengthSquared()), 0, 1);
        return Vector3.Distance(p, a + ab * t);
    }

    Vector3 CoreCentre() => (pos[core[0]] + pos[core[1]] + pos[core[2]] + pos[core[3]]) * 0.25f;

    /// <summary>Orthonormal basis of the core as matrix rows.</summary>
    Matrix4x4 CoreBasis()
    {
        var e1 = Vector3.Normalize(pos[core[0]] - (pos[core[1]] + pos[core[2]]) * 0.5f);
        var e2 = pos[core[3]] - CoreCentre();
        e2 = Vector3.Normalize(e2 - e1 * Vector3.Dot(e2, e1));
        var e3 = Vector3.Cross(e1, e2);
        return new Matrix4x4(e1.X, e1.Y, e1.Z, 0, e2.X, e2.Y, e2.Z, 0, e3.X, e3.Y, e3.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>Launch: velocity along the hit, more on the upper body so it topples, plus a little spin.</summary>
    public void Push(Vector3 vel, Random rng)
    {
        float lo = pos.Min(p => p.Y), hi = MathF.Max(lo + 0.1f, pos.Max(p => p.Y));
        for (int i = 0; i < pos.Count; i++)
        {
            float k = 0.45f + 0.75f * (pos[i].Y - lo) / (hi - lo);
            var jitter = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f;
            prev[i] = pos[i] - (vel * k + jitter) * Step;
        }
    }

    public void Update(float dt, Level L)
    {
        if (Asleep) return;
        acc += MathF.Min(dt, 0.05f);
        while (acc >= Step)
        {
            acc -= Step;
            Simulate(L);
        }
    }

    void Simulate(Level L)
    {
        float maxMove = 0;
        for (int i = 0; i < pos.Count; i++)
        {
            var p = pos[i];
            var v = (p - prev[i]) * 0.996f;
            prev[i] = p;
            pos[i] = p + v + new Vector3(0, -Gravity * Step * Step, 0);
            maxMove = MathF.Max(maxMove, v.LengthSquared());
        }
        for (int it = 0; it < 8; it++)
        {
            foreach (var (a, b, len, minOnly) in links)
            {
                var d = pos[b] - pos[a];
                float l = d.Length();
                if (l < 1e-6f || (minOnly && l >= len)) continue;
                var corr = d * ((l - len) / l * 0.5f);
                pos[a] += corr; pos[b] -= corr;
            }
            Collide(L);
        }
        stillT = maxMove < 0.0006f * 0.0006f ? stillT + Step : 0;
        if (stillT > 0.6f) Asleep = true;
    }

    void Collide(Level L)
    {
        for (int i = 0; i < pos.Count; i++)
        {
            var p = pos[i];
            float r = rad[i];
            if (L.Blocked(p.X, p.Z, r * 0.7f))
            {
                // slide along the wall: undo whichever axis moved into it
                if (L.Blocked(prev[i].X, p.Z, r * 0.7f)) p.Z = prev[i].Z;
                if (L.Blocked(p.X, p.Z, r * 0.7f)) p.X = prev[i].X;
            }
            if (p.Y < r)
            {
                p.Y = r;
                // ground friction
                var pv = prev[i];
                prev[i] = new Vector3(GMath.Lerp(pv.X, p.X, 0.35f), pv.Y, GMath.Lerp(pv.Z, p.Z, 0.35f));
            }
            if (p.Y > Level.WH - r) p.Y = Level.WH - r;
            pos[i] = p;
        }
    }

    /// <summary>Current world transform of a part.</summary>
    public Matrix4x4 Transform(Part p)
    {
        if (p.Seg < 0)
        {
            var rot = coreB0T * CoreBasis();
            return p.M0 * Matrix4x4.CreateTranslation(-coreC0) * rot * Matrix4x4.CreateTranslation(CoreCentre());
        }
        var (a, b, dir0, mid0) = Segs[p.Seg];
        var d = pos[b] - pos[a];
        float len = d.Length();
        var dir = len > 1e-5f ? d / len : dir0;
        return p.M0 * Matrix4x4.CreateTranslation(-mid0) * Matrix4x4.CreateFromQuaternion(FromTo(dir0, dir)) * Matrix4x4.CreateTranslation((pos[a] + pos[b]) * 0.5f);
    }

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float dot = Vector3.Dot(a, b);
        if (dot > 0.99999f) return Quaternion.Identity;
        if (dot < -0.99999f)
        {
            var axis = Vector3.Cross(a, MathF.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(a, b)), MathF.Acos(dot));
    }
}
