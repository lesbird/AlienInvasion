using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Special behaviour for the burrowing, ceiling-dropping and bursting aliens.</summary>
public partial class World
{
    // ---------------- ceiling crawler ----------------
    void UpdateCeilingCrawler(Enemy e, float dt)
    {
        if (e.Dropping)
        {
            e.DropVel += 22 * dt;
            e.Pos.Y -= e.DropVel * dt;
            if (e.Pos.Y <= 0)
            {
                e.Pos.Y = 0;
                e.Dropping = false;
                e.Alerted = false;
                Alert(e);
                e.AttackCd = 0.25f;
                Puff(e.Pos + Vector3.UnitY * 0.2f, 0.6f, new Vector4(0.5f, 0.5f, 0.5f, 0.4f));
                Snd.Play("screech", e.Mid, 0.9f, 1.2f);
            }
            return;
        }
        e.Yaw += dt * 0.25f * MathF.Sin(e.Seed + e.Anim * 0.3f);
        var flat = P.Pos - e.Pos;
        flat.Y = 0;
        bool trigger = flat.Length() < 2.8f || (e.Alerted && flat.Length() < 14);
        if (!P.Dead && trigger && L.LineOfSight(e.Mid - Vector3.UnitY * 0.5f, P.Eye))
        {
            e.OnCeiling = false;
            e.Dropping = true;
            e.DropVel = 0;
            e.Yaw = MathF.Atan2(flat.X, -flat.Z);
            Snd.Play("screech", e.Mid, 1f, 1.3f);
            for (int i = 0; i < 6; i++)
                AddParticle(new Particle { Pos = e.Pos + new Vector3(0, e.D.Height, 0) + RandDir() * 0.4f, Vel = RandDir() * 0.5f - Vector3.UnitY, Color = new(0.4f, 0.4f, 0.38f, 0.8f), Size = 0.08f, Life = 0.8f, Gravity = 10 });
        }
    }

    // ---------------- burrower ----------------
    void UpdateBurrowed(Enemy e, float dt)
    {
        e.SpecialCd -= dt;
        var toP = P.Pos - e.Pos;
        toP.Y = 0;
        float dist = toP.Length();
        if (!e.Alerted)
        {
            // feels footsteps through the floor
            if (P.Dead || dist > 10) { e.MoveSpeed = 0; return; }
            e.Alerted = true;
            e.LastKnown = P.Pos;
            Snd.Play("growl", e.Pos, 0.6f, 0.5f);
        }
        e.SpecialT += dt;
        float speed = 4.6f * SpeedMul;
        var move = FlowDir(e, dist) * speed * dt;
        var before = e.Pos;
        e.Pos = L.Move(e.Pos, move, e.D.Radius * 0.6f);
        e.MoveSpeed = (e.Pos - before).Length() / MathF.Max(dt, 1e-4f);
        if (move.LengthSquared() > 1e-6f) FaceToward(e, move, dt * 6);
        if (Rng.NextDouble() < dt * 25)
            AddParticle(new Particle { Pos = e.Pos + RandDir() * 0.3f * new Vector3(1, 0, 1) + Vector3.UnitY * 0.05f, Vel = RandDir() * 0.8f + Vector3.UnitY * 1.8f, Color = new(0.32f, 0.26f, 0.2f, 0.9f), Size = 0.09f, Life = 0.6f, Gravity = 12 });
        if (!P.Dead && (dist < 1.9f || e.SpecialT > 7)) Erupt(e, dist);
    }

    void Erupt(Enemy e, float dist)
    {
        e.Burrowed = false;
        e.SpecialT = 0;
        e.SpecialCd = 6;
        e.State = EState.Chase;
        e.AttackCd = 0.45f;
        Snd.Play("growl", e.Mid, 1f, 0.8f);
        Snd.Play("door", e.Pos, 0.5f, 0.5f);
        for (int i = 0; i < 24; i++)
            AddParticle(new Particle { Pos = e.Pos + Vector3.UnitY * 0.2f, Vel = RandDir() * 3 + Vector3.UnitY * 4, Color = new(0.35f, 0.28f, 0.2f, 1), Size = 0.12f + (float)Rng.NextDouble() * 0.1f, Life = 1f, Gravity = 14, Drag = 0.4f });
        Puff(e.Pos + Vector3.UnitY * 0.5f, 1.2f, new Vector4(0.4f, 0.35f, 0.3f, 0.5f));
        AddDecal(new Vector3(e.Pos.X, 0, e.Pos.Z), Vector3.UnitY, 1.3f, 1, new Vector4(0.8f, 0.7f, 0.6f, 0.8f));
        if (dist < 1.7f)
        {
            DamagePlayer(10 * DmgMul, e.Pos);
            P.Shake = MathF.Min(1, P.Shake + 0.5f);
        }
    }

    void Burrow(Enemy e)
    {
        e.Burrowed = true;
        e.SpecialT = 0;
        Snd.Play("door", e.Pos, 0.4f, 0.6f);
        for (int i = 0; i < 14; i++)
            AddParticle(new Particle { Pos = e.Pos + Vector3.UnitY * 0.2f, Vel = RandDir() * 2 + Vector3.UnitY * 3, Color = new(0.35f, 0.28f, 0.2f, 1), Size = 0.12f, Life = 0.8f, Gravity = 14 });
    }

    // ---------------- bloater ----------------
    /// <summary>Returns true when the bloater is swelling up (it stands still) or has burst.</summary>
    bool UpdateBloater(Enemy e, float dt, float dist)
    {
        if (e.SpecialT > 0)
        {
            e.SpecialT -= dt;
            e.State = EState.Windup;
            e.StateT = 1;
            e.RangedWindup = false;
            if (e.SpecialT <= 0) { e.Hp = 0; KillEnemy(e, Vector3.UnitY); }
            return true;
        }
        if (!P.Dead && dist < 2.4f && e.CanSee)
        {
            e.SpecialT = 0.7f;
            Snd.Play("spit", e.Mid, 1f, 0.45f);
            return true;
        }
        return false;
    }

    void BloaterBurst(Enemy e)
    {
        Explosion(e.Mid, 3.8f, 50 * DmgMul, false, 1);
        Snd.Play("splat", e.Mid, 1f, 0.6f);
        for (int i = 0; i < 5; i++) AddDecal(new Vector3(e.Pos.X, 0, e.Pos.Z) + RandDir() * 1.4f * new Vector3(1, 0, 1), Vector3.UnitY, 0.8f + (float)Rng.NextDouble(), 2, new Vector4(0.5f, 0.9f, 0.2f, 0.85f));
    }
}
