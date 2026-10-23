using System.Numerics;
using AlienInvasion.Engine;

namespace AlienInvasion.Game;

public struct InputState
{
    public float Forward, Strafe, LookX, LookY;
    public bool Sprint, Fire, Zoom, UsePressed, FlashToggle, NVToggle, XRayToggle;
    public int Select, Cycle;
}

public partial class World
{
    public Level L;
    public Player P;
    public SoundSystem Snd;
    public readonly List<Enemy> Enemies = new();
    public readonly List<Projectile> Projs = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<Particle> Particles = new();
    public readonly List<Beam> Beams = new();
    public readonly List<DynLight> DynLights = new();
    public Random Rng;
    public float Time, LevelTime;
    public int Kills, TotalEnemies, ItemsTaken, TotalItems, SecretsFound;
    public bool ExitReached, Victory, God;
    public float VictoryT = -1;
    public string Msg = "";
    public float MsgT;
    public Vector4 MsgColor = Vector4.One;
    public bool AtElevator;
    public Enemy PortalE;
    float portalSpawnT = 5, lockedMsgT, hurtSndT, growlT, exploreT, flowT, portalMsgT;
    int[] flow;
    int flowX = -1, flowY = -1;
    HashSet<int> lockedCells = new();

    // difficulty tuning (scales with depth)
    public float ReactDelay, Sight, Hearing, HpMul, DmgMul, AimLead, Inaccuracy, SpeedMul, Windup;

    public World(Level level, Player player, SoundSystem snd)
    {
        L = level; P = player; Snd = snd;
        Rng = new Random(level.Seed ^ 0x5151);
        Barrels = level.Props.Where(p => p.Kind == PropKind.ExplosiveBarrel).ToList();
        foreach (var b in Barrels) { b.Dead = false; b.Hp = Prop.ExplosiveHp; b.Fuse = -1; }
        float d = (level.Number - 1) / 29f;
        ReactDelay = GMath.Lerp(1.3f, 0.2f, d);
        Sight = GMath.Lerp(17f, 42f, d);
        Hearing = GMath.Lerp(12f, 36f, d);
        HpMul = 1 + d * 0.6f;
        DmgMul = 1 + d * 0.45f;
        AimLead = GMath.Lerp(0f, 0.9f, d);
        Inaccuracy = GMath.Lerp(0.09f, 0.025f, d);
        SpeedMul = 1 + d * 0.25f;
        Windup = GMath.Lerp(0.55f, 0.28f, d);

        P.Pos = Level.Center(L.StartX, L.StartY);
        P.Yaw = L.StartYaw;
        P.Pitch = 0;
        P.Vel = Vector3.Zero;
        P.Dead = false;
        Array.Clear(P.Keys);
        P.HurtFlash = P.PickupFlash = P.Shake = P.Heat = 0;

        var perCell = new Dictionary<int, int>();
        foreach (var it in L.Items)
        {
            int ci = L.Idx(it.X, it.Y);
            perCell.TryGetValue(ci, out int n);
            perCell[ci] = n + 1;
            // items sharing a cell are fanned out around the centre
            var off = n == 0 ? Vector3.Zero : new Vector3(MathF.Cos(n * 2.1f), 0, MathF.Sin(n * 2.1f)) * 1.1f;
            Pickups.Add(new Pickup { Kind = it.Kind, Arg = it.Arg, Pos = Level.Center(it.X, it.Y, 0.35f) + off, Phase = (float)Rng.NextDouble() * 6 });
        }
        TotalItems = Pickups.Count(p => p.Kind != PickupKind.Key);
        // stash upgrades adapt to the run: prefer a weapon the player owns and hasn't upgraded yet
        foreach (var pk in Pickups.Where(p => p.Kind == PickupKind.Upgrade))
        {
            if (P.Has[pk.Arg] && !P.Upgraded[pk.Arg]) continue;
            var owned = Enumerable.Range(0, Weapons.Count).Where(i => P.Has[i] && !P.Upgraded[i]).ToList();
            var any = Enumerable.Range(0, Weapons.Count).Where(i => !P.Upgraded[i]).ToList();
            if (owned.Count > 0) pk.Arg = owned[Rng.Next(owned.Count)];
            else if (any.Count > 0) pk.Arg = any[Rng.Next(any.Count)];
            else pk.Kind = PickupKind.Backpack;
        }
        foreach (var s in L.Enemies) SpawnEnemy(s.Type, Level.Center(s.X, s.Y) + new Vector3((float)Rng.NextDouble() - 0.5f, 0, (float)Rng.NextDouble() - 0.5f));

        if (L.IsFinal)
        {
            PortalE = SpawnEnemy(EnemyDefs.Portal, Level.Center(L.ExitX, L.ExitY));
            PortalE.Invulnerable = true;
            PortalE.Yaw = 0;
            int[,] offs = { { -3, -3 }, { 3, -3 }, { -3, 3 }, { 3, 3 } };
            for (int i = 0; i < 4; i++)
            {
                int x = L.ExitX + offs[i, 0], y = L.ExitY + offs[i, 1];
                if (!L.Floor(x, y) || L.Crate[L.Idx(x, y)]) { x = L.ExitX + offs[i, 0] / 2; y = L.ExitY + offs[i, 1] / 2; }
                SpawnEnemy(EnemyDefs.Pylon, Level.Center(x, y));
            }
        }
        TotalEnemies = Enemies.Count(e => !e.D.Stationary);
        InitHazards();
        InitBoss();
        RefreshLocked();
    }

    public Enemy SpawnEnemy(int type, Vector3 pos)
    {
        var def = EnemyDefs.All[type];
        var e = new Enemy
        {
            Type = type, D = def, Pos = pos + new Vector3(0, def.FlyHeight, 0),
            Yaw = (float)Rng.NextDouble() * MathF.Tau, Seed = (float)Rng.NextDouble() * 100,
            StateT = (float)Rng.NextDouble() * 3
        };
        e.Hp = e.MaxHp = def.Stationary ? def.Hp : def.Hp * HpMul;
        if (type == EnemyDefs.Burrower) e.Burrowed = true;
        if (type == EnemyDefs.Crawler) { e.OnCeiling = true; e.Pos.Y = Level.WH - def.Height; }
        Enemies.Add(e);
        return e;
    }

    void RefreshLocked()
    {
        lockedCells = new HashSet<int>(L.Doors.Where(d => d.Locked).Select(d => L.Idx(d.X, d.Y)));
        foreach (var ld in L.Lockdowns)
            if (ld.State == 1) foreach (var (x, y, _, _) in ld.Barriers) lockedCells.Add(L.Idx(x, y));
        flowT = 0;
    }

    public void Message(string s, Vector4? col = null)
    {
        Msg = s;
        MsgT = 3.5f;
        MsgColor = col ?? new Vector4(1, 1, 1, 1);
    }

    // =====================================================================
    public void Update(float dt, InputState input)
    {
        Time += dt;
        if (!P.Dead && VictoryT < 0) LevelTime += dt;
        Snd.Update(dt);
        lockedMsgT -= dt; hurtSndT -= dt; growlT -= dt; portalMsgT -= dt;
        MsgT -= dt;

        if (!P.Dead) UpdatePlayer(dt, input);
        else
        {
            P.Vel *= MathF.Max(0, 1 - dt * 5);
            P.HurtFlash = MathF.Max(P.HurtFlash - dt * 0.3f, 0.35f);
        }

        UpdateDoors(dt);
        UpdateBarrels(dt);
        UpdateHazards(dt);
        UpdateFlow(dt);
        for (int i = 0; i < Enemies.Count; i++) UpdateEnemy(Enemies[i], dt);
        UpdateProjectiles(dt);
        UpdatePickups(dt);
        UpdateEffects(dt);
        UpdateChunks(dt);
        UpdateShockwaves(dt);
        UpdatePortal(dt);

        exploreT -= dt;
        if (exploreT <= 0) { exploreT = 0.2f; Explore(); }

        // remove old corpses to keep things tidy
        if (Enemies.Count > 80) Enemies.RemoveAll(e => e.Dead && e.DeathT > 20);
    }

    void Explore()
    {
        var (px, py) = Level.CellOf(P.Pos);
        var eye = P.Eye;
        for (int y = py - 6; y <= py + 6; y++)
            for (int x = px - 6; x <= px + 6; x++)
            {
                if (!L.In(x, y)) continue;
                int i = L.Idx(x, y);
                if (L.Explored[i] || L.Cells[i] == 0) continue;
                var sd = L.DoorIn(x, y);
                if (sd != null && sd.Secret && !sd.Unlocked) continue;
                var c = Level.Center(x, y, eye.Y);
                if (Math.Abs(x - px) + Math.Abs(y - py) <= 1 || L.LineOfSight(eye, c)) L.Explored[i] = true;
            }
    }

    // =====================================================================  player
    void UpdatePlayer(float dt, InputState inp)
    {
        P.Yaw += inp.LookX;
        P.Pitch = Math.Clamp(P.Pitch - inp.LookY, -1.45f, 1.45f);

        var wish = P.Flat * inp.Forward + P.Right * inp.Strafe;
        if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);
        float speed = inp.Sprint ? 9.5f : 6.2f;
        var target = wish * speed;
        float accel = wish.LengthSquared() > 0 ? 12 : 10;
        P.Vel += (target - P.Vel) * MathF.Min(1, accel * dt);
        P.Vel.Y = 0;
        P.Pos = L.Move(P.Pos, P.Vel * dt, Player.Radius);
        P.Pos.Y = 0;

        float hs = new Vector2(P.Vel.X, P.Vel.Z).Length();
        P.BobAmount = GMath.Approach(P.BobAmount, Math.Clamp(hs / 6f, 0, 1.4f), dt * 4);
        P.Bob += dt * hs * 1.15f;
        P.StepAccum += hs * dt;
        if (P.StepAccum > (inp.Sprint ? 3.0f : 2.4f)) { P.StepAccum = 0; Snd.Play("step", null, 0.35f, 1, 0.15f); }

        P.HurtFlash = MathF.Max(0, P.HurtFlash - dt * 1.5f);
        P.PickupFlash = MathF.Max(0, P.PickupFlash - dt * 2.5f);
        P.Shake = MathF.Max(0, P.Shake - dt * 2f);
        P.Heat = MathF.Max(0, P.Heat - dt * 0.3f);
        P.Recoil = MathF.Max(0, P.Recoil - dt * 1.2f) * MathF.Pow(0.0001f, dt);
        P.KnifeT = MathF.Max(0, P.KnifeT - dt * 3.5f);
        P.FireFlash -= dt;
        if (inp.FlashToggle) { P.Flashlight = !P.Flashlight; Snd.Play("empty", null, 0.5f); }
        if (inp.NVToggle) ToggleGoggles(false);
        if (inp.XRayToggle) ToggleGoggles(true);
        DrainGoggles(dt);

