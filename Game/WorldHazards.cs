using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Vents, lockdown ambushes, environmental hazards, power failures and the self-destruct countdown.</summary>
public partial class World
{
    public float PowerLevel = 1;          // 1 normal lighting, 0 blackout (emergency lights only)
    public float SelfDestructT = -1;      // seconds left, -1 = not armed
    public Lockdown ActiveLockdown;
    int powerState;
    float powerT, powerFailAt = -1, outage, klaxonT, rumbleT;

    /// <summary>Debug: make the power fail right away.</summary>
    public void ForcePowerFailure() { L.PowerFailure = true; powerFailAt = 0; powerState = 0; }

    void InitHazards()
    {
        foreach (var v in L.Vents) { v.Hp = Vent.MaxHp; v.Broken = false; v.Cd = 2 + (float)Rng.NextDouble() * 6; }
        foreach (var ld in L.Lockdowns) { ld.State = 0; ld.Spawned = 0; ld.IdleT = 0; ld.Wave.Clear(); }
        Array.Clear(L.Barrier);
        powerFailAt = L.PowerFailure ? 45 + (float)Rng.NextDouble() * 55 : -1;
        PowerLevel = 1;
        powerState = 0;
        SelfDestructT = -1;
    }

    void UpdateHazards(float dt)
    {
        UpdateVents(dt);
        UpdateLockdowns(dt);
        UpdateHazardZones(dt);
        UpdatePower(dt);
        UpdateSelfDestruct(dt);
    }

    // ---------------- vents ----------------
    int PickVentType()
    {
        for (int k = 0; k < 12; k++)
        {
            int t = EnemyDefs.PickType(L.Number, Rng);
            if (t != EnemyDefs.Burrower && t != EnemyDefs.Crawler && t != EnemyDefs.Brute && t != EnemyDefs.Overseer) return t;
        }
        return EnemyDefs.Skitter;
    }

    Enemy SpawnFromVent(Vent v)
    {
        var e = SpawnEnemy(PickVentType(), Level.Center(v.X, v.Y) - v.N * 0.6f);
        e.SpawnT = 0.6f;
        e.Yaw = MathF.Atan2(v.N.X, -v.N.Z);
        Alert(e);
        TotalEnemies++;
        Snd.Play("clink", v.Pos, 0.9f, 0.5f);
        Snd.Play("door", v.Pos, 0.4f, 1.6f);
        for (int i = 0; i < 10; i++)
            AddParticle(new Particle { Pos = v.Pos + v.N * 0.1f, Vel = v.N * 3 + RandDir() * 1.5f, Color = new(0.45f, 0.45f, 0.42f, 0.8f), Size = 0.07f, Life = 0.7f, Gravity = 10 });
        Puff(v.Pos + v.N * 0.4f, 0.5f, new Vector4(0.5f, 0.5f, 0.5f, 0.35f));
        return e;
    }

    void UpdateVents(float dt)
    {
        foreach (var v in L.Vents)
        {
            if (v.Broken) continue;
            v.Cd -= dt;
            if (v.Spawns <= 0 || v.Cd > 0 || P.Dead) continue;
            if (L.Lockdowns.Any(ld => ld.Room == v.Room && ld.State == 1)) continue;   // the lockdown drives these
            if (Vector3.Distance(v.Pos, P.Pos) > 13 || !L.LineOfSight(v.Pos + v.N * 0.6f, P.Eye)) continue;
            SpawnFromVent(v);
            v.Spawns--;
            v.Cd = 7 + (float)Rng.NextDouble() * 8;
        }
    }

    Vent VentOnRay(Vector3 o, Vector3 d, float maxT)
    {
        Vent hit = null;
        float best = maxT + 0.03f;
        foreach (var v in L.Vents)
            if (!v.Broken && RayAabb(o, d, v.Min, v.Max, out float t) && t <= best) { best = t; hit = v; }
        return hit;
    }

