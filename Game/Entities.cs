using System.Numerics;

namespace AlienInvasion.Game;

public class Player
{
    public Vector3 Pos;            // feet
    public Vector3 Vel;
    public float Yaw, Pitch;
    public float Health = 100, Armor = 0;
    public int[] Ammo = new int[5];
    public bool[] Has = new bool[Weapons.Count];
    public bool[] Upgraded = new bool[Weapons.Count];
    public int Weapon = Weapons.Pistol, Pending = Weapons.Pistol;
    public float SwitchT;           // 0 = raised; >0 lowering/raising
    public bool[] Keys = new bool[4];
    public float Cooldown, Recoil, Bob, BobAmount, HurtFlash, PickupFlash, Shake, KnifeT, FireFlash;
    public float ZoomT;             // 0..1, scope zoom-in (sniper rifle)
    public float Heat;   // 0..1, builds up with sustained fire; the barrel glows and shimmers
    public bool Flashlight = true;
    public bool Backpack;           // +50% ammo capacity
    public float AmpT;              // damage amplifier time left
    // goggles: charge is seconds of use left; one full charge lasts a few sublevels
    public const float GoggleMax = 900f, BatteryCharge = 450f;
    public bool HasNV, HasXRay, NVOn, XRayOn;
    public float NVCharge, XRayCharge;
    public bool Dead;
    public float StepAccum;
    // story progress for the run
    public List<int> Logs = new();  // data pads read (indexes into Story.Logs)
    public int SeenMask, KilledMask; // alien types seen (radio comment played) / killed (xeno file unlocked)
    public bool ReactorScrammed;
    public const float Radius = 0.4f, EyeHeight = 1.65f;

    public Vector3 Eye => Pos + new Vector3(0, EyeHeight + MathF.Sin(Bob * 2) * 0.05f * BobAmount, 0);
    public Vector3 Forward => Engine.GMath.DirFromYawPitch(Yaw, Pitch);
    public Vector3 Flat => new(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
    public Vector3 Right => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));

    public int MaxAmmo(int t) => Backpack ? Weapons.MaxAmmo[t] * 3 / 2 : Weapons.MaxAmmo[t];

    public void ResetNew()
    {
        Backpack = false;
        AmpT = ZoomT = 0;
        HasNV = HasXRay = NVOn = XRayOn = false;
        NVCharge = XRayCharge = 0;
        Health = 100; Armor = 0;
        Array.Clear(Ammo);
        Array.Clear(Has);
        Array.Clear(Upgraded);
        Logs.Clear();
        SeenMask = KilledMask = 0;
        ReactorScrammed = false;
        Has[Weapons.Knife] = Has[Weapons.Pistol] = true;
        Ammo[Weapons.Bullets] = 60;
        Weapon = Pending = Weapons.Pistol;
    }

    public PlayerSnapshot Snapshot() => new()
    {
        Health = Health, Armor = Armor, Ammo = (int[])Ammo.Clone(), Has = (bool[])Has.Clone(), Weapon = Weapon, Backpack = Backpack,
        HasNV = HasNV, HasXRay = HasXRay, NVCharge = NVCharge, XRayCharge = XRayCharge, Upgraded = (bool[])Upgraded.Clone(),
        Logs = Logs.ToArray(), SeenMask = SeenMask, KilledMask = KilledMask, ReactorScrammed = ReactorScrammed
    };

    public void Restore(PlayerSnapshot s)
    {
        Health = MathF.Max(s.Health, 50); Armor = s.Armor;
        Ammo = (int[])s.Ammo.Clone();
        // saves from before a weapon was added have shorter arrays
        Has = new bool[Weapons.Count]; Array.Copy(s.Has, Has, Math.Min(s.Has.Length, Weapons.Count));
        Weapon = Pending = s.Weapon;
        Backpack = s.Backpack;
        Upgraded = new bool[Weapons.Count];
        if (s.Upgraded != null) Array.Copy(s.Upgraded, Upgraded, Math.Min(s.Upgraded.Length, Weapons.Count));
        ZoomT = 0;
        Logs = s.Logs?.Where(i => i >= 0 && i < Story.Logs.Length).Distinct().ToList() ?? new();
        SeenMask = s.SeenMask; KilledMask = s.KilledMask; ReactorScrammed = s.ReactorScrammed;
        HasNV = s.HasNV; HasXRay = s.HasXRay; NVCharge = s.NVCharge; XRayCharge = s.XRayCharge;
        NVOn = XRayOn = false;
        if (Weapon < 0 || Weapon >= Weapons.Count || !Has[Weapon]) Weapon = Pending = Weapons.Pistol;
    }
}

