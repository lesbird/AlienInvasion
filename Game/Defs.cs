using System.Numerics;

namespace AlienInvasion.Game;

public enum FireMode { Melee, Hitscan, Projectile, Arc }

public enum ProjKind { None, Grenade, Rocket, Plasma, Acid, Void, EnemyAcid, EnemyPlasma, EnemyOrb, BruteOrb }

public class WeaponDef
{
    public string Name;
    public int Ammo = -1;          // AmmoType index, -1 = none
    public int AmmoPerShot = 1;
    public float Rate;             // shots per second
    public float Damage;
    public int Pellets = 1;
    public float Spread;
    public FireMode Mode;
    public ProjKind Proj;
    public float ProjSpeed;
    public float Splash;
    public string Sound;
    public bool Alien;
    public float Recoil = 0.05f;
    public Vector3 Flash = new(1f, 0.8f, 0.4f);
    public int PickupAmmo;
    public float Range = 200;
    public float Noise = 25;
    public int Chains = 3;         // arc caster jumps
    public bool Twin, Cluster;     // upgrade specials

    public WeaponDef Clone() => (WeaponDef)MemberwiseClone();
}

public static class Weapons
{
    public const int Knife = 0, Pistol = 1, Shotgun = 2, Rifle = 3, GrenadeLauncher = 4, RocketLauncher = 5,
        Plasma = 6, BioLauncher = 7, ArcCaster = 8, VoidCannon = 9, Count = 10;

    public const int Bullets = 0, Shells = 1, Grenades = 2, Rockets = 3, Cells = 4;
    public static readonly int[] MaxAmmo = { 300, 60, 40, 30, 400 };
    public static readonly string[] AmmoNames = { "BULLETS", "SHELLS", "GRENADES", "ROCKETS", "CELLS" };
    public static readonly int[] AmmoPickup = { 30, 8, 5, 4, 40 };

    /// <summary>Secret-stash upgrade for each weapon (same order as <see cref="All"/>).</summary>
    public static readonly string[] UpgradeNames =
    {
        "VIBRO-BLADE", "MAGNUM ROUNDS", "DRAGON'S BREATH SHELLS", "RAPID FIRE MOD", "CLUSTER GRENADES",
        "TWIN ROCKETS", "OVERCHARGED CELLS", "CORROSIVE MIX", "CHAIN STORM", "STABILISED CORE",
    };

    static WeaponDef[] upgraded;
    /// <summary>The weapon's stats, with its upgrade applied when the player has it.</summary>
    public static WeaponDef Get(int w, bool up) => up ? (upgraded ??= All.Select((d, i) => Upgrade(i, d.Clone())).ToArray())[w] : All[w];

    static WeaponDef Upgrade(int i, WeaponDef d)
    {
        switch (i)
        {
            case Knife: d.Damage *= 2; d.Rate *= 1.3f; break;
            case Pistol: d.Damage = 30; d.Rate *= 1.15f; break;
            case Shotgun: d.Pellets = 10; d.Damage = 14; d.Flash = new(1f, 0.55f, 0.2f); break;
            case Rifle: d.Rate = 15; d.Spread *= 0.55f; break;
            case GrenadeLauncher: d.Cluster = true; break;
            case RocketLauncher: d.Twin = true; break;
            case Plasma: d.Damage = 36; d.ProjSpeed = 62; break;
            case BioLauncher: d.Splash *= 1.6f; d.Damage *= 1.3f; break;
            case ArcCaster: d.Chains = 6; d.Range = 32; break;
            case VoidCannon: d.AmmoPerShot = 10; d.Rate = 0.9f; break;
        }
        return d;
    }