    void DamageVent(Vent v, float dmg)
    {
        if (v.Broken) return;
        v.Hp -= dmg;
        if (v.Hp > 0) return;
        v.Broken = true;
        Sparks(v.Pos + v.N * 0.1f, v.N, 14, new Vector4(1f, 0.8f, 0.4f, 1));
        Puff(v.Pos + v.N * 0.3f, 0.6f, new Vector4(0.4f, 0.4f, 0.4f, 0.4f));
        Snd.Play("clink", v.Pos, 1f, 0.6f);
        Message("VENT DESTROYED", new Vector4(0.8f, 0.8f, 0.8f, 1));
    }

    // ---------------- lockdown ambushes ----------------
    void UpdateLockdowns(float dt)
    {
        klaxonT -= dt;
        foreach (var ld in L.Lockdowns)
        {
            var room = L.Rooms[ld.Room];
            if (ld.State == 0)
            {
                var (px, py) = Level.CellOf(P.Pos);
                if (!P.Dead && !ExitReached && room.Contains(px, py)) ActivateLockdown(ld);
                continue;
            }
            if (ld.State != 1) continue;
            int alive = ld.Wave.Count(e => !e.Dead);
            if (ld.Spawned < ld.Total)
            {
                ld.SpawnT -= dt;
                if (ld.SpawnT <= 0 && alive < 3 + L.Number / 8)
                {
                    var vents = L.Vents.Where(v => v.Room == ld.Room && !v.Broken).ToList();
                    Enemy e;
                    if (vents.Count > 0) e = SpawnFromVent(vents[Rng.Next(vents.Count)]);
                    else
                    {
                        // every vent is wrecked: they teleport in instead
                        var c = Level.Center(room.X + Rng.Next(room.W), room.Y + Rng.Next(room.H));
                        e = SpawnEnemy(PickVentType(), c);
                        e.SpawnT = 1;
                        Alert(e);
                        TotalEnemies++;
                    }
                    ld.Wave.Add(e);
                    ld.Spawned++;
                    ld.SpawnT = 1.0f + (float)Rng.NextDouble() * 1.3f;
                }
            }
            else if (alive == 0) ClearLockdown(ld);
            else
            {
                // stragglers that won't come out (or got stuck) can't hold the room forever
                if (alive != ld.LastAlive) ld.IdleT = 0;
                ld.IdleT += dt;
                if (ld.IdleT > 20)
                {
                    foreach (var e in ld.Wave.Where(e => !e.Dead).ToList())
                    {
                        Puff(e.Mid, 0.8f, new Vector4(0.5f, 0.5f, 0.5f, 0.4f));
                        Enemies.Remove(e);
                        TotalEnemies--;
                    }
                    ld.Wave.Clear();
                    ClearLockdown(ld);
                    Message("THE LAST ALIENS RETREAT INTO THE VENTS - LOCKDOWN LIFTED", new Vector4(0.4f, 1f, 0.5f, 1));
                }
            }
            ld.LastAlive = alive;
            if (klaxonT <= 0 && ld.State == 1) { Snd.Play("alarm", null, 0.35f, 1, 0); klaxonT = 2.6f; }
        }
    }

    void ActivateLockdown(Lockdown ld)
    {
        ld.State = 1;
        ld.SpawnT = 1.2f;
        ActiveLockdown = ld;
        foreach (var (x, y, _, _) in ld.Barriers) L.Barrier[L.Idx(x, y)] = true;
        RefreshLocked();
        Message("LOCKDOWN! ROOM SEALED - SURVIVE THE AMBUSH", new Vector4(1f, 0.3f, 0.2f, 1));
        Snd.Play("alarm", null, 0.6f, 1, 0);
        Snd.Play("locked", null, 0.8f);
        klaxonT = 2.6f;
    }

