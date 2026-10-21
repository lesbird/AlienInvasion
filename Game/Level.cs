using System.Numerics;

namespace AlienInvasion.Game;

public class Room
{
    public int X, Y, W, H;
    public int CX => X + W / 2;
    public int CY => Y + H / 2;
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    public int Area => W * H;
}

public class Door
{
    public int X, Y;
    public bool AlongX;      // corridor runs along X, so the slab lies in the YZ plane
    public int Key = -1;     // -1 = normal door, otherwise KeyColor index
    public float Open;       // 0 closed .. 1 fully open
    public bool Unlocked;
    public float HoldOpen;   // seconds to stay open
    public bool Secret;      // looks like a wall; opened with the use key
    public bool Locked => (Key >= 0 || Secret) && !Unlocked;
}

public enum PickupKind { Health, Medkit, MegaHealth, Armor, ArmorVest, Bullets, Shells, Grenades, Rockets, Cells, Weapon, Key, Backpack, DamageAmp, NightVision, XRay, Battery, Upgrade }

public struct ItemSpawn { public PickupKind Kind; public int Arg; public int X, Y; }
public struct EnemySpawn { public int Type; public int X, Y; }

public struct LightDef
{
    public Vector3 Pos, Color;
    public float Radius, Flicker;
    public float Spin, Phase;   // rotating alarm beacon: angular speed (rad/s), 0 = ordinary light
    public bool Beacon => Spin != 0;
}

public partial class Level
{
    public const float CS = 4f;   // cell size (world units)
    public const float WH = 5f;   // wall height
    public static readonly string[] KeyNames = { "RED", "BLUE", "YELLOW", "GREEN" };
    public static readonly Vector3[] KeyColors = { new(1f, 0.15f, 0.1f), new(0.15f, 0.4f, 1f), new(1f, 0.85f, 0.1f), new(0.15f, 1f, 0.3f) };

    public int Number, W, H, Seed;
    public byte[] Cells;          // 0 solid, 1 floor
    public bool[] RoomCell, Crate, Explored, PropBlock, SecretCell;
    public List<Prop> Props = new();
    public List<int>[] PropAt;
    public int[] DoorAt;
    public List<Room> Rooms = new();
    public List<Door> Doors = new();
    public List<LightDef> Lights = new();
    public List<ItemSpawn> Items = new();
    public List<EnemySpawn> Enemies = new();
    public int StartX, StartY, ExitX, ExitY;
    public float StartYaw;
    public bool IsFinal;
    /// <summary>Mini-boss guarding the elevator on sublevels 5, 10, 15, 20 and 25 (-1 = none).</summary>
    public int BossType => Number % 5 != 0 || Number == 30 ? -1 : Number switch { 5 or 20 => EnemyDefs.Queen, 10 or 25 => EnemyDefs.Warlord, _ => EnemyDefs.Mind };
    public bool HasAlcove;
    public int ElevDirX, ElevDirY;  // from elevator cell into the room
    public int LockCount;
    public int[] LockColor = new int[4];
    public Room StartRoom, ExitRoom;
    public Vector3 Ambient, FogColor;
    public float FogDensity;
    Random rng;

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public int Idx(int x, int y) => y * W + x;
    public bool Floor(int x, int y) => In(x, y) && Cells[Idx(x, y)] != 0;
    public static Vector3 Center(int x, int y, float yy = 0) => new((x + 0.5f) * CS, yy, (y + 0.5f) * CS);
    public static (int x, int y) CellOf(Vector3 p) => ((int)MathF.Floor(p.X / CS), (int)MathF.Floor(p.Z / CS));

    public Door DoorIn(int x, int y) => In(x, y) && DoorAt[Idx(x, y)] >= 0 ? Doors[DoorAt[Idx(x, y)]] : null;

    /// <summary>Solid for movement / bullets.</summary>
    public bool Solid(int x, int y)
    {
        if (!In(x, y)) return true;
        int i = Idx(x, y);
        if (Cells[i] == 0 || Crate[i]) return true;
        int d = DoorAt[i];
        return d >= 0 && Doors[d].Open < 0.9f;
    }

