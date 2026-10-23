using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Runs the story: the radio queue, level cues, the Mind's whispers, boss cards,
/// data pads, use-key objectives and the specimen-tank ambush.</summary>
public partial class World
{
    readonly Queue<Line> commQueue = new();
    /// <summary>The radio line being shown (Mind lines show as a whisper instead of in the radio panel).</summary>
    public Line? Comm;
    public float CommT;
    public string Whisper;
    public float WhisperT;
    public string BossTitle;
    public float BossCardT;
    public int LogCard = -1;
    public float LogCardT;
    public string XenoUnlocked;
    public float XenoUnlockT;
    public bool[] ObjDone = Array.Empty<bool>();
    /// <summary>Objective console the player is standing at (index into ObjDone), or -1.</summary>
    public int ObjAim = -1;
    /// <summary>Set when a prop changed (a tank burst) so the renderer rebuilds the prop meshes.</summary>
    public bool PropsDirty;
    public bool ExitMarked;
    readonly HashSet<Cue> fired = new();
    float whisperCd, sightT, ambushT;
    int ambushes;
    const float CommCharsPerSec = 42;

    public ObjectiveDef Objective => Story.For(L.Number).Objective;
    public int ObjDoneCount => ObjDone.Count(d => d);
    public static float CommDuration(Line l) => l.Text.Length / CommCharsPerSec + 2.2f + l.Text.Length * 0.012f;
    public static float LogCardDuration(LogEntry e) => MathF.Max(9, e.Text.Length * 0.055f);
    public bool CommBusy => Comm != null || commQueue.Count > 0;

    void InitStory()
    {
        ObjDone = new bool[L.ObjectiveProps.Count];
        whisperCd = 30 + (float)Rng.NextDouble() * 40;
    }

    public void Say(Line l) => commQueue.Enqueue(l);

    /// <summary>Plays a level's lines for a cue, once per visit to the level.</summary>
    public void Cue(Cue c)
    {
        if (!fired.Add(c)) return;
        foreach (var b in Story.For(L.Number).Beats)
            if (b.Cue == c) foreach (var l in b.Lines) Say(l);
    }

    void UpdateStory(float dt)
    {
        if (LevelTime > 1.2f && fired.Add(Game.Cue.Start))
        {
            foreach (var b in Story.For(L.Number).Beats) if (b.Cue == Game.Cue.Start) foreach (var l in b.Lines) Say(l);
            if (L.IsFinal && P.ReactorScrammed) Cue(Game.Cue.Scrammed);
        }

        // radio queue
        if (Comm is { } cur)
        {
            CommT += dt;
            if (CommT > CommDuration(cur)) Comm = null;
        }
        if (Comm == null && commQueue.Count > 0)
        {
            Comm = commQueue.Dequeue();
            CommT = 0;
            if (Comm.Value.Who == Speaker.Mind) Snd.Play("whisper", null, 0.8f, 1, 0);
            else Snd.Play("radio", null, 0.6f, 1, 0);
        }

        WhisperT -= dt; BossCardT -= dt; LogCardT -= dt; XenoUnlockT -= dt;
        if (P.Dead) return;

        if (TotalEnemies > 0 && Kills * 2 >= TotalEnemies) Cue(Game.Cue.HalfCleared);
        if (AtElevator) Cue(Game.Cue.Elevator);

        // first sight of each alien type
        sightT -= dt;
        if (sightT <= 0 && fired.Contains(Game.Cue.Start))
        {
            sightT = 0.3f;
            foreach (var e in Enemies)
            {
                if (e.Dead || e.Hidden || e.D.Boss || e.D.Stationary || !e.CanSee) continue;
                if ((P.SeenMask & (1 << e.Type)) != 0 || Vector3.Distance(e.Pos, P.Pos) > 28) continue;
                P.SeenMask |= 1 << e.Type;
                if (Story.FirstSight(e.Type) is { } line) Say(line);
                break;
            }
        }

        // the Mind whispers once you've met it
        if (L.Number > 15 || (L.Number == 15 && Boss is { Dead: true }))
        {
            whisperCd -= dt;
            if (whisperCd <= 0 && !CommBusy)
            {
                Whisper = Story.Whispers[Rng.Next(Story.Whispers.Length)];
                WhisperT = 3.5f;
                Snd.Play("whisper", null, 0.7f, 0.9f + (float)Rng.NextDouble() * 0.2f, 0);
                whisperCd = L.Number >= 26 ? 30 + (float)Rng.NextDouble() * 35 : 55 + (float)Rng.NextDouble() * 60;
            }
        }

        UpdateObjectiveAim();
        UpdateAmbush(dt);
    }

    // ---------------- objective consoles ----------------
    void UpdateObjectiveAim()
    {
        ObjAim = -1;
        float best = 2.2f;
        for (int i = 0; i < ObjDone.Length; i++)
        {
            if (ObjDone[i]) continue;
            var p = L.Props[L.ObjectiveProps[i]];
            var flat = new Vector3(P.Pos.X, 0, P.Pos.Z);
            var closest = Vector3.Clamp(flat, new Vector3(p.Min.X, 0, p.Min.Z), new Vector3(p.Max.X, 0, p.Max.Z));
            var to = closest - flat;
            float d = to.Length();
            if (d > best) continue;
            if (d > 0.3f && Vector3.Dot(to / d, P.Flat) < 0.2f) continue;
            best = d;
            ObjAim = i;
        }
    }