        // weapon selection
        if (inp.Select >= 0 && inp.Select < Weapons.Count && P.Has[inp.Select] && inp.Select != P.Pending)
        {
            P.Pending = inp.Select;
            Snd.Play("switch", null, 0.6f);
        }
        if (inp.Cycle != 0)
        {
            int w = P.Pending;
            for (int k = 0; k < Weapons.Count; k++)
            {
                w = (w + inp.Cycle + Weapons.Count) % Weapons.Count;
                if (P.Has[w]) break;
            }
            if (w != P.Pending) { P.Pending = w; Snd.Play("switch", null, 0.6f); }
        }
        if (P.Pending != P.Weapon)
        {
            P.SwitchT += dt * 6;
            if (P.SwitchT >= 1) { P.Weapon = P.Pending; P.SwitchT = 1; }
        }
        else P.SwitchT = MathF.Max(0, P.SwitchT - dt * 5);

        // scope: hold right mouse with a zoomable weapon raised
        bool scoped = inp.Zoom && Weapons.All[P.Weapon].Zoom > 0 && P.Pending == P.Weapon && P.SwitchT < 0.25f;
        P.ZoomT = GMath.Approach(P.ZoomT, scoped ? 1 : 0, dt * (scoped ? 6 : 9));

        P.Cooldown -= dt;
        if (inp.Fire && P.Cooldown <= 0 && P.SwitchT < 0.25f && P.Pending == P.Weapon) Fire();

        P.AmpT = MathF.Max(0, P.AmpT - dt);
        if (inp.UsePressed) TryOpenSecret();

