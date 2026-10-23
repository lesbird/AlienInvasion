using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Story set dressing placed into a generated level: data pads, objective consoles,
/// and wall graffiti.</summary>
public partial class Level
{
    /// <summary>Props the player switches on for this sublevel's story objective (indexes into Props).</summary>
    public readonly List<int> ObjectiveProps = new();
    /// <summary>Painted wall messages: centre of the text on the wall face, the face normal and the text.</summary>
    public readonly List<(Vector3 Pos, Vector3 N, string Text)> Graffiti = new();

    void PlaceStory()
    {
        var st = Story.For(Number);
        if (Number == 9) PowerFailure = true;   // the hive is feeding on the grid

        // data pads: one out in the level, the last one often tucked into a secret stash
        var startDist = Distances(StartX, StartY, null);
        var logs = Enumerable.Range(0, Story.Logs.Length).Where(i => Story.Logs[i].Level == Number).ToList();
        for (int k = 0; k < logs.Count; k++)
        {
            if (k == logs.Count - 1 && k > 0 && SecretRooms.Count > 0 && rng.NextDouble() < 0.6)
            {
                var stash = SecretRooms[rng.Next(SecretRooms.Count)];
                int c = stash[rng.Next(stash.Count)];
                Items.Add(new ItemSpawn { Kind = PickupKind.Log, Arg = logs[k], X = c % W, Y = c / W });
            }
            else AddItem(PickupKind.Log, logs[k], c => RoomCell[c] && startDist[c] > 5 && startDist[c] < int.MaxValue);
        }

        if (st.Objective != null) PlaceObjective(st.Objective.Count);
        foreach (var text in st.Graffiti) PlaceGraffiti(text);
    }

    /// <summary>A free cell against a room wall (not an entrance), facing back into the room.</summary>
    bool WallSlot(Room r, out int x, out int y, out int fx, out int fy)
    {
        x = y = fx = fy = 0;
        var cands = new List<(int, int, int, int)>();
        for (int cy = r.Y; cy < r.Y + r.H; cy++)
            for (int cx = r.X; cx < r.X + r.W; cx++)
            {
                int i = Idx(cx, cy);
                if (Busy(i)) continue;
                bool entrance = false;
                var walls = new List<(int, int)>();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (r.Contains(nx, ny)) continue;
                    if (Floor(nx, ny)) entrance = true;
                    else walls.Add((-DX[k], -DY[k]));
                }
                if (entrance || walls.Count == 0) continue;
                var (a, b) = walls[rng.Next(walls.Count)];
                cands.Add((cx, cy, a, b));
            }
        if (cands.Count == 0) return false;
        (x, y, fx, fy) = cands[rng.Next(cands.Count)];
        return true;
    }

    void PlaceObjective(int count)
    {
        // reuse consoles already furnishing the level, one per room, spread out from the start
        var startDist = Distances(StartX, StartY, null);
        var used = new HashSet<Room>();
        var props = Enumerable.Range(0, Props.Count)
            .Where(i => Props[i].Kind is PropKind.Console or PropKind.ConsoleBank)
            .OrderByDescending(i => { var (cx, cy) = CellOf(Props[i].Pos); int d = startDist[Idx(cx, cy)]; return d == int.MaxValue ? 0 : d; })
            .ToList();
        foreach (int i in props)
        {
            if (ObjectiveProps.Count >= count) break;
            var (cx, cy) = CellOf(Props[i].Pos);
            var room = Rooms.FirstOrDefault(r => r.Contains(cx, cy));
            if (room == null || room == StartRoom || used.Contains(room) || startDist[Idx(cx, cy)] == int.MaxValue) continue;
            used.Add(room);
            ObjectiveProps.Add(i);
        }
        // not enough consoles: install some against room walls
        foreach (var room in Rooms.Where(r => r != StartRoom && !used.Contains(r)).OrderBy(_ => rng.Next()))
        {
            if (ObjectiveProps.Count >= count) break;
            if (!WallSlot(room, out int x, out int y, out int fx, out int fy)) continue;
            AddProp(PropKind.Console, x, y, fx, fy);
            used.Add(room);
            ObjectiveProps.Add(Props.Count - 1);
        }
    }

    void PlaceGraffiti(string text)
    {
        for (int t = 0; t < 200; t++)
        {
            int x = rng.Next(1, W - 1), y = rng.Next(1, H - 1);
            int i = Idx(x, y);
            if (!Floor(x, y) || SecretCell[i] || DoorAt[i] >= 0 || PropAt[i] != null || Crate[i] || PropBlock[i]) continue;
            if ((x == ExitX && y == ExitY) || (x == StartX && y == StartY)) continue;
            int k = rng.Next(4);
            int nx = x + DX[k], ny = y + DY[k];
            if (!In(nx, ny) || Cells[Idx(nx, ny)] != 0) continue;
            var n = new Vector3(-DX[k], 0, -DY[k]);
            var pos = Center(x, y, 2.3f) - n * (CS / 2 - 0.012f);
            if (Hazards.Any(h => Vector3.Distance(h.Pos, pos with { Y = 0 }) < 2.5f)) continue;
            if (Graffiti.Any(g => Vector3.Distance(g.Pos, pos) < 6)) continue;
            Graffiti.Add((pos, n, text));
            return;
        }
    }
}
