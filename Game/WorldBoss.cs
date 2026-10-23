using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Mini-boss behaviour: the Hive Queen breeds minions, the Warlord charges and ground-slams,
/// The Mind teleports and fires rings of orbs. The elevator stays locked until the boss dies.</summary>
public partial class World
{
    public Enemy Boss;
    bool bossAnnounced;
    readonly List<Enemy> bossMinions = new();

    // Warlord / Mind modes
    const int ModeNormal = 0, ModeChargeWindup = 1, ModeCharging = 2, ModeStunned = 3, ModeVanish = 4;

    void InitBoss()
    {
        Boss = Enemies.FirstOrDefault(e => e.D.Boss);
        bossAnnounced = false;
        if (Boss != null) { Boss.SpecialCd = 6; Boss.AltCd = 4; }
    }

    /// <summary>Called for an alerted boss each frame. Returns true when it has taken over movement this frame.</summary>
    bool UpdateBoss(Enemy e, float dt, float dist, Vector3 toP, ref Vector3 want)
    {
        if (!bossAnnounced)
        {
            bossAnnounced = true;
            OnBossAnnounced(e);
            Snd.Play(e.D.Growl, e.Mid, 1f, 0.8f);
        }
        e.AltCd -= dt;
        switch (e.Type)
        {
            case EnemyDefs.Queen:
                // breeds minions from her egg sac
                bossMinions.RemoveAll(m => m.Dead);
                if (e.SpecialCd <= 0 && e.CanSee && bossMinions.Count < 6)
                {
                    e.SpecialCd = 9 + (float)Rng.NextDouble() * 3;
                    var back = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw)) * -1.6f;
                    int n = 2 + (L.Number >= 20 ? 1 : 0);
                    for (int i = 0; i < n; i++)
                    {
                        int type = L.Number >= 20 && i == 0 ? EnemyDefs.Bloater : EnemyDefs.Skitter;
                        var spot = L.Move(e.Pos, back + RandDir() * new Vector3(1.2f, 0, 1.2f), 0.5f);
                        var m = SpawnEnemy(type, spot);
                        m.SpawnT = 0.7f;
                        Alert(m);
                        bossMinions.Add(m);
                        TotalEnemies++;
                    }
                    Snd.Play("splat", e.Mid, 1f, 0.6f);
                    Snd.Play("growl_big", e.Mid, 0.8f, 1.2f);
                    Blood(e.Pos + back + Vector3.UnitY, Vector3.UnitY * 2, 20);
                }
                return false;

            case EnemyDefs.Warlord:
                switch (e.Mode)
                {
                    case ModeChargeWindup:
                        e.SpecialT -= dt;
                        FaceToward(e, toP, dt * 8);
                        want = Vector3.Zero;
                        if (e.SpecialT <= 0)
                        {
                            e.Mode = ModeCharging;
                            e.SpecialT = 1.3f;
                            e.ChargeDir = dist > 0.01f ? toP / dist : new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
                            Snd.Play("growl_big", e.Mid, 1f, 1.3f);
                        }
                        return true;
                    case ModeCharging:
                        e.SpecialT -= dt;
                        want = e.ChargeDir * 14;
                        if (Rng.NextDouble() < dt * 30) Puff(e.Pos + Vector3.UnitY * 0.3f, 0.6f, new Vector4(0.45f, 0.4f, 0.35f, 0.4f));
                        if (!P.Dead && dist < e.D.Radius + Player.Radius + 0.7f)
                        {
                            DamagePlayer(32 * DmgMul, e.Pos);
                            P.Vel += e.ChargeDir * 16;
                            P.Shake = 1;
                            e.Mode = ModeNormal;
                            e.SpecialCd = 7 + (float)Rng.NextDouble() * 3;
                        }
                        else if (e.SpecialT < 1.1f && e.MoveSpeed < 4)
                        {
                            // slammed into a wall: dazed
                            e.Mode = ModeStunned;
                            e.SpecialT = 1.4f;
                            Snd.Play("explode", e.Pos, 0.6f, 0.5f);
                            P.Shake = MathF.Min(1, P.Shake + 0.4f);
                        }
                        else if (e.SpecialT <= 0) { e.Mode = ModeNormal; e.SpecialCd = 7; }
                        return true;
                    case ModeStunned:
                        e.SpecialT -= dt;
                        want = Vector3.Zero;
                        if (e.SpecialT <= 0) { e.Mode = ModeNormal; e.SpecialCd = 7 + (float)Rng.NextDouble() * 3; }
                        return true;
                }
                if (e.SpecialCd <= 0 && e.CanSee && dist > 5 && dist < 22 && e.State == EState.Chase)
                {
                    e.Mode = ModeChargeWindup;
                    e.SpecialT = 0.8f;
                    Snd.Play("growl_big", e.Mid, 1f, 0.7f);
                    return true;
                }
                return false;

