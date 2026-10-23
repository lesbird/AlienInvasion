using System.Numerics;
using AlienInvasion.Engine;
using Silk.NET.OpenGL;

namespace AlienInvasion.Game;

/// <summary>Procedurally assembled meshes for weapons and pickups.</summary>
public static class Models
{
    static readonly Vector3 Gun = new(0.34f, 0.35f, 0.38f), GunLight = new(0.5f, 0.51f, 0.55f), Wood = new(0.36f, 0.22f, 0.12f),
        Olive = new(0.3f, 0.34f, 0.2f), Glove = new(0.12f, 0.11f, 0.1f), Sleeve = new(0.22f, 0.26f, 0.18f);

    static Matrix4x4 Rx(float a) => Matrix4x4.CreateRotationX(a);
    static Matrix4x4 T(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);

    static void Hands(MeshBuilder b, float gripZ, float foreZ, float foreY = -0.06f)
    {
        b.Col(Glove);
        b.Transform = Rx(0.25f) * T(0, -0.12f, gripZ);
        b.BoxC(Vector3.Zero, new(0.075f, 0.11f, 0.1f));
        b.Col(Sleeve);
        b.BoxC(new(0.01f, -0.09f, 0.1f), new(0.09f, 0.1f, 0.12f));
        b.Transform = Matrix4x4.Identity;
        if (foreZ < 0)
        {
            b.Col(Glove);
            b.BoxC(new(-0.03f, foreY - 0.03f, foreZ), new(0.09f, 0.07f, 0.11f));

        }
    }