    public static readonly WeaponDef[] All =
    {
        new() { Name = "COMBAT KNIFE", Rate = 2.2f, Damage = 32, Mode = FireMode.Melee, Sound = "knife", Range = 2.4f, Noise = 0, Recoil = 0 },
        new() { Name = "M9 PISTOL", Ammo = Bullets, Rate = 3.6f, Damage = 16, Spread = 0.012f, Mode = FireMode.Hitscan, Sound = "pistol", Recoil = 0.06f, PickupAmmo = 30 },
        new() { Name = "COMBAT SHOTGUN", Ammo = Shells, Rate = 1.1f, Damage = 11, Pellets = 8, Spread = 0.075f, Mode = FireMode.Hitscan, Sound = "shotgun", Recoil = 0.16f, PickupAmmo = 12 },
        new() { Name = "ASSAULT RIFLE", Ammo = Bullets, Rate = 10f, Damage = 13, Spread = 0.03f, Mode = FireMode.Hitscan, Sound = "rifle", Recoil = 0.035f, PickupAmmo = 50 },
        new() { Name = "GRENADE LAUNCHER", Ammo = Grenades, Rate = 1.4f, Damage = 100, Splash = 4.5f, Mode = FireMode.Projectile, Proj = ProjKind.Grenade, ProjSpeed = 22, Sound = "gl", Recoil = 0.12f, PickupAmmo = 8 },
        new() { Name = "ROCKET LAUNCHER", Ammo = Rockets, Rate = 1.15f, Damage = 140, Splash = 4.5f, Mode = FireMode.Projectile, Proj = ProjKind.Rocket, ProjSpeed = 30, Sound = "rocket", Recoil = 0.15f, PickupAmmo = 6 },
        new() { Name = "XENO PLASMA RIFLE", Ammo = Cells, Rate = 9f, Damage = 24, Mode = FireMode.Projectile, Proj = ProjKind.Plasma, ProjSpeed = 48, Sound = "plasma", Alien = true, Recoil = 0.03f, Flash = new(0.3f, 0.8f, 1f), PickupAmmo = 60 },
        new() { Name = "BIO-ACID LAUNCHER", Ammo = Cells, AmmoPerShot = 2, Rate = 2.6f, Damage = 45, Splash = 2.6f, Mode = FireMode.Projectile, Proj = ProjKind.Acid, ProjSpeed = 30, Sound = "acidfire", Alien = true, Recoil = 0.08f, Flash = new(0.5f, 1f, 0.2f), PickupAmmo = 60 },
        new() { Name = "ARC CASTER", Ammo = Cells, Rate = 12f, Damage = 12, Mode = FireMode.Arc, Sound = "zap", Alien = true, Recoil = 0.01f, Flash = new(0.5f, 0.6f, 1f), PickupAmmo = 80, Range = 24 },
        new() { Name = "VOID CANNON", Ammo = Cells, AmmoPerShot = 15, Rate = 0.6f, Damage = 400, Splash = 7f, Mode = FireMode.Projectile, Proj = ProjKind.Void, ProjSpeed = 18, Sound = "void", Alien = true, Recoil = 0.3f, Flash = new(0.8f, 0.3f, 1f), PickupAmmo = 60 },
    };
}

public class EnemyDef
{
    public string Name;
    public float Hp, Speed, Chase, Radius, Height, FlyHeight;
    public float MeleeDmg, MeleeRange = 0.9f, MeleeCooldown = 1f;
    public ProjKind Ranged = ProjKind.None;
    public float RangedDmg, ProjSpeed, RangedCooldown = 2f, RangeMin, RangeMax, Splash;
    public int Burst = 1;
    public int DropWeapon = -1;
    public float DropChance;
    public float PainChance = 0.5f;
    public string Growl = "growl", Death = "adeath";
    public Vector3 Color;
    public bool Stationary;
    public bool Boss;
}

public static class EnemyDefs
{
    public const int Skitter = 0, Grunt = 1, Spitter = 2, Trooper = 3, Stalker = 4, Wraith = 5, Brute = 6, Overseer = 7, Pylon = 8, Portal = 9,
        Bloater = 10, Burrower = 11, Crawler = 12, Queen = 13, Warlord = 14, Mind = 15;
    /// <summary>Types the level generator scatters around (no stationary objects or bosses).</summary>
    public static readonly int[] Roaming = { Skitter, Grunt, Spitter, Trooper, Stalker, Wraith, Brute, Overseer, Bloater, Burrower, Crawler };
    public static readonly int[] Bosses = { Queen, Warlord, Mind };