            case EnemyDefs.Mind:
                if (e.Mode == ModeVanish)
                {
                    e.SpecialT -= dt;
                    want = Vector3.Zero;
                    if (e.SpecialT <= 0) TeleportMind(e);
                    return true;
                }
                if (e.SpecialCd <= 0 && e.State == EState.Chase)
                {
                    e.Mode = ModeVanish;
                    e.SpecialT = 0.5f;
                    e.Invulnerable = true;
                    Snd.Play("void", e.Mid, 0.6f, 1.4f);
                    return true;
                }
                if (e.AltCd <= 0 && e.CanSee)
                {
                    // ring of orbs spreading out horizontally, aimed at the player's height
                    e.AltCd = 5 + (float)Rng.NextDouble() * 2;
                    int n = 14;
                    float dy = (P.Eye.Y - 0.4f - e.Mid.Y) / MathF.Max(dist, 1);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * MathF.Tau / n + e.Seed;
                        var d = Vector3.Normalize(new Vector3(MathF.Cos(a), dy, MathF.Sin(a)));
                        SpawnProj(ProjKind.EnemyOrb, e.Mid + d * 1.6f, d * 11, 10 * DmgMul, 0, false);
                    }
                    Snd.Play("orb", e.Mid, 1f, 0.6f);
                }
                return false;
        }
        return false;
    }

    void TeleportMind(Enemy e)
    {
        var cands = new List<Vector3>();
        var (px, py) = Level.CellOf(P.Pos);
        for (int y = py - 5; y <= py + 5; y++)
            for (int x = px - 5; x <= px + 5; x++)
            {
                if (!L.In(x, y) || !L.Floor(x, y) || L.Crate[L.Idx(x, y)] || L.PropBlock[L.Idx(x, y)] || L.SecretCell[L.Idx(x, y)]) continue;
                var c = Level.Center(x, y);
                float d = Vector3.Distance(c, P.Pos);
                if (d < 7 || d > 18 || L.Blocked(c.X, c.Z, e.D.Radius, e.D.FlyHeight)) continue;
                if (L.LineOfSight(c + Vector3.UnitY * 2.5f, P.Eye)) cands.Add(c);
            }
        if (cands.Count > 0)
        {
            var dest = cands[Rng.Next(cands.Count)];
            e.Pos = new Vector3(dest.X, e.D.FlyHeight, dest.Z);
        }
        e.Mode = ModeNormal;
        e.Invulnerable = false;
        e.SpawnT = 0.5f;
        e.SpecialCd = 6 + (float)Rng.NextDouble() * 3;
        e.AttackCd = MathF.Max(e.AttackCd, 0.6f);
        Snd.Play("void", e.Mid, 0.6f, 1.8f);
    }

    /// <summary>Warlord's melee is a ground slam that hits everything close by.</summary>
    void Slam(Enemy e)
    {
        Snd.Play("explode", e.Pos, 1f, 0.5f);
        for (int i = 0; i < 24; i++)
        {
            float a = i * MathF.Tau / 24;
            var d = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            AddParticle(new Particle { Pos = e.Pos + d * 1.2f + Vector3.UnitY * 0.2f, Vel = d * 9 + Vector3.UnitY, Color = new(0.5f, 0.45f, 0.4f, 0.5f), Size = 0.6f, Grow = 1.5f, Life = 0.8f, Drag = 2.5f, Smoke = true });
        }
        AddDecal(new Vector3(e.Pos.X, 0, e.Pos.Z), Vector3.UnitY, 3f, 1, new Vector4(0.8f, 0.75f, 0.7f, 0.8f));
        float pd = Vector3.Distance(new Vector3(e.Pos.X, 0, e.Pos.Z), new Vector3(P.Pos.X, 0, P.Pos.Z));
        if (pd < 5.5f) DamagePlayer(42 * DmgMul * (1 - pd / 5.5f * 0.6f), e.Pos);
        P.Shake = MathF.Min(1, P.Shake + MathF.Max(0, 1 - pd / 20));
    }

    void BossDefeated(Enemy e)
    {
        Message($"{BossTitle ?? e.D.Name} DESTROYED! THE ELEVATOR IS UNLOCKED", new Vector4(1f, 0.85f, 0.3f, 1));
        Cue(Game.Cue.BossDead);
        Snd.Play("bigboom", e.Mid, 1f, 1.2f);
        Explosion(e.Mid, 4, 0, true, 2, false);
        foreach (var m in bossMinions) if (!m.Dead) { m.Hp = 0; KillEnemy(m, Vector3.UnitY); }
        // guaranteed reward: a weapon the player is missing, else an upgrade, else a backpack
        var drops = new List<(PickupKind k, int arg)> { (PickupKind.MegaHealth, 0), (PickupKind.ArmorVest, 0) };
        int[] tier = { Weapons.Shotgun, Weapons.Rifle, Weapons.GrenadeLauncher, Weapons.Plasma, Weapons.RocketLauncher, Weapons.Sniper, Weapons.BioLauncher, Weapons.ArcCaster, Weapons.Railgun, Weapons.VoidCannon };
        int missing = tier.FirstOrDefault(w => !P.Has[w], -1);
        if (missing >= 0) drops.Add((PickupKind.Weapon, missing));
        else
        {
            var up = Enumerable.Range(0, Weapons.Count).Where(i => P.Has[i] && !P.Upgraded[i]).ToList();
            drops.Add(up.Count > 0 ? (PickupKind.Upgrade, up[Rng.Next(up.Count)]) : (PickupKind.Backpack, 0));
        }
        var cw = Weapons.All[BestWeapon()];
        if (cw.Ammo >= 0) { drops.Add((PickupKind.Bullets + cw.Ammo, 0)); drops.Add((PickupKind.Bullets + cw.Ammo, 0)); }
        for (int i = 0; i < drops.Count; i++)
        {
            float a = i * MathF.Tau / drops.Count;
            var at = L.Move(new Vector3(e.Pos.X, 0, e.Pos.Z), new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 1.6f, 0.3f);
            Pickups.Add(new Pickup { Kind = drops[i].k, Arg = drops[i].arg, Pos = at + Vector3.UnitY * 0.35f, Vel = new Vector3(0, 4, 0) });
        }
    }
}