    // ------------------------------------------------------------------
    public static Level Generate(int number, int baseSeed)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var lv = new Level();
            if (lv.TryGenerate(number, baseSeed + attempt * 7919, attempt > 40))
                return lv;
        }
        throw new Exception("Level generation failed");
    }

    bool TryGenerate(int number, int seed, bool relaxed)
    {
        Number = number;
        Seed = seed;
        IsFinal = number == 30;
        rng = new Random(seed);
        W = H = Math.Min(34 + number, 64) + (IsFinal ? 6 : 0);
        Cells = new byte[W * H];
        RoomCell = new bool[W * H];
        Crate = new bool[W * H];
        PropBlock = new bool[W * H];
        SecretCell = new bool[W * H];
        Barrier = new bool[W * H];
        PropAt = new List<int>[W * H];
        Explored = new bool[W * H];
        DoorAt = Enumerable.Repeat(-1, W * H).ToArray();

        // ---- rooms ----
        int target = Math.Min(7 + number / 2, 20);
        if (IsFinal) TryAddRoom(W / 2 - 6 + rng.Next(-4, 5), H / 2 - 6 + rng.Next(-4, 5), 12, 12);
        for (int t = 0; t < 600 && Rooms.Count < target; t++)
        {
            int maxS = 6 + Math.Min(number / 6, 3);
            int w = rng.Next(3, maxS + 1), h = rng.Next(3, maxS + 1);
            TryAddRoom(rng.Next(1, W - w - 1), rng.Next(1, H - h - 1), w, h);
        }
        if (Rooms.Count < 5) return false;
        foreach (var r in Rooms)
            for (int y = r.Y; y < r.Y + r.H; y++)
                for (int x = r.X; x < r.X + r.W; x++) { Cells[Idx(x, y)] = 1; RoomCell[Idx(x, y)] = true; }

        // ---- corridors: MST + a few loops ----
        var inTree = new bool[Rooms.Count];
        inTree[0] = true;
        var edges = new HashSet<(int, int)>();
        for (int k = 1; k < Rooms.Count; k++)
        {
            int ba = -1, bb = -1, bd = int.MaxValue;
            for (int a = 0; a < Rooms.Count; a++)
            {
                if (!inTree[a]) continue;
                for (int b = 0; b < Rooms.Count; b++)
                {
                    if (inTree[b]) continue;
                    int d = Math.Abs(Rooms[a].CX - Rooms[b].CX) + Math.Abs(Rooms[a].CY - Rooms[b].CY);
                    if (d < bd) { bd = d; ba = a; bb = b; }
                }
            }
            inTree[bb] = true;
            edges.Add((ba, bb));
            Corridor(Rooms[ba], Rooms[bb]);
        }
        int loops = rng.Next(1, 3 + number / 10);
        for (int k = 0; k < loops; k++)
        {
            int a = rng.Next(Rooms.Count), b = rng.Next(Rooms.Count);
            if (a == b || edges.Contains((a, b)) || edges.Contains((b, a))) continue;
            int d = Math.Abs(Rooms[a].CX - Rooms[b].CX) + Math.Abs(Rooms[a].CY - Rooms[b].CY);
            if (d > 22) continue;
            Corridor(Rooms[a], Rooms[b]);
        }

        // ---- start & exit ----
        if (IsFinal)
        {
            ExitRoom = Rooms[0];
            StartRoom = FarthestRoom(ExitRoom);
        }
        else
        {
            StartRoom = Rooms[rng.Next(Rooms.Count)];
            ExitRoom = FarthestRoom(StartRoom);
            StartRoom = FarthestRoom(ExitRoom);
        }
        if (StartRoom == ExitRoom) return false;
        StartX = StartRoom.CX; StartY = StartRoom.CY;

        if (IsFinal)
        {
            ExitX = ExitRoom.CX; ExitY = ExitRoom.CY;
        }
        else if (!PlaceElevator())
        {
            ExitX = ExitRoom.CX; ExitY = ExitRoom.CY;
            HasAlcove = false;
        }

        // ---- path & locked doors on chokepoints ----
        var path = Path(StartX, StartY, ExitX, ExitY, null);
        if (path == null) return false;
        int wantLocks = number == 1 ? 1 : number <= 4 ? rng.Next(1, 3) : number <= 12 ? rng.Next(2, 4) : rng.Next(3, 5);
        var cands = new List<int>();
        for (int i = 3; i < path.Count - 3; i++)
        {
            var (x, y) = path[i];
            int id = Idx(x, y);
            if (RoomCell[id] || !StraightCorridor(x, y)) continue;
            if (Math.Abs(x - ExitX) + Math.Abs(y - ExitY) < 2) continue;
            var blocked = new HashSet<int> { id };
            if (Path(StartX, StartY, ExitX, ExitY, blocked) == null) cands.Add(i);
        }
        var chosen = new List<int>();
        int last = -100;
        for (int j = 1; j <= wantLocks; j++)
        {
            int tgt = path.Count * j / (wantLocks + 1);
            int best = -1, bd = int.MaxValue;
            foreach (int c in cands)
            {
                if (c <= last + 4) continue;
                if (Math.Abs(c - tgt) < bd) { bd = Math.Abs(c - tgt); best = c; }
            }
            if (best < 0) break;
            chosen.Add(best);
            last = best;
        }
        if (chosen.Count == 0 && !relaxed) return false;
        LockCount = chosen.Count;
        var colors = new List<int> { 0, 1, 2, 3 };
        for (int i = 0; i < 4; i++) { int j = rng.Next(i, 4); (colors[i], colors[j]) = (colors[j], colors[i]); }
        if (number == 1) { colors.Remove(0); colors.Insert(0, 0); }   // the very first key is always red
        var lockCells = new List<int>();
        for (int j = 0; j < chosen.Count; j++)
        {
            var (x, y) = path[chosen[j]];
            LockColor[j] = colors[j];
            AddDoor(x, y, colors[j]);
            lockCells.Add(Idx(x, y));
        }

        // ---- ordinary doors at some room entrances ----
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                int id = Idx(x, y);
                if (Cells[id] == 0 || RoomCell[id] || DoorAt[id] >= 0 || !StraightCorridor(x, y)) continue;
                bool nearRoom = RoomCell[Idx(x - 1, y)] || RoomCell[Idx(x + 1, y)] || RoomCell[Idx(x, y - 1)] || RoomCell[Idx(x, y + 1)];
                if (!nearRoom || rng.NextDouble() > 0.35) continue;
                bool nearDoor = false;
                for (int oy = -2; oy <= 2; oy++) for (int ox = -2; ox <= 2; ox++) if (In(x + ox, y + oy) && DoorAt[Idx(x + ox, y + oy)] >= 0) nearDoor = true;
                if (nearDoor || (x == ExitX && y == ExitY)) continue;
                AddDoor(x, y, -1);
            }

        // ---- crates (never disconnect anything) ----
        int floorCount = Cells.Count(c => c != 0);
        foreach (var r in Rooms)
        {
            if (r == StartRoom || r.W < 5 || r.H < 5) continue;
            for (int y = r.Y + 1; y < r.Y + r.H - 1; y++)
                for (int x = r.X + 1; x < r.X + r.W - 1; x++)
                {
                    if (rng.NextDouble() > 0.11) continue;
                    if (Math.Abs(x - ExitX) + Math.Abs(y - ExitY) <= 2) continue;
                    if (IsFinal && r == ExitRoom && Math.Abs(x - ExitX) <= 3 && Math.Abs(y - ExitY) <= 3) continue;
                    int id = Idx(x, y);
                    Crate[id] = true;
                    if (Reachable(StartX, StartY, null).Count != floorCount - Crate.Count(c => c)) Crate[id] = false;
                }
        }

        PlaceProps();

        // ---- keys: key j lies in the area reachable with keys < j ----
        HashSet<int> prev = new();
        var keyCells = new List<(int, int)>();
        for (int j = 0; j < LockCount; j++)
        {
            var blocked = new HashSet<int>(lockCells.Skip(j));
            var region = Reachable(StartX, StartY, blocked);
            var fresh = region.Where(c => !prev.Contains(c)).ToList();
            int entry = j == 0 ? Idx(StartX, StartY) : lockCells[j - 1];
            var dist = Distances(entry % W, entry / W, blocked);
            List<int> pool = fresh.Where(c => RoomCell[c] && !Crate[c] && !PropBlock[c] && DoorAt[c] < 0 && !StartRoom.Contains(c % W, c / W)).ToList();
            if (pool.Count == 0) pool = fresh.Where(c => !Crate[c] && !PropBlock[c] && DoorAt[c] < 0 && c != Idx(StartX, StartY)).ToList();
            if (pool.Count == 0) pool = region.Where(c => !Crate[c] && !PropBlock[c] && DoorAt[c] < 0 && c != Idx(StartX, StartY)).ToList();
            if (pool.Count == 0) return false;
            pool.Sort((a, b) => dist[b].CompareTo(dist[a]));
            int pick = pool[rng.Next(Math.Max(1, pool.Count / 3))];
            Items.Add(new ItemSpawn { Kind = PickupKind.Key, Arg = LockColor[j], X = pick % W, Y = pick / W });
            keyCells.Add((pick % W, pick / W));
            prev = region;
        }

        PlaceItems();
        PlaceEnemies();
        PlaceSecrets();
        PlaceHazards();
        PlaceLights();
        SetAtmosphere();

        // face toward the room's most open direction
        StartYaw = 0;
        var firstStep = Path(StartX, StartY, ExitX, ExitY, null);
        if (firstStep != null && firstStep.Count > 3)
        {
            var (nx, ny) = firstStep[Math.Min(3, firstStep.Count - 1)];
            StartYaw = MathF.Atan2(nx - StartX, -(ny - StartY));
        }
        return Validate();
    }

    bool TryAddRoom(int x, int y, int w, int h)
    {
        if (x < 1 || y < 1 || x + w >= W - 1 || y + h >= H - 1) return false;
        foreach (var r in Rooms)
            if (x < r.X + r.W + 2 && x + w + 2 > r.X && y < r.Y + r.H + 2 && y + h + 2 > r.Y) return false;
        Rooms.Add(new Room { X = x, Y = y, W = w, H = h });
        return true;
    }

    void Corridor(Room a, Room b)
    {
        int ax = a.X + rng.Next(a.W), ay = a.Y + rng.Next(a.H);
        int bx = b.X + rng.Next(b.W), by = b.Y + rng.Next(b.H);
        if (rng.Next(2) == 0) { HLine(ax, bx, ay); VLine(ay, by, bx); }
        else { VLine(ay, by, ax); HLine(ax, bx, by); }
    }

    void HLine(int x0, int x1, int y) { for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Cells[Idx(x, y)] = 1; }
    void VLine(int y0, int y1, int x) { for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Cells[Idx(x, y)] = 1; }

    bool StraightCorridor(int x, int y)
    {
        bool l = Floor(x - 1, y), r = Floor(x + 1, y), u = Floor(x, y - 1), d = Floor(x, y + 1);
        return (l && r && !u && !d) || (u && d && !l && !r);
    }

    void AddDoor(int x, int y, int key)
    {
        var door = new Door { X = x, Y = y, Key = key, AlongX = Floor(x - 1, y) };
        DoorAt[Idx(x, y)] = Doors.Count;
        Doors.Add(door);
    }

    Room FarthestRoom(Room from)
    {
        var d = Distances(from.CX, from.CY, null);
        Room best = from;
        int bd = -1;
        foreach (var r in Rooms)
        {
            int v = d[Idx(r.CX, r.CY)];
            if (v > bd && v < int.MaxValue) { bd = v; best = r; }
        }
        return best;
    }

    bool PlaceElevator()
    {
        var r = ExitRoom;
        var cands = new List<(int x, int y, int dx, int dy)>();
        for (int x = r.X; x < r.X + r.W; x++)
        {
            cands.Add((x, r.Y - 1, 0, 1));
            cands.Add((x, r.Y + r.H, 0, -1));
        }
        for (int y = r.Y; y < r.Y + r.H; y++)
        {
            cands.Add((r.X - 1, y, 1, 0));
            cands.Add((r.X + r.W, y, -1, 0));
        }
        cands = cands.Where(c =>
        {
            if (c.x < 1 || c.y < 1 || c.x >= W - 1 || c.y >= H - 1) return false;
            if (Cells[Idx(c.x, c.y)] != 0) return false;
            int open = 0;
            if (Floor(c.x + 1, c.y)) open++;
            if (Floor(c.x - 1, c.y)) open++;
            if (Floor(c.x, c.y + 1)) open++;
            if (Floor(c.x, c.y - 1)) open++;
            // diagonal neighbours behind/side must be solid too
            int bx = c.x - c.dx, by = c.y - c.dy;
            return open == 1 && !Floor(bx, by);
        }).ToList();
        if (cands.Count == 0) return false;
        var e = cands[rng.Next(cands.Count)];
        Cells[Idx(e.x, e.y)] = 1;
        ExitX = e.x; ExitY = e.y;
        ElevDirX = e.dx; ElevDirY = e.dy;
        HasAlcove = true;
        return true;
    }

    // ------------------------------------------------------------------ search helpers
    static readonly int[] DX = { 1, -1, 0, 0 }, DY = { 0, 0, 1, -1 };

    public int[] Distances(int sx, int sy, HashSet<int> blocked, bool ignoreCrates = false)
    {
        var dist = new int[W * H];
        Array.Fill(dist, int.MaxValue);
        var q = new Queue<int>();
        int s = Idx(sx, sy);
        dist[s] = 0;
        q.Enqueue(s);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int cx = c % W, cy = c / W;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                if (!In(nx, ny)) continue;
                int n = Idx(nx, ny);
                if (Cells[n] == 0 || dist[n] != int.MaxValue) continue;
                if (!ignoreCrates && (Crate[n] || PropBlock[n])) continue;
                if (blocked != null && blocked.Contains(n)) continue;
                dist[n] = dist[c] + 1;
                q.Enqueue(n);
            }
        }
        return dist;
    }

    public HashSet<int> Reachable(int sx, int sy, HashSet<int> blocked)
    {
        var d = Distances(sx, sy, blocked);
        var set = new HashSet<int>();
        for (int i = 0; i < d.Length; i++) if (d[i] != int.MaxValue) set.Add(i);
        return set;
    }

    public List<(int, int)> Path(int sx, int sy, int tx, int ty, HashSet<int> blocked)
    {
        var d = Distances(tx, ty, blocked);
        if (d[Idx(sx, sy)] == int.MaxValue) return null;
        var path = new List<(int, int)> { (sx, sy) };
        int x = sx, y = sy;
        while (x != tx || y != ty)
        {
            int best = d[Idx(x, y)], bx = x, by = y;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + DX[k], ny = y + DY[k];
                if (In(nx, ny) && d[Idx(nx, ny)] < best) { best = d[Idx(nx, ny)]; bx = nx; by = ny; }
            }
            if (bx == x && by == y) return null;
            x = bx; y = by;
            path.Add((x, y));
        }
        return path;
    }

    /// <summary>Simulates a player collecting keys in order; true if the exit is reachable.</summary>
    public bool Validate()
    {
        var lockCells = new HashSet<int>(Doors.Where(d => d.Key >= 0).Select(d => Idx(d.X, d.Y)));
        var have = new HashSet<int>();
        for (int iter = 0; iter < 6; iter++)
        {
            var blocked = new HashSet<int>(Doors.Where(d => d.Key >= 0 && !have.Contains(d.Key)).Select(d => Idx(d.X, d.Y)));
            var reach = Reachable(StartX, StartY, blocked);
            if (reach.Contains(Idx(ExitX, ExitY)) && have.Count == LockCount) return true;
            bool gained = false;
            foreach (var it in Items)
                if (it.Kind == PickupKind.Key && reach.Contains(Idx(it.X, it.Y)) && have.Add(it.Arg)) gained = true;
            if (!gained) return reach.Contains(Idx(ExitX, ExitY)) && LockCount == 0;
        }
        return false;
    }

    // ------------------------------------------------------------------ population
    List<int> FreeCells(Func<int, bool> filter)
    {
        var occupied = new HashSet<int>(Items.Select(i => Idx(i.X, i.Y)));
        var list = new List<int>();
        for (int i = 0; i < Cells.Length; i++)
        {
            if (Cells[i] == 0 || Crate[i] || PropBlock[i] || SecretCell[i] || DoorAt[i] >= 0 || occupied.Contains(i)) continue;
            if (i == Idx(ExitX, ExitY)) continue;
            if (filter(i)) list.Add(i);
        }
        return list;
    }

    void AddItem(PickupKind k, int arg, Func<int, bool> filter)
    {
        var free = FreeCells(filter);
        if (free.Count == 0) free = FreeCells(_ => true);
        if (free.Count == 0) return;
        int c = free[rng.Next(free.Count)];
        Items.Add(new ItemSpawn { Kind = k, Arg = arg, X = c % W, Y = c / W });
    }

    void PlaceItems()
    {
        var startDist = Distances(StartX, StartY, null);
        var firstRegion = Reachable(StartX, StartY, new HashSet<int>(Doors.Where(d => d.Key >= 0).Select(d => Idx(d.X, d.Y))));
        bool InRoom(int c) => RoomCell[c];

        // Guaranteed weapon unlocks on specific levels
        int newWeapon = Number switch { 2 => Weapons.Shotgun, 4 => Weapons.Rifle, 7 => Weapons.GrenadeLauncher, 11 => Weapons.RocketLauncher, _ => -1 };
        if (newWeapon >= 0)
            AddItem(PickupKind.Weapon, newWeapon, c => firstRegion.Contains(c) && InRoom(c) && startDist[c] > 6 && startDist[c] < int.MaxValue);
        if (Number >= 5 && rng.NextDouble() < 0.5)
        {
            var pool = new List<int> { Weapons.Shotgun, Weapons.Rifle };
            if (Number >= 8) pool.Add(Weapons.GrenadeLauncher);
            if (Number >= 12) pool.Add(Weapons.RocketLauncher);
            AddItem(PickupKind.Weapon, pool[rng.Next(pool.Count)], c => InRoom(c) && startDist[c] > 10);
        }

        // Starting supplies
        AddItem(PickupKind.Bullets, 0, c => StartRoom.Contains(c % W, c / W));
        if (Number > 1) AddItem(PickupKind.Health, 0, c => StartRoom.Contains(c % W, c / W));

        var ammo = new List<PickupKind> { PickupKind.Bullets, PickupKind.Bullets };
        if (Number >= 2) { ammo.Add(PickupKind.Shells); ammo.Add(PickupKind.Shells); }
        if (Number >= 5) ammo.Add(PickupKind.Cells);
        if (Number >= 7) ammo.Add(PickupKind.Grenades);
        if (Number >= 11) ammo.Add(PickupKind.Rockets);
        if (Number >= 14) ammo.Add(PickupKind.Cells);

        foreach (var r in Rooms)
        {
            if (r == StartRoom) continue;
            bool InThis(int c) => r.Contains(c % W, c / W);
            int n = 1 + r.Area / 25;
            for (int k = 0; k < n; k++)
            {
                double p = rng.NextDouble();
                if (p < 0.30) AddItem(rng.NextDouble() < 0.25 + Number * 0.01 ? PickupKind.Medkit : PickupKind.Health, 0, InThis);
                else if (p < 0.42) AddItem(rng.NextDouble() < 0.3 ? PickupKind.ArmorVest : PickupKind.Armor, 0, InThis);
                else if (p < 0.85) AddItem(ammo[rng.Next(ammo.Count)], 0, InThis);
            }
        }
        // goggle batteries (useless until the player owns night vision or x-ray goggles)
        if (Number >= 3)
        {
            int nb = 1 + (rng.NextDouble() < 0.45 ? 1 : 0);
            for (int k = 0; k < nb; k++) AddItem(PickupKind.Battery, 0, c => InRoom(c) && startDist[c] > 8 && startDist[c] < int.MaxValue);
        }
        if (Number % 5 == 0 || Number == 29) AddItem(PickupKind.MegaHealth, 0, c => InRoom(c) && startDist[c] > 15);
        // the final arena gets supplies around its edge
        if (IsFinal)
        {
            for (int k = 0; k < 6; k++) AddItem(k % 2 == 0 ? PickupKind.Cells : PickupKind.Rockets, 0, c => ExitRoom.Contains(c % W, c / W) && Math.Abs(c % W - ExitX) > 3);
            AddItem(PickupKind.Medkit, 0, c => ExitRoom.Contains(c % W, c / W) && Math.Abs(c % W - ExitX) > 3);
            AddItem(PickupKind.ArmorVest, 0, c => ExitRoom.Contains(c % W, c / W) && Math.Abs(c % W - ExitX) > 3);
        }
    }

    void PlaceEnemies()
    {
        var startDist = Distances(StartX, StartY, null);
        int count = Math.Min((int)(5 + Number * 1.35f), 48);
        var free = FreeCells(c => startDist[c] >= 8 && startDist[c] != int.MaxValue && !StartRoom.Contains(c % W, c / W));
        var used = new HashSet<int>();
        for (int k = 0; k < count && free.Count > 0; k++)
        {
            int c = -1;
            for (int t = 0; t < 10; t++)
            {
                int cand = free[rng.Next(free.Count)];
                if (!RoomCell[cand] && rng.NextDouble() < 0.6) continue;  // prefer rooms
                if (used.Contains(cand)) continue;
                c = cand;
                break;
            }
            if (c < 0) continue;
            used.Add(c);
            Enemies.Add(new EnemySpawn { Type = EnemyDefs.PickType(Number, rng), X = c % W, Y = c / W });
        }
        if (BossType >= 0)
        {
            // the boss guards the elevator room; prefer a spot well away from the lift
            var exitCells = FreeCells(c => ExitRoom.Contains(c % W, c / W) && Math.Abs(c % W - ExitX) + Math.Abs(c / W - ExitY) >= 2);
            if (exitCells.Count == 0) exitCells = FreeCells(c => ExitRoom.Contains(c % W, c / W));
            if (exitCells.Count > 0)
            {
                int c = exitCells.OrderByDescending(i => Math.Abs(i % W - ExitX) + Math.Abs(i / W - ExitY)).First();
                Enemies.Add(new EnemySpawn { Type = BossType, X = c % W, Y = c / W });
            }
        }
    }

    void PlaceLights()
    {
        var t = Number;
        Vector3 main;
        float flickerChance, brokenChance;
        if (t <= 7) { main = new(0.85f, 0.92f, 1.0f); flickerChance = 0.04f; brokenChance = 0.0f; }
        else if (t <= 15) { main = new(1.0f, 0.78f, 0.48f); flickerChance = 0.12f; brokenChance = 0.08f; }
        else if (t <= 23) { main = new(0.8f, 0.85f, 0.95f); flickerChance = 0.2f; brokenChance = 0.18f; }
        else { main = new(0.6f, 0.35f, 1.0f); flickerChance = 0.25f; brokenChance = 0.2f; }

        void Add(Vector3 p, Vector3 c, float r, float f) => Lights.Add(new LightDef { Pos = p, Color = c, Radius = r, Flicker = f });

        foreach (var r in Rooms)
        {
            for (int y = r.Y + 1; y < r.Y + r.H; y += 3)
                for (int x = r.X + 1; x < r.X + r.W; x += 3)
                {
                    if (rng.NextDouble() < brokenChance && r != StartRoom) continue;
                    var c = main;
                    if (t > 15 && t <= 23 && rng.NextDouble() < 0.35) c = new(1f, 0.12f, 0.08f);
                    if (t > 23 && rng.NextDouble() < 0.4) c = new(0.3f, 1f, 0.5f);
                    if (t > 23 && rng.NextDouble() < 0.15) c = new(1f, 0.15f, 0.1f);
                    c *= 0.85f + (float)rng.NextDouble() * 0.3f;
                    float f = rng.NextDouble() < flickerChance ? 0.5f + (float)rng.NextDouble() * 0.5f : 0;
                    Add(Center(x, y, WH - 0.2f), c, 12f, f);
                }
        }
        // rotating alarm beacons in the lower levels
        if (t >= 8)
        {
            float chance = MathF.Min(0.65f, 0.2f + (t - 8) * 0.025f);
            foreach (var r in Rooms)
            {
                if (r == StartRoom || r.W < 3 || r.H < 3) continue;
                int count = r.W * r.H >= 42 ? 2 : 1;
                for (int k = 0; k < count; k++)
                {
                    if (rng.NextDouble() > chance) continue;
                    int bx = count == 1 ? r.X + r.W / 2 : r.X + (k == 0 ? r.W / 3 : r.W * 2 / 3);
                    int by = r.Y + r.H / 2;
                    // keep off the regular ceiling fixtures
                    if ((bx - r.X - 1) % 3 == 0 && (by - r.Y - 1) % 3 == 0) bx = Math.Min(bx + 1, r.X + r.W - 1);
                    if (Lights.Any(l => l.Beacon && Vector3.DistanceSquared(l.Pos, Center(bx, by, l.Pos.Y)) < 1)) continue;
                    var col = t > 23 && rng.NextDouble() < 0.4 ? new Vector3(0.7f, 0.25f, 1.6f)
                        : rng.NextDouble() < 0.7 ? new Vector3(1.9f, 0.18f, 0.06f) : new Vector3(1.8f, 0.8f, 0.08f);
                    Lights.Add(new LightDef
                    {
                        Pos = Center(bx, by, WH - 0.55f), Color = col, Radius = 14f,
                        Spin = (rng.NextDouble() < 0.5 ? -1 : 1) * (3f + (float)rng.NextDouble() * 1.5f),
                        Phase = (float)rng.NextDouble() * MathF.Tau
                    });
                }
            }
        }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = Idx(x, y);
                if (Cells[i] == 0 || RoomCell[i] || DoorAt[i] >= 0 || SecretCell[i]) continue;
                if ((x * 7 + y * 13) % 5 != 0) continue;
                if (rng.NextDouble() < brokenChance) continue;
                float f = rng.NextDouble() < flickerChance * 1.5 ? 0.7f : 0;
                Add(Center(x, y, WH - 0.2f), main * 0.7f, 8f, f);
            }
        foreach (var d in Doors.Where(d => d.Key >= 0 && !d.Secret))
            Add(Center(d.X, d.Y, WH - 0.6f), KeyColors[d.Key] * 0.9f, 7f, 0);
        if (!IsFinal)
            Add(Center(ExitX, ExitY, WH - 0.5f), new(0.2f, 1f, 0.35f), 9f, 0);
        else
            Add(Center(ExitX, ExitY, 3f), new(0.7f, 0.2f, 1f), 18f, 0.3f);
    }

    void SetAtmosphere()
    {
        float d = (Number - 1) / 29f;
        Ambient = Vector3.Lerp(new(0.16f, 0.17f, 0.19f), new(0.07f, 0.04f, 0.08f), d);
        FogColor = Vector3.Lerp(new(0.02f, 0.025f, 0.03f), new(0.03f, 0.01f, 0.035f), d);
        FogDensity = GMathL.Lerp(0.018f, 0.032f, d);
    }

    // ------------------------------------------------------------------ queries
    /// <summary>Ray vs. grid walls (with door slabs), floor and ceiling. Returns hit distance.</summary>
    public float Raycast(Vector3 o, Vector3 d, float maxDist, out Vector3 normal)
    {
        normal = Vector3.Zero;
        float tPlane = float.MaxValue;
        Vector3 planeN = Vector3.Zero;
        if (d.Y < -1e-6f) { tPlane = (0 - o.Y) / d.Y; planeN = Vector3.UnitY; }
        else if (d.Y > 1e-6f) { tPlane = (WH - o.Y) / d.Y; planeN = -Vector3.UnitY; }
        float limit = MathF.Min(maxDist, tPlane);

        int cx = (int)MathF.Floor(o.X / CS), cz = (int)MathF.Floor(o.Z / CS);
        int stepX = d.X > 0 ? 1 : -1, stepZ = d.Z > 0 ? 1 : -1;
        float tDX = MathF.Abs(d.X) > 1e-8f ? CS / MathF.Abs(d.X) : float.MaxValue;
        float tDZ = MathF.Abs(d.Z) > 1e-8f ? CS / MathF.Abs(d.Z) : float.MaxValue;
        float tMX = MathF.Abs(d.X) > 1e-8f ? (((d.X > 0 ? cx + 1 : cx) * CS) - o.X) / d.X : float.MaxValue;
        float tMZ = MathF.Abs(d.Z) > 1e-8f ? (((d.Z > 0 ? cz + 1 : cz) * CS) - o.Z) / d.Z : float.MaxValue;

        // starting inside a closed door cell
        float tEnter = 0;
        for (int guard = 0; guard < 512; guard++)
        {
            float tExit = MathF.Min(tMX, tMZ);
            // props standing in this cell
            var props = PropAt[Idx(cx, cz)];
            if (props != null)
            {
                float bestT = float.MaxValue;
                Vector3 bestN = Vector3.Zero;
                foreach (int pi in props)
                    if (!Props[pi].Dead && RayBox(o, d, Props[pi].Min, Props[pi].Max, out float tp, out var np) && tp < bestT) { bestT = tp; bestN = np; }
                if (bestT <= tExit && bestT <= limit) { normal = bestN; return bestT; }
            }
            // check current cell door slab
            var door = DoorIn(cx, cz);
            if (door != null && !door.Secret && door.Open < 0.9f)
            {
                float tSlab;
                if (door.AlongX) tSlab = MathF.Abs(d.X) > 1e-8f ? ((cx + 0.5f) * CS - o.X) / d.X : -1;
                else tSlab = MathF.Abs(d.Z) > 1e-8f ? ((cz + 0.5f) * CS - o.Z) / d.Z : -1;
                float hitY = o.Y + d.Y * tSlab;
                if (tSlab >= tEnter && tSlab <= tExit && tSlab <= limit && hitY > door.Open * WH)
                {
                    normal = door.AlongX ? new Vector3(-MathF.Sign(d.X), 0, 0) : new Vector3(0, 0, -MathF.Sign(d.Z));
                    return tSlab;
                }
            }
            Vector3 n;
            if (tMX < tMZ) { cx += stepX; tEnter = tMX; tMX += tDX; n = new Vector3(-stepX, 0, 0); }
            else { cz += stepZ; tEnter = tMZ; tMZ += tDZ; n = new Vector3(0, 0, -stepZ); }
            if (tEnter > limit) break;
            if (!In(cx, cz) || Cells[Idx(cx, cz)] == 0 || Crate[Idx(cx, cz)])
            {
                normal = n;
                return tEnter;
            }
            var sd = DoorIn(cx, cz);
            if (sd != null && sd.Secret && sd.Open < 0.9f && o.Y + d.Y * tEnter > sd.Open * WH)
            {
                normal = n;
                return tEnter;
            }
        }
        if (tPlane <= maxDist) { normal = planeN; return tPlane; }
        return maxDist;
    }

    public bool LineOfSight(Vector3 a, Vector3 b)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return true;
        return Raycast(a, d / len, len, out _) >= len - 0.05f;
    }

    /// <summary>Circle (square) vs grid collision test.</summary>
    public bool Blocked(float x, float z, float r, float y = 0)
    {
        int x0 = (int)MathF.Floor((x - r) / CS), x1 = (int)MathF.Floor((x + r) / CS);
        int z0 = (int)MathF.Floor((z - r) / CS), z1 = (int)MathF.Floor((z + r) / CS);
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                if (!In(cx, cz) || Cells[Idx(cx, cz)] == 0 || Crate[Idx(cx, cz)] || Barrier[Idx(cx, cz)]) return true;
                var props = PropAt[Idx(cx, cz)];
                if (props != null)
                    foreach (int pi in props)
                    {
                        var pr = Props[pi];
                        if (!pr.Dead && pr.Max.Y > y + 0.05f && x + r > pr.Min.X && x - r < pr.Max.X && z + r > pr.Min.Z && z - r < pr.Max.Z) return true;
                    }
                var door = DoorIn(cx, cz);
                if (door != null && door.Secret && door.Open < 0.9f) return true;
                if (door != null && door.Open < 0.9f)
                {
                    // door slab is a thin plane across the cell centre
                    float half = 0.3f;
                    if (door.AlongX) { float sx = (cx + 0.5f) * CS; if (x + r > sx - half && x - r < sx + half) return true; }
                    else { float sz = (cz + 0.5f) * CS; if (z + r > sz - half && z - r < sz + half) return true; }
                }
            }
        return false;
    }

    /// <summary>Slide-move an entity of radius r. Returns the new position.</summary>
    public Vector3 Move(Vector3 pos, Vector3 delta, float r)
    {
        // subdivide large moves
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Z)) / (r * 0.8f));
        steps = Math.Clamp(steps, 1, 16);
        var step = delta / steps;
        for (int i = 0; i < steps; i++)
        {
            if (!Blocked(pos.X + step.X, pos.Z, r, pos.Y)) pos.X += step.X;
            if (!Blocked(pos.X, pos.Z + step.Z, r, pos.Y)) pos.Z += step.Z;
        }
        pos.Y += delta.Y;
        return pos;
    }
}

static class GMathL
{
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