public class PlayerSnapshot
{
    public int Level { get; set; } = 1;
    public float Health { get; set; } = 100;
    public float Armor { get; set; }
    public int[] Ammo { get; set; } = new int[5];
    public bool[] Has { get; set; } = new bool[Weapons.Count];
    public int Weapon { get; set; } = Weapons.Pistol;
    public bool Backpack { get; set; }
    public bool HasNV { get; set; }
    public bool HasXRay { get; set; }
    public float NVCharge { get; set; }
    public float XRayCharge { get; set; }
    public bool[] Upgraded { get; set; } = new bool[Weapons.Count];
    public int[] Logs { get; set; } = Array.Empty<int>();
    public int SeenMask { get; set; }
    public int KilledMask { get; set; }
    public bool ReactorScrammed { get; set; }
}

public enum EState { Patrol, Idle, Chase, Windup, Pain, Dead }

public class Enemy
{
    public int Type;
    public EnemyDef D;
    public Vector3 Pos, Vel;
    public float Yaw, Hp, MaxHp;
    public EState State = EState.Idle;
    public float StateT, Anim, AttackCd, SightT, React, LostT, PainFlash, DeathT, BurstT;
    public int BurstLeft;
    public bool Alerted, CanSee, RangedWindup;
    public Vector3 LastKnown;
    public List<(int x, int y)> Path = new();
    public int PathIdx;
    public float StrafeDir = 1, StrafeT;
    public float Cloak;          // stalker invisibility 0..1
    public float MoveSpeed;
    public float SpawnT;         // portal spawn-in effect
    public float Seed;
    public bool Invulnerable;
    public bool Gibbed;              // blown apart: no corpse, chunks instead
    public bool Burrowed, OnCeiling, Dropping;
    public float SpecialT, SpecialCd, AltCd, DropVel;
    public int Mode;                 // boss sub-state
    public Vector3 ChargeDir;
    public bool Hidden => Burrowed;  // underground: can't be seen, shot or hurt
    public float LastHit;            // size of the most recent hit
    public bool LastHitExplosive;
    public Vector3 DeathVel;         // launch velocity for the ragdoll
    public Ragdoll Rag;              // built by the renderer from the pose at death

    public bool Dead => State == EState.Dead;
    public float Bottom => Pos.Y;
    public Vector3 Mid => Pos + new Vector3(0, D.Height * 0.55f, 0);
    public Vector3 EyePos => Pos + new Vector3(0, D.Height * 0.85f, 0);
}

/// <summary>Flat mark on a wall or floor. Kind: 0 bullet hole, 1 scorch, 2 blood.</summary>
public struct Decal
{
    public Vector3 Pos, N;
    public float Size, Rot, Life;
    public Vector4 Color;
    public int Kind;
}

/// <summary>A piece of a blown-apart alien that tumbles, bounces and settles.</summary>
/// <summary>Expanding screen-distortion ring from an explosion.</summary>
public class Shockwave
{
    public Vector3 Pos;
    public float Age, Life, Size, Strength;
}

public class Chunk
{
    public Vector3 Pos, Vel, Axis, Scale, Color;
    public float Angle, Spin, Life;
    public int Shape;                // 0 lump (sphere), 1 limb (cylinder)
    public bool Resting, Bled;
}

public class Projectile
{
    public ProjKind Kind;
    public Vector3 Pos, Vel;
    public float Damage, Splash, Life, Radius = 0.15f, Gravity;
    public bool FromPlayer, Cluster;
    public int Bounces;
    public float Trail;
}

public class Pickup
{
    public PickupKind Kind;
    public int Arg;
    public Vector3 Pos;
    public float Phase;
    public bool Taken;
    public bool Dropped;
    public Vector3 Vel;
}

public struct Particle
{
    public Vector3 Pos, Vel;
    public Vector4 Color;
    public float Size, Grow, Life, MaxLife, Gravity, Drag;
    public bool Additive, Smoke;
}

public struct Beam
{
    public Vector3 A, B;
    public Vector4 Color;
    public float Width, Life, MaxLife;
}

public struct DynLight
{
    public Vector3 Pos, Color;
    public float Radius, Life, MaxLife;
}
