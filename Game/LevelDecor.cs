using System.Numerics;

namespace AlienInvasion.Game;

public enum PropKind { Desk, Table, Console, ConsoleBank, ServerRack, Locker, Shelf, Bunk, Barrels, Tank, Pod, ExplosiveBarrel }

/// <summary>Solid furniture. Local frame: +Z faces into the room, X runs along the wall.</summary>
public class Prop
{
    public PropKind Kind;
    public Vector3 Pos;          // footprint centre on the floor
    public float Yaw;            // rotation so local +Z = facing
    public float Width, Depth, Height;
    public Vector3 Min, Max;     // world AABB
    public int Seed;
    // explosive barrels only
    public float Hp = ExplosiveHp, Fuse = -1;
    public bool Dead;
    public const float ExplosiveHp = 30f;
}

public enum RoomTheme { Office, Control, Server, Barracks, Storage, Lab, Hive }

public partial class Level
{
    public int SecretCount;
    public List<List<int>> SecretRooms = new();

    static (float w, float d, float h) Dims(PropKind k) => k switch
    {
        PropKind.Desk => (2.2f, 0.9f, 1.3f),
        PropKind.Table => (2.0f, 1.2f, 0.95f),
        PropKind.Console => (2.4f, 0.85f, 1.55f),
        PropKind.ConsoleBank => (1.8f, 1.8f, 1.3f),
        PropKind.ServerRack => (1.4f, 0.8f, 2.6f),
        PropKind.Locker => (2.6f, 0.6f, 2.2f),
        PropKind.Shelf => (2.8f, 0.7f, 2.0f),
        PropKind.Bunk => (2.4f, 0.9f, 1.7f),
        PropKind.Barrels => (1.5f, 1.5f, 1.2f),
        PropKind.Tank => (1.8f, 1.8f, 3.4f),
        PropKind.Pod => (1.5f, 1.5f, 1.3f),
        _ => (1, 1, 1)
    };

    RoomTheme PickTheme(Room r)
    {
        int n = Number;
        RoomTheme[] pool = n <= 7
            ? new[] { RoomTheme.Office, RoomTheme.Office, RoomTheme.Control, RoomTheme.Barracks, RoomTheme.Storage, RoomTheme.Server }
            : n <= 15
                ? new[] { RoomTheme.Storage, RoomTheme.Storage, RoomTheme.Control, RoomTheme.Server, RoomTheme.Office, RoomTheme.Barracks }
                : n <= 23
                    ? new[] { RoomTheme.Lab, RoomTheme.Lab, RoomTheme.Control, RoomTheme.Server, RoomTheme.Storage, RoomTheme.Office }
                    : new[] { RoomTheme.Hive, RoomTheme.Hive, RoomTheme.Hive, RoomTheme.Lab, RoomTheme.Storage, RoomTheme.Control };
        if (r == StartRoom) return n <= 15 ? RoomTheme.Office : RoomTheme.Control;
        return pool[rng.Next(pool.Length)];
    }

    PropKind WallKind(RoomTheme t) => t switch
    {
        RoomTheme.Office => rng.NextDouble() < 0.6 ? PropKind.Desk : rng.NextDouble() < 0.5 ? PropKind.Shelf : PropKind.Console,
        RoomTheme.Control => rng.NextDouble() < 0.75 ? PropKind.Console : PropKind.ServerRack,
        RoomTheme.Server => rng.NextDouble() < 0.8 ? PropKind.ServerRack : PropKind.Console,
        RoomTheme.Barracks => rng.NextDouble() < 0.55 ? PropKind.Bunk : PropKind.Locker,
        RoomTheme.Storage => rng.NextDouble() < 0.6 ? PropKind.Shelf : PropKind.Barrels,
        RoomTheme.Lab => rng.NextDouble() < 0.5 ? PropKind.Console : rng.NextDouble() < 0.5 ? PropKind.Desk : PropKind.Shelf,
        _ => rng.NextDouble() < 0.75 ? PropKind.Pod : PropKind.Barrels,
    };

    PropKind CenterKind(RoomTheme t) => t switch
    {
        RoomTheme.Office => PropKind.Table,
        RoomTheme.Control => rng.NextDouble() < 0.7 ? PropKind.ConsoleBank : PropKind.Table,
        RoomTheme.Server => PropKind.ConsoleBank,
        RoomTheme.Barracks => PropKind.Table,
        RoomTheme.Storage => PropKind.Barrels,
        RoomTheme.Lab => rng.NextDouble() < 0.6 ? PropKind.Tank : PropKind.Table,
        _ => rng.NextDouble() < 0.8 ? PropKind.Pod : PropKind.Tank,
    };

