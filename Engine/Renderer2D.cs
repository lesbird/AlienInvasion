using System.Numerics;
using Silk.NET.OpenGL;

namespace AlienInvasion.Engine;

/// <summary>Immediate-mode 2D batch for HUD/menus with a built-in 5x7 pixel font.</summary>
public class Renderer2D
{
    const string VS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;
uniform mat4 uProj;
out vec2 vUV; out vec4 vColor;
void main(){ vUV=aUV; vColor=aColor; gl_Position=uProj*vec4(aPos.xy,0,1); }";

    const string FS = @"#version 330 core
in vec2 vUV; in vec4 vColor;
uniform sampler2D uTex;
out vec4 FragColor;
void main(){ FragColor = texture(uTex,vUV)*vColor; }";

    // Classic 5x7 font, column-major, LSB = top row. ASCII 32..95.
    static readonly byte[] FontData =
    {
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14,
        0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x55,0x22,0x50, 0x00,0x05,0x03,0x00,0x00,
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x08,0x2A,0x1C,0x2A,0x08, 0x08,0x08,0x3E,0x08,0x08,
        0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02,
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31,
        0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03,
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00,
        0x00,0x08,0x14,0x22,0x41, 0x14,0x14,0x14,0x14,0x14, 0x41,0x22,0x14,0x08,0x00, 0x02,0x01,0x51,0x09,0x06,
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22,
        0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x01,0x01, 0x3E,0x41,0x41,0x51,0x32,
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41,
        0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x04,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E,
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31,
        0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x7F,0x20,0x18,0x20,0x7F,
        0x63,0x14,0x08,0x14,0x63, 0x03,0x04,0x78,0x04,0x03, 0x61,0x51,0x49,0x45,0x43, 0x00,0x00,0x7F,0x41,0x41,
        0x02,0x04,0x08,0x10,0x20, 0x41,0x41,0x7F,0x00,0x00, 0x04,0x02,0x01,0x02,0x04, 0x40,0x40,0x40,0x40,0x40,
    };

    const int GlyphCount = 64, CellW = 6, CellH = 8, AtlasCols = 16;

    readonly GL gl;
    readonly Shader shader;
    readonly Mesh mesh;
    readonly Texture white, font;
    readonly List<Vertex> verts = new();
    readonly List<uint> idx = new();
    Texture current;
    public float Width, Height;

    public Renderer2D(GL gl)
    {
        this.gl = gl;
        shader = new Shader(gl, VS, FS);
        mesh = new Mesh(gl);
        white = new Texture(gl, 1, 1, TexGen.White(), false, true);
        int aw = AtlasCols * CellW, ah = (GlyphCount / AtlasCols) * CellH;
        var px = new byte[aw * ah * 4];
        for (int g = 0; g < GlyphCount; g++)
        {
            int ox = (g % AtlasCols) * CellW, oy = (g / AtlasCols) * CellH;
            for (int col = 0; col < 5; col++)
            {
                byte bits = FontData[g * 5 + col];
                for (int row = 0; row < 7; row++)
                    if ((bits & (1 << row)) != 0)
                    {
                        int i = ((oy + row) * aw + ox + col) * 4;
                        px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255;
                    }
            }
        }
        font = new Texture(gl, aw, ah, px, false, true, true);
    }

    public void Begin(float w, float h)
    {
        Width = w; Height = h;
        shader.Use();
        shader.Set("uProj", GMath.Ortho(0, w, h, 0));
        shader.Set("uTex", 0);
        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        current = white;
    }

    public void End()
    {
        Flush();
        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
    }

    void Use(Texture t)
    {
        if (current != t) { Flush(); current = t; }
    }

    void Flush()
    {
        if (idx.Count == 0) return;
        current.Bind(0);
        mesh.Upload(verts, idx, true);
        mesh.Draw();
        verts.Clear();
        idx.Clear();
    }

    void PushQuad(float x, float y, float w, float h, float u0, float v0, float u1, float v1, Vector4 c)
    {
        uint b = (uint)verts.Count;
        verts.Add(new Vertex(new(x, y, 0), Vector3.UnitZ, new(u0, v0), c));
        verts.Add(new Vertex(new(x + w, y, 0), Vector3.UnitZ, new(u1, v0), c));
        verts.Add(new Vertex(new(x + w, y + h, 0), Vector3.UnitZ, new(u1, v1), c));
        verts.Add(new Vertex(new(x, y + h, 0), Vector3.UnitZ, new(u0, v1), c));
        idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
    }

    public void Rect(float x, float y, float w, float h, Vector4 c)
    {
        Use(white);
        PushQuad(x, y, w, h, 0, 0, 1, 1, c);
    }

    public void RectOutline(float x, float y, float w, float h, float t, Vector4 c)
    {
        Rect(x, y, w, t, c); Rect(x, y + h - t, w, t, c);
        Rect(x, y + t, t, h - 2 * t, c); Rect(x + w - t, y + t, t, h - 2 * t, c);
    }

    /// <summary>Rotated quad (for map arrows, etc.).</summary>
    public void Tri(Vector2 a, Vector2 b, Vector2 c, Vector4 col)
    {
        Use(white);
        uint i0 = (uint)verts.Count;
        verts.Add(new Vertex(new(a, 0), Vector3.UnitZ, Vector2.Zero, col));
        verts.Add(new Vertex(new(b, 0), Vector3.UnitZ, Vector2.Zero, col));
        verts.Add(new Vertex(new(c, 0), Vector3.UnitZ, Vector2.Zero, col));
        idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2);
    }

    public static float TextWidth(string s, float scale) => s.Length * CellW * scale - scale;

    /// <summary>align: 0 left, 1 center, 2 right.</summary>
    public void Text(string s, float x, float y, float scale, Vector4 c, int align = 0, bool shadow = true)
    {
        if (string.IsNullOrEmpty(s)) return;
        float w = TextWidth(s, scale);
        if (align == 1) x -= w / 2;
        else if (align == 2) x -= w;
        if (shadow) DrawText(s, x + scale, y + scale, scale, new Vector4(0, 0, 0, c.W * 0.8f));
        DrawText(s, x, y, scale, c);
    }

    void DrawText(string s, float x, float y, float scale, Vector4 c)
    {
        Use(font);
        float aw = AtlasCols * CellW, ah = (GlyphCount / AtlasCols) * CellH;
        foreach (char ch0 in s)
        {
            char ch = char.ToUpperInvariant(ch0);
            int g = ch - 32;
            if (g < 0 || g >= GlyphCount) g = '?' - 32;
            if (g != 0)
            {
                float gx = (g % AtlasCols) * CellW, gy = (g / AtlasCols) * CellH;
                PushQuad(x, y, CellW * scale, CellH * scale, gx / aw, gy / ah, (gx + CellW) / aw, (gy + CellH) / ah, c);
            }
            x += CellW * scale;
        }
    }
}
