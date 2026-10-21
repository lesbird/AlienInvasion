using System.Numerics;

namespace AlienInvasion.Game;

public enum HazardKind { Steam, Cable, Acid }

/// <summary>Environmental hazard. Steam vents blast periodically, cables spark, acid pools burn.</summary>
public class Hazard
{
    public HazardKind Kind;
    public Vector3 Pos;          // floor point (cable: the frayed end)
    public Vector3 N;            // cable: wall normal
    public float Radius, Phase;
    public float HurtCd;         // runtime
}

/// <summary>Wall grate that aliens crawl out of. Can be shot to seal it.</summary>
public class Vent
{
    public int X, Y, Room;
    public Vector3 Pos, N;       // centre of the grate on the wall face, facing into the room
    public const float MaxHp = 60;
    public float Hp = MaxHp, Cd;
    public int Spawns;
    public bool Broken;
    public Vector3 Min => Pos - new Vector3(MathF.Abs(N.Z) * 0.5f + 0.06f, 0.32f, MathF.Abs(N.X) * 0.5f + 0.06f);
    public Vector3 Max => Pos + new Vector3(MathF.Abs(N.Z) * 0.5f + 0.06f, 0.32f, MathF.Abs(N.X) * 0.5f + 0.06f);
}

/// <summary>Ambush room: energy barriers seal the entrances and aliens pour out of the vents.</summary>
public class Lockdown
{
    public int Room;
    public readonly List<(int x, int y, int dx, int dy)> Barriers = new();
    public int State;            // 0 waiting, 1 active, 2 cleared
    public int Spawned, Total;
    public float SpawnT, IdleT;  // IdleT: time since the last wave kill once everything has spawned
    public int LastAlive;
    public readonly List<Enemy> Wave = new();
}

public partial class Level
{
    public readonly List<Hazard> Hazards = new();
    public readonly List<Vent> Vents = new();
    public readonly List<Lockdown> Lockdowns = new();
    public bool[] Barrier;
    public bool PowerFailure, SelfDestruct;

    bool Busy(int i) => Crate[i] || PropBlock[i] || PropAt[i] != null || DoorAt[i] >= 0 || SecretCell[i] || i == Idx(ExitX, ExitY) || i == Idx(StartX, StartY)
        || Items.Any(it => Idx(it.X, it.Y) == i);

    /// <summary>Vents, hazards, lockdown rooms and level events. Runs after secrets so the walls are final.</summary>
    void PlaceHazards()
    {
        bool boss = Number % 5 == 0 && !IsFinal;
        PowerFailure = Number >= 6 && Number < 28 && !boss && rng.NextDouble() < 0.4;
        SelfDestruct = Number == 28 || Number == 29;

        // ---- vents ----
        double ventChance = Math.Min(0.6, 0.25 + Number * 0.015);
        for (int ri = 0; ri < Rooms.Count; ri++)
        {
            var r = Rooms[ri];
            if (r == StartRoom) continue;
            int want = rng.NextDouble() < ventChance ? 1 + (rng.NextDouble() < 0.3 ? 1 : 0) : 0;
            for (int k = 0; k < want; k++) TryAddVent(ri);
        }

        // ---- lockdown ambush rooms ----
        if (Number >= 3 && !IsFinal)
        {
            int count = Number >= 15 ? 2 : 1;
            var cands = Enumerable.Range(0, Rooms.Count).Where(i => Rooms[i] != StartRoom && Rooms[i] != ExitRoom && Rooms[i].W * Rooms[i].H >= 20).OrderBy(_ => rng.Next()).ToList();
            foreach (int ri in cands.Take(count))
            {
                while (Vents.Count(v => v.Room == ri) < 3 && TryAddVent(ri)) { }
                if (Vents.Count(v => v.Room == ri) < 2) continue;
                var r = Rooms[ri];
                var ld = new Lockdown { Room = ri, Total = 5 + Number / 3 };
                for (int y = r.Y; y < r.Y + r.H; y++)
                    for (int x = r.X; x < r.X + r.W; x++)
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + DX[k], ny = y + DY[k];
                            if (r.Contains(nx, ny) || !Floor(nx, ny) || SecretCell[Idx(nx, ny)]) continue;
                            ld.Barriers.Add((nx, ny, -DX[k], -DY[k]));
                        }
                if (ld.Barriers.Count > 0) Lockdowns.Add(ld);
            }
        }

