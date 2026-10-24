using System.Numerics;
using AlienInvasion.Engine;

namespace AlienInvasion.Game;

/// <summary>Headless checks for the gameplay systems: special aliens, vents, lockdowns, hazards,
/// power failure, self-destruct, bosses, weapon upgrades and scoring.</summary>
public static class FeatureTest
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
    }

    static World Make(int level, int seed, out Player p)
    {
        p = new Player();
        p.ResetNew();
        return new World(Level.Generate(level, seed), p, new SoundSystem()) { };
    }

    static void Run(World w, float seconds, InputState? inp = null)
    {
        for (float t = 0; t < seconds; t += 1 / 60f) w.Update(1 / 60f, inp ?? new InputState { Select = -1 });
    }

    /// <summary>An open floor spot with clear space around it, away from the start.</summary>
    static Vector3 OpenSpot(World w)
    {
        var L = w.L;
        foreach (var r in L.Rooms.Where(r => r != L.StartRoom).OrderByDescending(r => r.W * r.H))
            for (int y = r.Y + 1; y < r.Y + r.H - 1; y++)
                for (int x = r.X + 1; x < r.X + r.W - 1; x++)
                {
                    var c = Level.Center(x, y);
                    if (!L.Blocked(c.X, c.Z, 1.2f) && !L.Hazards.Any(h => Vector3.Distance(h.Pos, c) < 4)) return c;
                }
        return Level.Center(L.StartX, L.StartY);
    }

    /// <summary>A clear spot at the given distance from <paramref name="from"/>, in sight of it.</summary>
    static Vector3 NearSpot(World w, Vector3 from, float dist)
    {
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16;
            var c = from + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * dist;
            if (!w.L.Blocked(c.X, c.Z, 0.8f) && w.L.LineOfSight(c + Vector3.UnitY * 1.2f, from + Vector3.UnitY * 1.2f)) return c;
        }
        return from + new Vector3(dist, 0, 0);
    }

    public static void RunAll()
    {
        // ---------------- bloater ----------------
        {
            var w = Make(8, 101, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            var e = w.SpawnEnemy(EnemyDefs.Bloater, NearSpot(w, p.Pos, 3));
            float hp0 = p.Health + p.Armor;
            Run(w, 4);
            Check("bloater charges and bursts", e.Dead && p.Health + p.Armor < hp0, $"dead={e.Dead} hp {hp0}->{p.Health + p.Armor:F0}");
        }
        // ---------------- burrower ----------------
        {
            var w = Make(10, 102, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            var e = w.SpawnEnemy(EnemyDefs.Burrower, NearSpot(w, p.Pos, 6));
            bool wasHidden = e.Hidden;
            Run(w, 6);
            Check("burrower tunnels in and erupts", wasHidden && !e.Hidden, $"hidden {wasHidden}->{e.Hidden} dist={Vector3.Distance(e.Pos, p.Pos):F1}");
        }
        // ---------------- ceiling crawler ----------------
        {
            var w = Make(6, 103, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            var e = w.SpawnEnemy(EnemyDefs.Crawler, p.Pos + new Vector3(1.5f, 0, 0));
            bool up = e.OnCeiling && e.Pos.Y > 3;
            Run(w, 2);
            Check("ceiling crawler drops on the player", up && !e.OnCeiling && e.Pos.Y < 0.01f, $"y={e.Pos.Y:F2}");
        }
        // ---------------- vents ----------------
        if (Level.VentsEnabled)
        {
            int spawned = 0, tested = 0;
            for (int seed = 0; seed < 10 && tested < 3; seed++)
            {
                var w = Make(12, 200 + seed, out var p);
                var v = w.L.Vents.FirstOrDefault(v => !w.L.Lockdowns.Any(l => l.Room == v.Room));
                if (v == null) continue;
                tested++;
                w.Enemies.Clear();
                p.Pos = Level.Center(v.X, v.Y) + v.N * 2.5f;
                if (w.L.Blocked(p.Pos.X, p.Pos.Z, Player.Radius)) p.Pos = Level.Center(v.X, v.Y);
                v.Cd = 0.2f;
                int before = w.Enemies.Count;
                Run(w, 1);
                spawned += w.Enemies.Count - before;
            }
            Check("aliens crawl out of vents", tested > 0 && spawned >= tested, $"{spawned} spawned from {tested} vents");
        }
        // ---------------- lockdown ----------------
        if (Level.LockdownsEnabled)
        {
            World w = null; Player p = null; Lockdown ld = null;
            for (int seed = 0; seed < 20 && ld == null; seed++) { w = Make(9, 300 + seed, out p); ld = w.L.Lockdowns.FirstOrDefault(); }
            if (ld == null) Check("lockdown room exists", false);
            else
            {
                w.God = true;
                var room = w.L.Rooms[ld.Room];
                w.Enemies.Clear();
                p.Pos = Level.Center(room.CX, room.CY);
                Run(w, 0.3f);
                var (bx, by, _, _) = ld.Barriers[0];
                var bc = Level.Center(bx, by);
                bool sealedUp = ld.State == 1 && w.L.Blocked(bc.X, bc.Z, 0.3f);
                // fight: kill whatever comes out
                for (int f = 0; f < 60 * 90 && ld.State == 1; f++)
                {
                    w.Update(1 / 60f, new InputState { Select = -1 });
                    foreach (var e in ld.Wave) if (!e.Dead && e.SpawnT <= 0) w.DamageEnemy(e, 9999, Vector3.UnitX);
                }
                bool open = ld.State == 2 && !w.L.Barrier.Any(b => b);
                Check("lockdown seals the room, spawns a wave and lifts", sealedUp && open, $"sealed={sealedUp} wave={ld.Spawned}/{ld.Total} cleared={open}");
            }
        }
        // ---------------- hazards ----------------
        {
            World w = null; Player p = null; Hazard h = null;
            for (int seed = 0; seed < 10 && h == null; seed++) { w = Make(12, 400 + seed, out p); h = w.L.Hazards.FirstOrDefault(x => x.Kind == HazardKind.Steam); }
            w.Enemies.Clear();
            p.Pos = new Vector3(h.Pos.X, 0, h.Pos.Z);
            float hp0 = p.Health + p.Armor;
            Run(w, 6);
            Check("steam vent scalds the player", p.Health + p.Armor < hp0, $"hp {hp0}->{p.Health + p.Armor:F0}");
            var cable = Enumerable.Range(0, 10).Select(s => Make(14, 450 + s, out _)).SelectMany(x => x.L.Hazards.Select(hz => (x, hz))).FirstOrDefault(t => t.hz.Kind == HazardKind.Cable);
            if (cable.x != null)
            {
                var w2 = cable.x; var p2 = w2.P;
                w2.Enemies.Clear();
                p2.Pos = new Vector3(cable.hz.Pos.X, 0, cable.hz.Pos.Z) + cable.hz.N * 0.3f;
                float h0 = p2.Health + p2.Armor;
                Run(w2, 1);
                Check("live cable shocks the player", p2.Health + p2.Armor < h0, $"hp {h0}->{p2.Health + p2.Armor:F0}");
            }
        }
        // ---------------- power failure ----------------
        {
            World w = null;
            for (int seed = 0; seed < 30; seed++) { w = Make(12, 500 + seed, out _); if (w.L.PowerFailure) break; }
            w.Enemies.Clear();
            w.LevelTime = 200;
            Run(w, 3);
            float out1 = w.PowerLevel;
            Run(w, 60);
            Check("power fails and is restored", w.L.PowerFailure && out1 == 0 && w.PowerLevel == 1, $"during={out1} after={w.PowerLevel}");
        }
        // ---------------- self-destruct ----------------
        {
            var w = Make(28, 601, out var p);
            w.Enemies.Clear();
            for (int j = 0; j < w.L.LockCount; j++) p.Keys[w.L.LockColor[j]] = true;
            Run(w, 0.5f);
            float armed = w.SelfDestructT;
            Run(w, armed + 1);
            Check("self-destruct arms with the last key and kills a slow player", armed > 0 && p.Dead, $"timer={armed:F0}s dead={p.Dead}");
        }
        // ---------------- elevator waits for the radio ----------------
        {
            var w = Make(1, 650, out var p);
            w.God = true;
            var use = new InputState { Select = -1, UsePressed = true };
            var idle = new InputState { Select = -1 };
            Run(w, 2);
            p.Pos = Level.Center(w.L.ExitX, w.L.ExitY);
            w.Update(1 / 60f, use);
            w.Update(1 / 60f, use);
            bool early = w.ExitReached, talked = w.CommBusy;
            float waited = 0;
            for (; waited < 60 && !w.ElevatorReady; waited += 1 / 60f) { p.Pos = Level.Center(w.L.ExitX, w.L.ExitY); w.Update(1 / 60f, idle); }
            w.Update(1 / 60f, use);
            Check("elevator ignores E for its first few seconds", !early && talked && w.ExitReached, $"early={early} radio={talked} waited={waited:F1}s exit={w.ExitReached}");
        }
        // ---------------- bosses ----------------
        foreach (int lvl in new[] { 5, 10, 15 })
        {
            var w = Make(lvl, 700 + lvl, out var p);
            var boss = w.Boss;
            if (boss == null) { Check($"boss on sublevel {lvl}", false); continue; }
            w.God = true;
            p.Pos = Level.Center(w.L.ExitX, w.L.ExitY);
            var use = new InputState { Select = -1, UsePressed = true };
            w.Update(1 / 60f, use);
            bool locked = !w.ExitReached;
            Run(w, 8);   // let it fight a little (summons, charges, teleports)
            int pk0 = w.Pickups.Count;
            w.DamageEnemy(boss, 1e6f, Vector3.UnitX);
            Run(w, 0.5f);
            int drops = w.Pickups.Count - pk0;
            p.Pos = Level.Center(w.L.ExitX, w.L.ExitY);
            var idle = new InputState { Select = -1 };
            float waited = 0;
            for (; waited < 60 && !w.ElevatorReady; waited += 1 / 60f) { p.Pos = Level.Center(w.L.ExitX, w.L.ExitY); w.Update(1 / 60f, idle); }   // let the radio lines play out
            w.Update(1 / 60f, use);
            Check($"{boss.D.Name} locks the elevator, drops loot, unlocks it", locked && drops >= 4 && w.ExitReached, $"locked={locked} drops={drops} exit={w.ExitReached} minions={w.Enemies.Count(e => !e.Dead && !e.D.Boss)} waited={waited:F0}s");
        }
        // ---------------- weapon upgrades ----------------
        {
            var w = Make(12, 801, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            p.Has[Weapons.GrenadeLauncher] = true; p.Upgraded[Weapons.GrenadeLauncher] = true;
            p.Weapon = p.Pending = Weapons.GrenadeLauncher; p.Ammo[Weapons.Grenades] = 10;
            p.Pitch = -0.3f;
            w.Update(1 / 60f, new InputState { Select = -1, Fire = true });
            int max = 0;
            for (int f = 0; f < 200; f++) { w.Update(1 / 60f, new InputState { Select = -1 }); max = Math.Max(max, w.Projs.Count); }
            Check("cluster grenades split into bomblets", max >= 5, $"max projectiles {max}");
            p.Has[Weapons.RocketLauncher] = true; p.Upgraded[Weapons.RocketLauncher] = true;
            p.Weapon = p.Pending = Weapons.RocketLauncher; p.Ammo[Weapons.Rockets] = 10; p.Cooldown = 0;
            w.Projs.Clear();
            w.Update(1 / 60f, new InputState { Select = -1, Fire = true });
            Check("twin rockets fire two for one", w.Projs.Count(x => x.Kind == ProjKind.Rocket) == 2 && p.Ammo[Weapons.Rockets] == 9);
        }
        // ---------------- sniper rifle / rail gun ----------------
        {
            var w = Make(20, 851, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            // a direction with a long clear line of fire
            var dir = Enumerable.Range(0, 16).Select(k => new Vector3(MathF.Cos(k * MathF.Tau / 16), 0, MathF.Sin(k * MathF.Tau / 16)))
                .OrderByDescending(d => w.L.Raycast(p.Eye, d, 40, out _)).First();
            void Aim(Vector3 at) { var to = at - p.Eye; p.Yaw = MathF.Atan2(to.X, -to.Z); p.Pitch = MathF.Asin(to.Y / to.Length()); }
            var fire = new InputState { Select = -1, Fire = true, Zoom = true };

            p.Has[Weapons.Sniper] = true; p.Weapon = p.Pending = Weapons.Sniper; p.Ammo[Weapons.Bullets] = 50;
            Run(w, 0.5f, new InputState { Select = -1, Zoom = true });
            Check("sniper scope zooms in on right mouse", p.ZoomT > 0.99f && MathF.Abs(w.ZoomMag - 4) < 0.01f, $"zoomT={p.ZoomT:F2} mag={w.ZoomMag:F2}");
            var brute = w.SpawnEnemy(EnemyDefs.Brute, p.Pos + dir * 8);
            Aim(brute.Mid); p.Cooldown = 0;
            w.Update(1 / 60f, fire);
            Check("sniper one-shots a brute", brute.Dead && p.Ammo[Weapons.Bullets] == 48, $"hp={brute.Hp:F0} ammo={p.Ammo[Weapons.Bullets]}");

            w.Enemies.Clear();
            p.Has[Weapons.Railgun] = true; p.Weapon = p.Pending = Weapons.Railgun; p.Ammo[Weapons.Cells] = 100; p.SwitchT = 0;
            var line = new[] { 3f, 5.5f, 8f }.Select(d => w.SpawnEnemy(d < 5 ? EnemyDefs.Grunt : EnemyDefs.Trooper, p.Pos + dir * d)).ToList();
            Aim(line[0].Mid); p.Cooldown = 0;
            w.Update(1 / 60f, fire);
            Check("rail gun pierces and kills every alien on the line", line.All(e => e.Dead) && p.Ammo[Weapons.Cells] == 90, $"dead={line.Count(e => e.Dead)}/3 cells={p.Ammo[Weapons.Cells]}");
        }
        {
            var w = Make(10, 852, out var p);
            var boss = w.Boss;
            p.Pos = NearSpot(w, boss.Pos, 7);
            p.Has[Weapons.Sniper] = true; p.Weapon = p.Pending = Weapons.Sniper; p.Ammo[Weapons.Bullets] = 50;
            var to = boss.Mid - p.Eye; p.Yaw = MathF.Atan2(to.X, -to.Z); p.Pitch = MathF.Asin(to.Y / to.Length());
            float hp0 = boss.Hp;
            w.Update(1 / 60f, new InputState { Select = -1, Fire = true });
            Check("bosses survive a sniper round but take its damage", !boss.Dead && hp0 - boss.Hp >= 200, $"{boss.D.Name} hp {hp0:F0} -> {boss.Hp:F0}");
        }
        // ---------------- story ----------------
        {
            var w = Make(1, 1101, out var p);
            Run(w, 2f);
            Check("sublevel 1 opens with OVERWATCH on the radio", w.Comm is { Who: Speaker.Overwatch }, $"comm={w.Comm?.Text}");
            int missing = 0;
            for (int n = 1; n <= 30; n++)
            {
                var l = Level.Generate(n, 1200 + n);
                if (l.Items.Count(it => it.Kind == PickupKind.Log) != Story.LogCount(n)) missing++;
            }
            Check("every sublevel holds its data pads", missing == 0, $"{missing} levels short");
        }
        {
            var w = Make(2, 1102, out var p);
            w.Enemies.Clear();
            var pad = w.Pickups.First(x => x.Kind == PickupKind.Log);
            p.Pos = pad.Pos with { Y = 0 };
            Run(w, 0.2f);
            var snap = p.Snapshot();
            var p2 = new Player(); p2.ResetNew(); p2.Restore(snap);
            Check("data pad is read, shown and saved", p.Logs.Contains(pad.Arg) && w.LogCardT > 0 && p2.Logs.Contains(pad.Arg), $"logs={string.Join(",", p.Logs)}");
        }
        static void UseConsoles(World w, Player p)
        {
            foreach (int pi in w.L.ObjectiveProps)
            {
                var pr = w.L.Props[pi];
                var face = new Vector3(MathF.Sin(pr.Yaw), 0, MathF.Cos(pr.Yaw));
                p.Pos = new Vector3(pr.Pos.X, 0, pr.Pos.Z) + face * (pr.Depth / 2 + 0.9f);
                p.Yaw = MathF.Atan2(-face.X, face.Z);
                w.Update(1 / 60f, new InputState { Select = -1 });
                w.Update(1 / 60f, new InputState { Select = -1, UsePressed = true });
            }
        }
        {
            var w = Make(8, 1103, out var p);
            w.Enemies.Clear();
            UseConsoles(w, p);
            Check("restarting the comm relays marks the elevator", w.L.ObjectiveProps.Count == 3 && w.ObjDoneCount == 3 && w.ExitMarked, $"consoles={w.L.ObjectiveProps.Count} done={w.ObjDoneCount}");
        }
        {
            var w = Make(22, 1104, out var p);
            w.Enemies.Clear();
            UseConsoles(w, p);
            var w30 = new World(Level.Generate(30, 1105), p, new SoundSystem());
            int pylons = w30.Enemies.Count(e => e.Type == EnemyDefs.Pylon);
            Check("scramming the reactor leaves two pylons at the Gate", p.ReactorScrammed && pylons == 2, $"scrammed={p.ReactorScrammed} pylons={pylons}");
        }
        {
            var w = Make(18, 1106, out var p);
            w.Enemies.Clear();
            var tank = w.L.Props.FirstOrDefault(x => x.Kind == PropKind.Tank);
            if (tank != null)
            {
                p.Pos = NearSpot(w, new Vector3(tank.Pos.X, 0, tank.Pos.Z), 3.5f);
                w.LevelTime = 9;
                Run(w, 0.5f);
                Check("specimen tank bursts and something climbs out", tank.Dead && w.Enemies.Count(e => !e.Dead) >= 1, $"dead={tank.Dead} aliens={w.Enemies.Count}");
            }
            else Check("specimen tank bursts and something climbs out", false, "no tank on this seed");
        }
        // ---------------- gore / feedback ----------------
        {
            var w = Make(6, 901, out var p);
            w.Enemies.Clear();
            p.Pos = OpenSpot(w);
            var e = w.SpawnEnemy(EnemyDefs.Grunt, p.Pos + new Vector3(4, 0, 0));
            w.Explosion(e.Mid, 3, 500, true, 0);
            Run(w, 0.1f);
            Check("explosive kill gibs the alien into chunks", e.Gibbed && w.Chunks.Count >= 4 && w.Decals.Count > 0 && w.SlowMoT > 0, $"chunks={w.Chunks.Count} decals={w.Decals.Count}");
            Run(w, 4);
            Check("chunks come to rest", w.Chunks.All(c => c.Resting));
        }
        // ---------------- scoring ----------------
        {
            Scoring.ReadOnly = true;
            var w = Make(5, 1001, out _);
            w.Kills = w.TotalEnemies; w.SecretsFound = w.L.SecretCount; w.LevelTime = 60; w.ShotsFired = 100; w.ShotsHit = 90;
            w.DamageEnemy(w.Boss, 1e6f, Vector3.UnitX);
            var good = Scoring.Compute(w, 0);
            var w2 = Make(5, 1001, out _);
            w2.Kills = 1; w2.LevelTime = 900; w2.ShotsFired = 100; w2.ShotsHit = 10;
            var bad = Scoring.Compute(w2, 3);
            Check("scoring grades a clean run S and a sloppy one D", good.Grade == "S" && bad.Grade == "D" && good.Score > bad.Score, $"{good.Grade} {good.Score} vs {bad.Grade} {bad.Score}");
        }
        Console.WriteLine($"{pass} passed, {fail} failed");
    }
}