    void AddProp(PropKind k, int cx, int cy, int fx, int fy)
    {
        var (w, d, h) = Dims(k);
        var c = Center(cx, cy);
        bool wall = fx != 0 || fy != 0;
        Vector3 pos;
        float yaw;
        Vector3 ext;
        if (wall)
        {
            // pushed against the wall on the side opposite the facing direction
            pos = c - new Vector3(fx, 0, fy) * (CS / 2 - d / 2 - 0.05f);
            yaw = MathF.Atan2(fx, fy);
            ext = fx != 0 ? new Vector3(d / 2, 0, w / 2) : new Vector3(w / 2, 0, d / 2);
        }
        else
        {
            pos = c;
            bool rot = rng.Next(2) == 0;
            yaw = rot ? MathF.PI / 2 : 0;
            ext = rot ? new Vector3(d / 2, 0, w / 2) : new Vector3(w / 2, 0, d / 2);
        }
        var p = new Prop
        {
            Kind = k, Pos = pos, Yaw = yaw, Width = w, Depth = d, Height = h, Seed = rng.Next(),
            Min = pos - ext, Max = pos + ext + new Vector3(0, h, 0)
        };
        int i = Idx(cx, cy);
        (PropAt[i] ??= new List<int>()).Add(Props.Count);
        Props.Add(p);

        // light-emitting props
        if (k == PropKind.Tank) Lights.Add(new LightDef { Pos = pos + new Vector3(0, 1.6f, 0), Color = new(0.3f, 1f, 0.5f), Radius = 6f });
        else if (k == PropKind.ConsoleBank) Lights.Add(new LightDef { Pos = pos + new Vector3(0, 1.6f, 0), Color = new(0.25f, 0.55f, 1f), Radius = 5f });
        else if (k == PropKind.Pod && rng.NextDouble() < 0.5) Lights.Add(new LightDef { Pos = pos + new Vector3(0, 1.0f, 0), Color = new Vector3(0.4f, 1f, 0.3f) * 0.7f, Radius = 4.5f, Flicker = 0.3f });
    }

    /// <summary>A small group of red explosive barrels against a wall (or around the cell centre).</summary>
    void AddExplosives(int cx, int cy, int fx, int fy)
    {
        var c = Center(cx, cy);
        bool wall = fx != 0 || fy != 0;
        var into = new Vector3(fx, 0, fy);
        var side = new Vector3(-fy, 0, fx);
        int n = rng.Next(1, 4);
        Vector3[] spots = wall
            ? new[] { -into * (CS / 2 - 0.5f), -into * (CS / 2 - 0.5f) + side * 0.8f, -into * (CS / 2 - 1.25f) + side * 0.4f }
            : new[] { new Vector3(-0.4f, 0, -0.3f), new Vector3(0.45f, 0, 0.1f), new Vector3(-0.2f, 0, 0.55f) };
        int i = Idx(cx, cy);
        for (int k = 0; k < n; k++)
        {
            var pos = c + spots[k] + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.15f;
            var ext = new Vector3(0.34f, 0, 0.34f);
            (PropAt[i] ??= new List<int>()).Add(Props.Count);
            Props.Add(new Prop
            {
                Kind = PropKind.ExplosiveBarrel, Pos = pos, Yaw = (float)rng.NextDouble() * MathF.Tau, Width = 0.68f, Depth = 0.68f, Height = 1.1f,
                Seed = rng.Next(), Min = pos - ext, Max = pos + ext + new Vector3(0, 1.1f, 0)
            });
        }
    }