    void ClearLockdown(Lockdown ld)
    {
        ld.State = 2;
        if (ActiveLockdown == ld) ActiveLockdown = null;
        foreach (var (x, y, _, _) in ld.Barriers) L.Barrier[L.Idx(x, y)] = false;
        RefreshLocked();
        Message("LOCKDOWN LIFTED", new Vector4(0.4f, 1f, 0.5f, 1));
        Snd.Play("secret", null, 0.8f);
        // supplies for surviving
        var room = L.Rooms[ld.Room];
        var c = Level.Center(room.CX, room.CY, 0.35f);
        var w = Weapons.All[P.Weapon];
        Pickups.Add(new Pickup { Kind = PickupKind.Medkit, Pos = c, Vel = new Vector3(0, 3, 0), Dropped = true });
        if (w.Ammo >= 0) Pickups.Add(new Pickup { Kind = PickupKind.Bullets + w.Ammo, Pos = c + new Vector3(1, 0, 0), Vel = new Vector3(0, 3, 0) });
    }

    // ---------------- hazards ----------------
    void UpdateHazardZones(float dt)
    {
        var pp = new Vector2(P.Pos.X, P.Pos.Z);
        foreach (var h in L.Hazards)
        {
            h.HurtCd -= dt;
            float dist = Vector2.Distance(pp, new Vector2(h.Pos.X, h.Pos.Z));
            bool near = Vector3.DistanceSquared(h.Pos, P.Pos) < 40 * 40;
            switch (h.Kind)
            {
                case HazardKind.Steam:
                {
                    float cyc = (Time + h.Phase) % 5.5f;
                    bool active = cyc < 1.6f;
                    if (near && cyc < dt * 1.5f) Snd.Play("steam", h.Pos, 0.7f);
                    if (near && active && Rng.NextDouble() < dt * 45)
                        AddParticle(new Particle { Pos = h.Pos + RandDir() * 0.2f * new Vector3(1, 0, 1), Vel = new Vector3(0, 5.5f, 0) + RandDir() * 0.6f, Color = new(0.85f, 0.88f, 0.9f, 0.45f), Size = 0.4f, Grow = 1.6f, Life = 0.9f, Drag = 1.2f, Smoke = true });
                    if (active && dist < h.Radius && !P.Dead && h.HurtCd <= 0)
                    {
                        DamagePlayer(7, h.Pos);
                        h.HurtCd = 0.3f;
                    }
                    break;
                }
                case HazardKind.Cable:
                {
                    if (near && Rng.NextDouble() < dt * 2.2f)
                    {
                        Sparks(h.Pos, h.N + Vector3.UnitY * 0.3f, 8, new Vector4(1f, 0.9f, 0.5f, 1));
                        DynLights.Add(new DynLight { Pos = h.Pos + h.N * 0.3f, Color = new Vector3(0.6f, 0.7f, 1.4f), Radius = 5, Life = 0.08f, MaxLife = 0.08f });
                        if (Vector3.Distance(h.Pos, P.Pos) < 18) Snd.Play("zap", h.Pos, 0.25f, 1.5f);
                    }
                    if (dist < h.Radius && !P.Dead && h.HurtCd <= 0)
                    {
                        DamagePlayer(14, h.Pos);
                        h.HurtCd = 0.7f;
                        Sparks(P.Eye - Vector3.UnitY * 0.6f, -h.N, 12, new Vector4(0.6f, 0.8f, 1f, 1));
                        Snd.Play("zap", h.Pos, 0.8f, 1.2f);
                        var away = new Vector3(P.Pos.X - h.Pos.X, 0, P.Pos.Z - h.Pos.Z);
                        if (away.LengthSquared() > 1e-4f) P.Vel += Vector3.Normalize(away) * 7;
                    }
                    break;
                }
                case HazardKind.Acid:
                {
                    if (near && Rng.NextDouble() < dt * 8)
                    {
                        var p = h.Pos + RandDir() * h.Radius * 0.8f * new Vector3(1, 0, 1);
                        AddParticle(new Particle { Pos = p + Vector3.UnitY * 0.05f, Vel = Vector3.UnitY * 0.6f, Color = new(0.5f, 1f, 0.25f, 0.8f), Size = 0.12f, Grow = 0.2f, Life = 0.5f, Additive = true });
                    }
                    if (dist < h.Radius * 0.9f && !P.Dead && h.HurtCd <= 0)
                    {
                        DamagePlayer(6, h.Pos);
                        h.HurtCd = 0.4f;
                        Snd.Play("splat", P.Pos, 0.3f, 1.6f);
                    }
                    break;
                }
            }
        }
    }

