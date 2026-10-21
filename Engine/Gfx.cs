using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace AlienInvasion.Engine;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Pos;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Color; // rgb = color, a = glow (self illumination amount)

    public Vertex(Vector3 p, Vector3 n, Vector2 uv, Vector4 c) { Pos = p; Normal = n; UV = uv; Color = c; }
    public const int Stride = 12 * sizeof(float);
}

public unsafe class Shader
{
    public readonly uint Handle;
    readonly GL gl;
    readonly Dictionary<string, int> locs = new();

    public Shader(GL gl, string vs, string fs)
    {
        this.gl = gl;
        uint v = Compile(ShaderType.VertexShader, vs);
        uint f = Compile(ShaderType.FragmentShader, fs);
        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, v);
        gl.AttachShader(Handle, f);
        gl.BindAttribLocation(Handle, 0, "aPos");
        gl.BindAttribLocation(Handle, 1, "aNormal");
        gl.BindAttribLocation(Handle, 2, "aUV");
        gl.BindAttribLocation(Handle, 3, "aColor");
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0) throw new Exception("Shader link failed: " + gl.GetProgramInfoLog(Handle));
        gl.DeleteShader(v);
        gl.DeleteShader(f);
    }

    uint Compile(ShaderType type, string src)
    {
        uint s = gl.CreateShader(type);
        gl.ShaderSource(s, src);
        gl.CompileShader(s);
        gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"{type} compile failed: " + gl.GetShaderInfoLog(s));
        return s;
    }

    int Loc(string n)
    {
        if (!locs.TryGetValue(n, out int l)) { l = gl.GetUniformLocation(Handle, n); locs[n] = l; }
        return l;
    }

    public void Use() => gl.UseProgram(Handle);
    public void Set(string n, float v) => gl.Uniform1(Loc(n), v);
    public void Set(string n, int v) => gl.Uniform1(Loc(n), v);
    public void Set(string n, Vector2 v) => gl.Uniform2(Loc(n), v.X, v.Y);
    public void Set(string n, Vector3 v) => gl.Uniform3(Loc(n), v.X, v.Y, v.Z);
    public void Set(string n, Vector4 v) => gl.Uniform4(Loc(n), v.X, v.Y, v.Z, v.W);
    public void Set(string n, Matrix4x4 m) => gl.UniformMatrix4(Loc(n), 1, false, (float*)&m);
    public void SetArray(string n, Vector3[] arr, int count)
    {
        if (count <= 0) return;
        fixed (Vector3* p = arr) gl.Uniform3(Loc(n), (uint)count, (float*)p);
    }
    public void SetArray(string n, Vector4[] arr, int count)
    {
        if (count <= 0) return;
        fixed (Vector4* p = arr) gl.Uniform4(Loc(n), (uint)count, (float*)p);
    }
    public void SetArray(string n, float[] arr, int count)
    {
        if (count <= 0) return;
        fixed (float* p = arr) gl.Uniform1(Loc(n), (uint)count, p);
    }
}

public unsafe class Mesh : IDisposable
{
    readonly GL gl;
    uint vao, vbo, ebo;
    public int IndexCount;
    public Vector3 BoundsMin, BoundsMax;

    public Mesh(GL gl)
    {
        this.gl = gl;
        vao = gl.GenVertexArray();
        vbo = gl.GenBuffer();
        ebo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vertex.Stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Vertex.Stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Vertex.Stride, (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, Vertex.Stride, (void*)(8 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public Mesh(GL gl, List<Vertex> verts, List<uint> idx) : this(gl)
    {
        Upload(verts, idx, false);
    }

    public void Upload(List<Vertex> verts, List<uint> idx, bool stream)
    {
        var usage = stream ? BufferUsageARB.StreamDraw : BufferUsageARB.StaticDraw;
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        var vs = CollectionsMarshal.AsSpan(verts);
        var ix = CollectionsMarshal.AsSpan(idx);
        fixed (Vertex* pv = vs)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vs.Length * Vertex.Stride), pv, usage);
        fixed (uint* pi = ix)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(ix.Length * sizeof(uint)), pi, usage);
        IndexCount = ix.Length;
        gl.BindVertexArray(0);
        if (!stream && vs.Length > 0)
        {
            BoundsMin = new Vector3(float.MaxValue);
            BoundsMax = new Vector3(float.MinValue);
            foreach (var v in vs) { BoundsMin = Vector3.Min(BoundsMin, v.Pos); BoundsMax = Vector3.Max(BoundsMax, v.Pos); }
        }
    }

