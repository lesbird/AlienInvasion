using System.Numerics;
using AlienInvasion.Engine;

namespace AlienInvasion.Game;

/// <summary>
/// Headless play-through: a simple bot navigates each level (keys -> doors -> exit),
/// shoots what it sees and must reach the elevator / destroy the portal.
/// </summary>
public static class BotTest
{
    static double updTotal, updMax;
    static int updFrames;

    public static void Run(int from, int to, int seed, bool god)
    {
        var player = new Player();
        player.ResetNew();
        var snd = new SoundSystem();
        int passed = 0;
        for (int n = from; n <= to; n++)
        {
            var level = Level.Generate(n, seed + n * 1013);
            var w = new World(level, player, snd) { God = god };
            float t = 0, navT = 0;
            const float dt = 1 / 60f;
            List<(int, int)> path = null;
            int pathIdx = 0;
            float stuckT = 0;
            Vector3 lastPos = player.Pos;
            float dmgTaken = 0, lastHp = player.Health;
            int deaths = 0;
            while (t < 900)
            {
                t += dt;
                var inp = new InputState { Select = -1 };
                // ---------- targeting ----------
                Enemy target = null;
                float best = float.MaxValue;
                foreach (var e in w.Enemies)
                {
                    if (e.Dead || e.Invulnerable || e.Hidden) continue;
                    float d = Vector3.Distance(e.Mid, player.Eye);
                    if (d < 35 && d < best && level.LineOfSight(player.Eye, e.Mid)) { best = d; target = e; }
                }
                Vector3 moveDir = Vector3.Zero;
                // ---------- navigation ----------
                navT -= dt;
                if (navT <= 0)
                {
                    navT = 0.5f;
                    path = PlanPath(w);
                    pathIdx = 0;
                }
                if (path != null && pathIdx < path.Count)
                {
                    var (cx, cy) = path[pathIdx];
                    var c = Level.Center(cx, cy);
                    var d = c - player.Pos;
                    d.Y = 0;
                    if (d.Length() < 1.0f) pathIdx++;
                    else moveDir = Vector3.Normalize(d);
                }
                float wantYaw;
                if (target != null)
                {
                    var to2 = target.Mid - player.Eye;
                    wantYaw = MathF.Atan2(to2.X, -to2.Z);
                    float wantPitch = MathF.Asin(Math.Clamp(to2.Y / to2.Length(), -1, 1));
                    inp.LookY = -(wantPitch - player.Pitch);
                    float err = MathF.Abs(GMath.AngleDiff(player.Yaw, wantYaw));
                    inp.Fire = err < 0.08f;
                    // keep some distance from explosives
                    var wdef = Weapons.All[player.Weapon];
                    if (wdef.Splash > 0 && best < wdef.Splash + 1.5f) inp.Select = Weapons.Shotgun < Weapons.Count && player.Has[Weapons.Shotgun] ? Weapons.Shotgun : Weapons.Pistol;
                }
                else
                {
                    wantYaw = moveDir.LengthSquared() > 0 ? MathF.Atan2(moveDir.X, -moveDir.Z) : player.Yaw;
                    inp.LookY = player.Pitch;
                }
                inp.LookX = Math.Clamp(GMath.AngleDiff(player.Yaw, wantYaw), -0.25f, 0.25f);
                if (moveDir.LengthSquared() > 0)
                {
                    inp.Forward = Vector3.Dot(moveDir, player.Flat);
                    inp.Strafe = Vector3.Dot(moveDir, player.Right);
                }
                inp.UsePressed = w.AtElevator;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                w.Update(dt, inp);
                double ums = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (Perf.On)
                {
                    updTotal += ums; updFrames++; updMax = Math.Max(updMax, ums);
                    if (ums > 3 && t > 1) Console.WriteLine($"   PERF update {ums:F1}ms t={t:F1}s alive={w.Enemies.Count(e => !e.Dead)} projs={w.Projs.Count} parts={w.Particles.Count} lights={w.DynLights.Count}");
                }

                if (player.Health < lastHp) dmgTaken += lastHp - player.Health;
                lastHp = player.Health;
                if (player.Dead)
                {
                    deaths++;
                    player.Dead = false;
                    player.Health = 100;
                    lastHp = 100;
                }
                // stuck detection: jiggle
                stuckT += dt;
                if (stuckT > 3)
                {
                    if (Vector3.Distance(lastPos, player.Pos) < 1f) { player.Pos = level.Move(player.Pos, w.RandDir() * new Vector3(1.5f, 0, 1.5f), Player.Radius); navT = 0; }
                    lastPos = player.Pos;
                    stuckT = 0;
                }
                if (w.ExitReached || w.Victory) break;
                // cap ammo so the bot doesn't stall on a stubborn enemy
                if (player.Ammo[Weapons.Bullets] < 20) player.Ammo[Weapons.Bullets] = 20;
            }
            bool ok = w.ExitReached || w.Victory;
            if (!ok)
            {
                var (fx, fy) = Level.CellOf(player.Pos);
                int room = level.Rooms.FindIndex(r => r.Contains(fx, fy));
                Console.WriteLine($"   stuck at cell ({fx},{fy}) room={room} lockdowns=[{string.Join(", ", level.Lockdowns.Select(l => $"room {l.Room}: state {l.State} {l.Spawned}/{l.Total} alive {l.Wave.Count(e => !e.Dead)} [{string.Join(" ", l.Wave.Where(e => !e.Dead).Select(e => $"{e.D.Name}@{Level.CellOf(e.Pos)} hid={e.Hidden} ceil={e.OnCeiling}"))}]"))}] barrier={level.Barrier.Count(b => b)} power={w.PowerLevel} sd={w.SelfDestructT:F0}");
            }
            if (level.IsFinal) Console.WriteLine($"   portal hp={w.PortalE.Hp:F0} inv={w.PortalE.Invulnerable} pylons={w.Enemies.Count(e => e.Type == EnemyDefs.Pylon && !e.Dead)} pos={player.Pos} portalPos={w.PortalE.Pos} victoryT={w.VictoryT}");
            if (ok) passed++;
            if (Perf.On) { Console.WriteLine($"   PERF L{n} update avg={updTotal / Math.Max(1, updFrames):F3}ms max={updMax:F1}ms"); updTotal = updMax = 0; updFrames = 0; }
            Console.WriteLine($"L{n,2}: {(ok ? "OK  " : "FAIL")} time={t,5:F0}s kills={w.Kills,2}/{w.TotalEnemies,-2} dmgTaken={dmgTaken,5:F0} deaths={deaths} keys={string.Join(",", Enumerable.Range(0, level.LockCount).Select(j => player.Keys[level.LockColor[j]] ? Level.KeyNames[level.LockColor[j]][0].ToString() : "-"))} weapon={Weapons.All[player.Weapon].Name}");
            player.Health = MathF.Max(player.Health, 100);
        }
        Console.WriteLine($"{passed}/{to - from + 1} levels completed by bot");
    }