        var (cx, cy) = Level.CellOf(P.Pos);
        AtElevator = !L.IsFinal && cx == L.ExitX && cy == L.ExitY;
        if (AtElevator && inp.UsePressed && !ExitReached && Boss != null && !Boss.Dead)
        {
            if (lockedMsgT <= 0) { Message($"ELEVATOR LOCKED - KILL THE {Boss.D.Name}", new Vector4(1f, 0.4f, 0.3f, 1)); Snd.Play("locked", null, 0.8f); lockedMsgT = 1.5f; }
        }
        else if (AtElevator && inp.UsePressed && !ExitReached)
        {
            ExitReached = true;
            Snd.Play("elevator", null, 0.9f, 1, 0);
        }
        Snd.SetListener(P.Eye, P.Forward, Vector3.UnitY);
    }

    /// <summary>Use key: probe in front of the player for a fake wall panel.</summary>
    void TryOpenSecret()
    {
        for (float t = 0.6f; t <= 2.8f; t += 0.3f)
        {
            var probe = P.Pos + P.Flat * t;
            var (x, y) = Level.CellOf(probe);
            if (!L.In(x, y)) return;
            var d = L.DoorIn(x, y);
            if (d != null && d.Secret && !d.Unlocked)
            {
                d.Unlocked = true;
                SecretsFound++;
                RefreshLocked();
                Message("YOU FOUND A SECRET AREA!", new Vector4(1f, 0.85f, 0.3f, 1));
                Snd.Play("secret", null, 0.9f);
                Snd.Play("door", Level.Center(x, y, 2.5f), 0.8f, 0.7f);
                return;
            }
            if (L.Cells[L.Idx(x, y)] == 0 || L.Crate[L.Idx(x, y)]) return;
        }
    }

    public int BestWeapon()
    {
        int[] pref = { Weapons.Plasma, Weapons.Rifle, Weapons.Shotgun, Weapons.ArcCaster, Weapons.BioLauncher, Weapons.Pistol, Weapons.RocketLauncher, Weapons.GrenadeLauncher, Weapons.Sniper, Weapons.Railgun, Weapons.VoidCannon, Weapons.Knife };
        foreach (var w in pref)
        {
            var def = Weapons.All[w];
            if (P.Has[w] && (def.Ammo < 0 || P.Ammo[def.Ammo] >= def.AmmoPerShot)) return w;
        }
        return Weapons.Knife;
    }

    Vector3 Spread(Vector3 dir, float amount)
    {
        if (amount <= 0) return dir;
        var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        var up = Vector3.Cross(right, dir);
        float a = (float)Rng.NextDouble() * MathF.Tau, r = MathF.Sqrt((float)Rng.NextDouble()) * amount;
        return Vector3.Normalize(dir + right * MathF.Cos(a) * r + up * MathF.Sin(a) * r);
    }

    /// <summary>Current scope magnification (1 = no zoom).</summary>
    public float ZoomMag => 1 + MathF.Max(0, Weapons.Get(P.Weapon, P.Upgraded[P.Weapon]).Zoom - 1) * P.ZoomT;

    /// <summary>One-shot weapons kill any regular alien outright; bosses and structures take the listed damage.</summary>
    static float ShotDamage(Enemy e, float dmg, bool oneShot) => oneShot && !e.D.Boss && !e.D.Stationary ? MathF.Max(dmg, e.Hp + 1) : dmg;

    public Vector3 Muzzle => P.Eye + P.Forward * 0.7f + P.Right * 0.2f - Vector3.UnitY * 0.15f;

    void Fire()
    {
        var w = Weapons.Get(P.Weapon, P.Upgraded[P.Weapon]);
        if (w.Ammo >= 0 && P.Ammo[w.Ammo] < w.AmmoPerShot)
        {
            Snd.Play("empty", null, 0.6f);
            P.Cooldown = 0.35f;
            int best = BestWeapon();
            if (best != P.Weapon) P.Pending = best;
            return;
        }
        if (w.Ammo >= 0 && !God) P.Ammo[w.Ammo] -= w.AmmoPerShot;
        P.Cooldown = 1f / w.Rate;
        P.Recoil = MathF.Min(P.Recoil + w.Recoil, 0.6f);
        P.Pitch = MathF.Min(P.Pitch + w.Recoil * 0.15f, 1.45f);
        Snd.Play(w.Sound, null, 0.8f);
        var eye = P.Eye;
        var fwd = P.Forward;
        if (w.Mode != FireMode.Melee) P.FireFlash = 0.05f;
        if (w.Mode != FireMode.Melee)
        {
            DynLights.Add(new DynLight { Pos = eye + fwd * 1.0f, Color = w.Flash * 3f, Radius = 15, Life = 0.09f, MaxLife = 0.09f });
            P.Heat = MathF.Min(1, P.Heat + MathF.Min(0.12f, 0.35f / w.Rate + 0.02f));
        }

        switch (w.Mode)
        {
            case FireMode.Melee:
            {
                P.KnifeT = 1;
                Enemy best = null;
                float bd = float.MaxValue;
                foreach (var e in Enemies)
                {
                    if (e.Dead || e.Hidden) continue;
                    var to = e.Mid - eye;
                    var flat = new Vector3(to.X, 0, to.Z);
                    float dist = flat.Length() - e.D.Radius;
                    if (dist > w.Range || MathF.Abs(to.Y) > 2f) continue;
                    if (Vector3.Dot(Vector3.Normalize(flat), P.Flat) < 0.6f) continue;
                    if (dist < bd) { bd = dist; best = e; }
                }
                ShotsFired++;
                if (best != null)
                {
                    ShotsHit++;
                    float dmg = w.Damage * (best.Alerted ? 1 : 2.5f);
                    DamageEnemy(best, dmg, P.Flat);
                    Blood(best.Mid - P.Flat * best.D.Radius, P.Flat * -2, 12);
                    Snd.Play("splat", best.Mid, 0.8f);
                }
                break;
            }
            case FireMode.Hitscan:
                ShotsFired += w.Pellets;
            {
                // a scoped weapon is dead accurate zoomed in and sloppy from the hip
                float spread = w.Zoom > 0 ? w.Spread * (1 - P.ZoomT) : w.Spread + P.Recoil * 0.05f;
                for (int i = 0; i < w.Pellets; i++)
                    HitScan(eye, Spread(fwd, spread), w.Damage, P.Weapon == Weapons.Rifle || P.Weapon == Weapons.Sniper, w.OneShot);
                if (w.OneShot) P.Shake = MathF.Max(P.Shake, 0.35f);
                break;
            }
            case FireMode.Rail:
                ShotsFired++;
                RailFire(eye, fwd, w);
                P.Shake = MathF.Max(P.Shake, 0.5f);
                break;
            case FireMode.Projectile:
            {
                var dir = Spread(fwd, 0.004f);
                var start = Muzzle;
                if (L.Raycast(eye, Vector3.Normalize(start - eye), (start - eye).Length(), out _) < (start - eye).Length()) start = eye;
                ShotsFired++;
                var lob = w.Proj == ProjKind.Grenade ? new Vector3(0, 3, 0) : Vector3.Zero;
                if (w.Twin)
                {
                    // two rockets side by side, slightly diverging
                    foreach (int sd in new[] { -1, 1 })
                        SpawnProj(w.Proj, start + P.Right * sd * 0.18f, Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, sd * 0.035f)) * w.ProjSpeed, w.Damage, w.Splash, true);
                }
                else SpawnProj(w.Proj, start, dir * w.ProjSpeed + lob, w.Damage, w.Splash, true).Cluster = w.Cluster;
                break;
            }
            case FireMode.Arc:
                ShotsFired++;
                ArcFire(eye, fwd, w);
                break;
        }
        if (w.Noise > 0) AlertNearby(P.Pos, w.Noise * (Hearing / 25f));
    }

    static bool RayAabb(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
    {
        float tmin = 0, tmax = float.MaxValue;
        t = 0;
        for (int a = 0; a < 3; a++)
        {
            float oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z;
            float da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            float mn = a == 0 ? min.X : a == 1 ? min.Y : min.Z;
            float mx = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-8f) { if (oa < mn || oa > mx) return false; continue; }
            float t1 = (mn - oa) / da, t2 = (mx - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1);
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        return true;
    }

    public bool RayEnemy(Vector3 o, Vector3 d, Enemy e, out float t)
    {
        float r = e.D.Radius;
        var min = new Vector3(e.Pos.X - r, e.Pos.Y, e.Pos.Z - r);
        var max = new Vector3(e.Pos.X + r, e.Pos.Y + e.D.Height, e.Pos.Z + r);
        if (e.Type == EnemyDefs.Portal) { min.Y += 0.3f; }
        return RayAabb(o, d, min, max, out t);
    }

    void HitScan(Vector3 o, Vector3 d, float dmg, bool tracer, bool oneShot = false)
    {
        float wallT = L.Raycast(o, d, 250, out var n);
        Enemy best = null;
        float bestT = wallT;
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Hidden) continue;
            if (RayEnemy(o, d, e, out float t) && t < bestT) { bestT = t; best = e; }
        }
        var hit = o + d * bestT;
        // the level raycast already stops at barrels; find out whether that surface was one
        var barrel = best == null ? BarrelOnRay(o, d, bestT) : null;
        var vent = best == null && barrel == null ? VentOnRay(o, d, bestT) : null;
        if (barrel != null)
        {
            DamageBarrel(barrel, dmg);
            Sparks(hit + n * 0.05f, n, 6, new Vector4(1f, 0.6f, 0.3f, 1));
        }
        else if (vent != null)
        {
            DamageVent(vent, dmg);
            Sparks(hit + vent.N * 0.05f, vent.N, 5, new Vector4(1f, 0.85f, 0.5f, 1));
        }
        else if (best != null)
        {
            ShotsHit++;
            DamageEnemy(best, ShotDamage(best, dmg, oneShot), d);
            if (best.D.Stationary) Sparks(hit, -d, 6, new Vector4(0.8f, 0.4f, 1f, 1));
            else
            {
                Blood(hit, -d * 2, 6);
                if (Rng.NextDouble() < 0.5) SplatterBehind(hit + d * best.D.Radius, d);
            }
        }
        else
        {
            Sparks(hit + n * 0.05f, n, 5, new Vector4(1f, 0.8f, 0.4f, 1));
            Puff(hit + n * 0.1f, 0.3f, new Vector4(0.6f, 0.6f, 0.6f, 0.35f));
            if (wallT < 250) AddDecal(hit, n, 0.13f + (float)Rng.NextDouble() * 0.05f, 0, new Vector4(1, 1, 1, 0.95f));
        }
        if (tracer) Beams.Add(new Beam { A = Muzzle, B = hit, Color = new Vector4(1f, 0.85f, 0.4f, 0.35f), Width = oneShot ? 0.04f : 0.025f, Life = oneShot ? 0.12f : 0.05f, MaxLife = oneShot ? 0.12f : 0.05f });
    }

    /// <summary>Rail gun: a slug that punches through every alien on the line until it hits a wall.</summary>
    void RailFire(Vector3 eye, Vector3 fwd, WeaponDef w)
    {
        float wallT = L.Raycast(eye, fwd, w.Range, out var n);
        var hits = new List<(Enemy e, float t)>();
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Hidden) continue;
            if (RayEnemy(eye, fwd, e, out float t) && t < wallT) hits.Add((e, t));
        }
        if (hits.Count > 0) ShotsHit++;
        foreach (var (e, t) in hits.OrderBy(h => h.t))
        {
            var at = eye + fwd * t;
            DamageEnemy(e, ShotDamage(e, w.Damage, w.OneShot), fwd);
            if (e.D.Stationary) Sparks(at, -fwd, 10, new Vector4(0.6f, 0.8f, 1f, 1));
            else
            {
                Blood(at, fwd * 3, 10);
                SplatterBehind(at + fwd * e.D.Radius, fwd);
            }
        }
        var end = eye + fwd * wallT;
        var barrel = BarrelOnRay(eye, fwd, wallT);
        if (barrel != null) DamageBarrel(barrel, w.Damage);
        if (wallT < w.Range)
        {
            Sparks(end + n * 0.05f, n, 14, new Vector4(0.5f, 0.8f, 1f, 1));
            Puff(end + n * 0.1f, 0.6f, new Vector4(0.6f, 0.7f, 0.8f, 0.4f));
            if (barrel == null) AddDecal(end, n, 0.3f, 1, new Vector4(1, 1, 1, 0.9f));
            DynLights.Add(new DynLight { Pos = end + n * 0.3f, Color = new Vector3(0.6f, 1.2f, 2.5f), Radius = 6, Life = 0.25f, MaxLife = 0.25f });
        }

        // the trail: a hot core, a wide glow and a corkscrew of sparks that linger and drift
        var from = Muzzle;
        Beams.Add(new Beam { A = from, B = end, Color = new Vector4(0.3f, 0.6f, 1f, 0.5f), Width = 0.22f, Life = 0.45f, MaxLife = 0.45f });
        Beams.Add(new Beam { A = from, B = end, Color = new Vector4(0.9f, 0.97f, 1f, 1f), Width = 0.05f, Life = 0.3f, MaxLife = 0.3f });
        var dir = end - from;
        float len = dir.Length();
        if (len < 0.01f) return;
        dir /= len;
        var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY) + new Vector3(0.001f));
        var up = Vector3.Cross(side, dir);
        int steps = Math.Min(260, (int)(len / 0.12f));
        for (int i = 0; i < steps; i++)
        {
            float d = i * 0.12f, a = d * 3.2f;
            var off = (side * MathF.Cos(a) + up * MathF.Sin(a)) * 0.09f;
            AddParticle(new Particle { Pos = from + dir * d + off, Vel = off * 2.5f, Color = new(0.45f, 0.75f, 1f, 0.8f), Size = 0.07f, Grow = 0.12f, Life = 0.6f + (float)Rng.NextDouble() * 0.3f, Drag = 2, Additive = true });
        }
    }

    void ArcFire(Vector3 eye, Vector3 fwd, WeaponDef w)
    {
        Enemy target = null;
        float best = float.MaxValue;
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Hidden) continue;
            var to = e.Mid - eye;
            float dist = to.Length();
            if (dist > w.Range) continue;
            float dot = Vector3.Dot(to / dist, fwd);
            if (dot < 0.93f) continue;
            float score = dist * (1.6f - dot);
            if (score < best && L.LineOfSight(eye, e.Mid)) { best = score; target = e; }
        }
        var from = Muzzle;
        if (target == null)
        {
            float t = L.Raycast(eye, fwd, w.Range, out var n);
            var end = eye + fwd * t;
            Lightning(from, end);
            if (t < w.Range) Sparks(end, n, 4, new Vector4(0.5f, 0.7f, 1f, 1));
            return;
        }
        ShotsHit++;
        var hitList = new List<Enemy> { target };
        float dmg = w.Damage;
        Lightning(from, target.Mid);
        DamageEnemy(target, dmg, fwd);
        var cur = target;
        for (int k = 0; k < w.Chains; k++)
        {
            Enemy next = null;
            float nd = 8f;
            foreach (var e in Enemies)
            {
                if (e.Dead || e.Hidden || hitList.Contains(e)) continue;
                float d = Vector3.Distance(e.Mid, cur.Mid);
                if (d < nd && L.LineOfSight(cur.Mid, e.Mid)) { nd = d; next = e; }
            }
            if (next == null) break;
            dmg *= 0.75f;
            Lightning(cur.Mid, next.Mid);
            DamageEnemy(next, dmg, Vector3.Normalize(next.Mid - cur.Mid));
            hitList.Add(next);
            cur = next;
        }
    }

    void Lightning(Vector3 a, Vector3 b)
    {
        int segs = Math.Clamp((int)(Vector3.Distance(a, b) / 1.2f), 3, 14);
        var prev = a;
        var dir = b - a;
        var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY) + new Vector3(0.001f));
        var up = Vector3.Normalize(Vector3.Cross(side, dir));
        for (int i = 1; i <= segs; i++)
        {
            float f = i / (float)segs;
            var p = a + dir * f;
            if (i < segs) p += side * ((float)Rng.NextDouble() - 0.5f) * 0.6f + up * ((float)Rng.NextDouble() - 0.5f) * 0.6f;
            Beams.Add(new Beam { A = prev, B = p, Color = new Vector4(0.4f, 0.6f, 1f, 0.6f), Width = 0.18f, Life = 0.08f, MaxLife = 0.08f });
            Beams.Add(new Beam { A = prev, B = p, Color = new Vector4(0.9f, 0.95f, 1f, 1f), Width = 0.05f, Life = 0.08f, MaxLife = 0.08f });
            prev = p;
        }
        DynLights.Add(new DynLight { Pos = b, Color = new Vector3(0.5f, 0.6f, 1.5f), Radius = 7, Life = 0.08f, MaxLife = 0.08f });
    }

    public void AlertNearby(Vector3 pos, float radius)
    {
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Alerted || e.D.Stationary) continue;
            float d = Vector3.Distance(e.Pos, pos);
            if (d > radius) continue;
            if (d < radius * 0.35f || L.LineOfSight(e.EyePos, pos + Vector3.UnitY)) Alert(e, true);
        }
    }

    void Alert(Enemy e, bool delayed = false)
    {
        if (e.Alerted || e.Dead || e.D.Stationary) return;
        e.Alerted = true;
        e.LostT = 0;
        e.LastKnown = P.Pos;
        e.State = EState.Chase;
        e.AttackCd = MathF.Max(e.AttackCd, (delayed ? ReactDelay : 0) + 0.3f + (float)Rng.NextDouble() * 0.7f);
        if (growlT <= 0) { Snd.Play(e.D.Growl, e.EyePos, 0.9f); growlT = 0.4f; }
    }

    // =====================================================================  damage
    public void DamageEnemy(Enemy e, float dmg, Vector3 dir)
    {
        if (e.Dead) return;
        if (e.Invulnerable)
        {
            if (e.Type == EnemyDefs.Portal && portalMsgT <= 0) { Message("THE PORTAL IS SHIELDED - DESTROY THE PYLONS!", new Vector4(0.9f, 0.5f, 1, 1)); portalMsgT = 4; }
            Sparks(e.Mid, -dir, 3, new Vector4(0.8f, 0.4f, 1, 1));
            return;
        }
        if (P.AmpT > 0) dmg *= 2;
        e.Hp -= dmg;
        e.PainFlash = 0.12f;
        e.LastHit = dmg;
        e.LastHitExplosive = explosiveContext;
        if (!e.D.Stationary)
        {
            if (HitMarkerT < 0.06f) Snd.Play("hitmark", null, 0.35f, 1, 0.1f);
            HitMarkerT = 0.14f;
        }
        if (!e.Alerted) Alert(e);
        e.LastKnown = P.Pos;
        e.LostT = 0;
        if (e.Hp <= 0) { KillEnemy(e, dir); return; }
        if (!e.D.Stationary && e.State != EState.Windup && Rng.NextDouble() < e.D.PainChance * Math.Clamp(dmg / 25f, 0.3f, 1.5f))
        {
            e.State = EState.Pain;
            e.StateT = 0.22f;
            Snd.Play("ahurt", e.EyePos, 0.7f, e.D.Radius > 0.8f ? 0.6f : 1f);
        }
        if (e.D.Radius < 0.8f && !e.D.Stationary) e.Vel += new Vector3(dir.X, 0, dir.Z) * MathF.Min(dmg, 60) * 0.08f;
    }

    void KillEnemy(Enemy e, Vector3 dir)
    {
        e.State = EState.Dead;
        e.DeathT = 0;
        e.Hp = 0;
        Snd.Play(e.D.Death, e.Mid, 1f, e.D.Radius > 0.8f ? 0.8f : 1f);
        if (!e.D.Stationary) Kills++;
        if (e.Type == EnemyDefs.Pylon)
        {
            Explosion(e.Mid, 3f, 0, true, 2, false);
            int left = Enemies.Count(x => x.Type == EnemyDefs.Pylon && !x.Dead);
            if (left == 0 && PortalE != null)
            {
                PortalE.Invulnerable = false;
                Message("PORTAL SHIELD DOWN! DESTROY IT!", new Vector4(1, 0.4f, 1, 1));
            }
            else Message($"PYLON DESTROYED - {left} REMAINING", new Vector4(0.9f, 0.6f, 1, 1));
            return;
        }
        if (e.Type == EnemyDefs.Portal)
        {
            VictoryT = 0;
            Snd.SetLoop(null, Vector3.Zero, 0);
            Message("THE PORTAL HAS BEEN DESTROYED!", new Vector4(0.6f, 1, 0.6f, 1));
            foreach (var x in Enemies) if (!x.Dead && x != e) { x.Hp = 0; KillEnemy(x, Vector3.UnitY); }
            return;
        }
        if (e.Type == EnemyDefs.Bloater) BloaterBurst(e);
        if (e.D.Boss) BossDefeated(e);
        // ragdoll launch: along the killing blow, harder for big hits and explosions, lighter for bosses
        var flat = new Vector3(dir.X, 0, dir.Z);
        flat = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Zero;
        float force = e.LastHitExplosive ? 7.5f : Math.Clamp(e.LastHit / 22f, 1.6f, 5.5f);
        if (e.MaxHp >= 1500) force *= 0.4f;
        e.DeathVel = flat * force + Vector3.UnitY * (e.LastHitExplosive ? 4.5f : 1.0f);
        Blood(e.Mid, dir * 3 + Vector3.UnitY * 2, 28);
        Gibs(e);
        KillFeedback(e);
        Dismember(e, dir);
        // drops
        var d = e.D;
        var floorPos = new Vector3(e.Pos.X, 0.35f, e.Pos.Z);
        if (d.DropWeapon >= 0 && Rng.NextDouble() < d.DropChance)
            Pickups.Add(new Pickup { Kind = PickupKind.Weapon, Arg = d.DropWeapon, Pos = floorPos + Vector3.UnitY * d.FlyHeight, Dropped = true, Vel = new Vector3(0, 3, 0) });
        else if (Rng.NextDouble() < 0.22)
        {
            PickupKind k = L.Number >= 5 && Rng.NextDouble() < 0.5 ? PickupKind.Cells : Rng.NextDouble() < 0.5 ? PickupKind.Bullets : PickupKind.Health;
            if (L.Number >= 2 && k == PickupKind.Bullets && Rng.NextDouble() < 0.4) k = PickupKind.Shells;
            Pickups.Add(new Pickup { Kind = k, Pos = floorPos + Vector3.UnitY * d.FlyHeight, Dropped = true, Vel = new Vector3(0, 2.5f, 0) });
        }
    }

    public void DamagePlayer(float dmg, Vector3 from)
    {
        if (P.Dead || God || VictoryT >= 0) return;
        float absorbed = MathF.Min(P.Armor, dmg * 0.6f);
        P.Armor -= absorbed;
        P.Health -= dmg - absorbed;
        P.HurtFlash = MathF.Min(1, P.HurtFlash + dmg / 25f);
        P.Shake = MathF.Min(1, P.Shake + dmg / 50f);
        var push = P.Pos - from;
        push.Y = 0;
        if (push.LengthSquared() > 0.001f) P.Vel += Vector3.Normalize(push) * MathF.Min(dmg * 0.25f, 8);
        if (hurtSndT <= 0) { Snd.Play("hurt", null, 0.8f); hurtSndT = 0.35f; }
        if (P.Health <= 0)
        {
            P.Health = 0;
            P.Dead = true;
            Snd.Play("pdeath", null, 1f, 1, 0);
        }
    }

    public void Explosion(Vector3 pos, float radius, float dmg, bool fromPlayer, int style, bool sound = true)
    {
        if (sound) Snd.Play("explode", pos, 1.2f, style == 2 ? 0.6f : 1f);
        Vector4 c1, c2;
        switch (style)
        {
            case 1: c1 = new(0.5f, 1f, 0.2f, 1); c2 = new(0.2f, 0.6f, 0.1f, 0.6f); break;
            case 2: c1 = new(0.8f, 0.3f, 1f, 1); c2 = new(0.3f, 0.1f, 0.5f, 0.6f); break;
            case 3: c1 = new(0.9f, 0.5f, 1f, 1); c2 = new(0.4f, 0.2f, 0.6f, 0.5f); break;
            default: c1 = new(1f, 0.6f, 0.2f, 1); c2 = new(0.3f, 0.3f, 0.3f, 0.6f); break;
        }
        int n = (int)(14 + radius * 5);
        for (int i = 0; i < n; i++)
        {
            var v = RandDir() * (2 + (float)Rng.NextDouble() * radius * 2.2f);
            AddParticle(new Particle { Pos = pos, Vel = v, Color = c1, Size = 0.6f + radius * 0.25f, Grow = radius * 0.6f, Life = 0.35f + (float)Rng.NextDouble() * 0.3f, Drag = 3, Additive = true });
        }
        for (int i = 0; i < n / 2; i++)
            AddParticle(new Particle { Pos = pos + RandDir() * 0.5f, Vel = RandDir() * 1.5f + Vector3.UnitY * 1.5f, Color = c2, Size = 0.8f + radius * 0.2f, Grow = 1.4f, Life = 1.2f + (float)Rng.NextDouble(), Drag = 1.5f, Smoke = true });
        Sparks(pos, Vector3.UnitY, 14, c1 * 1.2f);
        DynLights.Add(new DynLight { Pos = pos, Color = new Vector3(c1.X, c1.Y, c1.Z) * 3, Radius = radius * 3.5f, Life = 0.45f, MaxLife = 0.45f });
        Scorch(pos, radius);
        if (dmg <= 0) return;
        explosiveContext = true;
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Hidden) continue;
            float d = MathF.Max(0, Vector3.Distance(pos, e.Mid) - e.D.Radius);
            if (d >= radius || !L.LineOfSight(pos, e.Mid)) continue;
            DamageEnemy(e, dmg * (1 - d / radius * 0.7f), Vector3.Normalize(e.Mid - pos + new Vector3(0, 0.01f, 0)));
        }
        explosiveContext = false;
        foreach (var v in L.Vents)
            if (!v.Broken && Vector3.Distance(pos, v.Pos) < radius) DamageVent(v, dmg * 0.6f);
        foreach (var b in Barrels)
        {
            if (b.Dead) continue;
            var bc = b.Pos + new Vector3(0, 0.55f, 0);
            float d = MathF.Max(0, Vector3.Distance(pos, bc) - 0.35f);
            // probe just outside the barrel, or the barrel itself would block the line of sight
            var probe = bc + Vector3.Normalize(pos - bc + new Vector3(0, 0.001f, 0)) * 0.55f;
            if (d < radius && L.LineOfSight(pos, probe)) DamageBarrel(b, dmg * (1 - d / radius * 0.7f));
        }
        float pd = Vector3.Distance(pos, P.Pos + Vector3.UnitY);
        if (pd < radius && L.LineOfSight(pos, P.Eye))
            DamagePlayer(dmg * (1 - pd / radius) * (fromPlayer ? 0.4f : 1f), pos);
        P.Shake = MathF.Min(1, P.Shake + MathF.Max(0, 1 - pd / 25f) * MathF.Min(1, 0.25f + radius * 0.12f));
        AddShockwave(pos, radius * 3.2f, MathF.Min(0.06f, 0.012f + radius * 0.009f));
    }

    // =====================================================================  explosive barrels
    public readonly List<Prop> Barrels;
    public const float BarrelRadius = 5f, BarrelDamage = 100f;

    Prop BarrelOnRay(Vector3 o, Vector3 d, float maxT)
    {
        Prop hit = null;
        float best = maxT + 0.02f;
        foreach (var b in Barrels)
            if (!b.Dead && RayAabb(o, d, b.Min, b.Max, out float t) && t <= best) { best = t; hit = b; }
        return hit;
    }

    void DamageBarrel(Prop b, float dmg)
    {
        if (b.Dead || b.Fuse >= 0) return;
        b.Hp -= dmg;
        if (b.Hp <= 0) b.Fuse = 0.08f + (float)Rng.NextDouble() * 0.12f;   // short fuse so chains ripple outward
        else if (b.Hp < Prop.ExplosiveHp * 0.6f) Snd.Play("clink", b.Pos, 0.6f, 0.7f);
    }

    void UpdateBarrels(float dt)
    {
        foreach (var b in Barrels)
        {
            if (b.Dead) continue;
            // a punctured barrel leaks flame
            if (b.Hp < Prop.ExplosiveHp && Rng.NextDouble() < dt * 12)
                AddParticle(new Particle { Pos = b.Pos + new Vector3(0, 1.1f, 0) + RandDir() * 0.15f, Vel = new Vector3(0, 1.5f, 0) + RandDir() * 0.3f, Color = new(1f, 0.5f, 0.15f, 1), Size = 0.35f, Grow = -0.2f, Life = 0.35f, Additive = true });
            if (b.Fuse < 0) continue;
            b.Fuse -= dt;
            if (b.Fuse > 0) continue;
            b.Dead = true;
            var pos = b.Pos + new Vector3(0, 0.6f, 0);
            Explosion(pos, BarrelRadius, BarrelDamage, false, 0);
            // burning debris
            for (int i = 0; i < 10; i++)
                AddParticle(new Particle { Pos = pos, Vel = RandDir() * 6 + Vector3.UnitY * 4, Color = new(0.15f, 0.1f, 0.08f, 1), Size = 0.18f, Life = 1.2f, Gravity = 14, Drag = 0.5f });
            for (int i = 0; i < 8; i++)
                AddParticle(new Particle { Pos = b.Pos + RandDir() * 0.4f, Vel = Vector3.UnitY * 1.2f, Color = new(0.25f, 0.25f, 0.25f, 0.5f), Size = 1.2f, Grow = 1.5f, Life = 2.5f + (float)Rng.NextDouble(), Drag = 1, Smoke = true });
            // a fully cleared cell is walkable again
            var (cx, cy) = Level.CellOf(b.Pos);
            int ci = L.Idx(cx, cy);
            if (L.PropBlock[ci] && L.PropAt[ci].All(pi => L.Props[pi].Dead)) L.PropBlock[ci] = false;
        }
    }

    // =====================================================================  doors / flow
    void UpdateDoors(float dt)
    {
        bool changed = false;
        foreach (var door in L.Doors)
        {
            if (door.Secret)
            {
                if (door.Unlocked) door.Open = MathF.Min(1, door.Open + dt * 0.7f);
                continue;
            }
            var c = Level.Center(door.X, door.Y);
            float pd = Vector2.Distance(new(P.Pos.X, P.Pos.Z), new(c.X, c.Z));
            bool want = false;
            if (door.Locked)
            {
                if (pd < 3.4f && !P.Dead)
                {
                    if (P.Keys[door.Key])
                    {
                        door.Unlocked = true;
                        changed = true;
                        Message($"{Level.KeyNames[door.Key]} SECURITY DOOR UNLOCKED", new Vector4(Level.KeyColors[door.Key], 1));
                    }
                    else if (pd < 3.0f && lockedMsgT <= 0)
                    {
                        Message($"YOU NEED THE {Level.KeyNames[door.Key]} KEYCARD", new Vector4(Level.KeyColors[door.Key], 1));
                        Snd.Play("locked", c + Vector3.UnitY * 2, 0.8f);
                        lockedMsgT = 2.5f;
                    }
                }
            }
            if (door.Unlocked) want = true;
            else if (!door.Locked)
            {
                if (pd < 3.6f) want = true;
                else
                    foreach (var e in Enemies)
                        if (!e.Dead && !e.D.Stationary && Vector2.Distance(new(e.Pos.X, e.Pos.Z), new(c.X, c.Z)) < 3.2f) { want = true; break; }
            }
            if (want) door.HoldOpen = 1.5f;
            door.HoldOpen -= dt;
            if (door.HoldOpen > 0)
            {
                if (door.Open <= 0) Snd.Play("door", c + Vector3.UnitY * 2.5f, 0.7f);
                door.Open = MathF.Min(1, door.Open + dt * 1.5f);
            }
            else if (door.Open > 0)
            {
                bool occupied = pd < 2.2f;
                foreach (var e in Enemies)
                    if (!e.Dead && Vector2.Distance(new(e.Pos.X, e.Pos.Z), new(c.X, c.Z)) < 2.2f + e.D.Radius) { occupied = true; break; }
                if (!occupied)
                {
                    if (door.Open >= 1) Snd.Play("door", c + Vector3.UnitY * 2.5f, 0.6f, 0.9f);
                    door.Open = MathF.Max(0, door.Open - dt * 1.2f);
                }
            }
        }
        if (changed) RefreshLocked();
    }

    void UpdateFlow(float dt)
    {
        flowT -= dt;
        var (cx, cy) = Level.CellOf(P.Pos);
        if (!L.In(cx, cy)) return;
        if (cx != flowX || cy != flowY || flowT <= 0 || flow == null)
        {
            flow = L.Distances(cx, cy, lockedCells);
            flowX = cx; flowY = cy;
            flowT = 1f;
        }
    }

    static readonly int[] NX = { 1, -1, 0, 0, 1, 1, -1, -1 }, NY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    bool Walkable(int x, int y) => L.In(x, y) && L.Cells[L.Idx(x, y)] != 0 && !L.Crate[L.Idx(x, y)] && !L.PropBlock[L.Idx(x, y)] && !lockedCells.Contains(L.Idx(x, y));

    Vector3 FlowDir(Enemy e, float dist)
    {
        var toP = P.Pos - e.Pos;
        toP.Y = 0;
        if (e.CanSee && dist < 9 && toP.LengthSquared() > 0.01f) return Vector3.Normalize(toP);
        var (cx, cy) = Level.CellOf(e.Pos);
        if (flow == null || !L.In(cx, cy) || flow[L.Idx(cx, cy)] == int.MaxValue)
        {
            var tl = e.LastKnown - e.Pos;
            tl.Y = 0;
            return tl.LengthSquared() > 0.5f ? Vector3.Normalize(tl) : Vector3.Zero;
        }
        int cur = flow[L.Idx(cx, cy)];
        if (cur == 0) return toP.LengthSquared() > 0.01f ? Vector3.Normalize(toP) : Vector3.Zero;
        int bx = -1, by = -1;
        float bv = cur - 0.5f;
        for (int k = 0; k < 8; k++)
        {
            int nx = cx + NX[k], ny = cy + NY[k];
            if (!Walkable(nx, ny)) continue;
            if (k >= 4 && (!Walkable(cx + NX[k], cy) || !Walkable(cx, cy + NY[k]))) continue;
            int raw = flow[L.Idx(nx, ny)];
            if (raw == int.MaxValue) continue;
            float v = raw - (k >= 4 ? 0.1f : 0f);
            if (v < bv) { bv = v; bx = nx; by = ny; }
        }
        if (bx < 0) return toP.LengthSquared() > 0.01f ? Vector3.Normalize(toP) : Vector3.Zero;
        var target = Level.Center(bx, by);
        // stay near the centre line in corridors so we do not snag on corners
        var dir = target - e.Pos;
        dir.Y = 0;
        return dir.LengthSquared() > 0.001f ? Vector3.Normalize(dir) : Vector3.Zero;
    }

    // =====================================================================  enemies
    bool CanSee(Enemy e)
    {
        if (P.Dead) return false;
        var to = P.Eye - e.EyePos;
        float dist = to.Length();
        if (dist > Sight) return false;
        if (!e.Alerted && dist > 4.5f)
        {
            var facing = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
            var flat = Vector3.Normalize(new Vector3(to.X, 0, to.Z));
            if (Vector3.Dot(flat, facing) < 0.25f) return false;
        }
        return L.LineOfSight(e.EyePos, P.Eye);
    }

    void UpdateEnemy(Enemy e, float dt)
    {
        if (e.Dead) { e.DeathT += dt; e.Rag?.Update(dt, L); return; }
        var D = e.D;
        e.Anim += dt;
        e.PainFlash -= dt;
        e.AttackCd -= dt;
        e.StateT -= dt;
        if (e.SpawnT > 0) e.SpawnT -= dt;
        if (D.Stationary) { e.Yaw += dt * (e.Type == EnemyDefs.Pylon ? 0.8f : 0.2f); return; }
        if (e.OnCeiling || e.Dropping) { UpdateCeilingCrawler(e, dt); return; }
        if (e.Burrowed) { UpdateBurrowed(e, dt); return; }
        e.SpecialCd -= dt;

        e.SightT -= dt;
        if (e.SightT <= 0)
        {
            float interval = 0.15f + (float)Rng.NextDouble() * 0.1f;
            e.SightT = interval;
            e.CanSee = CanSee(e);
            if (e.CanSee)
            {
                e.LastKnown = P.Pos;
                if (!e.Alerted)
                {
                    float dist0 = Vector3.Distance(e.Pos, P.Pos);
                    e.React += interval * (dist0 < 6 ? 2.5f : 1f);
                    if (e.React >= ReactDelay) Alert(e);
                }
            }
            else if (!e.Alerted) e.React = MathF.Max(0, e.React - interval * 0.5f);
        }

        var toP = P.Pos - e.Pos;
        toP.Y = 0;
        float dist = toP.Length();
        Vector3 want = Vector3.Zero;
        float speed;

        if (!e.Alerted)
        {
            speed = D.Speed * SpeedMul;
            if (e.State == EState.Idle || e.State == EState.Chase || e.State == EState.Pain || e.State == EState.Windup)
            {
                if (e.State != EState.Idle) { e.State = EState.Idle; e.StateT = 1; }
                if (e.StateT <= 0) PickPatrol(e);
            }
            else if (e.State == EState.Patrol)
            {
                if (e.PathIdx >= e.Path.Count) { e.State = EState.Idle; e.StateT = 1.5f + (float)Rng.NextDouble() * 3; }
                else
                {
                    var (tx, ty) = e.Path[e.PathIdx];
                    var tc = Level.Center(tx, ty);
                    var d = tc - e.Pos;
                    d.Y = 0;
                    if (d.Length() < 0.7f) e.PathIdx++;
                    else want = Vector3.Normalize(d) * speed;
                    if (e.StateT < -20) { e.State = EState.Idle; e.StateT = 1; } // stuck safeguard
                }
            }
        }
        else
        {
            speed = D.Chase * SpeedMul;
            if (e.CanSee) e.LostT = 0; else e.LostT += dt;
            if (e.LostT > 14) { e.Alerted = false; e.React = 0; e.State = EState.Idle; e.StateT = 2; }
            float meleeReach = D.Radius + Player.Radius + D.MeleeRange;
            if (e.Type == EnemyDefs.Burrower && !e.CanSee && e.LostT > 2.5f && e.SpecialCd <= 0 && e.State == EState.Chase) { Burrow(e); return; }
            if (e.Type == EnemyDefs.Bloater && UpdateBloater(e, dt, dist)) { if (e.Dead) return; }
            bool bossMove = D.Boss && UpdateBoss(e, dt, dist, toP, ref want);

            if (e.BurstLeft > 0)
            {
                e.BurstT -= dt;
                if (e.BurstT <= 0) { FireAt(e); e.BurstLeft--; e.BurstT = 0.16f; }
            }

            if (!bossMove) switch (e.State)
            {
                case EState.Pain:
                    if (e.StateT <= 0) e.State = EState.Chase;
                    break;
                case EState.Windup:
                    FaceToward(e, toP, dt * 10);
                    if (e.StateT <= 0)
                    {
                        if (e.RangedWindup)
                        {
                            if (L.LineOfSight(e.EyePos, P.Eye))
                            {
                                FireAt(e);
                                e.BurstLeft = D.Burst - 1;
                                e.BurstT = 0.16f;
                            }
                            e.AttackCd = D.RangedCooldown * (0.8f + (float)Rng.NextDouble() * 0.5f) * GMath.Lerp(1.3f, 0.85f, (L.Number - 1) / 29f);
                        }
                        else
                        {
                            if (e.Type == EnemyDefs.Warlord) Slam(e);
                            else if (dist <= meleeReach + 0.4f && MathF.Abs(P.Pos.Y - e.Pos.Y) < 3f)
                                DamagePlayer(D.MeleeDmg * DmgMul, e.Pos);
                            e.AttackCd = D.MeleeCooldown;
                        }
                        e.State = EState.Chase;
                    }
                    break;
                default:
                    e.State = EState.Chase;
                    if (!P.Dead && dist < meleeReach && e.AttackCd <= 0 && D.MeleeDmg > 0)
                    {
                        e.State = EState.Windup;
                        e.StateT = 0.3f;
                        e.RangedWindup = false;
                        Snd.Play("swipe", e.Mid, 0.6f, 0.8f);
                        break;
                    }
                    bool ranged = D.Ranged != ProjKind.None;
                    if (!P.Dead && ranged && e.CanSee && dist < D.RangeMax && dist > meleeReach && e.AttackCd <= 0)
                    {
                        e.State = EState.Windup;
                        e.StateT = Windup;
                        e.RangedWindup = true;
                        break;
                    }
                    e.StrafeT -= dt;
                    if (e.StrafeT <= 0) { e.StrafeT = 0.8f + (float)Rng.NextDouble() * 1.8f; e.StrafeDir = Rng.Next(2) * 2 - 1; }
                    var perp = dist > 0.01f ? new Vector3(-toP.Z, 0, toP.X) / dist : Vector3.Zero;
                    if (P.Dead)
                        want = Vector3.Zero;
                    else if (ranged && e.CanSee && dist < D.RangeMin)
                        want = (-toP / MathF.Max(dist, 0.01f) * 0.7f + perp * e.StrafeDir * 0.6f) * speed;
                    else if (ranged && e.CanSee && dist < D.RangeMax * 0.55f)
                        want = perp * e.StrafeDir * speed * 0.55f;
                    else
                    {
                        want = FlowDir(e, dist) * speed;
                        if (e.Type == EnemyDefs.Stalker && dist > 3) want += perp * MathF.Sin(e.Anim * 5 + e.Seed) * speed * 0.5f;
                    }
                    break;
            }
        }

        // separation from other enemies
        foreach (var o in Enemies)
        {
            if (o == e || o.Dead || o.Hidden || o.OnCeiling) continue;
            var d = e.Pos - o.Pos;
            d.Y = 0;
            float min = e.D.Radius + o.D.Radius;
            float l2 = d.LengthSquared();
            if (l2 < min * min && l2 > 1e-5f)
            {
                float l = MathF.Sqrt(l2);
                want += d / l * (min - l) * 6;
            }
        }
        // don't walk into the player
        if (dist < D.Radius + Player.Radius && dist > 0.01f) want -= toP / dist * 2;

        e.Vel *= MathF.Max(0, 1 - dt * 6);
        var move = (want + e.Vel) * dt;
        float y = e.Pos.Y;
        var before = e.Pos;
        e.Pos = L.Move(e.Pos, new Vector3(move.X, 0, move.Z), D.Radius);
        float moved = new Vector2(e.Pos.X - before.X, e.Pos.Z - before.Z).Length() / MathF.Max(dt, 1e-4f);
        e.MoveSpeed = GMath.Lerp(e.MoveSpeed, moved, MathF.Min(1, dt * 10));
        e.Pos.Y = D.FlyHeight > 0 ? D.FlyHeight + MathF.Sin(e.Anim * 2 + e.Seed) * 0.2f : y;

        if (e.Alerted && (e.CanSee || e.State == EState.Windup) && !P.Dead) FaceToward(e, toP, dt * 6);
        else if (want.LengthSquared() > 0.05f) FaceToward(e, want, dt * 5);
    }

    static void FaceToward(Enemy e, Vector3 dir, float rate)
    {
        if (dir.LengthSquared() < 1e-4f) return;
        float target = MathF.Atan2(dir.X, -dir.Z);
        float diff = GMath.AngleDiff(e.Yaw, target);
        e.Yaw += Math.Clamp(diff, -rate, rate);
    }

    void PickPatrol(Enemy e)
    {
        if (e.D.Boss) { e.StateT = 3; return; }   // bosses hold their ground until alerted
        var (cx, cy) = Level.CellOf(e.Pos);
        if (!L.In(cx, cy)) return;
        var dist = L.Distances(cx, cy, lockedCells);
        var cands = new List<int>();
        for (int y = Math.Max(0, cy - 10); y <= Math.Min(L.H - 1, cy + 10); y++)
            for (int x = Math.Max(0, cx - 10); x <= Math.Min(L.W - 1, cx + 10); x++)
            {
                int v = dist[L.Idx(x, y)];
                if (v >= 3 && v <= 10) cands.Add(L.Idx(x, y));
            }
        if (cands.Count == 0) { e.StateT = 3; return; }
        int t = cands[Rng.Next(cands.Count)];
        // walk back from target to source along decreasing distance
        var path = new List<(int, int)>();
        int px = t % L.W, py = t / L.W;
        while (dist[L.Idx(px, py)] > 0)
        {
            path.Add((px, py));
            int bv = dist[L.Idx(px, py)];
            int bx = px, by = py;
            for (int k = 0; k < 4; k++)
            {
                int nx = px + NX[k], ny = py + NY[k];
                if (L.In(nx, ny) && dist[L.Idx(nx, ny)] < bv) { bv = dist[L.Idx(nx, ny)]; bx = nx; by = ny; }
            }
            if (bx == px && by == py) break;
            px = bx; py = by;
        }
        path.Reverse();
        e.Path = path;
        e.PathIdx = 0;
        e.State = EState.Patrol;
        e.StateT = 0;
    }

    void FireAt(Enemy e)
    {
        var D = e.D;
        var facing = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
        var src = e.EyePos + facing * (D.Radius + 0.2f) - Vector3.UnitY * 0.2f;
        var target = P.Pos + new Vector3(0, 1.2f, 0);
        float t = Vector3.Distance(src, target) / D.ProjSpeed;
        target += P.Vel * t * AimLead;
        var dir = Vector3.Normalize(target - src);
        int n = e.Type is EnemyDefs.Overseer or EnemyDefs.Mind ? 3 : e.Type == EnemyDefs.Queen ? 5 : 1;
        for (int i = 0; i < n; i++)
        {
            var d = dir;
            if (n > 1) d = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (i - (n - 1) / 2f) * 0.13f));
            d = Spread(d, Inaccuracy);
            SpawnProj(D.Ranged, src, d * D.ProjSpeed, D.RangedDmg * DmgMul, D.Splash, false);
        }
        string snd = D.Ranged switch { ProjKind.EnemyAcid => "spit", ProjKind.EnemyPlasma => "eplasma", _ => "orb" };
        Snd.Play(snd, src, 0.8f, D.Ranged == ProjKind.BruteOrb ? 0.6f : 1f);
    }

    // =====================================================================  projectiles
    public Projectile SpawnProj(ProjKind kind, Vector3 pos, Vector3 vel, float dmg, float splash, bool fromPlayer)
    {
        var p = new Projectile { Kind = kind, Pos = pos, Vel = vel, Damage = dmg, Splash = splash, FromPlayer = fromPlayer, Life = 6 };
        switch (kind)
        {
            case ProjKind.Grenade: p.Gravity = 14; p.Life = 2.2f; p.Radius = 0.15f; break;
            case ProjKind.Acid: p.Gravity = 5; p.Radius = 0.2f; break;
            case ProjKind.EnemyAcid: p.Gravity = 4; p.Radius = 0.2f; break;
            case ProjKind.Void: p.Radius = 0.45f; break;
            case ProjKind.BruteOrb: p.Radius = 0.4f; break;
            case ProjKind.EnemyOrb: p.Radius = 0.25f; break;
        }
        Projs.Add(p);
        return p;
    }

    void UpdateProjectiles(float dt)
    {
        for (int i = Projs.Count - 1; i >= 0; i--)
        {
            var p = Projs[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                if (p.Kind == ProjKind.Grenade) Impact(p, p.Pos, Vector3.UnitY, null);
                Projs.RemoveAt(i);
                continue;
            }
            p.Vel.Y -= p.Gravity * dt;
            float len = p.Vel.Length() * dt;
            int steps = Math.Max(1, (int)MathF.Ceiling(len / 0.3f));
            bool dead = false;
            for (int s = 0; s < steps && !dead; s++)
            {
                var step = p.Vel * (dt / steps);
                float sl = step.Length();
                if (sl < 1e-6f) break;
                var dir = step / sl;
                float t = L.Raycast(p.Pos, dir, sl + p.Radius * 0.5f, out var n);
                // entity hits
                if (p.FromPlayer)
                {
                    foreach (var e in Enemies)
                    {
                        if (e.Dead || e.Hidden) continue;
                        if (RayEnemy(p.Pos, dir, e, out float te) && te <= sl + p.Radius && te < t)
                        {
                            ShotsHit++;
                            Impact(p, p.Pos + dir * te, -dir, e);
                            dead = true;
                            break;
                        }
                    }
                }
                else if (!P.Dead)
                {
                    var c = P.Pos + new Vector3(0, 1.0f, 0);
                    var min = new Vector3(c.X - 0.45f, 0, c.Z - 0.45f);
                    var max = new Vector3(c.X + 0.45f, 1.9f, c.Z + 0.45f);
                    if (RayAabb(p.Pos, dir, min - new Vector3(p.Radius), max + new Vector3(p.Radius), out float tp) && tp <= sl && tp < t)
                    {
                        DamagePlayer(p.Damage, p.Pos);
                        Impact(p, p.Pos + dir * tp, -dir, null, true);
                        dead = true;
                    }
                }
                if (dead) break;
                if (t < sl + p.Radius * 0.5f)
                {
                    var hit = p.Pos + dir * MathF.Max(0, t - 0.05f);
                    if (p.Kind == ProjKind.Grenade && p.Bounces < 6)
                    {
                        p.Vel = Vector3.Reflect(p.Vel, n) * 0.55f;
                        p.Pos = hit + n * 0.05f;
                        p.Bounces++;
                        if (p.Vel.Length() > 2) Snd.Play("clink", p.Pos, 0.5f);
                        break;
                    }
                    Impact(p, hit, n, null);
                    dead = true;
                    break;
                }
                p.Pos += step;
            }
            if (dead) { Projs.RemoveAt(i); continue; }
            // trails
            p.Trail -= dt;
            if (p.Trail <= 0)
            {
                p.Trail = 0.02f;
                switch (p.Kind)
                {
                    case ProjKind.Rocket:
                        AddParticle(new Particle { Pos = p.Pos, Vel = RandDir() * 0.3f, Color = new(0.5f, 0.5f, 0.5f, 0.5f), Size = 0.3f, Grow = 1.2f, Life = 0.9f, Drag = 1, Smoke = true });
                        AddParticle(new Particle { Pos = p.Pos, Vel = -p.Vel * 0.05f, Color = new(1f, 0.6f, 0.2f, 1), Size = 0.35f, Grow = -0.3f, Life = 0.12f, Additive = true });
                        break;
                    case ProjKind.Grenade:
                        AddParticle(new Particle { Pos = p.Pos, Vel = Vector3.Zero, Color = new(0.5f, 0.5f, 0.5f, 0.25f), Size = 0.15f, Grow = 0.5f, Life = 0.5f, Smoke = true });
                        break;
                    case ProjKind.Acid:
                    case ProjKind.EnemyAcid:
                        AddParticle(new Particle { Pos = p.Pos, Vel = RandDir() * 0.4f, Color = new(0.4f, 1f, 0.2f, 0.8f), Size = 0.15f, Life = 0.4f, Gravity = 6, Additive = true });
                        break;
                    case ProjKind.Void:
                        AddParticle(new Particle { Pos = p.Pos + RandDir() * 0.4f, Vel = RandDir() * 0.5f, Color = new(0.6f, 0.2f, 1f, 1), Size = 0.3f, Grow = -0.4f, Life = 0.5f, Additive = true });
                        break;
                }
            }
        }
    }

    void Impact(Projectile p, Vector3 at, Vector3 n, Enemy hitE, bool hitPlayer = false)
    {
        // a direct hit on an explosive barrel (splash weapons also reach it through Explosion)
        if (hitE == null && !hitPlayer)
            foreach (var v in L.Vents)
                if (!v.Broken && Vector3.Distance(at, v.Pos) < 0.7f) { DamageVent(v, p.Damage); break; }
        if (hitE == null && !hitPlayer)
            foreach (var b in Barrels)
                if (!b.Dead && at.X > b.Min.X - 0.2f && at.X < b.Max.X + 0.2f && at.Z > b.Min.Z - 0.2f && at.Z < b.Max.Z + 0.2f && at.Y < b.Max.Y + 0.2f)
                {
                    DamageBarrel(b, p.Damage);
                    break;
                }
        switch (p.Kind)
        {
            case ProjKind.Rocket:
            case ProjKind.Grenade:
                if (hitE != null) DamageEnemy(hitE, p.Damage * 0.4f, Vector3.Normalize(p.Vel));
                Explosion(at + n * 0.3f, p.Splash, p.Damage, true, 0);
                if (p.Cluster)
                    for (int k = 0; k < 5; k++)
                    {
                        // bomblets scatter and go off a moment later
                        var b = SpawnProj(ProjKind.Grenade, at + n * 0.4f, RandDir() * 5 + n * 3 + Vector3.UnitY * 3, 38, 2.6f, true);
                        b.Life = 0.45f + (float)Rng.NextDouble() * 0.45f;
                    }
                break;
            case ProjKind.Void:
                if (hitE != null) DamageEnemy(hitE, p.Damage * 0.5f, Vector3.Normalize(p.Vel));
                Explosion(at + n * 0.3f, p.Splash, p.Damage, true, 2);
                Snd.Play("void", at, 0.6f, 0.5f);
                break;
            case ProjKind.Acid:
                if (hitE != null) DamageEnemy(hitE, p.Damage, Vector3.Normalize(p.Vel));
                Explosion(at + n * 0.2f, p.Splash, p.Damage * 0.5f, true, 1, false);
                Snd.Play("splat", at, 0.9f);
                break;
            case ProjKind.Plasma:
                if (hitE != null) DamageEnemy(hitE, p.Damage, Vector3.Normalize(p.Vel));
                Sparks(at, n, 8, new Vector4(0.4f, 0.8f, 1f, 1));
                DynLights.Add(new DynLight { Pos = at + n * 0.3f, Color = new Vector3(0.4f, 0.8f, 1.5f), Radius = 5, Life = 0.15f, MaxLife = 0.15f });
                break;
            case ProjKind.EnemyAcid:
                Sparks(at, n, 10, new Vector4(0.4f, 1f, 0.2f, 1));
                if (!hitPlayer && p.Splash > 0 && Vector3.Distance(at, P.Pos + Vector3.UnitY) < p.Splash) DamagePlayer(p.Damage * 0.5f, at);
                Snd.Play("splat", at, 0.7f);
                break;
            case ProjKind.EnemyPlasma:
                Sparks(at, n, 6, new Vector4(1f, 0.4f, 0.9f, 1));
                break;
            case ProjKind.EnemyOrb:
                Sparks(at, n, 10, new Vector4(0.8f, 0.5f, 1f, 1));
                DynLights.Add(new DynLight { Pos = at + n * 0.3f, Color = new Vector3(0.8f, 0.4f, 1.5f), Radius = 5, Life = 0.2f, MaxLife = 0.2f });
                break;
            case ProjKind.BruteOrb:
                Explosion(at + n * 0.3f, p.Splash, hitPlayer ? p.Damage * 0.3f : p.Damage, false, 3);
                break;
        }
    }

    // =====================================================================  pickups
    void UpdatePickups(float dt)
    {
        foreach (var pk in Pickups)
        {
            if (pk.Taken) continue;
            pk.Phase += dt;
            if (pk.Dropped)
            {
                pk.Vel.Y -= 12 * dt;
                pk.Pos += pk.Vel * dt;
                if (pk.Pos.Y < 0.35f) { pk.Pos.Y = 0.35f; pk.Vel = Vector3.Zero; }
            }
            if (P.Dead) continue;
            float d = Vector2.Distance(new(pk.Pos.X, pk.Pos.Z), new(P.Pos.X, P.Pos.Z));
            if (d < 1.25f && TryTake(pk))
            {
                pk.Taken = true;
                if (pk.Kind != PickupKind.Key && !pk.Dropped) ItemsTaken++;
                P.PickupFlash = 0.5f;
            }
        }
        Pickups.RemoveAll(p => p.Taken);
    }

    bool GiveAmmo(int type, int amount)
    {
        if (P.Ammo[type] >= P.MaxAmmo(type)) return false;
        P.Ammo[type] = Math.Min(P.MaxAmmo(type), P.Ammo[type] + amount);
        return true;
    }

    bool TryTake(Pickup pk)
    {
        var green = new Vector4(0.6f, 1f, 0.6f, 1);
        switch (pk.Kind)
        {
            case PickupKind.Health:
                if (P.Health >= 100) return false;
                P.Health = MathF.Min(100, P.Health + 15);
                Message("PICKED UP A STIMPACK (+15)", green); Snd.Play("health", null, 0.7f); return true;
            case PickupKind.Medkit:
                if (P.Health >= 100) return false;
                P.Health = MathF.Min(100, P.Health + 40);
                Message("PICKED UP A MEDKIT (+40)", green); Snd.Play("health", null, 0.7f); return true;
            case PickupKind.MegaHealth:
                if (P.Health >= 200) return false;
                P.Health = MathF.Min(200, P.Health + 100);
                Message("XENO-SERUM! (+100 HEALTH)", new Vector4(0.4f, 0.8f, 1f, 1)); Snd.Play("secret", null, 0.8f); return true;
            case PickupKind.Armor:
                if (P.Armor >= 100) return false;
                P.Armor = MathF.Min(100, P.Armor + 15);
                Message("PICKED UP ARMOR PLATING (+15)", new Vector4(0.6f, 0.8f, 1f, 1)); Snd.Play("pickup", null, 0.7f); return true;
            case PickupKind.ArmorVest:
                if (P.Armor >= 100) return false;
                P.Armor = MathF.Min(100, P.Armor + 50);
                Message("PICKED UP A KEVLAR VEST (+50)", new Vector4(0.6f, 0.8f, 1f, 1)); Snd.Play("pickup", null, 0.7f); return true;
            case PickupKind.Bullets:
            case PickupKind.Shells:
            case PickupKind.Grenades:
            case PickupKind.Rockets:
            case PickupKind.Cells:
            {
                int t = pk.Kind - PickupKind.Bullets;
                int amt = pk.Dropped ? Weapons.AmmoPickup[t] / 2 + 1 : Weapons.AmmoPickup[t];
                if (!GiveAmmo(t, amt)) return false;
                Message($"PICKED UP {amt} {Weapons.AmmoNames[t]}");
                Snd.Play("pickup", null, 0.6f);
                return true;
            }
            case PickupKind.Weapon:
            {
                var w = Weapons.All[pk.Arg];
                bool had = P.Has[pk.Arg];
                bool gotAmmo = w.Ammo >= 0 && GiveAmmo(w.Ammo, w.PickupAmmo);
                if (had && !gotAmmo) return false;
                P.Has[pk.Arg] = true;
                if (!had)
                {
                    P.Pending = pk.Arg;
                    Message(w.Alien ? $"ALIEN WEAPON ACQUIRED: {w.Name}!" : $"YOU GOT THE {w.Name}!", new Vector4(1f, 0.9f, 0.4f, 1));
                    Snd.Play("weapon", null, 0.9f);
                }
                else
                {
                    Message($"{w.Name} AMMO");
                    Snd.Play("pickup", null, 0.6f);
                }
                return true;
            }
            case PickupKind.Backpack:
            {
                bool first = !P.Backpack;
                P.Backpack = true;
                bool any = first;
                for (int t = 0; t < 5; t++) any |= GiveAmmo(t, Weapons.AmmoPickup[t]);
                if (!any) return false;
                Message(first ? "AMMO BACKPACK! CAPACITY +50%" : "PICKED UP AN AMMO BACKPACK", new Vector4(1f, 0.85f, 0.3f, 1));
                Snd.Play("weapon", null, 0.9f);
                return true;
            }
            case PickupKind.DamageAmp:
                P.AmpT = 25;
                Message("DAMAGE AMPLIFIER! DOUBLE DAMAGE FOR 25 SECONDS", new Vector4(1f, 0.5f, 0.15f, 1));
                Snd.Play("secret", null, 1f, 0.7f, 0);
                return true;
            case PickupKind.NightVision:
            case PickupKind.XRay:
            {
                bool xray = pk.Kind == PickupKind.XRay;
                bool first = xray ? !P.HasXRay : !P.HasNV;
                if (!first && (xray ? P.XRayCharge : P.NVCharge) >= Player.GoggleMax) return false;
                if (xray) { P.HasXRay = true; P.XRayCharge = Player.GoggleMax; }
                else { P.HasNV = true; P.NVCharge = Player.GoggleMax; }
                Message(first ? (xray ? "X-RAY GOGGLES! PRESS X TO SEE THROUGH WALLS" : "NIGHT VISION GOGGLES! PRESS N TO SEE IN THE DARK")
                    : (xray ? "X-RAY GOGGLES RECHARGED" : "NIGHT VISION RECHARGED"), new Vector4(0.5f, 1f, 0.5f, 1));
                Snd.Play("weapon", null, 0.9f);
                return true;
            }
            case PickupKind.Battery:
            {
                bool nv = P.HasNV && P.NVCharge < Player.GoggleMax, xr = P.HasXRay && P.XRayCharge < Player.GoggleMax;
                if (!nv && !xr) return false;
                if (nv) P.NVCharge = MathF.Min(Player.GoggleMax, P.NVCharge + Player.BatteryCharge);
                if (xr) P.XRayCharge = MathF.Min(Player.GoggleMax, P.XRayCharge + Player.BatteryCharge);
                Message("PICKED UP A BATTERY - GOGGLES RECHARGED", new Vector4(0.5f, 1f, 0.5f, 1));
                Snd.Play("pickup", null, 0.7f, 1.3f, 0);
                return true;
            }
            case PickupKind.Upgrade:
            {
                if (P.Upgraded[pk.Arg]) return false;
                P.Upgraded[pk.Arg] = true;
                var wn = Weapons.All[pk.Arg].Name;
                Message($"UPGRADE: {Weapons.UpgradeNames[pk.Arg]} FOR THE {wn}" + (P.Has[pk.Arg] ? "!" : " (ONCE YOU FIND IT)"), new Vector4(1f, 0.8f, 0.3f, 1));
                Snd.Play("secret", null, 0.9f, 1.2f, 0);
                return true;
            }
            case PickupKind.Key:
                P.Keys[pk.Arg] = true;
                Message($"PICKED UP THE {Level.KeyNames[pk.Arg]} KEYCARD", new Vector4(Level.KeyColors[pk.Arg], 1));
                Snd.Play("key", null, 0.9f);
                return true;
        }
        return false;
    }

    // =====================================================================  goggles
    void ToggleGoggles(bool xray)
    {
        bool has = xray ? P.HasXRay : P.HasNV;
        string name = xray ? "X-RAY GOGGLES" : "NIGHT VISION";
        if (!has) { Message($"NO {name} - SEARCH SECRET AREAS", new Vector4(0.7f, 0.7f, 0.7f, 1)); Snd.Play("empty", null, 0.5f); return; }
        bool on = xray ? P.XRayOn : P.NVOn;
        if (!on && (xray ? P.XRayCharge : P.NVCharge) <= 0) { Message($"{name} BATTERY EMPTY - FIND BATTERIES", new Vector4(1f, 0.5f, 0.3f, 1)); Snd.Play("empty", null, 0.6f); return; }
        // one set of goggles at a time
        if (xray) { P.XRayOn = !on; if (P.XRayOn) P.NVOn = false; }
        else { P.NVOn = !on; if (P.NVOn) P.XRayOn = false; }
        Snd.Play(on ? "empty" : "pickup", null, 0.5f, on ? 0.8f : 1.6f, 0);
    }

    void DrainGoggles(float dt)
    {
        if (P.NVOn && (P.NVCharge -= dt) <= 0)
        {
            P.NVCharge = 0; P.NVOn = false;
            Message("NIGHT VISION BATTERY DEPLETED", new Vector4(1f, 0.5f, 0.3f, 1)); Snd.Play("empty", null, 0.7f);
        }
        if (P.XRayOn && (P.XRayCharge -= dt) <= 0)
        {
            P.XRayCharge = 0; P.XRayOn = false;
            Message("X-RAY BATTERY DEPLETED", new Vector4(1f, 0.5f, 0.3f, 1)); Snd.Play("empty", null, 0.7f);
        }
    }

    // =====================================================================  portal
    void UpdatePortal(float dt)
    {
        if (VictoryT >= 0)
        {
            VictoryT += dt;
            if (PortalE != null && Rng.NextDouble() < dt * 8 && VictoryT < 3.5f)
                Explosion(PortalE.Mid + RandDir() * 2.5f, 3, 0, true, Rng.Next(2) * 2, Rng.NextDouble() < 0.4);
            if (VictoryT > 1.2f && VictoryT - dt <= 1.2f) Snd.Play("bigboom", null, 1f, 1, 0);
            if (VictoryT > 5f) Victory = true;
            return;
        }
        if (PortalE == null || PortalE.Dead) return;
        Snd.SetLoop("portal", PortalE.Mid, 1.2f);
        if (Rng.NextDouble() < dt * 20)
            AddParticle(new Particle { Pos = PortalE.Mid + RandDir() * 2f, Vel = RandDir() * 0.5f, Color = new(0.7f, 0.3f, 1f, 1), Size = 0.25f, Grow = -0.2f, Life = 1f, Additive = true });
        float pd = Vector3.Distance(P.Pos, PortalE.Pos);
        if (pd > 45 || P.Dead) return;
        portalSpawnT -= dt;
        int alive = Enemies.Count(e => !e.Dead && !e.D.Stationary);
        if (portalSpawnT <= 0 && alive < 10)
        {
            portalSpawnT = PortalE.Invulnerable ? 10f : 6.5f;
            int type = EnemyDefs.PickType(30, Rng);
            if (type == EnemyDefs.Overseer && Rng.NextDouble() < 0.6) type = EnemyDefs.Stalker;
            float a = (float)Rng.NextDouble() * MathF.Tau;
            var pos = PortalE.Pos + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 3.2f;
            if (L.Blocked(pos.X, pos.Z, 1f)) pos = PortalE.Pos + new Vector3(0, 0, 3.2f);
            if (L.Blocked(pos.X, pos.Z, 1f)) return;
            var e = SpawnEnemy(type, pos);
            e.SpawnT = 1;
            TotalEnemies++;
            Alert(e);
            for (int i = 0; i < 20; i++)
                AddParticle(new Particle { Pos = e.Mid, Vel = RandDir() * 4, Color = new(0.7f, 0.3f, 1f, 1), Size = 0.4f, Grow = -0.3f, Life = 0.6f, Drag = 2, Additive = true });
            Snd.Play("wail", e.Mid, 0.8f, 1.3f);
        }
    }

    // =====================================================================  effects
    public Vector3 RandDir()
    {
        Vector3 v;
        do v = new Vector3((float)Rng.NextDouble() * 2 - 1, (float)Rng.NextDouble() * 2 - 1, (float)Rng.NextDouble() * 2 - 1);
        while (v.LengthSquared() > 1 || v.LengthSquared() < 0.01f);
        return Vector3.Normalize(v);
    }

    public void AddParticle(Particle p)
    {
        if (Particles.Count > 4000) return;
        p.MaxLife = p.Life;
        Particles.Add(p);
    }

    void Sparks(Vector3 pos, Vector3 n, int count, Vector4 c)
    {
        for (int i = 0; i < count; i++)
            AddParticle(new Particle { Pos = pos, Vel = (n + RandDir() * 0.8f) * (3 + (float)Rng.NextDouble() * 5), Color = c, Size = 0.06f, Life = 0.2f + (float)Rng.NextDouble() * 0.3f, Gravity = 12, Additive = true });
    }

    void Puff(Vector3 pos, float size, Vector4 c) =>
        AddParticle(new Particle { Pos = pos, Vel = Vector3.UnitY * 0.3f, Color = c, Size = size, Grow = 0.8f, Life = 0.6f, Smoke = true });

    Vector4 BloodColor => Vector4.Lerp(new Vector4(0.45f, 0.75f, 0.1f, 0.9f), new Vector4(0.55f, 0.2f, 0.7f, 0.9f), (L.Number - 1) / 29f);

    void Blood(Vector3 pos, Vector3 dir, int count)
    {
        var c = BloodColor;
        for (int i = 0; i < count; i++)
            AddParticle(new Particle { Pos = pos, Vel = dir + RandDir() * 3, Color = c, Size = 0.1f + (float)Rng.NextDouble() * 0.12f, Life = 0.5f + (float)Rng.NextDouble() * 0.5f, Gravity = 14, Drag = 0.5f });
    }

    void Gibs(Enemy e)
    {
        int n = (int)(6 + e.D.Radius * 10);
        var col = new Vector4(e.D.Color * 0.8f, 1);
        for (int i = 0; i < n; i++)
            AddParticle(new Particle { Pos = e.Mid + RandDir() * e.D.Radius * 0.5f, Vel = RandDir() * 5 + Vector3.UnitY * 3, Color = col, Size = 0.18f + (float)Rng.NextDouble() * 0.15f, Life = 1.5f + (float)Rng.NextDouble(), Gravity = 16, Drag = 0.3f });
    }

    void UpdateEffects(float dt)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Particles);
        for (int i = 0; i < span.Length; i++)
        {
            ref var p = ref span[i];
            p.Life -= dt;
            p.Vel.Y -= p.Gravity * dt;
            p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
            p.Pos += p.Vel * dt;
            p.Size = MathF.Max(0.01f, p.Size + p.Grow * dt);
            if (p.Gravity > 0 && p.Pos.Y < 0.03f) { p.Pos.Y = 0.03f; p.Vel *= new Vector3(0.3f, -0.2f, 0.3f); }
        }
        Particles.RemoveAll(p => p.Life <= 0);
        var bs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Beams);
        for (int i = 0; i < bs.Length; i++) bs[i].Life -= dt;
        Beams.RemoveAll(b => b.Life <= 0);
        var ls = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(DynLights);
        for (int i = 0; i < ls.Length; i++) ls[i].Life -= dt;
        DynLights.RemoveAll(l => l.Life <= 0);
    }
}