    public void Draw()
    {
        if (IndexCount == 0) return;
        gl.BindVertexArray(vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        gl.DeleteVertexArray(vao);
        gl.DeleteBuffer(vbo);
        gl.DeleteBuffer(ebo);
    }
}

public unsafe class Texture : IDisposable
{
    readonly GL gl;
    public readonly uint Handle;
    public readonly int Width, Height;

    public Texture(GL gl, int w, int h, byte[] rgba, bool mipmap = true, bool nearest = false, bool clamp = false)
    {
        this.gl = gl;
        Width = w; Height = h;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);
        fixed (byte* p = rgba)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        var wrap = clamp ? (int)GLEnum.ClampToEdge : (int)GLEnum.Repeat;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        if (nearest)
        {
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        }
        else
        {
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, mipmap ? (int)GLEnum.LinearMipmapLinear : (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        }
        if (mipmap) gl.GenerateMipmap(TextureTarget.Texture2D);
    }

    public void Bind(int unit = 0)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => gl.DeleteTexture(Handle);
}

/// <summary>Accumulates triangles; optional transform applied to everything added.</summary>
public class MeshBuilder
{
    public readonly List<Vertex> V = new();
    public readonly List<uint> I = new();
    public Vector4 Color = new(1, 1, 1, 0);
    public Matrix4x4 Transform = Matrix4x4.Identity;

    public void Clear() { V.Clear(); I.Clear(); Transform = Matrix4x4.Identity; Color = new(1, 1, 1, 0); }

    public MeshBuilder Col(float r, float g, float b, float glow = 0) { Color = new Vector4(r, g, b, glow); return this; }
    public MeshBuilder Col(Vector3 c, float glow = 0) { Color = new Vector4(c, glow); return this; }

    uint Add(Vector3 p, Vector3 n, Vector2 uv)
    {
        var tp = Vector3.Transform(p, Transform);
        var tn = Vector3.TransformNormal(n, Transform);
        float l = tn.Length();
        if (l > 1e-6f) tn /= l;
        V.Add(new Vertex(tp, tn, uv, Color));
        return (uint)(V.Count - 1);
    }

    /// <summary>Quad a-b-c-d (any consistent order), single normal.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        uint i0 = Add(a, n, ta), i1 = Add(b, n, tb), i2 = Add(c, n, tc), i3 = Add(d, n, td);
        I.Add(i0); I.Add(i1); I.Add(i2);
        I.Add(i0); I.Add(i2); I.Add(i3);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float uScale = 1, float vScale = 1)
        => Quad(a, b, c, d, n, new(0, vScale), new(uScale, vScale), new(uScale, 0), new(0, 0));

    /// <summary>Axis-aligned box between min and max (in local space before Transform).</summary>
    public void Box(Vector3 min, Vector3 max, float uvScale = 0)
    {
        var s = max - min;
        float su = uvScale > 0 ? uvScale : 1, sx = uvScale > 0 ? s.X * su : 1, sy = uvScale > 0 ? s.Y * su : 1, sz = uvScale > 0 ? s.Z * su : 1;
        Vector3 p000 = min, p100 = new(max.X, min.Y, min.Z), p010 = new(min.X, max.Y, min.Z), p110 = new(max.X, max.Y, min.Z);
        Vector3 p001 = new(min.X, min.Y, max.Z), p101 = new(max.X, min.Y, max.Z), p011 = new(min.X, max.Y, max.Z), p111 = max;
        Quad(p001, p101, p111, p011, Vector3.UnitZ, sx, sy);       // +Z
        Quad(p100, p000, p010, p110, -Vector3.UnitZ, sx, sy);      // -Z
        Quad(p101, p100, p110, p111, Vector3.UnitX, sz, sy);       // +X
        Quad(p000, p001, p011, p010, -Vector3.UnitX, sz, sy);      // -X
        Quad(p011, p111, p110, p010, Vector3.UnitY, sx, sz);       // +Y
        Quad(p000, p100, p101, p001, -Vector3.UnitY, sx, sz);      // -Y
    }

    public void BoxC(Vector3 center, Vector3 size, float uvScale = 0) => Box(center - size * 0.5f, center + size * 0.5f, uvScale);