    /// <summary>Weapon mesh in view space (barrel along -Z). Returns muzzle offset via out.</summary>
    public static MeshBuilder Weapon(int id, out Vector3 muzzle, bool withHands = true)
    {
        var b = new MeshBuilder();
        muzzle = new Vector3(0, 0, -0.5f);
        switch (id)
        {
            case Weapons.Knife:
                b.Col(Glove);
                b.Transform = Rx(-0.2f) * T(0, -0.05f, 0);
                b.BoxC(new(0, -0.06f, 0.02f), new(0.04f, 0.14f, 0.05f));
                b.Col(Gun);
                b.BoxC(new(0, 0.02f, 0.02f), new(0.08f, 0.02f, 0.05f));
                b.Col(0.75f, 0.77f, 0.8f);
                b.Box(new(-0.006f, 0.03f, -0.005f), new(0.006f, 0.26f, 0.035f));
                b.Col(0.5f, 0.52f, 0.55f);
                b.Box(new(-0.007f, 0.03f, 0.03f), new(0.007f, 0.2f, 0.045f));
                b.Transform = Matrix4x4.Identity;

                muzzle = new(0, 0.2f, -0.1f);
                break;
            case Weapons.Pistol:
                b.Col(Gun); b.BoxC(new(0, 0.02f, -0.1f), new(0.05f, 0.06f, 0.26f));
                b.Col(GunLight); b.BoxC(new(0, -0.025f, -0.08f), new(0.045f, 0.035f, 0.2f));
                b.Col(Gun); b.CylinderZ(new(0, 0.02f, -0.23f), 0.012f, 0.012f, 0.02f, 8);
                b.Col(0.1f, 0.1f, 0.1f); b.Transform = Rx(0.25f) * T(0, -0.09f, 0.0f); b.BoxC(Vector3.Zero, new(0.048f, 0.13f, 0.07f)); b.Transform = Matrix4x4.Identity;
                b.Col(0.9f, 0.9f, 0.2f, 0.8f); b.BoxC(new(0, 0.055f, -0.21f), new(0.008f, 0.01f, 0.01f));
                if (withHands) Hands(b, 0.0f, 1);
                muzzle = new(0, 0.02f, -0.25f);
                break;
            case Weapons.Shotgun:
                b.Col(Gun); b.CylinderZ(new(0, 0.02f, -0.05f), 0.022f, 0.022f, 0.65f, 10);
                b.Col(GunLight); b.CylinderZ(new(0, -0.025f, -0.05f), 0.019f, 0.019f, 0.52f, 10);
                b.Col(Wood); b.BoxC(new(0, -0.03f, -0.36f), new(0.065f, 0.055f, 0.2f));
                b.Col(Gun); b.BoxC(new(0, 0.0f, 0.05f), new(0.07f, 0.09f, 0.24f));
                b.Col(Wood); b.Transform = Rx(0.15f) * T(0, -0.06f, 0.3f); b.BoxC(Vector3.Zero, new(0.06f, 0.1f, 0.3f)); b.Transform = Matrix4x4.Identity;
                if (withHands) Hands(b, 0.12f, -0.36f, -0.05f);
                muzzle = new(0, 0.02f, -0.72f);
                break;
            case Weapons.Rifle:
                b.Col(Gun); b.BoxC(new(0, 0, 0), new(0.065f, 0.1f, 0.38f));
                b.Col(GunLight); b.BoxC(new(0, 0, -0.3f), new(0.075f, 0.075f, 0.24f));
                b.Col(Gun); b.CylinderZ(new(0, 0.005f, -0.4f), 0.015f, 0.015f, 0.2f, 8);
                b.Col(0.15f, 0.15f, 0.15f); b.Transform = Rx(-0.25f) * T(0, -0.12f, -0.06f); b.BoxC(Vector3.Zero, new(0.045f, 0.17f, 0.08f)); b.Transform = Matrix4x4.Identity;
                b.Col(Gun); b.BoxC(new(0, 0.075f, 0.0f), new(0.035f, 0.05f, 0.16f));
                b.Col(1f, 0.3f, 0.2f, 1f); b.BoxC(new(0, 0.1f, -0.06f), new(0.01f, 0.01f, 0.01f));
                b.Col(0.15f, 0.15f, 0.15f); b.BoxC(new(0, -0.02f, 0.3f), new(0.055f, 0.09f, 0.24f));
                if (withHands) Hands(b, 0.1f, -0.3f);
                muzzle = new(0, 0.005f, -0.6f);
                break;
            case Weapons.GrenadeLauncher:
                b.Col(Olive); b.CylinderZ(new(0, 0, 0.08f), 0.085f, 0.085f, 0.16f, 12);
                b.Col(Gun); b.CylinderZ(new(0, 0.01f, -0.08f), 0.045f, 0.045f, 0.4f, 12);
                b.Col(0.05f, 0.05f, 0.05f); b.CylinderZ(new(0, 0.01f, -0.48f), 0.035f, 0.035f, 0.01f, 12);
                b.Col(Gun); b.BoxC(new(0, -0.02f, 0.18f), new(0.06f, 0.08f, 0.2f));
                b.Col(Olive); b.BoxC(new(0, -0.04f, 0.38f), new(0.05f, 0.1f, 0.22f));
                if (withHands) Hands(b, 0.16f, -0.25f);
                muzzle = new(0, 0.01f, -0.5f);
                break;
            case Weapons.RocketLauncher:
                b.Col(Olive); b.CylinderZ(new(0, 0.06f, 0.4f), 0.085f, 0.085f, 1.0f, 14);
                b.Col(Gun); b.CylinderZ(new(0, 0.06f, -0.6f), 0.09f, 0.11f, 0.08f, 14);
                b.Col(Gun); b.CylinderZ(new(0, 0.06f, 0.48f), 0.11f, 0.09f, 0.08f, 14);
                b.Col(Gun); b.BoxC(new(0, -0.06f, 0.05f), new(0.05f, 0.12f, 0.08f));
                b.Col(Gun); b.BoxC(new(0.09f, 0.12f, -0.1f), new(0.03f, 0.06f, 0.12f));
                b.Col(1f, 0.2f, 0.1f, 1f); b.BoxC(new(0.09f, 0.155f, -0.1f), new(0.012f, 0.012f, 0.012f));
                if (withHands) Hands(b, 0.05f, -0.25f, 0.0f);
                muzzle = new(0, 0.06f, -0.66f);
                break;
            case Weapons.Plasma:
            {
                var purple = new Vector3(0.28f, 0.16f, 0.35f);
                b.Col(purple); b.Sphere(new(0, 0, -0.1f), new(0.07f, 0.065f, 0.3f), 12, 8);
                b.Col(purple * 0.7f); b.Sphere(new(0, -0.07f, 0.08f), new(0.05f, 0.09f, 0.08f), 10, 6);
                b.Col(0.3f, 0.9f, 1f, 1f);
                for (int i = 0; i < 3; i++) { b.Transform = T(0, 0, -0.15f - i * 0.09f); b.Torus(0.07f - i * 0.008f, 0.012f, 16, 6); }
                b.Transform = Matrix4x4.Identity;
                b.Col(0.6f, 1f, 1f, 1f); b.Sphere(new(0, 0, -0.05f), new(0.035f), 10, 6);
                b.Col(purple * 0.6f);
                b.Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2) * T(0.04f, 0.0f, -0.32f); b.Cylinder(Vector3.Zero, 0.015f, 0.0f, 0.18f, 6);
                b.Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2) * T(-0.04f, 0.0f, -0.32f); b.Cylinder(Vector3.Zero, 0.015f, 0.0f, 0.18f, 6);
                b.Transform = Matrix4x4.Identity;
                if (withHands) Hands(b, 0.08f, -0.2f, -0.05f);
                muzzle = new(0, 0, -0.45f);
                break;
            }
            case Weapons.BioLauncher:
            {
                var bone = new Vector3(0.7f, 0.65f, 0.5f);
                b.Col(0.4f, 0.9f, 0.2f, 0.6f); b.Sphere(new(0, 0.03f, 0.0f), new(0.1f, 0.09f, 0.14f), 12, 8);
                b.Col(bone);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI / 2 + 0.4f;
                    b.Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2) * T(MathF.Cos(a) * 0.08f, 0.03f + MathF.Sin(a) * 0.075f, 0.14f);
                    b.Cylinder(Vector3.Zero, 0.012f, 0.008f, 0.4f, 6);
                }
                b.Transform = Matrix4x4.Identity;
                b.Col(bone * 0.8f); b.CylinderZ(new(0, 0.03f, -0.14f), 0.05f, 0.025f, 0.2f, 10);
                b.Col(0.3f, 0.25f, 0.2f); b.Sphere(new(0, -0.08f, 0.1f), new(0.045f, 0.09f, 0.06f), 8, 6);
                if (withHands) Hands(b, 0.1f, -0.15f, -0.04f);
                muzzle = new(0, 0.03f, -0.36f);
                break;
            }
            case Weapons.ArcCaster:
                b.Col(0.18f, 0.2f, 0.28f); b.BoxC(new(0, 0, 0.05f), new(0.09f, 0.09f, 0.3f));
                b.Col(0.3f, 0.32f, 0.4f);
                b.BoxC(new(0.035f, 0.0f, -0.2f), new(0.018f, 0.03f, 0.3f));
                b.BoxC(new(-0.035f, 0.0f, -0.2f), new(0.018f, 0.03f, 0.3f));
                b.Col(0.5f, 0.7f, 1f, 1f);
                b.Sphere(new(0.035f, 0, -0.36f), new(0.022f), 8, 6);
                b.Sphere(new(-0.035f, 0, -0.36f), new(0.022f), 8, 6);
                for (int i = 0; i < 3; i++) { b.Transform = T(0, 0, -0.04f - i * 0.06f); b.Torus(0.05f, 0.009f, 14, 5); }
                b.Transform = Matrix4x4.Identity;
                b.Col(0.15f, 0.15f, 0.2f); b.BoxC(new(0, -0.08f, 0.1f), new(0.05f, 0.1f, 0.07f));
                if (withHands) Hands(b, 0.1f, 1);
                muzzle = new(0, 0, -0.38f);
                break;
            case Weapons.VoidCannon:
                b.Col(0.06f, 0.05f, 0.08f); b.Sphere(new(0, 0.02f, 0), new(0.15f, 0.14f, 0.2f), 14, 10);
                b.Col(0.7f, 0.25f, 1f, 1f); b.Sphere(new(0, 0.02f, -0.12f), new(0.08f), 10, 8);
                b.Col(0.25f, 0.18f, 0.3f);
                for (int i = 0; i < 3; i++) { b.Transform = T(0, 0.02f, -0.18f - i * 0.08f); b.Torus(0.12f - i * 0.02f, 0.018f, 18, 6); }
                b.Transform = Matrix4x4.Identity;
                b.Col(0.15f, 0.1f, 0.2f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * MathF.Tau / 5;
                    b.Transform = Matrix4x4.CreateRotationZ(a) * T(0, 0.02f, 0.05f);
                    b.Transform = Matrix4x4.CreateRotationX(0.3f) * Matrix4x4.CreateTranslation(0, 0.13f, 0.05f) * Matrix4x4.CreateRotationZ(a) * T(0, 0.02f, 0);
                    b.Cylinder(Vector3.Zero, 0.02f, 0, 0.12f, 5);
                }
                b.Transform = Matrix4x4.Identity;
                if (withHands) Hands(b, 0.16f, -0.2f, -0.1f);
                muzzle = new(0, 0.02f, -0.45f);
                break;
            case Weapons.Sniper:
                b.Col(Olive); b.BoxC(new(0, 0, 0.02f), new(0.06f, 0.085f, 0.4f));
                b.Col(Gun); b.CylinderZ(new(0, 0.01f, -0.18f), 0.014f, 0.014f, 0.62f, 8);
                b.Col(Gun); b.CylinderZ(new(0, 0.01f, -0.8f), 0.022f, 0.022f, 0.08f, 8);              // muzzle brake
                b.Col(0.1f, 0.1f, 0.1f); b.CylinderZ(new(0, 0.085f, 0.12f), 0.03f, 0.03f, 0.3f, 12);   // scope tube
                b.CylinderZ(new(0, 0.085f, -0.18f), 0.03f, 0.04f, 0.05f, 12);
                b.CylinderZ(new(0, 0.085f, 0.16f), 0.036f, 0.03f, 0.04f, 12);
                b.Col(0.2f, 0.5f, 0.9f, 0.8f); b.CylinderZ(new(0, 0.085f, -0.229f), 0.037f, 0.037f, 0.004f, 12);   // lens glint
                b.Col(Gun); b.BoxC(new(0, 0.055f, -0.04f), new(0.025f, 0.03f, 0.03f)); b.BoxC(new(0, 0.055f, 0.08f), new(0.025f, 0.03f, 0.03f));
                b.Col(GunLight); b.BoxC(new(0.04f, 0.01f, 0.06f), new(0.03f, 0.012f, 0.012f));           // bolt handle
                b.Col(0.15f, 0.15f, 0.15f); b.BoxC(new(0, -0.07f, -0.02f), new(0.035f, 0.08f, 0.05f));   // magazine
                b.Col(Olive); b.Transform = Rx(0.12f) * T(0, -0.035f, 0.34f); b.BoxC(Vector3.Zero, new(0.055f, 0.11f, 0.26f)); b.Transform = Matrix4x4.Identity;
                if (withHands) Hands(b, 0.14f, -0.28f, -0.04f);
                muzzle = new(0, 0.01f, -0.88f);
                break;
            case Weapons.Railgun:
            {
                var body = new Vector3(0.36f, 0.38f, 0.43f);
                b.Col(body); b.BoxC(new(0, -0.01f, 0.08f), new(0.1f, 0.11f, 0.36f));
                b.Col(GunLight);
                b.BoxC(new(0, 0.045f, -0.3f), new(0.07f, 0.02f, 0.52f));    // upper rail
                b.BoxC(new(0, -0.045f, -0.3f), new(0.07f, 0.02f, 0.52f));   // lower rail
                b.Col(0.4f, 0.8f, 1f, 1f);
                b.BoxC(new(0, 0, -0.32f), new(0.006f, 0.05f, 0.48f));       // glowing gap between the rails
                b.Col(0.28f, 0.3f, 0.36f);
                for (int i = 0; i < 4; i++) { b.Transform = T(0, 0, -0.1f - i * 0.11f); b.Torus(0.068f, 0.011f, 14, 5); }
                b.Transform = Matrix4x4.Identity;
                b.Col(body * 0.7f); b.BoxC(new(0.065f, 0.02f, 0.1f), new(0.03f, 0.07f, 0.18f));   // capacitor pack
                b.Col(0.4f, 0.8f, 1f, 1f); b.BoxC(new(0.081f, 0.02f, 0.1f), new(0.004f, 0.04f, 0.12f));
                b.Col(0.12f, 0.12f, 0.14f); b.Transform = Rx(0.25f) * T(0, -0.11f, 0.12f); b.BoxC(Vector3.Zero, new(0.045f, 0.12f, 0.07f)); b.Transform = Matrix4x4.Identity;
                b.Col(body); b.BoxC(new(0, -0.02f, 0.36f), new(0.06f, 0.08f, 0.2f));
                if (withHands) Hands(b, 0.12f, -0.2f, -0.08f);
                muzzle = new(0, 0, -0.58f);
                break;
            }
        }
        return b;
    }

    public static MeshBuilder Pickup(PickupKind k)
    {
        var b = new MeshBuilder();
        void Cross(float y, float size)
        {
            b.Col(0.9f, 0.1f, 0.1f, 0.3f);
            b.BoxC(new(0, y, 0), new(size, 0.02f, size * 0.3f));
            b.BoxC(new(0, y, 0), new(size * 0.3f, 0.02f, size));
        }
        switch (k)
        {
            case PickupKind.Health:
                b.Col(0.9f, 0.9f, 0.88f, 0.1f); b.BoxC(new(0, 0, 0), new(0.4f, 0.22f, 0.3f));
                Cross(0.115f, 0.2f);
                break;
            case PickupKind.Medkit:
                b.Col(0.9f, 0.9f, 0.88f, 0.1f); b.BoxC(new(0, 0, 0), new(0.65f, 0.35f, 0.45f));
                Cross(0.18f, 0.3f);
                b.Col(0.2f, 0.2f, 0.2f); b.BoxC(new(0, 0.22f, 0), new(0.25f, 0.05f, 0.06f));
                break;
            case PickupKind.MegaHealth:
                b.Col(0.6f, 0.6f, 0.65f); b.Cylinder(new(0, -0.2f, 0), 0.22f, 0.22f, 0.08f, 14);
                b.Cylinder(new(0, 0.35f, 0), 0.22f, 0.22f, 0.08f, 14);
                b.Col(0.3f, 0.7f, 1f, 1f); b.Sphere(new(0, 0.1f, 0), new(0.18f, 0.26f, 0.18f), 12, 8);
                break;
            case PickupKind.Armor:
                b.Col(0.3f, 0.5f, 0.75f, 0.15f); b.BoxC(Vector3.Zero, new(0.35f, 0.08f, 0.25f));
                b.Col(0.6f, 0.8f, 1f, 0.6f); b.BoxC(new(0, 0.045f, 0), new(0.2f, 0.01f, 0.05f));
                break;
            case PickupKind.ArmorVest:
                b.Col(0.22f, 0.3f, 0.2f); b.BoxC(new(0, 0.1f, 0), new(0.5f, 0.6f, 0.22f));
                b.BoxC(new(0.18f, 0.43f, 0), new(0.14f, 0.1f, 0.24f));
                b.BoxC(new(-0.18f, 0.43f, 0), new(0.14f, 0.1f, 0.24f));
                b.Col(0.3f, 0.5f, 0.8f, 0.4f); b.BoxC(new(0, 0.12f, -0.115f), new(0.3f, 0.06f, 0.01f));
                break;
            case PickupKind.Bullets:
                b.Col(0.35f, 0.38f, 0.22f); b.BoxC(Vector3.Zero, new(0.45f, 0.25f, 0.28f));
                b.Col(0.85f, 0.7f, 0.3f, 0.2f);
                for (int i = 0; i < 4; i++) b.Cylinder(new(-0.15f + i * 0.1f, 0.12f, 0), 0.025f, 0.012f, 0.1f, 6);
                break;
            case PickupKind.Shells:
                b.Col(0.6f, 0.12f, 0.1f); b.BoxC(Vector3.Zero, new(0.4f, 0.2f, 0.3f));
                b.Col(0.85f, 0.2f, 0.15f, 0.15f);
                for (int i = 0; i < 4; i++) b.Cylinder(new(-0.14f + i * 0.095f, 0.1f, 0), 0.035f, 0.035f, 0.08f, 8);
                b.Col(0.8f, 0.65f, 0.3f); for (int i = 0; i < 4; i++) b.Cylinder(new(-0.14f + i * 0.095f, 0.18f, 0), 0.036f, 0.036f, 0.02f, 8);
                break;
            case PickupKind.Grenades:
                b.Col(0.3f, 0.25f, 0.18f); b.BoxC(new(0, -0.05f, 0), new(0.5f, 0.12f, 0.3f));
                b.Col(0.25f, 0.35f, 0.18f);
                for (int i = 0; i < 3; i++) b.Sphere(new(-0.15f + i * 0.15f, 0.06f, 0), new(0.07f, 0.09f, 0.07f), 10, 6);
                break;
            case PickupKind.Rockets:
                for (int i = 0; i < 2; i++)
                {
                    b.Col(0.35f, 0.38f, 0.22f);
                    b.Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * T(0.3f, -0.05f + i * 0.13f, 0);
                    b.Cylinder(Vector3.Zero, 0.055f, 0.055f, 0.5f, 10);
                    b.Col(0.7f, 0.15f, 0.1f);
                    b.Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * T(-0.2f, -0.05f + i * 0.13f, 0);
                    b.Cylinder(Vector3.Zero, 0.055f, 0.0f, 0.15f, 10);
                }
                b.Transform = Matrix4x4.Identity;
                break;
            case PickupKind.Cells:
                b.Col(0.25f, 0.27f, 0.3f); b.Cylinder(new(0, -0.15f, 0), 0.14f, 0.14f, 0.06f, 12);
                b.Cylinder(new(0, 0.25f, 0), 0.14f, 0.14f, 0.06f, 12);
                b.Col(0.2f, 1f, 0.9f, 1f); b.Cylinder(new(0, -0.09f, 0), 0.11f, 0.11f, 0.34f, 12);
                break;
            case PickupKind.Backpack:
                b.Col(0.3f, 0.34f, 0.2f); b.BoxC(new(0, 0.05f, 0), new(0.5f, 0.55f, 0.3f));
                b.Col(0.26f, 0.3f, 0.17f); b.BoxC(new(0, -0.05f, 0.18f), new(0.38f, 0.28f, 0.1f));
                b.BoxC(new(0, 0.38f, 0), new(0.46f, 0.12f, 0.28f));
                b.Col(0.15f, 0.15f, 0.12f); b.BoxC(new(0.14f, 0.05f, -0.16f), new(0.06f, 0.55f, 0.03f)); b.BoxC(new(-0.14f, 0.05f, -0.16f), new(0.06f, 0.55f, 0.03f));
                b.Col(0.85f, 0.7f, 0.3f, 0.3f); b.BoxC(new(0, 0.1f, 0.24f), new(0.12f, 0.06f, 0.02f));
                break;
            case PickupKind.DamageAmp:
                b.Col(0.2f, 0.2f, 0.22f); b.Cylinder(new(0, -0.25f, 0), 0.25f, 0.2f, 0.08f, 12); b.Cylinder(new(0, 0.42f, 0), 0.2f, 0.25f, 0.08f, 12);
                for (int i = 0; i < 4; i++) { float a = i * MathF.PI / 2; b.BoxC(new(MathF.Cos(a) * 0.2f, 0.1f, MathF.Sin(a) * 0.2f), new(0.04f, 0.6f, 0.04f)); }
                b.Col(1f, 0.45f, 0.1f, 1f); b.Sphere(new(0, 0.1f, 0), new(0.3f), 14, 10);
                break;
            case PickupKind.NightVision:
            case PickupKind.XRay:
            {
                // goggles: head strap, housing, two lens tubes (green lenses / violet lenses)
                bool xr = k == PickupKind.XRay;
                b.Col(0.12f, 0.12f, 0.13f); b.BoxC(new(0, 0.1f, 0), new(0.5f, 0.2f, 0.18f));
                b.Col(0.2f, 0.2f, 0.22f); b.BoxC(new(0, 0.1f, 0.2f), new(0.56f, 0.06f, 0.24f));
                b.Col(0.16f, 0.16f, 0.17f);
                foreach (float x in new[] { -0.13f, 0.13f }) b.CylinderZ(new(x, 0.1f, -0.09f), 0.08f, 0.075f, 0.22f, 12);
                b.Col(xr ? new Vector3(0.8f, 0.4f, 1f) : new Vector3(0.3f, 1f, 0.35f), 1f);
                foreach (float x in new[] { -0.13f, 0.13f }) b.CylinderZ(new(x, 0.1f, -0.315f), 0.06f, 0.06f, 0.012f, 12);
                if (xr) { b.Col(0.9f, 0.85f, 0.2f, 0.6f); b.BoxC(new(0, 0.21f, 0), new(0.12f, 0.03f, 0.08f)); }
                break;
            }
            case PickupKind.Upgrade:
                // weapon mod kit: armoured case with a glowing gold chip
                b.Col(0.16f, 0.16f, 0.18f); b.BoxC(new(0, 0.05f, 0), new(0.55f, 0.22f, 0.38f));
                b.Col(0.85f, 0.65f, 0.15f, 0.3f); b.BoxC(new(0, 0.05f, 0.192f), new(0.5f, 0.04f, 0.01f)); b.BoxC(new(0, 0.05f, -0.192f), new(0.5f, 0.04f, 0.01f));
                b.Col(1f, 0.75f, 0.2f, 1f); b.BoxC(new(0, 0.18f, 0), new(0.22f, 0.04f, 0.16f));
                b.Col(0.3f, 0.3f, 0.32f); b.BoxC(new(0, 0.24f, 0), new(0.18f, 0.03f, 0.04f));
                break;
            case PickupKind.Log:
                // data pad: a rugged slate with a glowing screen of text lines
                b.Transform = Matrix4x4.CreateRotationX(-0.35f);
                b.Col(0.2f, 0.21f, 0.22f); b.BoxC(new(0, 0, 0), new(0.34f, 0.04f, 0.46f));
                b.Col(0.3f, 0.85f, 1f, 0.9f); b.BoxC(new(0, 0.022f, -0.02f), new(0.28f, 0.006f, 0.34f));
                b.Col(0.85f, 1f, 1f, 1f);
                for (int i = 0; i < 5; i++) b.BoxC(new(-0.03f + (i % 2) * 0.02f, 0.027f, -0.15f + i * 0.06f), new(0.18f - (i % 2) * 0.05f, 0.004f, 0.015f));
                b.Col(0.9f, 0.6f, 0.15f, 1f); b.BoxC(new(0.12f, 0.022f, 0.19f), new(0.03f, 0.008f, 0.03f));
                b.Transform = Matrix4x4.Identity;
                break;
            case PickupKind.Battery:
                b.Col(0.15f, 0.15f, 0.16f); b.Cylinder(new(0, -0.18f, 0), 0.11f, 0.11f, 0.36f, 12);
                b.Col(0.95f, 0.65f, 0.1f, 0.2f); b.Cylinder(new(0, 0.05f, 0), 0.115f, 0.115f, 0.13f, 12);
                b.Col(0.75f, 0.75f, 0.78f); b.Cylinder(new(0, 0.18f, 0), 0.04f, 0.04f, 0.04f, 8);
                b.Col(0.3f, 1f, 0.35f, 1f); b.BoxC(new(0, -0.05f, -0.112f), new(0.05f, 0.14f, 0.01f));
                break;
            case PickupKind.Key:
                b.Col(1, 1, 1, 0.7f); b.BoxC(Vector3.Zero, new(0.45f, 0.3f, 0.03f));
                b.Col(0.1f, 0.1f, 0.1f); b.BoxC(new(0.05f, -0.05f, 0), new(0.25f, 0.06f, 0.035f));
                b.Col(0.9f, 0.85f, 0.6f, 0.3f); b.BoxC(new(-0.14f, 0.06f, 0), new(0.1f, 0.08f, 0.035f));
                break;
        }
        return b;
    }

    /// <summary>Red explosive barrel: ribbed drum, yellow hazard band, flammable diamonds front and back.</summary>
    public static MeshBuilder ExplosiveBarrel()
    {
        var b = new MeshBuilder();
        b.Col(0.8f, 0.08f, 0.05f); b.Cylinder(Vector3.Zero, 0.33f, 0.33f, 1.1f, 18);
        b.Col(0.45f, 0.05f, 0.04f);
        foreach (float y in new[] { 0.06f, 0.3f, 0.78f, 1.02f }) b.Cylinder(new(0, y, 0), 0.345f, 0.345f, 0.05f, 18, false);
        b.Col(0.95f, 0.75f, 0.1f); b.Cylinder(new(0, 0.47f, 0), 0.337f, 0.337f, 0.18f, 18, false);
        b.Col(0.08f, 0.08f, 0.08f);
        for (int i = 0; i < 9; i++)
        {
            float a = i * MathF.Tau / 9;
            b.Transform = Matrix4x4.CreateRotationZ(0.6f) * Matrix4x4.CreateRotationY(-a) * T(MathF.Cos(a) * 0.339f, 0.56f, MathF.Sin(a) * 0.339f);
            b.BoxC(Vector3.Zero, new(0.01f, 0.2f, 0.07f));
        }
        for (int s = -1; s <= 1; s += 2)
        {
            b.Transform = Matrix4x4.CreateRotationZ(MathF.PI / 4) * T(0, 0.86f, s * 0.335f);
            b.Col(0.95f, 0.93f, 0.88f); b.BoxC(Vector3.Zero, new(0.15f, 0.15f, 0.012f));
            b.Transform = T(0, 0.85f, s * 0.343f);
            b.Col(1f, 0.35f, 0.05f, 0.5f); b.BoxC(Vector3.Zero, new(0.05f, 0.08f, 0.006f));
        }
        b.Transform = Matrix4x4.Identity;
        b.Col(0.35f, 0.35f, 0.37f); b.Cylinder(new(0.15f, 1.1f, 0.05f), 0.055f, 0.055f, 0.04f, 8);
        return b;
    }

    public static MeshBuilder Crystal()
    {
        var b = new MeshBuilder();
        b.Sphere(Vector3.Zero, new Vector3(0.5f), 5, 2);
        return b;
    }

    // ======================================================================  room props
    /// <summary>Builds a prop into world space. Glass parts go into a separate translucent builder;
    /// LEDs and screens go into <paramref name="blink"/>, whose UVs carry the blink pattern (see the lit shader).</summary>
    public static void Prop(MeshBuilder b, MeshBuilder glass, MeshBuilder blink, Prop p, int level)
    {
        var r = new Random(p.Seed);
        float w = p.Width, d = p.Depth;
        var xf = Matrix4x4.CreateRotationY(p.Yaw) * Matrix4x4.CreateTranslation(p.Pos);
        b.Transform = xf;
        glass.Transform = xf;
        float rust = level > 8 && level <= 15 ? 0.15f : 0;
        var steel = new Vector3(0.5f, 0.52f, 0.55f) - new Vector3(0, rust, rust * 1.3f);
        var dark = new Vector3(0.2f, 0.21f, 0.23f);
        Vector3 Screen() => r.Next(4) switch { 0 => new(0.2f, 0.9f, 0.4f), 1 => new(0.25f, 0.6f, 1f), 2 => new(1f, 0.65f, 0.2f), _ => new(0.4f, 0.9f, 1f) };
        Vector3 Led() => r.Next(3) switch { 0 => new(1f, 0.2f, 0.15f), 1 => new(0.2f, 1f, 0.3f), _ => new(1f, 0.8f, 0.2f) };

        // uv.x > 0: square-wave blink at uv.x Hz, uv.y = phase; uv.x < 0: random data-activity flicker;
        // uv.y >= 10: screen (rolling scanlines and the odd glitch)
        void Emit(Vector3 c, Vector3 size, Vector3 col, Vector2 uv)
        {
            int v0 = blink.V.Count;
            blink.Transform = b.Transform;
            blink.Col(col, 1f);
            blink.BoxC(c, size);
            for (int i = v0; i < blink.V.Count; i++) { var v = blink.V[i]; v.UV = uv; blink.V[i] = v; }
        }
        void BlinkLed(Vector3 c, Vector3 size, Vector3 col)
        {
            double k = r.NextDouble();
            var uv = k < 0.35 ? new Vector2(-(4f + (float)r.NextDouble() * 10f), (float)r.NextDouble())
                : k < 0.8 ? new Vector2(0.4f + (float)r.NextDouble() * 1.8f, (float)r.NextDouble())
                : new Vector2(0.01f, 0.25f);   // effectively steady
            Emit(c, size, col, uv);
        }
        void ScreenFace(Vector3 c, Vector3 size, Vector3 col) => Emit(c, size, col, new Vector2(1, 10 + (float)r.NextDouble() * 5));

        void Legs(float hw, float hd, float h, float th)
        {
            b.BoxC(new(-hw, h / 2, -hd), new(th, h, th)); b.BoxC(new(hw, h / 2, -hd), new(th, h, th));
            b.BoxC(new(-hw, h / 2, hd), new(th, h, th)); b.BoxC(new(hw, h / 2, hd), new(th, h, th));
        }
        void Monitor(Vector3 at, float yaw)
        {
            var saved = b.Transform;
            b.Transform = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(at) * saved;
            b.Col(dark); b.BoxC(new(0, 0.02f, 0), new(0.3f, 0.04f, 0.2f)); b.BoxC(new(0, 0.15f, -0.02f), new(0.05f, 0.25f, 0.05f));
            b.Col(0.12f, 0.12f, 0.13f); b.BoxC(new(0, 0.42f, 0), new(0.72f, 0.46f, 0.06f));
            ScreenFace(new(0, 0.42f, 0.032f), new(0.64f, 0.38f, 0.01f), Screen() * 0.9f);
            b.Transform = saved;
        }

        switch (p.Kind)
        {
            case PropKind.Desk:
            {
                b.Col(steel * 0.9f); b.BoxC(new(0, 0.76f, 0), new(w, 0.06f, d));
                b.Col(dark); Legs(w / 2 - 0.08f, d / 2 - 0.08f, 0.73f, 0.06f);
                b.Col(steel * 0.75f); b.BoxC(new(w / 2 - 0.35f, 0.37f, 0), new(0.55f, 0.72f, d * 0.92f));
                b.Col(dark); for (int i = 0; i < 3; i++) b.BoxC(new(w / 2 - 0.35f, 0.15f + i * 0.22f, d / 2 * 0.92f + 0.005f), new(0.2f, 0.03f, 0.01f));
                Monitor(new(-0.3f, 0.79f, -0.15f), 0);
                b.Col(0.1f, 0.1f, 0.1f); b.BoxC(new(-0.3f, 0.8f, 0.2f), new(0.5f, 0.025f, 0.17f));
                b.Col(0.9f, 0.9f, 0.85f); b.Transform = Matrix4x4.CreateRotationY((float)r.NextDouble()) * Matrix4x4.CreateTranslation(0.35f, 0.795f, 0.1f) * xf; b.BoxC(Vector3.Zero, new(0.3f, 0.01f, 0.4f)); b.Transform = xf;
                break;
            }
            case PropKind.Table:
            {
                var top = level > 15 ? new Vector3(0.75f, 0.77f, 0.8f) : new Vector3(0.38f, 0.3f, 0.22f);
                b.Col(top); b.BoxC(new(0, 0.82f, 0), new(w, 0.07f, d));
                b.Col(dark); Legs(w / 2 - 0.1f, d / 2 - 0.1f, 0.79f, 0.07f);
                // clutter
                int items = r.Next(2, 5);
                for (int i = 0; i < items; i++)
                {
                    float x = ((float)r.NextDouble() - 0.5f) * (w - 0.5f), z = ((float)r.NextDouble() - 0.5f) * (d - 0.4f);
                    switch (r.Next(4))
                    {
                        case 0: b.Col(0.9f, 0.9f, 0.86f); b.BoxC(new(x, 0.865f, z), new(0.32f, 0.01f, 0.42f)); break;
                        case 1: b.Col(0.7f, 0.2f, 0.15f); b.Cylinder(new(x, 0.855f, z), 0.05f, 0.05f, 0.11f, 8); break;
                        case 2:
                            b.Col(0.12f, 0.12f, 0.13f); b.BoxC(new(x, 0.87f, z), new(0.42f, 0.02f, 0.3f));
                            b.Transform = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(x, 1.0f, z - 0.17f) * xf;
                            b.BoxC(Vector3.Zero, new(0.42f, 0.28f, 0.02f)); ScreenFace(new(0, 0, 0.012f), new(0.38f, 0.24f, 0.005f), Screen());
                            b.Transform = xf; break;
                        default: b.Col(0.3f, 0.35f, 0.2f); b.BoxC(new(x, 0.95f, z), new(0.4f, 0.22f, 0.26f)); break;
                    }
                }
                break;
            }
            case PropKind.Console:
            {
                b.Col(steel * 0.6f); b.BoxC(new(0, 0.5f, -0.05f), new(w, 1.0f, d - 0.1f));
                b.Col(dark); b.BoxC(new(0, 0.05f, 0.02f), new(w - 0.1f, 0.1f, d - 0.15f));
                // slanted control surface
                b.Transform = Matrix4x4.CreateRotationX(0.55f) * Matrix4x4.CreateTranslation(0, 1.08f, 0.05f) * xf;
                b.Col(steel * 0.5f); b.BoxC(Vector3.Zero, new(w, 0.06f, 0.55f));
                for (int i = 0; i < 18; i++)
                {
                    float x = -w / 2 + 0.15f + (float)r.NextDouble() * (w - 0.3f), z = -0.2f + (float)r.NextDouble() * 0.4f;
                    if (r.NextDouble() < 0.6) BlinkLed(new(x, 0.04f, z), new(0.05f, 0.03f, 0.05f), Led());
                    else { b.Col(Led() * 0.5f); b.BoxC(new(x, 0.04f, z), new(0.05f, 0.03f, 0.05f)); }
                }
                b.Transform = xf;
                // status lamps along the top of the screen housing
                for (int i = 0; i < 6; i++)
                    BlinkLed(new(-w / 2 + 0.2f + i * (w - 0.4f) / 5, 1.6f, -d / 2 + 0.145f), new(0.07f, 0.05f, 0.02f), Led());
                // upright screens
                b.Col(steel * 0.55f); b.BoxC(new(0, 1.3f, -d / 2 + 0.08f), new(w, 0.55f, 0.12f));
                int screens = w > 2 ? 3 : 2;
                for (int i = 0; i < screens; i++)
                {
                    float x = -w / 2 + (i + 0.5f) * w / screens;
                    b.Col(0.05f, 0.05f, 0.06f); b.BoxC(new(x, 1.32f, -d / 2 + 0.145f), new(w / screens - 0.15f, 0.42f, 0.01f));
                    ScreenFace(new(x, 1.32f, -d / 2 + 0.152f), new(w / screens - 0.25f, 0.34f, 0.005f), Screen());
                }
                break;
            }
            case PropKind.ConsoleBank:
            {
                b.Col(dark); b.BoxC(new(0, 0.55f, 0), new(0.8f, 1.1f, 0.8f));
                for (int k = 0; k < 4; k++)
                {
                    b.Transform = Matrix4x4.CreateRotationX(0.6f) * Matrix4x4.CreateTranslation(0, 1.05f, 0.62f) * Matrix4x4.CreateRotationY(k * MathF.PI / 2) * xf;
                    b.Col(steel * 0.55f); b.BoxC(Vector3.Zero, new(1.4f, 0.08f, 0.6f));
                    b.Col(0.05f, 0.05f, 0.06f); b.BoxC(new(0, 0.045f, 0), new(1.0f, 0.01f, 0.42f));
                    ScreenFace(new(0, 0.05f, 0), new(0.9f, 0.01f, 0.34f), Screen());
                    for (int i = 0; i < 5; i++) BlinkLed(new(-0.6f + i * 0.3f, 0.05f, 0.25f), new(0.06f, 0.02f, 0.04f), Led());
                    b.Transform = Matrix4x4.CreateTranslation(0, 0.5f, 0.75f) * Matrix4x4.CreateRotationY(k * MathF.PI / 2) * xf;
                    b.Col(steel * 0.65f); b.BoxC(Vector3.Zero, new(1.3f, 0.9f, 0.3f));
                }
                b.Transform = xf;
                Emit(new(0, 1.22f, 0), new(0.16f, 0.25f, 0.16f), new(0.3f, 0.6f, 1f), new(1.1f, (float)r.NextDouble()));
                break;
            }
            case PropKind.ServerRack:
            {
                b.Col(0.13f, 0.13f, 0.15f); b.BoxC(new(0, 1.3f, 0), new(w, 2.6f, d));
                b.Col(0.22f, 0.22f, 0.25f);
                for (int i = 0; i < 9; i++) b.BoxC(new(0, 0.25f + i * 0.26f, d / 2 + 0.01f), new(w - 0.15f, 0.2f, 0.02f));
                for (int i = 0; i < 40; i++)
                {
                    int row = r.Next(9);
                    float x = -w / 2 + 0.2f + (float)r.NextDouble() * (w - 0.4f);
                    var lc = r.NextDouble() < 0.7 ? new Vector3(0.2f, 1f, 0.35f) : r.NextDouble() < 0.7 ? new Vector3(0.3f, 0.6f, 1f) : new Vector3(1f, 0.6f, 0.1f);
                    BlinkLed(new(x, 0.25f + row * 0.26f, d / 2 + 0.025f), new(0.035f, 0.035f, 0.01f), lc);
                }
                b.Col(0.05f, 0.05f, 0.05f);
                b.BoxC(new(0, 2.62f, 0), new(w + 0.04f, 0.05f, d + 0.04f));
                break;
            }
            case PropKind.Locker:
            {
                var lc = level <= 7 ? new Vector3(0.32f, 0.38f, 0.3f) : level <= 15 ? new Vector3(0.45f, 0.38f, 0.28f) : new Vector3(0.35f, 0.38f, 0.45f);
                b.Col(lc); b.BoxC(new(0, 1.1f, 0), new(w, 2.2f, d));
                for (int i = 0; i < 4; i++)
                {
                    float x = -w / 2 + (i + 0.5f) * w / 4;
                    b.Col(0.08f, 0.08f, 0.08f); b.BoxC(new(x - w / 8, 1.1f, d / 2 + 0.005f), new(0.02f, 2.1f, 0.01f));
                    for (int v = 0; v < 4; v++) b.BoxC(new(x, 1.85f - v * 0.07f, d / 2 + 0.005f), new(0.35f, 0.025f, 0.01f));
                    b.Col(0.75f, 0.75f, 0.7f); b.BoxC(new(x + 0.18f, 1.15f, d / 2 + 0.02f), new(0.03f, 0.18f, 0.03f));
                    if (r.NextDouble() < 0.25)
                    {   // one door hanging open
                        b.Col(lc * 0.9f);
                        b.Transform = Matrix4x4.CreateTranslation(0.3f, 0, 0) * Matrix4x4.CreateRotationY(-1.2f) * Matrix4x4.CreateTranslation(x - w / 8, 0, d / 2) * xf;
                        b.BoxC(new(-0.3f, 1.1f, 0), new(0.6f, 2.0f, 0.03f));
                        b.Transform = xf;
                    }
                }
                break;
            }
            case PropKind.Shelf:
            {
                b.Col(steel * 0.7f);
                foreach (float x in new[] { -w / 2 + 0.04f, w / 2 - 0.04f })
                    foreach (float z in new[] { -d / 2 + 0.04f, d / 2 - 0.04f })
                        b.BoxC(new(x, 1.0f, z), new(0.06f, 2.0f, 0.06f));
                for (int sIdx = 0; sIdx < 4; sIdx++)
                {
                    float y = 0.12f + sIdx * 0.6f;
                    b.Col(steel * 0.6f); b.BoxC(new(0, y, 0), new(w, 0.04f, d));
                    if (sIdx == 3) break;
                    float x = -w / 2 + 0.15f;
                    while (x < w / 2 - 0.4f)
                    {
                        float bw = 0.25f + (float)r.NextDouble() * 0.45f, bh = 0.2f + (float)r.NextDouble() * 0.3f;
                        int kind = r.Next(4);
                        if (kind == 0) { b.Col(0.55f, 0.42f, 0.28f); b.BoxC(new(x + bw / 2, y + 0.02f + bh / 2, 0), new(bw, bh, d * 0.75f)); }
                        else if (kind == 1) { b.Col(0.3f, 0.35f, 0.2f); b.BoxC(new(x + bw / 2, y + 0.02f + bh * 0.4f, 0), new(bw, bh * 0.8f, d * 0.6f)); }
                        else if (kind == 2) { b.Col(r.NextDouble() < 0.5 ? new Vector3(0.7f, 0.15f, 0.1f) : new Vector3(0.2f, 0.4f, 0.7f)); b.Cylinder(new(x + 0.12f, y + 0.02f, 0), 0.1f, 0.1f, 0.35f, 10); bw = 0.24f; }
                        x += bw + 0.06f;
                    }
                }
                break;
            }
            case PropKind.Bunk:
            {
                b.Col(steel * 0.7f);
                foreach (float x in new[] { -w / 2 + 0.04f, w / 2 - 0.04f })
                    foreach (float z in new[] { -d / 2 + 0.04f, d / 2 - 0.04f })
                        b.BoxC(new(x, 0.85f, z), new(0.06f, 1.7f, 0.06f));
                foreach (float y in new[] { 0.35f, 1.3f })
                {
                    b.Col(steel * 0.6f); b.BoxC(new(0, y, 0), new(w, 0.06f, d));
                    b.Col(0.3f, 0.34f, 0.22f); b.BoxC(new(0, y + 0.1f, 0), new(w - 0.1f, 0.14f, d - 0.08f));
                    b.Col(0.85f, 0.85f, 0.8f); b.BoxC(new(-w / 2 + 0.3f, y + 0.22f, 0), new(0.35f, 0.1f, d - 0.25f));
                    if (r.NextDouble() < 0.5) { b.Col(0.25f, 0.3f, 0.2f); b.BoxC(new(0.3f, y + 0.19f, 0.05f), new(1.2f, 0.04f, d - 0.1f)); }
                }
                break;
            }
            case PropKind.Barrels:
            {
                int n = r.Next(2, 4);
                Vector3[] spots = { new(-0.35f, 0, -0.3f), new(0.35f, 0, 0.3f), new(0.35f, 0, -0.35f), new(-0.3f, 0, 0.35f) };
                for (int i = 0; i < n; i++)
                {
                    var sp = spots[i];
                    int kind = r.Next(4);
                    var col = kind switch { 0 => new Vector3(0.75f, 0.6f, 0.1f), 1 => new Vector3(0.2f, 0.3f, 0.55f), 2 => new Vector3(0.42f, 0.43f, 0.42f), _ => new Vector3(0.25f, 0.4f, 0.2f) };
                    b.Col(col); b.Cylinder(sp, 0.33f, 0.33f, 1.1f, 14);
                    b.Col(col * 0.6f); b.Cylinder(sp + new Vector3(0, 0.3f, 0), 0.345f, 0.345f, 0.05f, 14, false); b.Cylinder(sp + new Vector3(0, 0.75f, 0), 0.345f, 0.345f, 0.05f, 14, false);
                    if (kind == 3) { b.Col(0.4f, 1f, 0.2f, 1f); b.Cylinder(sp + new Vector3(0, 1.1f, 0), 0.27f, 0.27f, 0.01f, 12); }
                    if (kind == 0) { b.Col(0.08f, 0.08f, 0.08f); b.Cylinder(sp + new Vector3(0, 0.5f, 0), 0.335f, 0.335f, 0.12f, 14, false); }
                }
                break;
            }
            case PropKind.Tank when p.Dead:
            {
                // burst open: frame, a ring of jagged glass and a drained, dripping interior
                b.Col(dark); b.Cylinder(new(0, 0, 0), 0.9f, 0.9f, 0.35f, 18);
                b.Col(dark); b.Cylinder(new(0, 3.05f, 0), 0.9f, 0.85f, 0.35f, 18);
                b.Col(0.35f, 0.38f, 0.4f); b.Cylinder(new(0, 3.4f, 0), 0.12f, 0.12f, Level.WH - 3.4f, 8, false);
                b.Col(0.2f, 0.7f, 0.35f, 0.5f); b.Cylinder(new(0, 0.35f, 0), 0.72f, 0.72f, 0.06f, 18);
                for (int i = 0; i < 9; i++)
                {
                    float a = i * MathF.Tau / 9 + (float)r.NextDouble() * 0.3f, hgt = 0.25f + (float)r.NextDouble() * 0.9f;
                    glass.Transform = Matrix4x4.CreateRotationZ(((float)r.NextDouble() - 0.5f) * 0.5f) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(MathF.Cos(a) * 0.78f, 0.35f + hgt / 2, MathF.Sin(a) * 0.78f) * xf;
                    glass.Col(0.7f, 0.9f, 1f); glass.BoxC(Vector3.Zero, new(0.03f, hgt, 0.3f + (float)r.NextDouble() * 0.2f));
                    hgt = 0.15f + (float)r.NextDouble() * 0.5f;
                    glass.Transform = Matrix4x4.CreateRotationZ(MathF.PI + ((float)r.NextDouble() - 0.5f) * 0.5f) * Matrix4x4.CreateRotationY(-a - 0.3f) * Matrix4x4.CreateTranslation(MathF.Cos(a + 0.3f) * 0.78f, 3.05f - hgt / 2, MathF.Sin(a + 0.3f) * 0.78f) * xf;
                    glass.BoxC(Vector3.Zero, new(0.03f, hgt, 0.25f));
                }
                glass.Transform = xf;
                break;
            }
            case PropKind.Tank:
            {
                b.Col(dark); b.Cylinder(new(0, 0, 0), 0.9f, 0.9f, 0.35f, 18);
                b.Col(dark); b.Cylinder(new(0, 3.05f, 0), 0.9f, 0.85f, 0.35f, 18);
                b.Col(0.35f, 0.38f, 0.4f); b.Cylinder(new(0, 3.4f, 0), 0.12f, 0.12f, Level.WH - 3.4f, 8, false);
                b.Col(0.25f, 0.9f, 0.45f, 0.75f); b.Cylinder(new(0, 0.35f, 0), 0.72f, 0.72f, 2.5f, 18);
                // specimen silhouette
                b.Col(0.1f, 0.18f, 0.12f);
                b.Sphere(new(0, 1.7f, 0), new(0.35f, 0.6f, 0.3f), 10, 8);
                b.Sphere(new(0, 2.35f, -0.05f), new(0.22f, 0.28f, 0.3f), 10, 8);
                b.Transform = Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(0.25f, 1.9f, 0) * xf; b.Cylinder(Vector3.Zero, 0.06f, 0.05f, 0.6f, 6);
                b.Transform = Matrix4x4.CreateRotationZ(-0.5f) * Matrix4x4.CreateTranslation(-0.25f, 1.9f, 0) * xf; b.Cylinder(Vector3.Zero, 0.06f, 0.05f, 0.6f, 6);
                b.Transform = xf;
                glass.Col(0.7f, 0.9f, 1f); glass.Cylinder(new(0, 0.35f, 0), 0.8f, 0.8f, 2.7f, 20, false);
                break;
            }
            case PropKind.Pod:
            {
                var flesh = new Vector3(0.38f, 0.16f, 0.36f);
                b.Col(flesh * 0.7f); b.Sphere(new(0, 0.03f, 0), new(1.5f, 0.12f, 1.5f), 14, 6);
                int n = r.Next(2, 5);
                for (int i = 0; i < n; i++)
                {
                    float a = i * MathF.Tau / n + (float)r.NextDouble();
                    float rr = n == 1 ? 0 : 0.35f;
                    var c = new Vector3(MathF.Cos(a) * rr, 0, MathF.Sin(a) * rr);
                    float h = 0.8f + (float)r.NextDouble() * 0.45f;
                    b.Col(flesh * (0.9f + (float)r.NextDouble() * 0.3f)); b.Sphere(c + new Vector3(0, h / 2, 0), new(0.42f, h, 0.42f), 12, 8);
                    b.Col(0.4f, 1f, 0.35f, 1f); b.Sphere(c + new Vector3(0, h - 0.04f, 0), new(0.2f, 0.08f, 0.2f), 10, 4);
                }
                for (int i = 0; i < 5; i++)
                {
                    float a = (float)r.NextDouble() * MathF.Tau;
                    b.Col(flesh * 0.6f);
                    b.Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2 - 0.15f) * Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(0, 0.05f, 0) * xf;
                    b.Cylinder(Vector3.Zero, 0.07f, 0.02f, 0.75f, 6);
                    b.Transform = xf;
                }
                break;
            }
        }
        b.Transform = Matrix4x4.Identity;
        glass.Transform = Matrix4x4.Identity;
        blink.Transform = Matrix4x4.Identity;
    }
}
