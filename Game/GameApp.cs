using System.Numerics;
using System.Text.Json;
using AlienInvasion.Engine;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace AlienInvasion.Game;

public enum GameState { Title, Controls, Playing, Paused, LevelComplete, Victory }

public class SaveData
{
    public int Level { get; set; } = 1;
    public int Seed { get; set; }
    public PlayerSnapshot Player { get; set; } = new();
    public int Score { get; set; }
}

public class Options
{
    public int StartLevel = 0;
    public bool God;
    public string ShotFile;
    public int ShotFrame = 90;
    public string ShotMode = "play";
    public int Seed = -1;
    public bool NoShadows, NoBloom, NoAO, Windowed;
    public string Goggles = "";
    public float YawOffset;
    public float SmokeSeconds;
    public int Weapon = -1;
}

public class GameApp
{
    static readonly string[] AreaNames =
    {
        "", "GROUND LEVEL - SURFACE ACCESS", "B1 - SECURITY CHECKPOINT", "B2 - BARRACKS", "B3 - MESS HALL", "B4 - ARMORY",
        "B5 - MOTOR POOL", "B6 - SUPPLY DEPOT", "B7 - COMMUNICATIONS", "B8 - POWER DISTRIBUTION", "B9 - WATER RECLAMATION",
        "B10 - MAINTENANCE TUNNELS", "B11 - COMMAND CENTER", "B12 - MEDICAL WING", "B13 - DETENTION BLOCK", "B14 - DEEP STORAGE",
        "B15 - GEOTHERMAL PLANT", "B16 - RESEARCH WING", "B17 - XENOBIOLOGY LAB", "B18 - SPECIMEN VAULT", "B19 - CONTAINMENT",
        "B20 - QUARANTINE ZONE", "B21 - REACTOR CORE", "B22 - EXCAVATION SITE", "B23 - THE OLD MINES", "B24 - INFESTED SECTOR",
        "B25 - THE NEST", "B26 - THE HIVE", "B27 - BROOD CHAMBERS", "B28 - XENO CATHEDRAL", "B29 - THE BREACH"
    };

    readonly Options opt;
    IWindow window;
    GL gl;
    IInputContext input;
    IKeyboard kb;
    IMouse mouse;
    Renderer renderer;
    Renderer2D ui;
    readonly SoundSystem snd = new();
    Task<List<(string, float[])>> soundTask;
    Task<float[]> musicTask;
    bool soundsLoaded;

    World world, titleWorld;
    readonly Player player = new();
    int levelNum = 1, runSeed;
    PlayerSnapshot levelStart;
    GameState state = GameState.Title;
    float time, stateT, deadT;
    int menuIdx;
    bool showMap, showFps;
    float fps, fpsAccum;
    int fpsFrames, frame;
    bool musicMuted;

    readonly HashSet<Key> held = new(), pressed = new();
    bool mouseL, mouseR, mouseLPressed, mouseRPressed;
    Vector2 lastMouse;
    bool firstMouse = true, captured;
    float mdx, mdy, scroll;
    public float Sensitivity = 0.0022f;

    static string SavePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlienInvasion", "save.json");

    public GameApp(Options o) { opt = o; }

