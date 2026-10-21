using System.Numerics;
using AlienInvasion.Game;

namespace AlienInvasion;

static class Program
{
    static void Main(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (a)
            {
                case "--level": o.StartLevel = Math.Clamp(int.Parse(Next()), 1, 30); break;
                case "--god": o.God = true; break;
                case "--seed": o.Seed = int.Parse(Next()); break;
                case "--noshadows": o.NoShadows = true; break;
                case "--nobloom": o.NoBloom = true; break;
                case "--noao": o.NoAO = true; break;
                case "--goggles": o.Goggles = Next(); break;
                case "--yaw": o.YawOffset = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--weapon": o.Weapon = int.Parse(Next()); break;
                case "--shot": o.ShotFile = Next(); break;
                case "--shotframe": o.ShotFrame = int.Parse(Next()); break;
                case "--shotmode": o.ShotMode = Next(); break;
                case "--smoke": o.SmokeSeconds = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--gentest": GenTest(); return;
                case "--musictest": MusicTest(Next()); return;
                case "--secrettest": SecretTest(); return;
                case "--barreltest": BarrelTest(); return;
                case "--featuretest": FeatureTest.RunAll(); return;
                case "--bot":
                {
                    int from = int.Parse(Next()), to = int.Parse(Next());
                    BotTest.Run(from, to, 12345, args.Contains("--god"));
                    return;
                }
            }
        }
        new GameApp(o).Run();
    }

    /// <summary>Renders the title/sample-level/victory tracks to WAV files and prints stats.</summary>
    static void MusicTest(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (int lvl in new[] { 1, 2, 5, 10, 20, 30 })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var m = AlienInvasion.Engine.Music.LevelTrackF(lvl, 777 + lvl);
            double sum = 0; float peak = 0; bool nan = false;
            foreach (var v in m) { sum += v * v; peak = MathF.Max(peak, MathF.Abs(v)); nan |= float.IsNaN(v); }
            var pcm = AlienInvasion.Engine.Synth.ToPcm(m);
            string path = Path.Combine(dir, $"music_{lvl:00}.wav");
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                int rate = AlienInvasion.Engine.Synth.Rate, bytes = pcm.Length * 2;
                bw.Write("RIFF"u8); bw.Write(36 + bytes); bw.Write("WAVEfmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
                bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16); bw.Write("data"u8); bw.Write(bytes);
                foreach (var s in pcm) bw.Write(s);
            }
            Console.WriteLine($"level {lvl,2}: {m.Length / (float)AlienInvasion.Engine.Synth.Rate:F1}s rms={Math.Sqrt(sum / m.Length):F3} peak={peak:F2} nan={nan} gen={sw.ElapsedMilliseconds}ms -> {path}");
        }
    }

    /// <summary>Sets off one barrel next to an alien and the player; checks the chain reaction and damage.</summary>
    static void BarrelTest()
    {
        int total = 0, levels = 0;
        for (int n = 1; n <= 30; n++)
        {
            int c = Level.Generate(n, 4000 + n).Props.Count(p => p.Kind == PropKind.ExplosiveBarrel);
            total += c; if (c > 0) levels++;
        }
        Console.WriteLine($"explosive barrels: {total} over 30 levels ({levels} levels have some)");
        for (int n = 1; n <= 30; n++)
        {
            var lv = Level.Generate(n, 4000 + n);
            var group = lv.Props.Where(p => p.Kind == PropKind.ExplosiveBarrel).GroupBy(p => Level.CellOf(p.Pos)).FirstOrDefault(g => g.Count() >= 2);
            if (group == null) continue;
            var player = new Player(); player.ResetNew();
            var w = new AlienInvasion.Game.World(lv, player, new AlienInvasion.Engine.SoundSystem());
            var first = group.First();
            var e = w.Enemies.First(x => !x.D.Stationary);
            e.Pos = first.Pos + new Vector3(1.6f, 0, 0);
            if (lv.Blocked(e.Pos.X, e.Pos.Z, e.D.Radius)) e.Pos = first.Pos + new Vector3(-1.6f, 0, 0);
            if (lv.Blocked(e.Pos.X, e.Pos.Z, e.D.Radius)) e.Pos = first.Pos + new Vector3(0, 0, 1.6f);
            player.Pos = first.Pos + new Vector3(0, 0, -3.2f);
            if (lv.Blocked(player.Pos.X, player.Pos.Z, Player.Radius)) player.Pos = first.Pos + new Vector3(0, 0, 3.2f);
            float hp0 = e.Hp, php0 = player.Health + player.Armor;
            first.Fuse = 0.01f;
            for (int f = 0; f < 60; f++) w.Update(1 / 60f, new InputState { Select = -1 });
            // shooting: a fresh world, aim the pistol at a barrel from 6 m and fire
            var lv2 = Level.Generate(n, 4000 + n);
            var p2 = new Player(); p2.ResetNew();
            var w2 = new AlienInvasion.Game.World(lv2, p2, new AlienInvasion.Engine.SoundSystem());
            w2.Enemies.Clear();
            var tgt = w2.Barrels.First(b => Level.CellOf(b.Pos) == Level.CellOf(first.Pos));
            bool aimed = false;
            foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                var from = tgt.Pos + dir * 6;
                p2.Pos = from;
                var eye = p2.Eye; var mid = tgt.Pos + new Vector3(0, 0.6f, 0);
                if (lv2.Blocked(from.X, from.Z, Player.Radius) || lv2.Raycast(eye, Vector3.Normalize(mid - eye), 20, out _) < Vector3.Distance(eye, mid) - 0.6f) continue;
                var to = mid - eye;
                p2.Yaw = MathF.Atan2(to.X, -to.Z); p2.Pitch = MathF.Asin(to.Y / to.Length());
                aimed = true; break;
            }
            int frames = 0;
            while (aimed && !tgt.Dead && frames < 300) { w2.Update(1 / 60f, new InputState { Select = -1, Fire = true }); frames++; }
            Console.WriteLine($"   shooting: aimed={aimed} barrel destroyed={tgt.Dead} after {frames / 60f:F2}s, player hp {p2.Health:F0}");
            Console.WriteLine($"L{n}: group of {group.Count()}, exploded {group.Count(p => p.Dead)}; alien {e.D.Name} hp {hp0:F0} -> {e.Hp:F0} dead={e.Dead}; player hp+armor {php0:F0} -> {player.Health + player.Armor:F0}");
            break;
        }
    }

    static void SecretTest()
    {
        var l = Level.Generate(5, 1000 + 5 * 7);
        for (int r = 0; r < l.SecretRooms.Count; r++)
        {
            var cells = l.SecretRooms[r];
            var items = l.Items.Where(it => cells.Contains(l.Idx(it.X, it.Y))).Select(it => it.Kind + (it.Kind == PickupKind.Weapon ? ":" + Weapons.All[it.Arg].Name : ""));
            Console.WriteLine($"room {r}: {cells.Count} cells, items=[{string.Join(", ", items)}]");
        }
        var dd = l.Doors.First(d => d.Secret);
        for (int y = dd.Y - 5; y <= dd.Y + 5; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = dd.X - 8; x <= dd.X + 8; x++)
                sb.Append(!l.In(x, y) ? ' ' : l.DoorIn(x, y)?.Secret == true ? 'S' : l.SecretCell[l.Idx(x, y)] ? (l.Items.Any(i => i.X == x && i.Y == y) ? '$' : 's') : l.Cells[l.Idx(x, y)] != 0 ? '.' : '#');
            Console.WriteLine(sb);
        }
    }

    static void GenTest()
    {
        int noGoggles = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (!Level.Generate(2, seed * 13).Items.Any(i => i.Kind == PickupKind.NightVision)) noGoggles++;
            if (!Level.Generate(5, seed * 13).Items.Any(i => i.Kind == PickupKind.XRay)) noGoggles++;
        }
        Console.WriteLine($"goggles missing on sublevel 2/5: {noGoggles} of 400 levels");
        int fails = 0;
        for (int seed = 0; seed < 20; seed++)
            for (int n = 1; n <= 30; n++)
            {
                try { if (!Level.Generate(n, 1000 + seed * 31 + n * 7).Validate()) fails++; }
                catch (Exception e) { fails++; Console.WriteLine($"level {n} seed {seed}: {e.Message}"); }
            }
        foreach (int n in new[] { 1, 8, 16, 24, 30 })
        {
            var l = Level.Generate(n, 1000 + n * 7);
            Console.WriteLine($"  level {n,2}: props={l.Props.Count,3} ({string.Join(" ", l.Props.GroupBy(p => p.Kind).Select(g => $"{g.Key}:{g.Count()}"))}) secrets={l.SecretCount}");
        }
        Console.WriteLine(fails == 0 ? "All 600 generated levels are solvable." : $"{fails} failures");
    }
}