    /// <summary>Path to the next objective: missing key (reachable), else the exit / portal area.</summary>
    static List<(int, int)> PlanPath(World w)
    {
        var L = w.L;
        var p = w.P;
        var blocked = new HashSet<int>(L.Doors.Where(d => d.Locked && (d.Secret || !p.Keys[d.Key])).Select(d => L.Idx(d.X, d.Y)));
        var (px, py) = Level.CellOf(p.Pos);
        if (!L.In(px, py)) return null;
        var dist = L.Distances(px, py, blocked);
        (int x, int y) goal = (-1, -1);
        int bd = int.MaxValue;
        foreach (var pk in w.Pickups)
        {
            if (pk.Kind != PickupKind.Key || p.Keys[pk.Arg]) continue;
            var (kx, ky) = Level.CellOf(pk.Pos);
            int d = dist[L.Idx(kx, ky)];
            if (d < bd) { bd = d; goal = (kx, ky); }
        }
        if (goal.x < 0)
        {
            // locked door we can now open?
            foreach (var d in L.Doors)
            {
                if (!d.Locked || d.Secret || !p.Keys[d.Key]) continue;
                int v = dist[L.Idx(d.X, d.Y)];
                if (v < bd) { bd = v; goal = (d.X, d.Y); }
            }
        }
        if (goal.x < 0)
        {
            goal = (L.ExitX, L.ExitY);
            if (L.IsFinal)
            {
                // approach the arena; pylons & portal are shot from range
                goal = (L.ExitX, L.ExitY + 4);
                if (!L.Floor(goal.x, goal.y) || L.Crate[L.Idx(goal.x, goal.y)]) goal = (L.ExitX + 4, L.ExitY);
                var c = Level.Center(L.ExitX, L.ExitY);
                if (Vector3.Distance(c, p.Pos) < 20)
                {
                    // circle around so every pylon gets line of sight
                    float a = w.Time * 0.15f;
                    var target = c + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 16;
                    var (tx, ty) = Level.CellOf(target);
                    if (L.In(tx, ty) && L.Floor(tx, ty) && !L.Crate[L.Idx(tx, ty)]) goal = (tx, ty);
                }
            }
        }
        return L.Path(px, py, goal.x, goal.y, blocked);
    }
}