    public static readonly EnemyDef[] All =
    {
        new() { Name = "SKITTER", Hp = 30, Speed = 2.2f, Chase = 5.5f, Radius = 0.45f, Height = 0.7f, MeleeDmg = 6, MeleeCooldown = 0.8f, Growl = "growl_small", Color = new(0.62f, 0.55f, 0.4f), PainChance = 0.8f },
        new() { Name = "XENO GRUNT", Hp = 70, Speed = 2.0f, Chase = 4.3f, Radius = 0.5f, Height = 1.9f, MeleeDmg = 12, Color = new(0.45f, 0.55f, 0.45f), PainChance = 0.6f },
        new() { Name = "SPITTER", Hp = 60, Speed = 1.8f, Chase = 3.6f, Radius = 0.55f, Height = 1.5f, MeleeDmg = 8, Ranged = ProjKind.EnemyAcid, RangedDmg = 10, ProjSpeed = 16, RangedCooldown = 2.3f, RangeMin = 4, RangeMax = 22, Splash = 1.5f, DropWeapon = Weapons.BioLauncher, DropChance = 0.22f, Growl = "growl_small", Color = new(0.55f, 0.6f, 0.3f) },
        new() { Name = "XENO TROOPER", Hp = 100, Speed = 2.2f, Chase = 4.6f, Radius = 0.5f, Height = 2.0f, MeleeDmg = 10, Ranged = ProjKind.EnemyPlasma, RangedDmg = 7, ProjSpeed = 26, RangedCooldown = 2.0f, Burst = 3, RangeMin = 3, RangeMax = 28, DropWeapon = Weapons.Plasma, DropChance = 0.35f, Color = new(0.35f, 0.3f, 0.45f), PainChance = 0.4f },
        new() { Name = "STALKER", Hp = 90, Speed = 3f, Chase = 8.5f, Radius = 0.5f, Height = 1.6f, MeleeDmg = 16, MeleeCooldown = 0.7f, Growl = "screech", Color = new(0.2f, 0.18f, 0.22f), PainChance = 0.3f },
        new() { Name = "WRAITH", Hp = 120, Speed = 2f, Chase = 4.5f, Radius = 0.6f, Height = 1.4f, FlyHeight = 1.9f, MeleeDmg = 10, Ranged = ProjKind.EnemyOrb, RangedDmg = 14, ProjSpeed = 14, RangedCooldown = 2.0f, RangeMin = 5, RangeMax = 24, DropWeapon = Weapons.ArcCaster, DropChance = 0.3f, Growl = "wail", Color = new(0.55f, 0.4f, 0.8f), PainChance = 0.3f },
        new() { Name = "BRUTE", Hp = 500, Speed = 1.8f, Chase = 3.7f, Radius = 0.9f, Height = 2.8f, MeleeDmg = 35, MeleeRange = 1.2f, MeleeCooldown = 1.4f, Ranged = ProjKind.BruteOrb, RangedDmg = 30, ProjSpeed = 12, RangedCooldown = 3.5f, RangeMin = 6, RangeMax = 25, Splash = 3f, DropWeapon = Weapons.VoidCannon, DropChance = 0.3f, Growl = "growl_big", Death = "adeath_big", Color = new(0.5f, 0.2f, 0.15f), PainChance = 0.12f },
        new() { Name = "OVERSEER", Hp = 900, Speed = 1.5f, Chase = 3f, Radius = 1.1f, Height = 2.0f, FlyHeight = 2.0f, MeleeDmg = 20, Ranged = ProjKind.EnemyOrb, RangedDmg = 16, ProjSpeed = 16, RangedCooldown = 1.6f, Burst = 3, RangeMin = 4, RangeMax = 30, DropWeapon = Weapons.VoidCannon, DropChance = 0.6f, Growl = "wail", Death = "adeath_big", Color = new(0.75f, 0.5f, 0.6f), PainChance = 0.08f },
        new() { Name = "SHIELD PYLON", Hp = 600, Radius = 0.8f, Height = 3.6f, Color = new(0.7f, 0.2f, 1f), Stationary = true, PainChance = 0, Death = "shatter" },
        new() { Name = "ALIEN PORTAL", Hp = 3000, Radius = 2.3f, Height = 4.6f, Color = new(0.6f, 0.2f, 1f), Stationary = true, PainChance = 0, Death = "bigboom" },
        // charges in and bursts in a spray of acid - also when shot
        new() { Name = "BLOATER", Hp = 45, Speed = 1.6f, Chase = 5.4f, Radius = 0.6f, Height = 1.4f, Growl = "growl_small", Color = new(0.55f, 0.62f, 0.25f), PainChance = 0.15f },
        // tunnels under the floor and erupts next to you
        new() { Name = "BURROWER", Hp = 110, Speed = 2.4f, Chase = 5.6f, Radius = 0.55f, Height = 1.5f, MeleeDmg = 18, MeleeCooldown = 0.9f, Color = new(0.52f, 0.4f, 0.28f), PainChance = 0.35f },
        // waits on the ceiling and drops on you
        new() { Name = "CEILING CRAWLER", Hp = 55, Speed = 2.2f, Chase = 5.8f, Radius = 0.5f, Height = 0.8f, MeleeDmg = 11, MeleeCooldown = 0.8f, Growl = "screech", Color = new(0.3f, 0.36f, 0.42f), PainChance = 0.5f },
        // mini-bosses (sublevels 5/20, 10/25, 15)
        new() { Name = "HIVE QUEEN", Hp = 2000, Speed = 1.4f, Chase = 2.7f, Radius = 1.6f, Height = 3.4f, MeleeDmg = 32, MeleeRange = 1.6f, MeleeCooldown = 1.3f, Ranged = ProjKind.EnemyAcid, RangedDmg = 14, ProjSpeed = 18, RangedCooldown = 2.4f, RangeMin = 3, RangeMax = 34, Splash = 2f, Growl = "growl_big", Death = "adeath_big", Color = new(0.48f, 0.52f, 0.25f), PainChance = 0, Boss = true },
        new() { Name = "WARLORD", Hp = 3000, Speed = 1.7f, Chase = 3.3f, Radius = 1.3f, Height = 3.6f, MeleeDmg = 40, MeleeRange = 1.6f, MeleeCooldown = 1.4f, Ranged = ProjKind.BruteOrb, RangedDmg = 22, ProjSpeed = 15, RangedCooldown = 2.8f, Burst = 3, RangeMin = 5, RangeMax = 34, Splash = 3f, Growl = "growl_big", Death = "adeath_big", Color = new(0.55f, 0.16f, 0.12f), PainChance = 0, Boss = true },
        new() { Name = "THE MIND", Hp = 2600, Speed = 1.5f, Chase = 2.4f, Radius = 1.4f, Height = 2.4f, FlyHeight = 2.0f, MeleeDmg = 20, Ranged = ProjKind.EnemyOrb, RangedDmg = 14, ProjSpeed = 15, RangedCooldown = 1.9f, RangeMin = 4, RangeMax = 36, Growl = "wail", Death = "adeath_big", Color = new(0.72f, 0.45f, 0.78f), PainChance = 0, Boss = true },
    };

