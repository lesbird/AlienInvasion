using System.Numerics;
using Silk.NET.OpenGL;

namespace AlienInvasion.Engine;

/// <summary>
/// HDR bloom. The scene renders into a multisampled RGBA16F framebuffer, which is resolved,
/// bright-passed into a 1/4-res texture, then blurred at 1/4, 1/8 and 1/16 resolution
/// (separable gaussian) and added back on top of the scene in the default framebuffer.
/// </summary>
public unsafe class Bloom : IDisposable
{
    const string VS = @"#version 330 core
out vec2 vUV;
void main(){
  vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);   // fullscreen triangle
  vUV = p;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

    // 4-tap box downsample; with uThreshold >= 0 also a soft-knee bright pass
    const string DownFS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform vec2 uTexel; uniform float uThreshold;
out vec4 FragColor;
void main(){
  vec3 c = texture(uTex, vUV + uTexel * vec2(-0.5, -0.5)).rgb + texture(uTex, vUV + uTexel * vec2(0.5, -0.5)).rgb
         + texture(uTex, vUV + uTexel * vec2(-0.5, 0.5)).rgb + texture(uTex, vUV + uTexel * vec2(0.5, 0.5)).rgb;
  c *= 0.25;
  if (uThreshold >= 0.0) {
    float br = max(c.r, max(c.g, c.b));
    float knee = 0.35;
    float soft = clamp(br - uThreshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-4);
    c *= max(soft, br - uThreshold) / max(br, 1e-4);
  }
  FragColor = vec4(min(c, vec3(16.0)), 1.0);
}";

    // 9-tap gaussian folded into 5 bilinear taps
    const string BlurFS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uTex; uniform vec2 uDir;
out vec4 FragColor;
void main(){
  vec3 c = texture(uTex, vUV).rgb * 0.2270270;
  c += (texture(uTex, vUV + uDir * 1.3846154).rgb + texture(uTex, vUV - uDir * 1.3846154).rgb) * 0.3162162;
  c += (texture(uTex, vUV + uDir * 3.2307692).rgb + texture(uTex, vUV - uDir * 3.2307692).rgb) * 0.0702703;
  FragColor = vec4(c, 1.0);
}";

    const string CompositeFS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uScene, uB0, uB1, uB2; uniform float uIntensity;
out vec4 FragColor;
void main(){
  vec3 b = texture(uB0, vUV).rgb * 0.5 + texture(uB1, vUV).rgb * 0.8 + texture(uB2, vUV).rgb * 1.1;
  FragColor = vec4(texture(uScene, vUV).rgb + b * uIntensity, 1.0);
}";

    const int Levels = 3, Samples = 4;
    readonly GL gl;
    readonly Shader down, blur, composite;
    readonly uint vao, msFbo, msColor, msDepth, sceneFbo, sceneTex;
    readonly uint[] fbo = new uint[Levels * 2], tex = new uint[Levels * 2];   // ping-pong pair per level
    readonly int[] lw = new int[Levels], lh = new int[Levels];
    int w, h;
    public float Threshold = 0.72f, Intensity = 0.9f;

    public Bloom(GL gl)
    {
        this.gl = gl;
        down = new Shader(gl, VS, DownFS);
        blur = new Shader(gl, VS, BlurFS);
        composite = new Shader(gl, VS, CompositeFS);
        vao = gl.GenVertexArray();
        msFbo = gl.GenFramebuffer();
        msColor = gl.GenRenderbuffer();
        msDepth = gl.GenRenderbuffer();
        sceneFbo = gl.GenFramebuffer();
        sceneTex = gl.GenTexture();
        for (int i = 0; i < fbo.Length; i++) { fbo[i] = gl.GenFramebuffer(); tex[i] = gl.GenTexture(); }
    }