        // ---- hazards ----
        var floors = new List<int>();
        for (int i = 0; i < Cells.Length; i++)
            if (Cells[i] != 0 && !Busy(i) && !StartRoom.Contains(i % W, i / W)) floors.Add(i);
        int Pick() { if (floors.Count == 0) return -1; int j = rng.Next(floors.Count); int c = floors[j]; floors.RemoveAt(j); return c; }

        if (Number >= 2)
            for (int k = 0; k < 1 + Number / 6; k++)
            {
                int c = Pick(); if (c < 0) break;
                var p = Center(c % W, c / W) + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 1.4f;
                Hazards.Add(new Hazard { Kind = HazardKind.Steam, Pos = p, Radius = 0.9f, Phase = (float)rng.NextDouble() * 6 });
            }
        if (Number >= 4)
            for (int k = 0; k < 1 + Number / 8; k++)
            {
                int c = Pick(); if (c < 0) break;
                int x = c % W, y = c / W;
                var walls = Enumerable.Range(0, 4).Where(d => !Floor(x + DX[d], y + DY[d])).ToList();
                if (walls.Count == 0) { k--; continue; }
                int dd = walls[rng.Next(walls.Count)];
                var n = new Vector3(-DX[dd], 0, -DY[dd]);
                var p = Center(x, y, 1.1f) - n * (CS / 2 - 0.45f) + new Vector3(n.Z, 0, -n.X) * ((float)rng.NextDouble() - 0.5f) * 1.6f;
                Hazards.Add(new Hazard { Kind = HazardKind.Cable, Pos = p, N = n, Radius = 1.0f, Phase = (float)rng.NextDouble() * 6 });
                Lights.Add(new LightDef { Pos = p + n * 0.3f, Color = new Vector3(0.55f, 0.65f, 1.1f) * 0.8f, Radius = 4.5f, Flicker = 0.95f });
            }
        if (Number >= 16)
            for (int k = 0; k < Number / 5; k++)
            {
                int c = Pick(); if (c < 0) break;
                if (!RoomCell[c]) continue;
                var p = Center(c % W, c / W) + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.6f;
                float rad = 1.0f + (float)rng.NextDouble() * 0.6f;
                Hazards.Add(new Hazard { Kind = HazardKind.Acid, Pos = p, Radius = rad, Phase = (float)rng.NextDouble() * 6 });
                Lights.Add(new LightDef { Pos = p + new Vector3(0, 0.5f, 0), Color = new Vector3(0.35f, 1f, 0.2f) * 0.6f, Radius = 4.5f, Flicker = 0.1f });
            }
    }

    bool TryAddVent(int ri)
    {
        var r = Rooms[ri];
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int x = r.X + rng.Next(r.W), y = r.Y + rng.Next(r.H);
            int i = Idx(x, y);
            if (Crate[i] || PropAt[i] != null || i == Idx(ExitX, ExitY) || Vents.Any(v => v.X == x && v.Y == y)) continue;
            var dirs = Enumerable.Range(0, 4).Where(k => !Floor(x + DX[k], y + DY[k]) && In(x + DX[k], y + DY[k])).ToList();
            if (dirs.Count == 0) continue;
            int d = dirs[rng.Next(dirs.Count)];
            var n = new Vector3(-DX[d], 0, -DY[d]);
            Vents.Add(new Vent { X = x, Y = y, Room = ri, N = n, Pos = Center(x, y) - n * (CS / 2 - 0.03f) + new Vector3(0, 0.5f, 0), Spawns = 1 + rng.Next(2) });
            return true;
        }
        return false;
    }
}