    /// <summary>Use key at an objective console. Returns true if it consumed the key press.</summary>
    bool TryObjective()
    {
        if (ObjAim < 0) return false;
        var def = Objective;
        ObjDone[ObjAim] = true;
        var p = L.Props[L.ObjectiveProps[ObjAim]];
        ObjAim = -1;
        int n = ObjDoneCount;
        Snd.Play("powerup", p.Pos, 0.8f, 1.3f);
        Snd.Play("key", null, 0.7f);
        Sparks(p.Pos + new Vector3(0, p.Height, 0), Vector3.UnitY, 10, new Vector4(0.5f, 1f, 0.6f, 1));
        DynLights.Add(new DynLight { Pos = p.Pos + new Vector3(0, p.Height + 0.5f, 0), Color = new Vector3(0.4f, 1.5f, 0.6f), Radius = 6, Life = 0.6f, MaxLife = 0.6f });
        Message($"{def.Thing} {n}/{ObjDone.Length} {def.Done}", new Vector4(0.5f, 1f, 0.6f, 1));
        if (n < ObjDone.Length) { Cue(Game.Cue.Objective); return true; }
        Cue(Game.Cue.ObjectiveDone);
        if (L.Number == 22)
        {
            P.ReactorScrammed = true;
            L.PowerFailure = true;
            if (powerState is 0 or 4) { powerFailAt = LevelTime; powerState = 0; }
        }
        else
        {
            // OVERWATCH pings the elevator: the exit shows on the automap
            ExitMarked = true;
            for (int y = L.ExitY - 1; y <= L.ExitY + 1; y++)
                for (int x = L.ExitX - 1; x <= L.ExitX + 1; x++)
                    if (L.In(x, y)) L.Explored[L.Idx(x, y)] = true;
        }
        return true;
    }

    // ---------------- specimen tanks burst open (sublevels 18 and 19) ----------------
    void UpdateAmbush(float dt)
    {
        if (L.Number is not (18 or 19) || ambushes >= 2 || LevelTime < 8) return;
        ambushT -= dt;
        if (ambushT > 0) return;
        ambushT = 0.3f;
        foreach (var p in L.Props)
        {
            if (p.Kind != PropKind.Tank || p.Dead) continue;
            var flat = new Vector3(P.Pos.X, 0, P.Pos.Z) - p.Pos;
            if (flat.Length() > 6.5f || flat.Length() < 0.01f) continue;
            // aim at the glass facing the player: the tank's own collision box would block the ray
            var face = p.Pos + Vector3.Normalize(flat) * 1.05f + new Vector3(0, 1.6f, 0);
            if (!L.LineOfSight(P.Eye, face)) continue;
            BreakTank(p);
            break;
        }
    }

    public void BreakTank(Prop p)
    {
        p.Dead = true;
        PropsDirty = true;
        ambushes++;
        var mid = p.Pos + new Vector3(0, 1.6f, 0);
        Snd.Play("shatter", mid, 1f);
        Snd.Play("screech", mid, 0.9f);
        for (int i = 0; i < 40; i++)
            AddParticle(new Particle { Pos = mid + RandDir() * 0.7f, Vel = RandDir() * 5 + Vector3.UnitY * 2, Color = new(0.75f, 0.9f, 1f, 0.9f), Size = 0.06f, Life = 1.2f, Gravity = 14, Drag = 0.3f });
        for (int i = 0; i < 30; i++)
            AddParticle(new Particle { Pos = mid + RandDir() * 0.5f, Vel = RandDir() * 3.5f, Color = new(0.3f, 0.95f, 0.45f, 0.85f), Size = 0.14f, Life = 1f, Gravity = 12, Additive = true });
        AddDecal(p.Pos + Vector3.UnitY * 0.02f, Vector3.UnitY, 2.4f, 2, new Vector4(0.3f, 0.85f, 0.35f, 0.85f));
        DynLights.Add(new DynLight { Pos = mid, Color = new Vector3(0.6f, 2f, 0.8f), Radius = 9, Life = 0.5f, MaxLife = 0.5f });
        var toP = P.Pos - p.Pos; toP.Y = 0;
        var dir = toP.LengthSquared() > 0.01f ? Vector3.Normalize(toP) : Vector3.UnitX;
        var spot = L.Move(p.Pos + dir * 0.2f, dir * 1.4f, 0.5f);
        var e = SpawnEnemy(L.Number == 18 ? EnemyDefs.Stalker : EnemyDefs.Trooper, spot);
        e.SpawnT = 0.4f;
        Alert(e);
        TotalEnemies++;
        Cue(Game.Cue.Ambush);
    }

    // ---------------- data pads & xeno files ----------------
    bool TakeLog(int id)
    {
        if (!P.Logs.Contains(id)) P.Logs.Add(id);
        LogCard = id;
        LogCardT = LogCardDuration(Story.Logs[id]);
        Snd.Play("datapad", null, 0.8f);
        return true;
    }

    void UnlockXeno(Enemy e)
    {
        if ((P.KilledMask & (1 << e.Type)) != 0) return;
        P.KilledMask |= 1 << e.Type;
        var f = Story.Xeno.FirstOrDefault(x => x.Type == e.Type);
        if (f == null) return;
        XenoUnlocked = f.Title;
        XenoUnlockT = 3.5f;
    }

    void OnBossAnnounced(Enemy e)
    {
        BossTitle = Story.BossTitle(L.Number, e.D.Name);
        BossCardT = 3.5f;
        Cue(Game.Cue.Boss);
    }
}