    static float Weight(int type, int level) => type switch
    {
        Skitter => level <= 20 ? 10 - level * 0.45f : 0,
        Grunt => level <= 24 ? 9 - level * 0.3f : 0,
        Spitter => level >= 3 ? 4 + level * 0.1f : 0,
        Trooper => level >= 5 ? 3 + level * 0.2f : 0,
        Stalker => level >= 9 ? 2 + level * 0.15f : 0,
        Wraith => level >= 12 ? 1.5f + level * 0.15f : 0,
        Brute => level >= 16 ? 0.6f + (level - 16) * 0.12f : 0,
        Overseer => level >= 23 ? 0.3f + (level - 23) * 0.08f : 0,
        Crawler => level >= 4 ? 1.8f + level * 0.05f : 0,
        Bloater => level >= 6 ? 1.4f + level * 0.07f : 0,
        Burrower => level >= 8 ? 1.0f + level * 0.06f : 0,
        _ => 0
    };

    public static int PickType(int level, Random rng)
    {
        float total = 0;
        foreach (int t in Roaming) total += MathF.Max(0, Weight(t, level));
        float r = (float)rng.NextDouble() * total;
        foreach (int t in Roaming)
        {
            r -= MathF.Max(0, Weight(t, level));
            if (r <= 0) return t;
        }
        return Grunt;
    }
}