    // ---------------- power failure ----------------
    void UpdatePower(float dt)
    {
        if (powerFailAt < 0) return;
        powerT += dt;
        switch (powerState)
        {
            case 0:
                if (LevelTime < powerFailAt) return;
                powerState = 1; powerT = 0;
                Snd.Play("powerdown", null, 0.9f, 1, 0);
                Message("POWER FAILURE! EMERGENCY LIGHTING ONLY", new Vector4(1f, 0.55f, 0.2f, 1));
                Cue(Game.Cue.PowerFail);
                break;
            case 1:   // dying flicker
                PowerLevel = powerT < 1.4f ? (Rng.NextDouble() < 0.5 ? 0.1f : 0.85f) * (1 - powerT / 1.4f) : 0;
                if (powerT >= 1.4f) { powerState = 2; powerT = 0; outage = 35 + (float)Rng.NextDouble() * 20; }
                break;
            case 2:
                PowerLevel = 0;
                if (powerT >= outage)
                {
                    powerState = 3; powerT = 0;
                    Snd.Play("powerup", null, 0.9f, 1, 0);
                    Message("POWER RESTORED", new Vector4(0.6f, 0.9f, 1f, 1));
                    Cue(Game.Cue.PowerOn);
                }
                break;
            case 3:   // stuttering back on
                PowerLevel = powerT < 1.2f ? (Rng.NextDouble() < 0.5 ? 0.2f : 1f) * (powerT / 1.2f) : 1;
                if (powerT >= 1.2f) { powerState = 4; PowerLevel = 1; }
                break;
        }
    }

    // ---------------- self-destruct ----------------
    void UpdateSelfDestruct(float dt)
    {
        if (!L.SelfDestruct || ExitReached) return;
        if (SelfDestructT < 0)
        {
            bool armed = L.LockCount == 0 ? LevelTime > 20 : Enumerable.Range(0, L.LockCount).All(j => P.Keys[L.LockColor[j]]);
            if (!armed || P.Dead) return;
            var (px, py) = Level.CellOf(P.Pos);
            int cells = L.Path(px, py, L.ExitX, L.ExitY, null)?.Count ?? 30;
            SelfDestructT = Math.Clamp(cells * Level.CS / 6.2f * 2.2f + 45, 80, 220);
            Message("SELF-DESTRUCT ACTIVATED! GET TO THE ELEVATOR!", new Vector4(1f, 0.25f, 0.2f, 1));
            Cue(Game.Cue.SelfDestruct);
            Snd.Play("alarm", null, 0.8f, 1, 0);
            klaxonT = 3;
            return;
        }
        if (P.Dead || SelfDestructT <= 0) return;
        SelfDestructT -= dt;
        if (klaxonT <= 0) { Snd.Play("alarm", null, 0.5f, SelfDestructT < 15 ? 1.15f : 1f, 0); klaxonT = SelfDestructT < 15 ? 1.2f : 3f; }
        if (SelfDestructT < 25) P.Shake = MathF.Max(P.Shake, (25 - SelfDestructT) / 25f * 0.35f);
        rumbleT -= dt;
        if (rumbleT <= 0)
        {
            rumbleT = 2 + (float)Rng.NextDouble() * 4 * MathF.Min(1, SelfDestructT / 40);
            Snd.Play("explode", P.Pos + RandDir() * 18 * new Vector3(1, 0, 1), 0.45f, 0.6f);
            P.Shake = MathF.Min(1, P.Shake + 0.25f);
        }
        if (SelfDestructT <= 0)
        {
            SelfDestructT = 0;
            Snd.Play("bigboom", null, 1f, 1, 0);
            Explosion(P.Eye + P.Forward * 3, 6, 0, false, 0, false);
            P.HurtFlash = 1;
            P.Health = 0;
            P.Dead = true;
            Message("THE BASE WAS DESTROYED", new Vector4(1f, 0.4f, 0.2f, 1));
        }
    }
}
