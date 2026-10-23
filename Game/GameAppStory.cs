using System.Numerics;
using AlienInvasion.Engine;
using Silk.NET.Input;

namespace AlienInvasion.Game;

/// <summary>Story presentation: the mission briefing, the radio panel, the Mind's whispers, boss cards,
/// data pads and the INTEL screen, the objective tracker, the epilogue and the credits.</summary>
public partial class GameApp
{
    int intelTab, intelIdx;

    static List<string> Wrap(string text, int maxChars)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length > 0 && cur.Length + 1 + word.Length > maxChars) { lines.Add(cur); cur = word; }
            else cur = cur.Length == 0 ? word : cur + " " + word;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Text revealed by a typewriter at the given rate.</summary>
    static string Typed(string text, float t, float cps) => text[..Math.Clamp((int)(t * cps), 0, text.Length)];

    // ------------------------------------------------------------------ briefing
    float BriefingLineStart(int i) => 0.6f + i * 2.4f;

    void StartBriefing()
    {
        state = GameState.Briefing;
        stateT = 0;
        SetCapture(false);
    }

    void UpdateBriefing()
    {
        bool done = stateT > BriefingLineStart(Story.Briefing.Length);
        if (Pressed(Key.Escape)) { GoTitle(); return; }
        if (Pressed(Key.Enter, Key.Space, Key.E) || mouseLPressed)
        {
            if (!done) stateT = BriefingLineStart(Story.Briefing.Length) + 0.01f;
            else { snd.Play("weapon"); NewGame(); }
        }
    }

    void DrawBriefing(int W, int H, float s)
    {
        ui.Rect(0, 0, W, H, C(0, 0.01f, 0.005f, 0.86f));
        float cx = W / 2f;
        ui.Text("OPERATION DEEP DESCENT", cx, H * 0.14f, 5 * s, C(0.4f, 1f, 0.5f), 1);
        ui.Text("MISSION BRIEFING - EYES ONLY", cx, H * 0.14f + 52 * s, 2.2f * s, C(0.9f, 0.8f, 0.5f, 0.8f), 1);
        float y = H * 0.3f;
        int maxChars = (int)(W * 0.8f / (6 * 2.6f * s));
        for (int i = 0; i < Story.Briefing.Length; i++)
        {
            float t = stateT - BriefingLineStart(i);
            if (t <= 0) break;
            bool last = i == Story.Briefing.Length - 1;
            var col = last ? C(1f, 0.85f, 0.4f) : C(0.82f, 0.9f, 0.84f);
            foreach (var l in Wrap(Typed(Story.Briefing[i], t, 45), maxChars))
            {
                ui.Text(l, cx, y, 2.6f * s, col, 1);
                y += 28 * s;
            }
            y += 14 * s;
        }
        bool done = stateT > BriefingLineStart(Story.Briefing.Length);
        ui.Text(done ? "PRESS ENTER TO BEGIN THE DESCENT" : "PRESS ENTER TO SKIP", cx, H * 0.88f, 2.8f * s, C(1, 1, 0.5f, (done ? 0.6f : 0.35f) + 0.3f * MathF.Sin(time * 4)), 1);
    }

    // ------------------------------------------------------------------ in-game story HUD
    void DrawStoryHud(int W, int H, float s, float barH)
    {
        var w = world;
        // radio panel (bottom left, above the goggle meters)
        if (w.Comm is { } line && line.Who != Speaker.Mind)
        {
            float pw = MathF.Min(600 * s, W * 0.5f), ts = 2.1f * s;
            int maxChars = (int)((pw - 28 * s) / (6 * ts));
            var lines = Wrap(Typed(line.Text, w.CommT, 42), maxChars);
            int full = Wrap(line.Text, maxChars).Count;
            float ph = 34 * s + full * 20 * s;
            float px = 20 * s, py = H - barH - 70 * s - ph;
            float fade = MathF.Min(1, MathF.Min(w.CommT * 5, (World.CommDuration(line) - w.CommT) * 2.5f));
            var sc = Story.Color(line.Who);
            ui.Rect(px, py, pw, ph, C(0.01f, 0.03f, 0.02f, 0.72f * fade));
            ui.Rect(px, py, 3 * s, ph, sc * new Vector4(1, 1, 1, fade));
            // speaker and a little voice meter while the line is still coming in
            ui.Text(Story.Name(line.Who), px + 14 * s, py + 8 * s, 2 * s, sc * new Vector4(1, 1, 1, fade));
            bool talking = w.CommT * 42 < line.Text.Length;
            float mx = px + 14 * s + Renderer2D.TextWidth(Story.Name(line.Who), 2 * s) + 12 * s;
            for (int i = 0; i < 6; i++)
            {
                float hgt = talking ? (3 + 9 * MathF.Abs(MathF.Sin(time * 17 + i * 1.9f))) * s : 2 * s;
                ui.Rect(mx + i * 5 * s, py + 15 * s - hgt / 2, 3 * s, hgt, sc * new Vector4(1, 1, 1, 0.7f * fade));
            }
            float ly = py + 28 * s;
            foreach (var l in lines) { ui.Text(l, px + 14 * s, ly, ts, C(0.9f, 0.93f, 0.9f, fade)); ly += 20 * s; }
        }

        // the Mind: purple text that crawls across the middle of the screen
        string whisper = null;
        float wa = 0;
        if (w.Comm is { Who: Speaker.Mind } ml) { whisper = Typed(ml.Text, w.CommT, 30); wa = MathF.Min(1, MathF.Min(w.CommT * 3, World.CommDuration(ml) - w.CommT)); }
        else if (w.WhisperT > 0) { whisper = w.Whisper; wa = MathF.Min(1, MathF.Min((3.5f - w.WhisperT) * 2, w.WhisperT)); }
        if (whisper != null && wa > 0)
        {
            var mc = Story.Color(Speaker.Mind);
            ui.RectOutline(0, 0, W, H, 18 * s, C(0.5f, 0.15f, 0.7f, 0.12f * wa));
            var rnd = new Random((int)(time * 20));
            for (int k = 0; k < 3; k++)
            {
                float jx = ((float)rnd.NextDouble() - 0.5f) * 6 * s, jy = ((float)rnd.NextDouble() - 0.5f) * 4 * s;
                ui.Text(whisper, W / 2f + jx, H * 0.3f + jy, 3.4f * s, mc * new Vector4(1, 1, 1, wa * (k == 0 ? 0.9f : 0.25f)), 1, k == 0);
            }
        }

        // boss card
        if (w.BossCardT > 0 && w.BossTitle != null)
        {
            float t = 3.5f - w.BossCardT;
            float a = MathF.Min(1, MathF.Min(t * 3, w.BossCardT * 1.5f));
            float slam = 1 + MathF.Max(0, 0.4f - t) * 1.5f;
            ui.Rect(0, H * 0.2f, W, 96 * s, C(0.15f, 0.02f, 0.01f, 0.65f * a));
            ui.Rect(0, H * 0.2f, W, 2 * s, C(1f, 0.35f, 0.2f, a));
            ui.Rect(0, H * 0.2f + 94 * s, W, 2 * s, C(1f, 0.35f, 0.2f, a));
            ui.Text("WARNING", W / 2f, H * 0.2f + 10 * s, 2.2f * s, C(1f, 0.6f, 0.4f, a * (0.6f + 0.4f * MathF.Sin(time * 12))), 1);
            ui.Text(w.BossTitle, W / 2f, H * 0.2f + 36 * s, 6 * s * slam, C(1f, 0.4f, 0.25f, a), 1);
        }

        // data pad card (right side)
        if (w.LogCardT > 0 && w.LogCard >= 0)
        {
            var log = Story.Logs[w.LogCard];
            float a = MathF.Min(1, MathF.Min((World.LogCardDuration(log) - w.LogCardT) * 4, w.LogCardT));
            float pw = MathF.Min(470 * s, W * 0.4f), ts = 1.9f * s;
            int maxChars = (int)((pw - 28 * s) / (6 * ts));
            var body = Wrap(log.Text, maxChars);
            float ph = 70 * s + body.Count * 18 * s + 26 * s;
            float px = W - pw - 20 * s, py = MathF.Max(70 * s, H * 0.45f - ph / 2);
            var cc = C(0.35f, 0.85f, 1f);
            ui.Rect(px, py, pw, ph, C(0.01f, 0.04f, 0.06f, 0.82f * a));
            ui.RectOutline(px, py, pw, ph, MathF.Max(1, 1.5f * s), cc * new Vector4(1, 1, 1, 0.6f * a));
            ui.Text("DATA PAD RECOVERED", px + 14 * s, py + 10 * s, 1.8f * s, cc * new Vector4(1, 1, 1, a));
            ui.Text(log.Title, px + 14 * s, py + 28 * s, 2.3f * s, C(1f, 0.9f, 0.6f, a));
            ui.Text(log.Author, px + 14 * s, py + 50 * s, 1.7f * s, C(0.6f, 0.75f, 0.8f, a));
            float y = py + 70 * s;
            foreach (var l in body) { ui.Text(l, px + 14 * s, y, ts, C(0.88f, 0.93f, 0.95f, a)); y += 18 * s; }
            ui.Text("ESC > INTEL TO READ AGAIN", px + 14 * s, y + 6 * s, 1.6f * s, C(0.55f, 0.7f, 0.75f, a * 0.8f));
        }

        // xeno file unlocked
        if (w.XenoUnlockT > 0 && w.XenoUnlocked != null)
            ui.Text($"XENO FILE ADDED: {w.XenoUnlocked}", W - 24 * s, 66 * s, 1.8f * s, C(0.75f, 0.55f, 1f, MathF.Min(1, w.XenoUnlockT)), 2);

        // objective tracker and the console prompt
        if (w.Objective is { } obj && w.ObjDone.Length > 0)
        {
            int n = w.ObjDoneCount;
            bool done = n == w.ObjDone.Length;
            ui.Text(done ? $"{obj.Title} - DONE" : $"{obj.Title}  {n}/{w.ObjDone.Length}", 20 * s, 124 * s, 2 * s, done ? C(0.5f, 1f, 0.6f, 0.8f) : C(1f, 0.75f, 0.3f, 0.9f));
            if (w.ObjAim >= 0 && state == GameState.Playing)
                ui.Text(obj.Prompt, W / 2f, H * 0.58f, 3 * s, C(1f, 0.8f, 0.35f, 0.7f + 0.3f * MathF.Sin(time * 6)), 1);
        }
    }

    // ------------------------------------------------------------------ INTEL screen
    List<int> IntelItems() => intelTab == 0
        ? player.Logs.OrderBy(i => Story.Logs[i].Level).ThenBy(i => i).ToList()
        : Story.Xeno.Select((x, i) => (x, i)).Where(p => (player.KilledMask & (1 << p.x.Type)) != 0).Select(p => p.i).ToList();

    void UpdateIntel()
    {
        if (Pressed(Key.Escape, Key.Backspace)) { state = GameState.Paused; menuIdx = 1; snd.Play("menu"); return; }
        if (Pressed(Key.Left, Key.Right, Key.A, Key.D, Key.Tab) || mouseRPressed) { intelTab = 1 - intelTab; intelIdx = 0; snd.Play("menu"); }
        var items = IntelItems();
        if (Pressed(Key.Up, Key.W) || scroll > 0.1f) { intelIdx--; snd.Play("menu"); }
        if (Pressed(Key.Down, Key.S) || scroll < -0.1f) { intelIdx++; snd.Play("menu"); }
        intelIdx = items.Count == 0 ? 0 : Math.Clamp(intelIdx, 0, items.Count - 1);
    }

    void DrawIntel(int W, int H, float s)
    {
        ui.Rect(0, 0, W, H, C(0, 0.01f, 0.01f, 0.88f));
        float cx = W / 2f;
        ui.Text("INTEL", cx, 30 * s, 5 * s, C(0.4f, 1f, 0.5f), 1);
        string[] tabs = { $"DATA PADS {player.Logs.Count}/{Story.Logs.Length}", $"XENO FILES {Story.Xeno.Count(x => (player.KilledMask & (1 << x.Type)) != 0)}/{Story.Xeno.Length}" };
        for (int i = 0; i < 2; i++)
        {
            float tx = cx + (i == 0 ? -170 : 170) * s;
            bool sel = i == intelTab;
            ui.Text(tabs[i], tx, 84 * s, 2.4f * s, sel ? C(1f, 0.85f, 0.4f) : C(0.55f, 0.6f, 0.55f), 1);
            if (sel) ui.Rect(tx - Renderer2D.TextWidth(tabs[i], 2.4f * s) / 2, 108 * s, Renderer2D.TextWidth(tabs[i], 2.4f * s), 2 * s, C(1f, 0.85f, 0.4f));
        }

        var items = IntelItems();
        float listX = 40 * s, listW = MathF.Min(380 * s, W * 0.32f), top = 130 * s, rowH = 24 * s;
        float textX = listX + listW + 30 * s, textW = W - textX - 40 * s;
        ui.Rect(listX - 10 * s, top - 10 * s, listW + 20 * s, H - top - 70 * s, C(0.02f, 0.06f, 0.04f, 0.6f));
        if (items.Count == 0)
            ui.Text(intelTab == 0 ? "NO DATA PADS RECOVERED YET." : "KILL AN ALIEN TO OPEN ITS FILE.", listX, top, 2 * s, C(0.6f, 0.65f, 0.6f));
        int visible = (int)((H - top - 90 * s) / rowH);
        int first = Math.Clamp(intelIdx - visible / 2, 0, Math.Max(0, items.Count - visible));
        int maxList = (int)(listW / (6 * 1.9f * s));
        for (int k = first; k < items.Count && k < first + visible; k++)
        {
            bool sel = k == intelIdx;
            float y = top + (k - first) * rowH;
            int lv = intelTab == 0 ? Story.Logs[items[k]].Level : 0;
            string label = intelTab == 0 ? $"{(lv == 1 ? "GL " : $"B{lv - 1:00}")}  {Story.Logs[items[k]].Title}" : Story.Xeno[items[k]].Title;
            if (label.Length > maxList) label = label[..(maxList - 2)] + "..";
            if (sel) ui.Rect(listX - 6 * s, y - 5 * s, listW + 12 * s, rowH, C(0.2f, 0.8f, 0.3f, 0.2f));
            ui.Text(label, listX, y, 1.9f * s, sel ? C(0.6f, 1f, 0.6f) : C(0.78f, 0.8f, 0.78f));
        }

        if (items.Count > 0)
        {
            string title, author, text;
            if (intelTab == 0) { var l = Story.Logs[items[intelIdx]]; title = l.Title; author = $"{l.Author} - SUBLEVEL {l.Level}"; text = l.Text; }
            else { var x = Story.Xeno[items[intelIdx]]; title = x.Title; author = "LANTERN XENOBIOLOGY DIVISION"; text = x.Text; }
            float ts = 2.2f * s;
            ui.Text(title, textX, top, 3 * s, C(1f, 0.85f, 0.45f));
            ui.Text(author, textX, top + 34 * s, 1.9f * s, C(0.55f, 0.8f, 0.9f));
            float y = top + 70 * s;
            foreach (var l in Wrap(text, (int)(textW / (6 * ts)))) { ui.Text(l, textX, y, ts, C(0.88f, 0.92f, 0.9f)); y += 26 * s; }
        }
        ui.Text("UP/DOWN SELECT   LEFT/RIGHT SWITCH TAB   ESC BACK", cx, H - 44 * s, 2 * s, C(0.7f, 0.75f, 0.7f, 0.8f), 1);
    }

    /// <summary>Debug views of the story screens for the screenshot tool.</summary>
    void SetupStoryView(string mode)
    {
        var L = world.L;
        void Look(Vector3 from, Vector3 at)
        {
            player.Pos = from;
            var to = at - from;
            player.Yaw = MathF.Atan2(to.X, -to.Z);
            player.Pitch = MathF.Atan2(at.Y - Player.EyeHeight, new Vector2(to.X, to.Z).Length());
        }
        Vector3 Front(Vector3 at, Vector3 n, float d)
        {
            var p = new Vector3(at.X, 0, at.Z) + n * d;
            return L.Blocked(p.X, p.Z, Player.Radius) ? new Vector3(at.X, 0, at.Z) + n * 2f : p;
        }
        switch (mode)
        {
            case "briefing": state = GameState.Briefing; stateT = 60; break;
            case "epilogue": state = GameState.Victory; stateT = 100; break;
            case "credits": state = GameState.Credits; stateT = 9; break;
            case "intel":
                for (int i = 0; i < 24; i++) player.Logs.Add(i);
                player.KilledMask = 0x1FFF;
                state = GameState.Intel; intelIdx = 3; SetCapture(false);
                break;
            case "comms":
                world.LogCard = 1; world.LogCardT = 9;
                world.BossTitle = "THE ARMORY QUEEN"; world.BossCardT = 2.2f;
                break;
            case "whisper": world.Whisper = Story.Whispers[0]; world.WhisperT = 2.5f; break;
            case "graffiti" when L.Graffiti.Count > 0:
            {
                var (pos, n, _) = L.Graffiti[0];
                Look(Front(pos, n, 4.5f), pos);
                break;
            }
            case "tank":
            {
                var tk = L.Props.FirstOrDefault(p => p.Kind == PropKind.Tank);
                if (tk == null) break;
                var dir = Vector3.Normalize(Level.Center(L.StartX, L.StartY) - new Vector3(tk.Pos.X, 0, tk.Pos.Z));
                Look(Front(tk.Pos, dir, 5f), tk.Pos + Vector3.UnitY * 1.4f);
                world.God = true;
                world.BreakTank(tk);
                break;
            }
            case "objective" when L.ObjectiveProps.Count > 0:
            {
                var op = L.Props[L.ObjectiveProps[0]];
                var dir = Vector3.Normalize(new Vector3(MathF.Sin(op.Yaw), 0, MathF.Cos(op.Yaw)));
                Look(Front(op.Pos, dir, 1.9f), op.Pos + Vector3.UnitY * 1.0f);
                world.God = true;
                break;
            }
        }
    }

    // ------------------------------------------------------------------ epilogue & credits
    float EpilogueLineStart(int i)
    {
        float t = 2.5f;
        for (int k = 0; k < i; k++) t += Story.Epilogue[k].Text.Length / 32f + 1.4f;
        return t;
    }
    bool EpilogueDone => stateT > EpilogueLineStart(Story.Epilogue.Length) + 1;

    void DrawEpilogue(int W, int H, float s)
    {
        float k = MathF.Min(1, stateT / 2f);
        ui.Rect(0, 0, W, H, C(0, 0, 0, k * 0.88f));
        ui.Text("MISSION ACCOMPLISHED", W / 2f, H * 0.1f, 6 * s, C(0.5f, 1, 0.5f, k), 1);
        float y = H * 0.24f;
        int maxChars = (int)(W * 0.8f / (6 * 2.5f * s));
        for (int i = 0; i < Story.Epilogue.Length; i++)
        {
            float t = stateT - EpilogueLineStart(i);
            if (t <= 0) break;
            var l = Story.Epilogue[i];
            var col = l.Who == Speaker.Station ? C(0.8f, 0.85f, 0.8f) : Story.Color(l.Who);
            string text = (l.Who == Speaker.Station ? "" : Story.Name(l.Who) + ": ") + l.Text;
            foreach (var wl in Wrap(Typed(text, t, 32), maxChars)) { ui.Text(wl, W / 2f, y, 2.5f * s, col, 1); y += 26 * s; }
            y += 12 * s;
        }
        if (EpilogueDone)
        {
            float a = MathF.Min(1, stateT - EpilogueLineStart(Story.Epilogue.Length) - 1);
            ui.Text("TO BE CONTINUED...", W / 2f, H * 0.68f, 4 * s, C(0.8f, 0.45f, 1f, a), 1);
            ui.Text($"FINAL SCORE  {runScore:N0}     INTEL  {player.Logs.Count}/{Story.Logs.Length}", W / 2f, H * 0.68f + 48 * s, 2.6f * s, C(1f, 0.85f, 0.4f, a), 1);
            ui.Text("PRESS ENTER", W / 2f, H * 0.86f, 3 * s, C(1, 1, 0.5f, a * (0.6f + 0.4f * MathF.Sin(time * 4))), 1);
        }
        else ui.Text("PRESS ENTER TO SKIP", W / 2f, H * 0.92f, 2 * s, C(1, 1, 0.5f, 0.35f), 1);
    }

    static readonly (string Head, string Body)[] Credits =
    {
        ("ALIEN INVASION", "OPERATION DEEP DESCENT"),
        ("A GAME BY", "LES BIRD"),
        ("BUILT WITH", "CLAUDE CODE"),
        ("GRAPHICS, SOUND AND MUSIC", "ALL GENERATED BY CODE AT RUNTIME"),
        ("STARRING", "NOMAD  -  MAJ. ELENA CROSS  -  DR. ILSE KESSLER"),
        ("IN MEMORY OF", "COL. MARCUS HALE  -  PVT. DANNY OKAFOR  -  THE GARRISON OF HADAL STATION"),
        ("THE STORY CONTINUES", "ON THE OTHER SIDE OF THE GATE"),
    };

    void DrawCredits(int W, int H, float s)
    {
        ui.Rect(0, 0, W, H, C(0, 0, 0, 0.8f));
        // scroll up until the last entry sits in the middle of the screen
        float y = MathF.Max(H - stateT * 45 * s, H * 0.45f - (Credits.Length - 1) * 120 * s);
        foreach (var (head, body) in Credits)
        {
            ui.Text(head, W / 2f, y, 2.2f * s, C(0.55f, 0.8f, 0.6f), 1);
            ui.Text(body, W / 2f, y + 28 * s, 3.2f * s, C(0.95f, 0.95f, 0.85f), 1);
            y += 120 * s;
        }
        ui.Rect(0, H - 70 * s, W, 70 * s, C(0, 0, 0, 0.9f));
        ui.Text("PRESS ENTER", W / 2f, H - 40 * s, 2.2f * s, C(1, 1, 0.5f, 0.4f + 0.3f * MathF.Sin(time * 4)), 1);
    }
}