    public void Sphere(Vector3 c, Vector3 radii, int seg = 14, int rings = 10)
    {
        uint start = (uint)V.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings;
            for (int s = 0; s <= seg; s++)
            {
                float th = MathF.Tau * s / seg;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(th), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(th));
                var p = c + n * radii;
                var nn = Vector3.Normalize(n / Vector3.Max(radii, new Vector3(1e-4f)));
                Add(p, nn, new Vector2((float)s / seg, (float)r / rings));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                uint a = start + (uint)(r * (seg + 1) + s), b = a + (uint)(seg + 1);
                I.Add(a); I.Add(b); I.Add(a + 1);
                I.Add(a + 1); I.Add(b); I.Add(b + 1);
            }
    }

    /// <summary>Cylinder/cone along +Y from y0 to y1 centered at (cx,cz). r0 bottom, r1 top radius.</summary>
    public void Cylinder(Vector3 bottomCenter, float r0, float r1, float h, int seg = 12, bool caps = true)
    {
        uint start = (uint)V.Count;
        float slope = (r0 - r1) / MathF.Max(h, 1e-4f);
        for (int s = 0; s <= seg; s++)
        {
            float th = MathF.Tau * s / seg;
            float cx = MathF.Cos(th), cz = MathF.Sin(th);
            var n = Vector3.Normalize(new Vector3(cx, slope, cz));
            Add(bottomCenter + new Vector3(cx * r0, 0, cz * r0), n, new Vector2((float)s / seg, 1));
            Add(bottomCenter + new Vector3(cx * r1, h, cz * r1), n, new Vector2((float)s / seg, 0));
        }
        for (int s = 0; s < seg; s++)
        {
            uint a = start + (uint)(s * 2);
            I.Add(a); I.Add(a + 1); I.Add(a + 2);
            I.Add(a + 2); I.Add(a + 1); I.Add(a + 3);
        }
        if (!caps) return;
        for (int cap = 0; cap < 2; cap++)
        {
            float r = cap == 0 ? r0 : r1;
            if (r <= 0.0001f) continue;
            float y = cap == 0 ? 0 : h;
            var n = cap == 0 ? -Vector3.UnitY : Vector3.UnitY;
            uint ci = Add(bottomCenter + new Vector3(0, y, 0), n, new Vector2(0.5f, 0.5f));
            uint first = (uint)V.Count;
            for (int s = 0; s <= seg; s++)
            {
                float th = MathF.Tau * s / seg;
                Add(bottomCenter + new Vector3(MathF.Cos(th) * r, y, MathF.Sin(th) * r), n, new Vector2(0.5f + MathF.Cos(th) * 0.5f, 0.5f + MathF.Sin(th) * 0.5f));
            }
            for (int s = 0; s < seg; s++) { I.Add(ci); I.Add(first + (uint)s); I.Add(first + (uint)s + 1); }
        }
    }

    /// <summary>Cylinder along -Z (forward in view space) starting at 'start'.</summary>
    public void CylinderZ(Vector3 start, float r0, float r1, float len, int seg = 12)
    {
        var saved = Transform;
        Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(start) * saved;
        Cylinder(Vector3.Zero, r0, r1, len, seg);
        Transform = saved;
    }

    public void Torus(float R, float r, int seg = 32, int sides = 8)
    {
        uint start = (uint)V.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = MathF.Tau * i / seg;
            var center = new Vector3(MathF.Cos(a) * R, MathF.Sin(a) * R, 0);
            var radial = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            for (int j = 0; j <= sides; j++)
            {
                float b = MathF.Tau * j / sides;
                var n = radial * MathF.Cos(b) + Vector3.UnitZ * MathF.Sin(b);
                Add(center + n * r, n, new Vector2((float)i / seg * 4, (float)j / sides));
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                uint a = start + (uint)(i * (sides + 1) + j), b = a + (uint)(sides + 1);
                I.Add(a); I.Add(b); I.Add(a + 1);
                I.Add(a + 1); I.Add(b); I.Add(b + 1);
            }
    }

    public Mesh Build(GL gl) => new Mesh(gl, V, I);
}

public static class GMath
{
    /// <summary>OpenGL style perspective (clip z in -1..1).</summary>
    public static Matrix4x4 Perspective(float fovY, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fovY / 2);
        var m = new Matrix4x4();
        m.M11 = f / aspect;
        m.M22 = f;
        m.M33 = (far + near) / (near - far);
        m.M34 = -1;
        m.M43 = 2 * far * near / (near - far);
        return m;
    }

    public static Matrix4x4 Ortho(float l, float r, float b, float t)
    {
        var m = Matrix4x4.Identity;
        m.M11 = 2 / (r - l);
        m.M22 = 2 / (t - b);
        m.M33 = -1;
        m.M41 = -(r + l) / (r - l);
        m.M42 = -(t + b) / (t - b);
        return m;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    public static float Approach(float v, float target, float delta) => v < target ? MathF.Min(v + delta, target) : MathF.Max(v - delta, target);
    public static float AngleDiff(float a, float b)
    {
        float d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
    public static Vector3 DirFromYawPitch(float yaw, float pitch)
        => new(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
}