    /// <summary>Furnish rooms. Wall props never block entrances and leave the cell centre free;
    /// centre props are only kept if the level stays fully connected.</summary>
    void PlaceProps()
    {
        int floorCount = Cells.Count(c => c != 0);
        int Blocked0() => Crate.Count(c => c) + PropBlock.Count(c => c);
        foreach (var r in Rooms)
        {
            var theme = PickTheme(r);
            bool arena = IsFinal && r == ExitRoom;
            double wallChance = arena ? 0.25 : r == StartRoom ? 0.3 : 0.45;
            for (int y = r.Y; y < r.Y + r.H; y++)
                for (int x = r.X; x < r.X + r.W; x++)
                {
                    int i = Idx(x, y);
                    if (Crate[i]) continue;
                    // entrance cells (a corridor opens here) stay clear
                    bool entrance = false;
                    var walls = new List<(int, int)>();
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + DX[k], ny = y + DY[k];
                        if (r.Contains(nx, ny)) continue;
                        if (Floor(nx, ny)) entrance = true;
                        else walls.Add((-DX[k], -DY[k]));   // facing back into the room
                    }
                    if (entrance || walls.Count == 0) continue;
                    if (rng.NextDouble() > wallChance) continue;
                    var (fx, fy) = walls[rng.Next(walls.Count)];
                    var kind = arena ? PropKind.Pod : WallKind(theme);
                    // red explosive barrels: some barrel stacks, plus the odd group in any room
                    if (!arena && r != StartRoom && (kind == PropKind.Barrels ? rng.NextDouble() < 0.4 : rng.NextDouble() < 0.03))
                        AddExplosives(x, y, fx, fy);
                    else AddProp(kind, x, y, fx, fy);
                }

            if (r == StartRoom || r.W < 4 || r.H < 4) continue;
            for (int y = r.Y + 1; y < r.Y + r.H - 1; y++)
                for (int x = r.X + 1; x < r.X + r.W - 1; x++)
                {
                    int i = Idx(x, y);
                    if (Crate[i] || PropAt[i] != null || rng.NextDouble() > 0.17) continue;
                    if (Math.Abs(x - ExitX) + Math.Abs(y - ExitY) <= 2) continue;
                    if (arena && Math.Abs(x - ExitX) <= 4 && Math.Abs(y - ExitY) <= 4) continue;
                    PropBlock[i] = true;
                    if (Reachable(StartX, StartY, null).Count != floorCount - Blocked0()) { PropBlock[i] = false; continue; }
                    var ck = arena ? PropKind.Pod : CenterKind(theme);
                    if (ck == PropKind.Barrels && rng.NextDouble() < 0.4) AddExplosives(x, y, 0, 0);
                    else AddProp(ck, x, y, 0, 0);
                }
        }
    }

    /// <summary>Carves small hidden stash rooms behind fake wall panels.</summary>
    void PlaceSecrets()
    {
        int want = Math.Min(4, 1 + Number / 10 + (rng.NextDouble() < 0.5 ? 1 : 0));
        var floors = new List<int>();
        for (int i = 0; i < Cells.Length; i++)
            if (Cells[i] != 0 && DoorAt[i] < 0 && !Crate[i] && !PropBlock[i] && i != Idx(ExitX, ExitY)) floors.Add(i);
        for (int attempt = 0; attempt < 1500 && SecretCount < want; attempt++)
        {
            int f = floors[rng.Next(floors.Count)];
            int fx0 = f % W, fy0 = f / W;
            int k = rng.Next(4);
            int dx = DX[k], dy = DY[k];
            int ddx = fx0 + dx, ddy = fy0 + dy;            // the fake wall cell
            if (ddx < 2 || ddy < 2 || ddx >= W - 2 || ddy >= H - 2 || Cells[Idx(ddx, ddy)] != 0) continue;
            // don't hide a wall panel right behind a prop
            if (PropAt[f] != null) continue;
            int px = -dy, py = dx;                          // perpendicular
            if (Floor(ddx + px, ddy + py) || Floor(ddx - px, ddy - py)) continue;
            int depth = rng.Next(2, 4), span = rng.Next(2, 4), off = rng.Next(span);
            var cells = new List<int>();
            bool ok = true;
            for (int a = 1; a <= depth && ok; a++)
                for (int b = -off; b < span - off && ok; b++)
                {
                    int cx = ddx + dx * a + px * b, cy = ddy + dy * a + py * b;
                    if (cx < 2 || cy < 2 || cx >= W - 2 || cy >= H - 2) { ok = false; break; }
                    cells.Add(Idx(cx, cy));
                }
            if (!ok) continue;
            // the room plus a one-cell margin must be solid rock (except the fake wall itself)
            foreach (int c in cells)
            {
                int cx = c % W, cy = c / W;
                for (int oy = -1; oy <= 1 && ok; oy++)
                    for (int ox = -1; ox <= 1 && ok; ox++)
                    {
                        int nx = cx + ox, ny = cy + oy;
                        if (nx == ddx && ny == ddy) continue;
                        if (!In(nx, ny) || Cells[Idx(nx, ny)] != 0) ok = false;
                    }
            }
            if (!ok) continue;

            foreach (int c in cells) { Cells[c] = 1; SecretCell[c] = true; }
            int di = Idx(ddx, ddy);
            Cells[di] = 1;
            SecretCell[di] = true;
            DoorAt[di] = Doors.Count;
            Doors.Add(new Door { X = ddx, Y = ddy, Secret = true, AlongX = dx != 0 });
            SecretRooms.Add(cells);
            SecretCount++;
            StockSecret(cells, SecretCount == 1);
        }
    }

    void StockSecret(List<int> cells, bool first)
    {
        var order = cells.OrderBy(_ => rng.Next()).ToList();
        int slot = 0;
        void Put(PickupKind kind, int arg = 0)
        {
            // one item per cell; if the stash is small, items double up in a cell (spread apart by the world)
            int c = order[slot % order.Count];
            slot++;
            Items.Add(new ItemSpawn { Kind = kind, Arg = arg, X = c % W, Y = c / W });
        }
        // the goodie goes in first so it is never squeezed out
        // goggles: guaranteed in the first stash of sublevels 2 (night vision) and 5 (x-ray), then a regular find
        double g = rng.NextDouble();
        if (first && Number == 2) Put(PickupKind.NightVision);
        else if (first && Number == 5) Put(PickupKind.XRay);
        else if (Number >= 3 && g < 0.32) Put(PickupKind.Upgrade, Weapons.Pistol);   // the world swaps in a weapon the player owns
        else if (Number >= 2 && g < 0.42) Put(PickupKind.NightVision);
        else if (Number >= 5 && g < 0.52) Put(PickupKind.XRay);
        else if (g < 0.7) Put(PickupKind.DamageAmp);
        else if (g < 0.82) Put(PickupKind.Backpack);
        else
        {
            var pool = new List<int> { Weapons.Shotgun };
            if (Number >= 2) pool.Add(Weapons.Rifle);
            if (Number >= 5) pool.Add(Weapons.GrenadeLauncher);
            if (Number >= 9) pool.Add(Weapons.RocketLauncher);
            if (Number >= 6) pool.Add(Weapons.Plasma);
            if (Number >= 14) pool.Add(Weapons.ArcCaster);
            Put(PickupKind.Weapon, pool[rng.Next(pool.Count)]);
        }
        Put(rng.NextDouble() < 0.3 ? PickupKind.MegaHealth : PickupKind.Medkit);
        Put(rng.NextDouble() < 0.6 ? PickupKind.ArmorVest : PickupKind.Armor);
        var ammo = new List<PickupKind> { PickupKind.Bullets, PickupKind.Shells };
        if (Number >= 5) ammo.Add(PickupKind.Cells);
        if (Number >= 6) ammo.Add(PickupKind.Grenades);
        if (Number >= 9) ammo.Add(PickupKind.Rockets);
        for (int i = 0; i < 2; i++) Put(ammo[rng.Next(ammo.Count)]);
        if (Number >= 2 && rng.NextDouble() < 0.6) Put(PickupKind.Battery);
        // a dim warm light so the stash is visible once opened
        int mid = cells[cells.Count / 2];
        Lights.Add(new LightDef { Pos = Center(mid % W, mid / W, WH - 0.4f), Color = new Vector3(1f, 0.8f, 0.45f) * 0.8f, Radius = 7f });
    }

    public static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t, out Vector3 n)
    {
        float tmin = 0, tmax = float.MaxValue;
        int axis = -1;
        float sign = 0;
        t = 0;
        n = Vector3.Zero;
        for (int a = 0; a < 3; a++)
        {
            float oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z;
            float da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            float mn = a == 0 ? min.X : a == 1 ? min.Y : min.Z;
            float mx = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-8f) { if (oa < mn || oa > mx) return false; continue; }
            float t1 = (mn - oa) / da, t2 = (mx - oa) / da;
            float s = -1;
            if (t1 > t2) { (t1, t2) = (t2, t1); s = 1; }
            if (t1 > tmin) { tmin = t1; axis = a; sign = s; }
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        if (axis >= 0) n = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
        return true;
    }
}