    public void Run()
    {
        var wo = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1280, 720),
            Title = "Alien Invasion",
            VSync = opt.ShotFile == null && opt.SmokeSeconds <= 0,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
            PreferredDepthBufferBits = 24,
            Samples = 4,
        };
        window = Window.Create(wo);
        window.Load += () => { try { OnLoad(); } catch (Exception e) { Console.WriteLine(e); throw; } };
        window.Update += d => { try { OnUpdate(d); } catch (Exception e) { Console.WriteLine(e); window.Close(); } };
        window.Render += d => { try { OnRender(d); } catch (Exception e) { Console.WriteLine(e); window.Close(); } };
        window.Closing += () => snd.Dispose();
        window.Run();
        window.Dispose();
    }

    void OnLoad()
    {
        gl = window.CreateOpenGL();
        Console.WriteLine($"OpenGL: {gl.GetStringS(StringName.Version)} / {gl.GetStringS(StringName.Renderer)}");
        // title bar / taskbar icon (macOS takes the Dock icon from the app bundle instead)
        if (!OperatingSystem.IsMacOS())
        {
            try { window.SetWindowIcon(new[] { 32, 64, 256 }.Select(s => new Silk.NET.Core.RawImage(s, s, GameIcon.Render(s))).ToArray()); }
            catch (Exception e) { Console.WriteLine($"window icon: {e.Message}"); }
        }
        // fullscreen by default; screenshots and smoke tests stay in a fixed 1280x720 window
        if (!opt.Windowed && opt.ShotFile == null && opt.SmokeSeconds <= 0) window.WindowState = WindowState.Fullscreen;
        input = window.CreateInput();
        kb = input.Keyboards[0];
        mouse = input.Mice[0];
        kb.KeyDown += (_, k, _) => { held.Add(k); pressed.Add(k); };
        kb.KeyUp += (_, k, _) => held.Remove(k);
        mouse.MouseDown += (_, b) => { if (b == MouseButton.Left) { mouseL = true; mouseLPressed = true; } if (b == MouseButton.Right) { mouseR = true; mouseRPressed = true; } };
        mouse.MouseUp += (_, b) => { if (b == MouseButton.Left) mouseL = false; if (b == MouseButton.Right) mouseR = false; };
        mouse.MouseMove += (_, p) =>
        {
            if (firstMouse) { lastMouse = p; firstMouse = false; return; }
            mdx += p.X - lastMouse.X;
            mdy += p.Y - lastMouse.Y;
            lastMouse = p;
        };
        mouse.Scroll += (_, w) => scroll += w.Y;

        renderer = new Renderer(gl) { Shadows = !opt.NoShadows, BloomOn = !opt.NoBloom, AO = !opt.NoAO };
        Scoring.ReadOnly = opt.ShotFile != null || opt.SmokeSeconds > 0;
        ui = new Renderer2D(gl);
        snd.Init();
        soundTask = Task.Run(SoundBank.BuildAll);
        musicTask = Task.Run(() => Music.LevelTrackF(1, 777));

        titleWorld = new World(Level.Generate(24, 4242), new Player(), new SoundSystem());
        titleWorld.P.Flashlight = false;

        if (opt.StartLevel > 0)
        {
            NewGame(opt.StartLevel);
            if (opt.ShotMode == "map") showMap = true;
            if (opt.ShotMode.EndsWith("gallery")) SetupGallery();
            if (opt.ShotMode.StartsWith("secret")) SetupSecretView(opt.ShotMode == "secretopen");
            if (opt.ShotMode.StartsWith("beacon")) SetupBeaconView(opt.ShotMode == "beaconcrowd" ? 24 : 3);
            if (opt.ShotMode == "behindwall") SetupBehindWall();
            if (opt.ShotMode == "barrels") SetupBarrelView();
            if (opt.ShotMode is "lockdown" or "complete" or "hazard" or "vent") SetupFeatureView(opt.ShotMode);
            if (opt.ShotMode == "ragdoll")
            {
                // debug: line up the aliens and drop them all, knocked back away from the camera
                SetupGallery();
                world.God = true;
                player.Pos.Z -= 15;
                foreach (var e in world.Enemies) { e.Hp = 1; world.DamageEnemy(e, 5, Vector3.Normalize(new Vector3(0.3f, 0, -1))); }
            }
            if (opt.ShotMode == "hurt") { world.God = true; player.Health = 16; player.HurtFlash = 0.7f; }
            if (opt.ShotMode == "blast") { world.God = true; world.Explosion(player.Eye + player.Forward * 7 - Vector3.UnitY, 5, 0, false, 0); }
            if (opt.ShotMode == "blackout") { world.ForcePowerFailure(); world.God = true; }
            player.Yaw += opt.YawOffset;
            if (opt.Goggles == "nv") { player.HasNV = true; player.NVCharge = Player.GoggleMax; player.NVOn = true; }
            if (opt.Goggles == "xray") { player.HasXRay = true; player.XRayCharge = Player.GoggleMax; player.XRayOn = true; }
            if (opt.Weapon >= 0) { player.Has[opt.Weapon] = true; player.Weapon = player.Pending = opt.Weapon; player.Ammo[Math.Max(0, Weapons.All[opt.Weapon].Ammo)] = 50; }
        }
        else if (opt.ShotMode == "controls") state = GameState.Controls;
    }

    // =====================================================================  flow
    LevelResult result;
    int runScore, levelDeaths, deathsLevel = -1;

    /// <summary>Score breakdown with the letter grade, revealed line by line.</summary>
    void DrawResult(LevelResult r, int W, int H, float s)
    {
        if (r == null) return;
        string Time(float t) => $"{(int)t / 60}:{(int)t % 60:00}";
        var lines = new List<(string label, string val, int pts, Vector4 col)>
        {
            ("KILLS", $"{world.Kills}/{world.TotalEnemies}", r.KillPts, C(0.9f, 0.9f, 0.9f)),
            ("SECRETS", $"{world.SecretsFound}/{world.L.SecretCount}", r.SecretPts, C(0.95f, 0.85f, 0.45f)),
            ("TIME", $"{Time(r.Time)} (PAR {Time(r.Par)})", r.TimePts, C(0.9f, 0.9f, 0.9f)),
            ("ACCURACY", $"{r.Accuracy * 100:F0}%", r.AccPts, C(0.9f, 0.9f, 0.9f)),
        };
        if (r.BossPts > 0) lines.Insert(1, ("BOSS", world.Boss.D.Name, r.BossPts, C(1f, 0.55f, 0.35f)));
        if (r.Deaths > 0) lines.Add(("DEATHS", $"{r.Deaths}", r.DeathPts, C(1f, 0.4f, 0.35f)));
        float x0 = W / 2f - 330 * s, x1 = W / 2f + 40 * s, x2 = W / 2f + 250 * s, y = H * 0.25f;
        float reveal = (stateT - 0.8f) * 5;
        for (int i = 0; i < lines.Count && i < reveal; i++)
        {
            var (label, val, pts, col) = lines[i];
            ui.Text(label, x0, y, 3 * s, col);
            ui.Text(val, x1, y, 3 * s, col, 2);
            ui.Text(pts >= 0 ? $"+{pts}" : $"{pts}", x2, y, 3 * s, pts >= 0 ? C(0.6f, 1f, 0.6f) : C(1f, 0.4f, 0.35f), 2);
            y += 34 * s;
        }
        if (reveal > lines.Count)
        {
            ui.Rect(x0, y, x2 - x0, 2 * s, C(0.5f, 0.6f, 0.5f, 0.6f));
            y += 12 * s;
            ui.Text("SCORE", x0, y, 3.5f * s, C(1f, 1f, 1f));
            ui.Text($"{r.Score:N0}", x2, y, 3.5f * s, C(1f, 1f, 1f), 2);
            ui.Text($"RUN TOTAL {runScore:N0}", x0, y + 36 * s, 2.3f * s, C(0.7f, 0.75f, 0.7f));
            if (r.NewBest && r.Previous != null) ui.Text("NEW BEST!", x2, y + 36 * s, 2.3f * s, C(1f, 0.85f, 0.3f, 0.6f + 0.4f * MathF.Sin(time * 6)), 2);
            else if (r.Previous != null) ui.Text($"BEST {r.Previous.Score:N0} ({r.Previous.Grade})", x2, y + 36 * s, 2.3f * s, C(0.7f, 0.75f, 0.7f), 2);
        }
        if (reveal > lines.Count + 2)
        {
            var gc = r.Grade switch { "S" => C(1f, 0.85f, 0.25f), "A" => C(0.5f, 1f, 0.5f), "B" => C(0.5f, 0.8f, 1f), "C" => C(0.9f, 0.9f, 0.9f), _ => C(1f, 0.5f, 0.4f) };
            float pop = MathF.Min(1, (reveal - lines.Count - 2) * 0.6f);
            ui.Text("RANK", x2 + 120 * s, H * 0.25f, 3 * s, C(0.7f, 0.75f, 0.7f), 1);
            ui.Text(r.Grade, x2 + 120 * s, H * 0.25f + 34 * s, (26 - 8 * pop) * s, gc * new Vector4(1, 1, 1, pop), 1);
        }
    }

    void Line(Vector2 a, Vector2 b, float th, Vector4 c)
    {
        var d = b - a;
        if (d.LengthSquared() < 1e-4f) return;
        var n = Vector2.Normalize(new Vector2(-d.Y, d.X)) * th * 0.5f;
        ui.Tri(a - n, a + n, b + n, c);
        ui.Tri(a - n, b + n, b - n, c);
    }

    void SetCapture(bool on)
    {
        if (captured == on) return;
        captured = on;
        mouse.Cursor.CursorMode = on ? (mouse.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled) : CursorMode.Normal;
        firstMouse = true;
    }

    void NewGame(int startLevel = 1)
    {
        runSeed = opt.Seed >= 0 ? opt.Seed : Environment.TickCount & 0x7fffffff;
        runScore = 0;
        deathsLevel = -1;
        player.ResetNew();
        if (startLevel > 1) GiveDebugLoadout(startLevel);
        StartLevel(startLevel);
    }

    void SetupBeaconView(int crowd)
    {
        // debug: stand a few cells from a rotating beacon with a clear view, aliens posed beneath it
        var L = world.L;
        bool Open(int x, int y) => L.Floor(x, y) && !L.Crate[L.Idx(x, y)] && !L.PropBlock[L.Idx(x, y)] && L.DoorAt[L.Idx(x, y)] < 0;
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        foreach (var b in L.Lights.Where(l => l.Pos.Y > Level.WH - 0.8f).OrderBy(l => l.Beacon ? 0 : 1))
        {
            var (bx, by) = Level.CellOf(b.Pos);
            for (int k = 0; k < 4; k++)
            {
                if (!Enumerable.Range(0, 4).All(i => Open(bx + dx[k] * i, by + dy[k] * i))) continue;
                player.Pos = Level.Center(bx + dx[k] * 3, by + dy[k] * 3) - new Vector3(dx[k], 0, dy[k]) * 0.5f;
                var to = b.Pos - player.Pos;
                player.Yaw = MathF.Atan2(to.X, -to.Z);
                player.Pitch = -0.05f;
                var free = world.Enemies.Where(e => !e.D.Stationary && e.D.FlyHeight <= 0).Take(crowd).ToList();
                world.Enemies.Clear();
                var side = new Vector3(-dy[k], 0, dx[k]);
                for (int n = 0; n < free.Count; n++)
                {
                    var e = free[n];
                    e.Pos = Level.Center(bx, by) + new Vector3(dx[k], 0, dy[k]) * (1.0f + n % 3 * 0.6f - n / 3 * 1.2f) + side * (n % 3 - 1) * 1.6f;
                    e.Yaw = player.Yaw + MathF.PI;
                    world.Enemies.Add(e);
                }
                player.Flashlight = opt.ShotMode == "beaconflash";
                if (player.Flashlight) player.Pos -= new Vector3(dx[k], 0, dy[k]) * 4.5f;
                world.Msg = "";
                return;
            }
        }
    }

    void SetupFeatureView(string mode)
    {
        // debug views for the screenshot tool
        var L = world.L;
        player.Flashlight = false;
        world.God = true;
        world.Msg = "";
        void Look(Vector3 from, Vector3 at, float pitch = -0.1f)
        {
            player.Pos = from;
            var to = at - from;
            player.Yaw = MathF.Atan2(to.X, -to.Z);
            player.Pitch = pitch;
        }
        switch (mode)
        {
            case "lockdown":
            {
                var ld = L.Lockdowns.FirstOrDefault();
                if (ld == null) return;
                var r = L.Rooms[ld.Room];
                var (bx, by, dx, dy) = ld.Barriers[0];
                // stand inside, facing the sealed entrance
                Look(Level.Center(bx + dx * 2, by + dy * 2), Level.Center(bx, by, 1.5f), 0);
                break;
            }
            case "complete":
                world.Kills = world.TotalEnemies - 2; world.SecretsFound = 1; world.LevelTime = 140; world.ShotsFired = 220; world.ShotsHit = 140;
                world.ExitReached = true;
                break;
            case "hazard":
            {
                var h = L.Hazards.FirstOrDefault(x => x.Kind == HazardKind.Cable) ?? L.Hazards.FirstOrDefault();
                if (h == null) return;
                var from = new Vector3(h.Pos.X, 0, h.Pos.Z) + (h.Kind == HazardKind.Cable ? h.N : Vector3.UnitX) * 4f;
                if (L.Blocked(from.X, from.Z, 0.4f)) from = new Vector3(h.Pos.X, 0, h.Pos.Z) + new Vector3(h.N.Z, 0, -h.N.X) * 3.5f;
                Look(from, h.Pos, -0.15f);
                world.Enemies.Clear();
                break;
            }
            case "vent":
            {
                var v = L.Vents.FirstOrDefault();
                if (v == null) return;
                player.Flashlight = true;
                Look(Level.Center(v.X, v.Y) + v.N * 2.5f + new Vector3(v.N.Z, 0, -v.N.X) * 1.0f, v.Pos, -0.15f);
                world.Enemies.Clear();
                // decorate: a gibbed alien and some bullet holes for the gore check
                var e = world.SpawnEnemy(EnemyDefs.Grunt, Level.Center(v.X, v.Y) + v.N * 1.2f);
                world.Explosion(e.Mid, 2, 900, true, 0, false);
                for (int i = 0; i < 6; i++) world.AddDecal(v.Pos + new Vector3(v.N.Z, 0, -v.N.X) * (0.9f + i * 0.25f) + Vector3.UnitY * (0.6f + (i % 3) * 0.3f), v.N, 0.15f, 0, Vector4.One);
                break;
            }
        }
    }

    void SetupBarrelView()
    {
        // debug: look at the first explosive barrel group from a few metres away
        var L = world.L;
        foreach (var b in world.Barrels)
            foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                var from = b.Pos + dir * 4.5f;
                if (L.Blocked(from.X, from.Z, Player.Radius) || !L.LineOfSight(from + Vector3.UnitY * 1.6f, b.Pos + Vector3.UnitY * 1.2f)) continue;
                player.Pos = from;
                var to = b.Pos - from;
                player.Yaw = MathF.Atan2(to.X, -to.Z);
                player.Pitch = -0.25f;
                world.Msg = "";
                return;
            }
    }

    void SetupBehindWall()
    {
        // debug: stand near an alien with a wall in between, facing it (for the x-ray goggles)
        var L = world.L;
        foreach (var e in world.Enemies.Where(e => !e.D.Stationary))
        {
            var (ex, ey) = Level.CellOf(e.Pos);
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = ex + dx, y = ey + dy;
                    if (!L.Floor(x, y) || L.Crate[L.Idx(x, y)] || L.PropBlock[L.Idx(x, y)] || Math.Abs(dx) + Math.Abs(dy) < 2) continue;
                    var p = Level.Center(x, y);
                    if (L.LineOfSight(p + Vector3.UnitY * Player.EyeHeight, e.Mid)) continue;
                    player.Pos = p;
                    var to = e.Pos - p;
                    player.Yaw = MathF.Atan2(to.X, -to.Z);
                    player.Pitch = 0;
                    world.Msg = "";
                    return;
                }
        }
    }

    void SetupSecretView(bool open)
    {
        // debug: stand in front of the first secret panel, back up a little to see it
        var L = world.L;
        var d = L.Doors.FirstOrDefault(x => x.Secret);
        if (d == null) return;
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        for (int k = 0; k < 4; k++)
        {
            int fx = d.X + dx[k], fy = d.Y + dy[k];
            if (!L.Floor(fx, fy) || L.SecretCell[L.Idx(fx, fy)]) continue;
            var dir = new Vector3(-dx[k], 0, -dy[k]);
            player.Pos = Level.Center(fx, fy) + new Vector3(dx[k], 0, dy[k]) * 1.2f;
            if (open) player.Pos = Level.Center(fx, fy) - new Vector3(dx[k], 0, dy[k]) * 3.6f;
            player.Yaw = MathF.Atan2(dir.X, -dir.Z);
            player.Pitch = -0.08f;
            break;
        }
        if (open) { d.Unlocked = true; d.Open = 1; }
        world.Enemies.Clear();
        world.Msg = "";
    }

    void SetupGallery()
    {
        // debug: line up every alien type in the final arena for inspection
        var L = world.L;
        var c = Level.Center(L.ExitX, L.ExitY);
        world.Enemies.Clear();
        world.PortalE = null;
        L.Ambient = new Vector3(0.55f);
        L.FogDensity = 0.004f;
        bool bosses = opt.ShotMode == "bossgallery";
        var types = bosses ? EnemyDefs.Bosses : EnemyDefs.Roaming;
        float gap = bosses ? 7f : 3f;
        for (int i = 0; i < types.Length; i++)
        {
            var e = world.SpawnEnemy(types[i], c + new Vector3((i - (types.Length - 1) / 2f) * gap, 0, 5));
            e.Yaw = MathF.PI;
            e.State = EState.Idle;
            e.OnCeiling = e.Burrowed = false;
            e.Pos.Y = e.D.FlyHeight;
        }
        player.Pos = c + new Vector3(0, 0, bosses ? 19 : 22);
        for (int i = 0; i < L.Crate.Length; i++) L.Crate[i] = false;
        player.Yaw = 0;
        player.Pitch = -0.05f;
        world.Msg = "";
    }

    void GiveDebugLoadout(int lvl)
    {
        // Starting deeper (debug): hand out weapons appropriate to the depth.
        player.Has[Weapons.Shotgun] = lvl > 2;
        player.Has[Weapons.Rifle] = lvl > 4;
        player.Has[Weapons.GrenadeLauncher] = lvl > 7;
        player.Has[Weapons.RocketLauncher] = lvl > 11;
        player.Has[Weapons.Plasma] = lvl > 8;
        player.Has[Weapons.Sniper] = lvl > 9;
        player.Has[Weapons.Railgun] = lvl > 17;
        for (int i = 0; i < 5; i++) player.Ammo[i] = Weapons.MaxAmmo[i] / 2;
        player.Armor = 50;
        // goggles turn up in the secret stashes of sublevels 2 and 5
        if (lvl > 2) { player.HasNV = true; player.NVCharge = Player.GoggleMax; }
        if (lvl > 5) { player.HasXRay = true; player.XRayCharge = Player.GoggleMax; }
    }

    void ContinueGame()
    {
        try
        {
            var s = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath));
            runSeed = s.Seed;
            runScore = s.Score;
            player.ResetNew();
            player.Restore(s.Player);
            StartLevel(Math.Clamp(s.Level, 1, 30));
        }
        catch { NewGame(); }
    }

    SaveData ReadSave()
    {
        try { return File.Exists(SavePath) ? JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath)) : null; }
        catch { return null; }
    }

    void StartLevel(int n)
    {
        if (n != deathsLevel) { levelDeaths = 0; deathsLevel = n; }
        levelNum = n;
        var level = Level.Generate(n, runSeed + n * 1013);
        player.Pos = Vector3.Zero;
        levelStart = player.Snapshot();
        levelStart.Level = n;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            File.WriteAllText(SavePath, JsonSerializer.Serialize(new SaveData { Level = n, Seed = runSeed, Player = levelStart, Score = runScore }));
        }
        catch (Exception e) { Console.WriteLine("Save failed: " + e.Message); }
        world = new World(level, player, snd) { God = opt.God };
        world.Message(n == 30 ? "FIND AND DESTROY THE ALIEN PORTAL!" : $"SUBLEVEL {n}: FIND THE EXIT ELEVATOR", new Vector4(1f, 0.85f, 0.4f, 1));
        int mseed = runSeed + n * 77;
        musicTask = Task.Run(() => Music.LevelTrackF(n, mseed));
        snd.SetLoop(null, Vector3.Zero, 0);
        state = GameState.Playing;
        stateT = 0;
        deadT = 0;
        showMap = false;
        SetCapture(true);
    }

    void RestartLevel()
    {
        levelDeaths++;
        player.ResetNew();
        player.Restore(levelStart);
        player.Health = MathF.Max(levelStart.Health, 100);
        StartLevel(levelNum);
    }

    // =====================================================================  update
    bool Pressed(params Key[] keys) => keys.Any(k => pressed.Contains(k));
    bool Held(params Key[] keys) => keys.Any(k => held.Contains(k));

    void OnUpdate(double dtd)
    {
        float dt = MathF.Min((float)dtd, 0.05f);
        time += dt;
        stateT += dt;
        frame++;
        fpsAccum += (float)dtd; fpsFrames++;
        if (fpsAccum > 0.5f) { fps = fpsFrames / fpsAccum; fpsAccum = 0; fpsFrames = 0; }

        if (!soundsLoaded && soundTask.IsCompleted)
        {
            foreach (var (n, d) in soundTask.Result) snd.Load(n, d);
            soundsLoaded = true;
        }
        if (musicTask != null && musicTask.IsCompleted)
        {
            if (!musicMuted) snd.PlayMusic(musicTask.Result);
            musicTask = null;
        }

        if (Pressed(Key.F11)) window.WindowState = window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        if (Pressed(Key.F3)) showFps = !showFps;
        if (Pressed(Key.M)) { musicMuted = !musicMuted; snd.SetMusicGain(musicMuted ? 0 : 1); }

        switch (state)
        {
            case GameState.Title: UpdateTitle(dt); break;
            case GameState.Controls:
                if (Pressed(Key.Escape, Key.Enter, Key.Space) || mouseLPressed) { state = GameState.Title; snd.Play("menu"); }
                break;
            case GameState.Playing: UpdatePlaying(dt); break;
            case GameState.Paused: UpdatePaused(); break;
            case GameState.LevelComplete:
                world.Update(dt, new InputState { Select = -1 });
                if (stateT > 3.5f && (Pressed(Key.Enter, Key.Space, Key.E) || mouseLPressed))
                {
                    player.Pos = Vector3.Zero;
                    StartLevel(levelNum + 1);
                }
                break;
            case GameState.Victory:
                world.Update(dt, new InputState { Select = -1 });
                if (stateT > 4 && (Pressed(Key.Enter, Key.Space, Key.Escape) || mouseLPressed))
                {
                    try { File.Delete(SavePath); } catch { }
                    GoTitle();
                }
                break;
        }

        if (opt.SmokeSeconds > 0 && time > opt.SmokeSeconds)
        {
            Console.WriteLine($"SMOKE OK: frames={frame} fps~{fps:F0} level={levelNum} enemies={world?.Enemies.Count} kills={world?.Kills} hp={player.Health} particles={world?.Particles.Count}");
            window.Close();
        }

        pressed.Clear();
        mouseLPressed = mouseRPressed = false;
        mdx = mdy = 0;
        scroll = 0;
    }

    void GoTitle()
    {
        state = GameState.Title;
        menuIdx = 0;
        SetCapture(false);
        snd.SetLoop(null, Vector3.Zero, 0);
        musicTask = Task.Run(() => Music.LevelTrackF(1, 777));
    }

    List<string> TitleItems()
    {
        var items = new List<string> { "NEW GAME" };
        var s = ReadSave();
        if (s != null) items.Add($"CONTINUE - SUBLEVEL {s.Level}");
        items.Add("CONTROLS");
        items.Add("QUIT");
        return items;
    }

    void UpdateTitle(float dt)
    {
        titleWorld.P.Yaw += dt * 0.08f;
        titleWorld.Time += dt;
        var items = TitleItems();
        if (Pressed(Key.Up, Key.W)) { menuIdx = (menuIdx + items.Count - 1) % items.Count; snd.Play("menu"); }
        if (Pressed(Key.Down, Key.S)) { menuIdx = (menuIdx + 1) % items.Count; snd.Play("menu"); }
        menuIdx = Math.Clamp(menuIdx, 0, items.Count - 1);
        if (Pressed(Key.Enter, Key.Space) || mouseLPressed)
        {
            snd.Play("weapon");
            var it = items[menuIdx];
            if (it == "NEW GAME") NewGame();
            else if (it.StartsWith("CONTINUE")) ContinueGame();
            else if (it == "CONTROLS") state = GameState.Controls;
            else if (it == "QUIT") window.Close();
        }
    }

    void UpdatePaused()
    {
        string[] items = { "RESUME", "RESTART SUBLEVEL", "QUIT TO TITLE" };
        if (Pressed(Key.Escape)) { state = GameState.Playing; SetCapture(true); return; }
        if (Pressed(Key.Up, Key.W)) { menuIdx = (menuIdx + items.Length - 1) % items.Length; snd.Play("menu"); }
        if (Pressed(Key.Down, Key.S)) { menuIdx = (menuIdx + 1) % items.Length; snd.Play("menu"); }
        if (Pressed(Key.Enter, Key.Space) || mouseLPressed)
        {
            snd.Play("menu");
            if (menuIdx == 0) { state = GameState.Playing; SetCapture(true); }
            else if (menuIdx == 1) RestartLevel();
            else GoTitle();
        }
    }

    void UpdatePlaying(float dt)
    {
        if (Pressed(Key.Escape)) { state = GameState.Paused; menuIdx = 0; SetCapture(false); return; }
        if (Pressed(Key.Tab)) showMap = !showMap;
        if (Pressed(Key.B)) { renderer.BloomOn = !renderer.BloomOn; world.Message(renderer.BloomOn ? "BLOOM ON" : "BLOOM OFF"); }
        if (!captured && mouseLPressed) SetCapture(true);

        var inp = new InputState { Select = -1 };
        if (!player.Dead)
        {
            if (Held(Key.W, Key.Up)) inp.Forward += 1;
            if (Held(Key.S, Key.Down)) inp.Forward -= 1;
            if (Held(Key.D)) inp.Strafe += 1;
            if (Held(Key.A)) inp.Strafe -= 1;
            inp.Sprint = Held(Key.ShiftLeft, Key.ShiftRight);
            inp.Fire = mouseL || Held(Key.ControlLeft, Key.ControlRight);
            inp.UsePressed = Pressed(Key.E, Key.Space, Key.Enter);
            inp.FlashToggle = Pressed(Key.F);
            inp.NVToggle = Pressed(Key.N);
            inp.XRayToggle = Pressed(Key.X);
            float turn = (Held(Key.Right) ? 1 : 0) - (Held(Key.Left) ? 1 : 0);
            float look = 1 / world.ZoomMag;   // slower aim through the scope
            inp.LookX = (mdx * Sensitivity + turn * dt * 2.4f) * look;
            inp.LookY = mdy * Sensitivity * look;
            Key[] nums = { Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5, Key.Number6, Key.Number7, Key.Number8, Key.Number9, Key.Number0, Key.Minus, Key.Equal };
            for (int i = 0; i < nums.Length; i++) if (pressed.Contains(nums[i])) inp.Select = i;
            // right mouse zooms a scoped weapon, otherwise it cycles weapons
            bool scoped = Weapons.All[player.Weapon].Zoom > 0;
            inp.Zoom = scoped && mouseR;
            if (scroll > 0.1f || Pressed(Key.Q)) inp.Cycle = -1;
            if (scroll < -0.1f || (mouseRPressed && !scoped)) inp.Cycle = 1;
        }
        if (opt.SmokeSeconds > 0) SmokeInput(ref inp, dt);
        if (opt.ShotMode == "scope") inp.Zoom = true;
        if (opt.ShotMode == "fire" && opt.ShotFrame - frame is > 0 and < 8) inp.Fire = true;
        if (opt.ShotMode.EndsWith("gallery") || opt.ShotMode.StartsWith("beacon") || opt.ShotMode == "behindwall" || opt.ShotMode == "barrels" || opt.ShotMode.StartsWith("secret")) { world.Time += dt; return; }
        world.TickRealTime(dt);
        world.Update(dt * world.TimeScale, inp);

        if (player.Dead)
        {
            deadT += dt;
            if (deadT > 1.5f && (Pressed(Key.Enter, Key.Space, Key.E) || mouseLPressed)) RestartLevel();
        }
        if (world.ExitReached)
        {
            result = Scoring.Compute(world, levelDeaths);
            runScore += result.Score;
            state = GameState.LevelComplete;
            stateT = 0;
            showMap = false;
            snd.StopMusic();
        }
        if (world.Victory)
        {
            result = Scoring.Compute(world, levelDeaths);
            runScore += result.Score;
            state = GameState.Victory;
            stateT = 0;
            SetCapture(false);
            musicTask = Task.Run(() => Music.LevelTrackF(10, 999));
        }
    }

    void SmokeInput(ref InputState inp, float dt)
    {
        // automated input for headless-ish smoke tests: wander, turn and shoot
        inp.Forward = MathF.Sin(time * 0.7f) > -0.3f ? 1 : -1;
        inp.Strafe = MathF.Sin(time * 1.3f);
        inp.LookX = dt * 0.9f;
        inp.Fire = (int)(time * 2) % 3 != 0;
        if ((int)time % 4 == 0 && frame % 30 == 0) inp.Cycle = 1;
        world.God = true;
    }

    // =====================================================================  render
    void OnRender(double dtd)
    {
        var fb = window.FramebufferSize;
        int W = fb.X, H = fb.Y;
        if (W <= 0 || H <= 0) return;
        float dt = MathF.Min((float)dtd, 0.05f);

        if (state == GameState.Title || state == GameState.Controls)
        {
            renderer.Render(titleWorld, W, H, time, dt);
            DrawTitle(W, H);
        }
        else
        {
            renderer.Render(world, W, H, time, dt);
            DrawHud(W, H);
        }

        if (opt.ShotFile != null && frame == opt.ShotFrame)
        {
            Screenshot.Save(gl, W, H, opt.ShotFile);
            Console.WriteLine("Saved screenshot " + opt.ShotFile);
            window.Close();
        }
    }

    static Vector4 C(float r, float g, float b, float a = 1) => new(r, g, b, a);

    static readonly (string Head, (string Key, string Action)[] Rows)[][] ControlColumns =
    {
        new[]
        {
            ("MOVEMENT", new[] { ("WASD / ARROWS", "MOVE / TURN"), ("MOUSE", "LOOK"), ("SHIFT", "SPRINT"), ("E / SPACE", "USE / SEARCH WALLS") }),
            ("COMBAT", new[] { ("LMB / CTRL", "FIRE"), ("1 - 0, -, =", "SELECT WEAPON"), ("WHEEL / Q / RMB", "CYCLE WEAPON"), ("RMB (SNIPER)", "ZOOM SCOPE") }),
        },
        new[]
        {
            ("GEAR", new[] { ("F", "FLASHLIGHT"), ("N", "NIGHT VISION"), ("X", "X-RAY GOGGLES"), ("TAB", "AUTOMAP") }),
            ("SYSTEM", new[] { ("B", "BLOOM ON/OFF"), ("M", "MUSIC ON/OFF"), ("F11", "FULLSCREEN"), ("ESC", "PAUSE") }),
        },
    };

    static readonly string[] ControlTips =
    {
        "FIND COLORED KEYCARDS TO OPEN SECURITY DOORS.",
        "SOME WALL PANELS HIDE SECRET STASHES - PRESS E.",
        "GOGGLES RUN ON BATTERIES - PICK UP SPARES.",
        "REACH THE ELEVATOR TO DESCEND. 30 SUBLEVELS.",
        "DESTROY THE ALIEN PORTAL AT THE BOTTOM.",
    };

    /// <summary>Controls screen: a framed panel with two aligned key/action columns and the mission briefing.</summary>
    void DrawControls(int W, int H, float s, float pulse)
    {
        float cx = W / 2f;
        float t = MathF.Min(s, W / 1150f);   // text scale, shrunk on narrow windows
        ui.Text("ALIEN INVASION", cx, 34 * s, 7 * s, C(0.4f * pulse, 1f * pulse, 0.5f * pulse), 1);
        ui.Text("OPERATION DEEP DESCENT", cx, 34 * s + 66 * s, 2.6f * s, C(0.9f, 0.85f, 0.6f), 1);

        float ts = 2.2f * t, hs = 2.2f * t, row = 23 * s, gap = 24 * t, colGap = 80 * t, pad = 36 * t;
        // measure each column: right-aligned keys, left-aligned actions
        var keyW = new float[2]; var actW = new float[2]; var rows = new int[2];
        for (int c = 0; c < 2; c++)
            foreach (var (head, list) in ControlColumns[c])
            {
                rows[c] += list.Length + 2;
                foreach (var (k, a) in list)
                {
                    keyW[c] = MathF.Max(keyW[c], Renderer2D.TextWidth(k, ts));
                    actW[c] = MathF.Max(actW[c], Renderer2D.TextWidth(a, ts));
                }
            }
        float tipW = ControlTips.Max(l => Renderer2D.TextWidth(l, ts));
        float colsW = keyW[0] + gap + actW[0] + colGap + keyW[1] + gap + actW[1];
        float panelW = MathF.Max(colsW, tipW) + pad * 2;
        float top = 128 * s, bindH = (Math.Max(rows[0], rows[1]) - 1) * row;
        float divY = top + 26 * s + bindH + 6 * s;
        float panelH = divY - top + 22 * s + ControlTips.Length * row + 14 * s;
        float px = cx - panelW / 2;

        var accent = C(0.4f, 1f, 0.5f);
        ui.Rect(px, top, panelW, panelH, C(0.02f, 0.06f, 0.04f, 0.78f));
        ui.RectOutline(px, top, panelW, panelH, MathF.Max(1, 1.5f * s), C(0.4f, 1f, 0.5f, 0.55f));

        float x0 = cx - colsW / 2;
        for (int c = 0; c < 2; c++)
        {
            float split = x0 + keyW[c];              // keys end here, actions start after the gap
            float y = top + 26 * s;
            foreach (var (head, list) in ControlColumns[c])
            {
                ui.Text(head, x0, y, hs, accent);
                ui.Rect(x0, y + 8 * hs + 4 * s, keyW[c] + gap + actW[c], MathF.Max(1, s), C(0.4f, 1f, 0.5f, 0.3f));
                y += row * 1.25f;
                foreach (var (k, a) in list)
                {
                    ui.Text(k, split, y, ts, C(1f, 0.9f, 0.55f), 2);
                    ui.Text(a, split + gap, y, ts, C(0.88f, 0.92f, 0.88f));
                    y += row;
                }
                y += row * 0.75f;
            }
            x0 = split + gap + actW[c] + colGap;
        }

        ui.Rect(px + pad, divY, panelW - pad * 2, MathF.Max(1, s), C(0.4f, 1f, 0.5f, 0.3f));
        float ty = divY + 20 * s;
        foreach (var line in ControlTips)
        {
            ui.Text(line, cx, ty, ts, C(0.75f, 0.85f, 0.78f), 1);
            ty += row;
        }

        ui.Text("PRESS ENTER TO GO BACK", cx, H - 52 * s, 2.5f * s, C(1, 1, 0.5f, 0.6f + 0.4f * MathF.Sin(time * 4)), 1);
        ui.End();
    }

    void DrawTitle(int W, int H)
    {
        float s = H / 720f;
        ui.Begin(W, H);
        ui.Rect(0, 0, W, H, C(0, 0, 0, 0.55f));
        float pulse = 0.85f + 0.15f * MathF.Sin(time * 2);
        if (state == GameState.Controls)
        {
            DrawControls(W, H, s, pulse);
            return;
        }
        ui.Text("ALIEN INVASION", W / 2f, H * 0.16f, 11 * s, C(0.4f * pulse, 1f * pulse, 0.5f * pulse), 1);
        ui.Text("OPERATION DEEP DESCENT", W / 2f, H * 0.16f + 95 * s, 3.5f * s, C(0.9f, 0.85f, 0.6f), 1);
        var items = TitleItems();
        for (int i = 0; i < items.Count; i++)
        {
            bool sel = i == menuIdx;
            float y = H * 0.5f + i * 48 * s;
            if (sel) ui.Rect(W / 2f - 260 * s, y - 10 * s, 520 * s, 44 * s, C(0.2f, 0.8f, 0.3f, 0.18f));
            ui.Text((sel ? "> " : "") + items[i] + (sel ? " <" : ""), W / 2f, y, 4 * s, sel ? C(0.5f, 1f, 0.5f) : C(0.75f, 0.75f, 0.75f), 1);
        }
        ui.Text("ALL GRAPHICS, SOUND AND MUSIC PROCEDURALLY GENERATED", W / 2f, H - 40 * s, 2 * s, C(0.6f, 0.6f, 0.6f, 0.7f), 1);
        if (!soundsLoaded) ui.Text("SYNTHESIZING AUDIO...", 20 * s, H - 40 * s, 2 * s, C(0.6f, 0.8f, 1f, 0.8f));
        ui.End();
    }

    /// <summary>Sniper scope: black surround, a round lens with fine crosshairs and mil-dots.</summary>
    void DrawScope(int W, int H, float s, float a)
    {
        float cx = W / 2f, cy = H / 2f, r = H * 0.42f;
        var black = C(0, 0, 0, a);
        ui.Rect(0, 0, cx - r, H, black);
        ui.Rect(cx + r, 0, W - cx - r, H, black);
        // a ring of quads from the lens edge outwards blacks out the rest of the centre strip
        const int seg = 64;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * MathF.Tau / seg, a1 = (i + 1) * MathF.Tau / seg;
            var d0 = new Vector2(MathF.Cos(a0), MathF.Sin(a0)); var d1 = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            var c = new Vector2(cx, cy);
            ui.Tri(c + d0 * r, c + d0 * r * 2, c + d1 * r * 2, black);
            ui.Tri(c + d0 * r, c + d1 * r * 2, c + d1 * r, black);
        }
        // lens rim
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * MathF.Tau / seg, a1 = (i + 1) * MathF.Tau / seg;
            Line(new Vector2(cx + MathF.Cos(a0) * r, cy + MathF.Sin(a0) * r), new Vector2(cx + MathF.Cos(a1) * r, cy + MathF.Sin(a1) * r), 4 * s, C(0.05f, 0.06f, 0.05f, a));
        }
        // reticle: thick outer posts, thin centre lines and mil-dots
        var rc = C(0.02f, 0.02f, 0.02f, 0.9f * a);
        float thin = MathF.Max(1, 1.2f * s), thick = 4 * s, gap = r * 0.45f;
        ui.Rect(cx - r, cy - thick / 2, r - gap, thick, rc); ui.Rect(cx + gap, cy - thick / 2, r - gap, thick, rc);
        ui.Rect(cx - thick / 2, cy + gap, thick, r - gap, rc);
        ui.Rect(cx - gap, cy - thin / 2, gap * 2, thin, rc);
        ui.Rect(cx - thin / 2, cy - r, thin, r + gap, rc);
        for (int i = 1; i <= 4; i++)
        {
            float d = gap * i / 4.5f, ds = 3 * s;
            ui.Rect(cx + d - ds / 2, cy - ds / 2, ds, ds, rc); ui.Rect(cx - d - ds / 2, cy - ds / 2, ds, ds, rc);
            ui.Rect(cx - ds / 2, cy + d - ds / 2, ds, ds, rc);
        }
        ui.Rect(cx - 1.5f * s, cy - 1.5f * s, 3 * s, 3 * s, C(1f, 0.2f, 0.15f, a));
    }

    void DrawHud(int W, int H)
    {
        float s = H / 720f;
        var P = player;
        var L = world.L;
        ui.Begin(W, H);

        // screen flashes (damage and low health are drawn by the post-processing pass)
        if (P.PickupFlash > 0) ui.Rect(0, 0, W, H, C(1f, 0.9f, 0.5f, P.PickupFlash * 0.18f));

        if (state == GameState.Playing && !P.Dead && world.VictoryT < 0)
        {
            // crosshair
            float cx = W / 2f, cy = H / 2f, g = 5 * s, l = 9 * s, th = MathF.Max(1, 2 * s);
            if (P.ZoomT > 0.5f) DrawScope(W, H, s, (P.ZoomT - 0.5f) * 2);
            else
            {
                var cc = C(0.6f, 1f, 0.6f, 0.85f);
                ui.Rect(cx - g - l, cy - th / 2, l, th, cc); ui.Rect(cx + g, cy - th / 2, l, th, cc);
                ui.Rect(cx - th / 2, cy - g - l, th, l, cc); ui.Rect(cx - th / 2, cy + g, th, l, cc);
            }
            // hit / kill marker: a diagonal X around the crosshair
            if (world.HitMarkerT > 0 || world.KillMarkerT > 0)
            {
                bool kill = world.KillMarkerT > 0;
                float k = kill ? world.KillMarkerT / 0.35f : world.HitMarkerT / 0.14f;
                var mc = kill ? C(1f, 0.25f, 0.2f, k) : C(1f, 1f, 1f, k);
                float r0 = (kill ? 9 : 7) * s, r1 = r0 + (kill ? 11 : 8) * s, mt = (kill ? 2.6f : 2f) * s;
                for (int q = 0; q < 4; q++)
                {
                    var d = new Vector2(q % 2 == 0 ? 1 : -1, q < 2 ? 1 : -1) * 0.7071f;
                    Line(new Vector2(cx, cy) + d * r0, new Vector2(cx, cy) + d * r1, mt, mc);
                }
            }
        }

        // bottom bar
        float barH = 74 * s;
        ui.Rect(0, H - barH, W, barH, C(0.02f, 0.03f, 0.03f, 0.6f));
        ui.Rect(0, H - barH, W, 2 * s, C(0.3f, 0.6f, 0.35f, 0.6f));
        var hc = P.Health > 60 ? C(0.5f, 1f, 0.5f) : P.Health > 30 ? C(1f, 0.85f, 0.3f) : C(1f, 0.3f, 0.25f);
        ui.Text("HEALTH", 24 * s, H - barH + 10 * s, 2 * s, C(0.7f, 0.7f, 0.7f));
        ui.Text($"{(int)MathF.Ceiling(P.Health)}", 24 * s, H - barH + 28 * s, 6 * s, hc);
        ui.Text("ARMOR", 180 * s, H - barH + 10 * s, 2 * s, C(0.7f, 0.7f, 0.7f));
        ui.Text($"{(int)P.Armor}", 180 * s, H - barH + 28 * s, 6 * s, C(0.5f, 0.75f, 1f));

        var wdef = Weapons.Get(P.Weapon, P.Upgraded[P.Weapon]);
        ui.Text(wdef.Name + (P.Upgraded[P.Weapon] ? " +" : ""), W - 24 * s, H - barH + 10 * s, 2 * s, P.Upgraded[P.Weapon] ? C(1f, 0.78f, 0.3f) : wdef.Alien ? C(0.7f, 0.5f, 1f) : C(0.7f, 0.7f, 0.7f), 2);
        if (wdef.Ammo >= 0)
        {
            int a = P.Ammo[wdef.Ammo];
            var ac = a == 0 ? C(1, 0.3f, 0.3f) : a < wdef.AmmoPerShot * 5 ? C(1, 0.8f, 0.3f) : C(1, 1, 1);
            ui.Text($"{a}", W - 24 * s, H - barH + 28 * s, 6 * s, ac, 2);
            ui.Text(Weapons.AmmoNames[wdef.Ammo], W - 24 * s - Renderer2D.TextWidth($"{a}", 6 * s) - 14 * s, H - barH + 46 * s, 2 * s, C(0.6f, 0.6f, 0.6f), 2);
        }
        else ui.Text("--", W - 24 * s, H - barH + 28 * s, 6 * s, C(0.7f, 0.7f, 0.7f), 2);

        // weapon slots
        float slotW = 30 * s, sx0 = W / 2f - slotW * Weapons.Count / 2f;
        for (int i = 0; i < Weapons.Count; i++)
        {
            float x = sx0 + i * slotW;
            bool have = P.Has[i];
            bool cur = P.Weapon == i;
            if (cur) ui.Rect(x + 2 * s, H - barH + 34 * s, slotW - 4 * s, 28 * s, C(0.3f, 0.8f, 0.4f, 0.35f));
            var col = !have ? C(0.25f, 0.25f, 0.25f) : P.Upgraded[i] ? C(1f, 0.78f, 0.3f) : Weapons.All[i].Alien ? C(0.75f, 0.55f, 1f) : C(0.85f, 0.85f, 0.85f);
            ui.Text(i < 10 ? ((i + 1) % 10).ToString() : i == 10 ? "-" : "=", x + slotW / 2, H - barH + 40 * s, 2.5f * s, col, 1);
        }
        ui.Text(L.Number == 30 ? "THE BREACH" : $"SUBLEVEL {L.Number:00}/30", W / 2f, H - barH + 10 * s, 2 * s, C(0.6f, 0.8f, 0.6f), 1);

        // top left info
        ui.Text(AreaNames[Math.Clamp(L.Number, 1, 30)], 20 * s, 18 * s, 2.5f * s, C(0.85f, 0.85f, 0.7f, 0.9f));
        ui.Text($"KILLS {world.Kills}/{world.TotalEnemies}", 20 * s, 44 * s, 2 * s, C(0.7f, 0.7f, 0.7f, 0.8f));
        int tsec = (int)world.LevelTime;
        ui.Text($"TIME {tsec / 60}:{tsec % 60:00}", 20 * s, 64 * s, 2 * s, C(0.7f, 0.7f, 0.7f, 0.8f));
        if (L.SecretCount > 0) ui.Text($"SECRETS {world.SecretsFound}/{L.SecretCount}", 20 * s, 84 * s, 2 * s, C(0.9f, 0.8f, 0.4f, 0.8f));
        if (showFps) ui.Text($"{fps:F0} FPS", 20 * s, 104 * s, 2 * s, C(1, 1, 0, 0.8f));
        // goggle charge meters (bottom left, above the status bar)
        float goggleY = H - barH - 26 * s;
        void Goggle(string label, bool on, float charge, Vector4 col)
        {
            float k = charge / Player.GoggleMax;
            bool low = k < 0.15f;
            float a = on ? 1f : 0.55f;
            if (low && on) a *= 0.55f + 0.45f * MathF.Sin(time * 10);
            ui.Text(label + (on ? " ON" : ""), 20 * s, goggleY, 2 * s, col * new Vector4(1, 1, 1, a));
            float bx = 20 * s + Renderer2D.TextWidth("XRAY ON", 2 * s) + 10 * s, bw = 110 * s, bh = 10 * s;
            ui.Rect(bx, goggleY + 2 * s, bw, bh, C(0, 0, 0, 0.5f));
            ui.Rect(bx, goggleY + 2 * s, bw * k, bh, (low ? C(1f, 0.35f, 0.2f) : col) * new Vector4(1, 1, 1, a));
            ui.RectOutline(bx, goggleY + 2 * s, bw, bh, 1.5f * s, C(0.6f, 0.6f, 0.6f, 0.6f));
            goggleY -= 20 * s;
        }
        if (P.HasXRay) Goggle("XRAY", P.XRayOn, P.XRayCharge, C(0.75f, 0.5f, 1f));
        if (P.HasNV) Goggle("NV", P.NVOn, P.NVCharge, C(0.35f, 1f, 0.4f));
        if (P.AmpT > 0)
        {
            float blink = P.AmpT < 4 ? 0.5f + 0.5f * MathF.Sin(time * 14) : 1;
            ui.Text($"DAMAGE x2  {(int)MathF.Ceiling(P.AmpT)}", W / 2f, H - barH - 34 * s, 3 * s, C(1f, 0.55f, 0.15f, blink), 1);
            ui.RectOutline(0, 0, W, H, 4 * s, C(1f, 0.45f, 0.1f, 0.35f * blink));
        }

        // boss health
        if (world.Boss is { } boss && boss.Alerted && !boss.Dead && Vector3.Distance(boss.Pos, P.Pos) < 50)
        {
            float bw = 460 * s, bx = W / 2f - bw / 2, by = 118 * s;
            ui.Text(boss.D.Name, W / 2f, by - 20 * s, 2.5f * s, C(1f, 0.55f, 0.35f), 1);
            ui.Rect(bx, by, bw, 14 * s, C(0, 0, 0, 0.6f));
            ui.Rect(bx, by, bw * Math.Clamp(boss.Hp / boss.MaxHp, 0, 1), 14 * s, C(0.9f, 0.2f, 0.12f, 0.9f));
            ui.RectOutline(bx, by, bw, 14 * s, 2 * s, C(1f, 0.6f, 0.4f, 0.7f));
        }
        // self-destruct countdown / lockdown status
        if (world.SelfDestructT > 0 && !world.ExitReached)
        {
            float tl = world.SelfDestructT;
            float blink = tl < 15 ? 0.55f + 0.45f * MathF.Sin(time * 16) : 1;
            ui.Text("SELF-DESTRUCT", W / 2f, 70 * s, 2.5f * s, C(1f, 0.35f, 0.25f, blink), 1);
            ui.Text($"{(int)tl / 60}:{(int)tl % 60:00}.{(int)(tl * 10) % 10}", W / 2f, 92 * s, 6 * s, C(1f, 0.25f, 0.2f, blink), 1);
            ui.RectOutline(0, 0, W, H, 3 * s, C(1f, 0.1f, 0.05f, 0.25f * blink));
        }
        if (world.ActiveLockdown is { } ld)
        {
            int left = ld.Total - ld.Spawned + ld.Wave.Count(e => !e.Dead);
            float pulse = 0.6f + 0.4f * MathF.Sin(time * 6);
            ui.Text($"LOCKDOWN - {left} HOSTILES", W / 2f, 70 * s, 3 * s, C(1f, 0.3f, 0.2f, pulse), 1);
        }

        // keycards
        float kx = W - 24 * s;
        for (int j = L.LockCount - 1; j >= 0; j--)
        {
            int k = L.LockColor[j];
            float kw = 34 * s, kh = 22 * s;
            kx -= kw + 8 * s;
            var kc = new Vector4(Level.KeyColors[k], 1);
            if (P.Keys[k]) { ui.Rect(kx, 18 * s, kw, kh, kc); ui.Rect(kx + 4 * s, 18 * s + kh - 8 * s, kw * 0.5f, 4 * s, C(0, 0, 0, 0.5f)); }
            else ui.RectOutline(kx, 18 * s, kw, kh, 2 * s, kc * new Vector4(1, 1, 1, 0.5f));
        }
        if (L.LockCount > 0) ui.Text("KEYCARDS", W - 24 * s, 46 * s, 1.8f * s, C(0.6f, 0.6f, 0.6f, 0.8f), 2);

        // portal health
        if (world.PortalE != null && !world.PortalE.Dead)
        {
            var pe = world.PortalE;
            float d = Vector3.Distance(pe.Pos, P.Pos);
            if (d < 40)
            {
                float bw = 420 * s;
                ui.Rect(W / 2f - bw / 2, 22 * s, bw, 14 * s, C(0, 0, 0, 0.6f));
                ui.Rect(W / 2f - bw / 2, 22 * s, bw * pe.Hp / pe.MaxHp, 14 * s, pe.Invulnerable ? C(0.5f, 0.4f, 0.8f) : C(0.9f, 0.2f, 0.9f));
                int pylons = world.Enemies.Count(e => e.Type == EnemyDefs.Pylon && !e.Dead);
                ui.Text(pe.Invulnerable ? $"ALIEN PORTAL - SHIELDED ({pylons} PYLONS)" : "ALIEN PORTAL", W / 2f, 42 * s, 2 * s, C(0.9f, 0.7f, 1f), 1);
            }
        }

        // messages & prompts
        if (world.MsgT > 0)
        {
            float a = MathF.Min(1, world.MsgT);
            ui.Text(world.Msg, W / 2f, H * 0.66f, 2.8f * s, world.MsgColor * new Vector4(1, 1, 1, a), 1);
        }
        if (world.AtElevator && state == GameState.Playing)
            ui.Text("PRESS E TO TAKE THE ELEVATOR DOWN", W / 2f, H * 0.58f, 3 * s, C(0.4f, 1f, 0.5f, 0.7f + 0.3f * MathF.Sin(time * 6)), 1);

        if (showMap) DrawMap(W, H, s);

        if (P.Dead && state == GameState.Playing)
        {
            ui.Rect(0, 0, W, H, C(0.25f, 0, 0, MathF.Min(0.5f, deadT * 0.4f)));
            ui.Text("YOU DIED", W / 2f, H * 0.35f, 10 * s, C(1, 0.2f, 0.15f, MathF.Min(1, deadT)), 1);
            if (deadT > 1.5f) ui.Text("PRESS ENTER TO RESTART THE SUBLEVEL", W / 2f, H * 0.5f, 3 * s, C(1, 1, 1, 0.6f + 0.4f * MathF.Sin(time * 4)), 1);
        }

        if (state == GameState.Paused)
        {
            ui.Rect(0, 0, W, H, C(0, 0, 0, 0.6f));
            ui.Text("PAUSED", W / 2f, H * 0.28f, 8 * s, C(0.5f, 1f, 0.5f), 1);
            string[] items = { "RESUME", "RESTART SUBLEVEL", "QUIT TO TITLE" };
            for (int i = 0; i < items.Length; i++)
            {
                bool sel = i == menuIdx;
                ui.Text((sel ? "> " : "") + items[i] + (sel ? " <" : ""), W / 2f, H * 0.45f + i * 46 * s, 3.5f * s, sel ? C(0.5f, 1, 0.5f) : C(0.75f, 0.75f, 0.75f), 1);
            }
        }

        if (state == GameState.LevelComplete)
        {
            float k = MathF.Min(1, stateT / 1.5f);
            ui.Rect(0, 0, W, H, C(0, 0, 0, k * 0.92f));
            if (stateT > 0.8f)
            {
                ui.Text($"SUBLEVEL {L.Number} CLEARED", W / 2f, H * 0.12f, 6 * s, C(0.5f, 1f, 0.5f), 1);
                DrawResult(result, W, H, s);
                // elevator descending indicator
                float depth = (L.Number + MathF.Min(1, stateT / 3.5f)) / 30f;
                float gx = W / 2f - 200 * s, gy = H * 0.76f;
                ui.Rect(gx, gy, 400 * s, 10 * s, C(0.2f, 0.2f, 0.2f));
                ui.Rect(gx, gy, 400 * s * depth, 10 * s, C(0.3f, 0.9f, 0.4f));
                ui.Text(stateT < 3.5f ? "DESCENDING..." : $"NEXT: {AreaNames[Math.Min(30, L.Number + 1)]}", W / 2f, gy + 24 * s, 2.5f * s, C(0.7f, 0.9f, 0.7f), 1);
                if (stateT > 3.5f) ui.Text("PRESS ENTER TO CONTINUE", W / 2f, H * 0.9f, 3 * s, C(1, 1, 0.5f, 0.6f + 0.4f * MathF.Sin(time * 4)), 1);
            }
        }

        if (state == GameState.Victory)
        {
            float k = MathF.Min(1, stateT / 2f);
            ui.Rect(0, 0, W, H, C(0, 0, 0, k * 0.85f));
            ui.Text("MISSION ACCOMPLISHED", W / 2f, H * 0.2f, 7 * s, C(0.5f, 1, 0.5f, k), 1);
            ui.Text("THE PORTAL HAS COLLAPSED. THE INVASION IS OVER.", W / 2f, H * 0.36f, 3 * s, C(0.9f, 0.9f, 0.8f, k), 1);
            ui.Text("EARTH IS SAFE... FOR NOW.", W / 2f, H * 0.36f + 36 * s, 3 * s, C(0.9f, 0.9f, 0.8f, k), 1);
            ui.Text($"FINAL SCORE  {runScore:N0}", W / 2f, H * 0.36f + 84 * s, 4 * s, C(1f, 0.85f, 0.4f, k), 1);
            if (stateT > 4) ui.Text("PRESS ENTER", W / 2f, H * 0.75f, 3 * s, C(1, 1, 0.5f, 0.6f + 0.4f * MathF.Sin(time * 4)), 1);
        }
        ui.End();
    }

    void DrawMap(int W, int H, float s)
    {
        var L = world.L;
        bool MapFloor(int x, int y)
        {
            if (!L.Floor(x, y)) return false;
            var d = L.DoorIn(x, y);
            return d == null || !d.Secret || d.Unlocked;
        }
        ui.Rect(0, 0, W, H, C(0, 0.02f, 0.01f, 0.75f));
        float cell = MathF.Min(W * 0.9f / L.W, (H - 120 * s) * 0.92f / L.H);
        float ox = (W - cell * L.W) / 2, oy = (H - 80 * s - cell * L.H) / 2;
        for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                int i = L.Idx(x, y);
                if (!L.Explored[i] || !MapFloor(x, y)) continue;
                var col = L.RoomCell[i] ? C(0.18f, 0.4f, 0.22f, 0.9f) : C(0.14f, 0.3f, 0.17f, 0.9f);
                if (L.Crate[i] || L.PropBlock[i]) col = C(0.3f, 0.3f, 0.2f, 0.9f);
                if (L.SecretCell[i]) col = C(0.45f, 0.38f, 0.12f, 0.9f);
                if (!L.IsFinal && x == L.ExitX && y == L.ExitY) col = C(0.3f, 1f, 0.4f, 1);
                ui.Rect(ox + x * cell, oy + y * cell, cell + 0.5f, cell + 0.5f, col);
                // wall edges
                var wc = C(0.5f, 0.9f, 0.55f, 0.9f);
                float t = MathF.Max(1, cell * 0.12f);
                if (!MapFloor(x - 1, y)) ui.Rect(ox + x * cell, oy + y * cell, t, cell, wc);
                if (!MapFloor(x + 1, y)) ui.Rect(ox + (x + 1) * cell - t, oy + y * cell, t, cell, wc);
                if (!MapFloor(x, y - 1)) ui.Rect(ox + x * cell, oy + y * cell, cell, t, wc);
                if (!MapFloor(x, y + 1)) ui.Rect(ox + x * cell, oy + (y + 1) * cell - t, cell, t, wc);
            }
        foreach (var d in L.Doors)
        {
            if (d.Secret || !L.Explored[L.Idx(d.X, d.Y)]) continue;
            var col = d.Key >= 0 ? new Vector4(Level.KeyColors[d.Key], d.Locked ? 1 : 0.4f) : C(0.8f, 0.6f, 0.2f, 0.7f);
            if (d.AlongX) ui.Rect(ox + (d.X + 0.4f) * cell, oy + d.Y * cell, cell * 0.2f, cell, col);
            else ui.Rect(ox + d.X * cell, oy + (d.Y + 0.4f) * cell, cell, cell * 0.2f, col);
        }
        foreach (var pk in world.Pickups)
        {
            if (pk.Kind != PickupKind.Key) continue;
            var (x, y) = Level.CellOf(pk.Pos);
            if (!L.Explored[L.Idx(x, y)]) continue;
            ui.Rect(ox + (x + 0.2f) * cell, oy + (y + 0.25f) * cell, cell * 0.6f, cell * 0.5f, new Vector4(Level.KeyColors[pk.Arg], 1));
        }
        if (L.IsFinal && L.Explored[L.Idx(L.ExitX, L.ExitY)])
            ui.Rect(ox + (L.ExitX - 0.5f) * cell, oy + (L.ExitY - 0.5f) * cell, cell * 2, cell * 2, C(0.8f, 0.3f, 1f, 0.8f + 0.2f * MathF.Sin(time * 5)));
        // player arrow
        var pp = new Vector2(ox + player.Pos.X / Level.CS * cell, oy + player.Pos.Z / Level.CS * cell);
        var f = new Vector2(MathF.Sin(player.Yaw), -MathF.Cos(player.Yaw));
        var r = new Vector2(-f.Y, f.X);
        float sz = MathF.Max(cell * 0.9f, 8 * s);
        ui.Tri(pp + f * sz, pp - f * sz * 0.6f + r * sz * 0.55f, pp - f * sz * 0.6f - r * sz * 0.55f, C(1, 1, 0.3f));
        ui.Text("AUTOMAP - TAB TO CLOSE", W / 2f, oy - 28 * s, 2.5f * s, C(0.6f, 1f, 0.6f), 1);
    }
}