    void Resize(int nw, int nh)
    {
        w = nw; h = nh;
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msColor);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, InternalFormat.R11fG11fB10f, (uint)w, (uint)h);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msDepth);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, InternalFormat.DepthComponent24, (uint)w, (uint)h);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, msFbo);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msColor);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, msDepth);
        Check("scene (msaa)");

        AllocTex(sceneTex, w, h);
        Attach(sceneFbo, sceneTex, "scene");
        for (int l = 0; l < Levels; l++)
        {
            lw[l] = Math.Max(1, w >> (l + 2));
            lh[l] = Math.Max(1, h >> (l + 2));
            for (int k = 0; k < 2; k++)
            {
                AllocTex(tex[l * 2 + k], lw[l], lh[l]);
                Attach(fbo[l * 2 + k], tex[l * 2 + k], "bloom");
            }
        }
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    void AllocTex(uint t, int tw, int th)
    {
        gl.BindTexture(TextureTarget.Texture2D, t);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R11fG11fB10f, (uint)tw, (uint)th, 0, PixelFormat.Rgb, PixelType.HalfFloat, (void*)0);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    void Attach(uint f, uint t, string what)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, f);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, t, 0);
        Check(what);
    }

    void Check(string what)
    {
        var st = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (st != GLEnum.FramebufferComplete) throw new Exception($"Bloom {what} framebuffer incomplete: {st}");
    }

    /// <summary>Bind the offscreen scene target (call before drawing the 3D scene).</summary>
    public void Begin(int fbW, int fbH)
    {
        if (fbW != w || fbH != h) Resize(fbW, fbH);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, msFbo);
        gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        gl.Viewport(0, 0, (uint)w, (uint)h);
    }

    /// <summary>Resolve, blur the bright parts and composite into the default framebuffer.</summary>
    public void End()
    {
        // resolve MSAA
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msFbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, sceneFbo);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.DepthMask(false);
        gl.BindVertexArray(vao);
        gl.ActiveTexture(TextureUnit.Texture0);

        // bright pass / downsample chain
        down.Use();
        down.Set("uTex", 0);
        uint src = sceneTex;
        int sw = w, sh = h;
        for (int l = 0; l < Levels; l++)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo[l * 2]);
            gl.Viewport(0, 0, (uint)lw[l], (uint)lh[l]);
            gl.BindTexture(TextureTarget.Texture2D, src);
            down.Set("uTexel", new Vector2(1f / sw, 1f / sh) * (l == 0 ? 2f : 1f));
            down.Set("uThreshold", l == 0 ? Threshold : -1f);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            src = tex[l * 2];
            sw = lw[l]; sh = lh[l];
        }
        // blur each level (horizontal into the pair, vertical back)
        blur.Use();
        blur.Set("uTex", 0);
        for (int l = 0; l < Levels; l++)
        {
            gl.Viewport(0, 0, (uint)lw[l], (uint)lh[l]);
            for (int pass = 0; pass < 2; pass++)
            {
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo[l * 2 + 1]);
                gl.BindTexture(TextureTarget.Texture2D, tex[l * 2]);
                blur.Set("uDir", new Vector2(1f / lw[l], 0));
                gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo[l * 2]);
                gl.BindTexture(TextureTarget.Texture2D, tex[l * 2 + 1]);
                blur.Set("uDir", new Vector2(0, 1f / lh[l]));
                gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }
        // composite
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)w, (uint)h);
        composite.Use();
        composite.Set("uScene", 0); composite.Set("uB0", 1); composite.Set("uB1", 2); composite.Set("uB2", 3);
        composite.Set("uIntensity", Intensity);
        gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D, sceneTex);
        for (int l = 0; l < Levels; l++) { gl.ActiveTexture(TextureUnit.Texture1 + l); gl.BindTexture(TextureTarget.Texture2D, tex[l * 2]); }
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindVertexArray(0);
        gl.DepthMask(true);
    }

    public void Dispose()
    {
        gl.DeleteFramebuffer(msFbo); gl.DeleteFramebuffer(sceneFbo);
        gl.DeleteRenderbuffer(msColor); gl.DeleteRenderbuffer(msDepth);
        gl.DeleteTexture(sceneTex);
        foreach (var f in fbo) gl.DeleteFramebuffer(f);
        foreach (var t in tex) gl.DeleteTexture(t);
        gl.DeleteVertexArray(vao);
    }
}
