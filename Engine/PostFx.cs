using System.Numerics;
using Silk.NET.OpenGL;

namespace AlienInvasion.Engine;

/// <summary>Per-frame settings for the post-processing composite.</summary>
public class PostParams
{
    public bool Bloom = true, AO = true;
    public float BloomIntensity = 0.9f;
    // camera, for rebuilding view-space positions from depth (SSAO)
    public Vector2 TanHalf;
    public float Near, Far;
    // colour grade
    public Vector3 Lift = Vector3.Zero, Gain = Vector3.One;
    public float Saturation = 1, Contrast = 1;
    public float Vignette = 0.35f, Lens = 0.06f;
    // player feedback
    public float Hurt, LowHealth, Desaturate, Chroma;
    public float GoggleT = -1;   // 0..1 while goggles power up or down, else -1
    public float Time;
}

/// <summary>
/// The scene renders into a multisampled HDR framebuffer: the world into depth range [0.01, 1] and the
/// first-person weapon into [0, 0.01], so one depth copy at the end serves SSAO, the soft distortion
/// sprites and the weapon mask (a mid-frame copy would split the render pass, which is slow on Metal).
/// Finish() then builds bloom (bright pass + 3 blur levels), screen-space ambient occlusion and a
/// distortion buffer (shockwaves, heat haze, portal swirl), and composites everything with lens
/// distortion, chromatic aberration, colour grading, vignette, damage edges and the goggle power-up wipe.
/// </summary>
public unsafe class PostFx : IDisposable
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

    // half-res SSAO from depth only (normals from position derivatives), spiral of 12 taps
    const string AoFS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uDepth; uniform vec2 uTanHalf; uniform float uNear, uFar, uRadius;
out vec4 FragColor;
// the world is drawn into depth range [0.01, 1]; the first-person weapon into [0, 0.01]
float linZ(float d) { float z = (d - 0.01) / 0.99 * 2.0 - 1.0; return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear)); }
vec3 viewPos(vec2 uv) { float z = linZ(texture(uDepth, uv).r); return vec3((uv * 2.0 - 1.0) * uTanHalf * z, -z); }
void main(){
  float d = texture(uDepth, vUV).r;
  if (d >= 0.99999 || d < 0.0101) { FragColor = vec4(1.0); return; }
  vec3 P = viewPos(vUV);
  vec3 N = normalize(cross(dFdx(P), dFdy(P)));
  if (dot(N, P) > 0.0) N = -N;
  float z = -P.z;
  float rs = uRadius / (z * uTanHalf.y) * 0.5;               // radius in uv (vertical)
  vec2 aspect = vec2(uTanHalf.y / uTanHalf.x, 1.0);
  float rot = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) * 6.2831;
  float occ = 0.0;
  const int N_TAPS = 12;
  for (int i = 0; i < N_TAPS; i++) {
    float a = rot + float(i) * 2.39996;
    float r = (float(i) + 0.5) / float(N_TAPS);
    vec2 off = vec2(cos(a), sin(a)) * aspect * r * rs;
    vec3 v = viewPos(vUV + off) - P;
    float vv = dot(v, v);
    if (vv > uRadius * uRadius * 4.0) continue;
    occ += max(0.0, dot(v, N) - 0.015 * z) / (vv + 0.02) * uRadius;
  }
  float ao = clamp(1.0 - occ / float(N_TAPS) * 0.9, 0.0, 1.0);
  ao = mix(ao, 1.0, smoothstep(22.0, 40.0, z));
  FragColor = vec4(vec3(ao), 1.0);
}";

    const string CompositeFS = @"#version 330 core
in vec2 vUV;
uniform sampler2D uScene, uB0, uB1, uB2, uAO, uDist, uVM;
uniform float uBloom, uAOOn;
uniform vec3 uLift, uGain; uniform float uSat, uContrast, uVig, uLens;
uniform float uHurt, uLow, uDesat, uChroma, uGoggle, uTime, uAspect;
out vec4 FragColor;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main(){
  vec2 c = vUV - 0.5;
  // gentle barrel distortion, scaled so the corners stay on screen
  vec2 uv = 0.5 + c * (1.0 + uLens * dot(c, c)) / (1.0 + uLens * 0.5);
  float vm = texture(uVM, uv).r < 0.0101 ? 1.0 : 0.0;      // first-person weapon pixel (depth range [0, 0.01])
  vec2 dist = texture(uDist, uv).rg * (1.0 - vm);
  uv += dist;
  vec2 cd = (uv - 0.5) * (uChroma + length(dist) * 1.5);
  vec3 col = vec3(texture(uScene, uv + cd).r, texture(uScene, uv).g, texture(uScene, uv - cd).b);
  if (uBloom > 0.0)
    col += (texture(uB0, uv).rgb * 0.5 + texture(uB1, uv).rgb * 0.8 + texture(uB2, uv).rgb * 1.1) * uBloom;
  if (uAOOn > 0.5) col *= mix(texture(uAO, uv).r, 1.0, vm);

  // colour grade: gain, lift into the shadows, saturation, contrast around a dark pivot
  col *= uGain;
  float lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
  col += uLift * (1.0 - smoothstep(0.0, 0.45, lum));
  lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
  col = mix(vec3(lum), col, uSat * (1.0 - uDesat * 0.85));
  col = max(vec3(0.0), (col - 0.16) * uContrast + 0.16);

  float rr = length(c * vec2(uAspect, 1.0));
  col *= mix(1.0, smoothstep(1.15, 0.3, rr), uVig);
  // damage: red bleeding in from the edges
  float edge = smoothstep(0.3, 1.0, rr);
  float red = clamp(uHurt + uLow, 0.0, 1.0);
  col = mix(col, vec3(0.32, 0.0, 0.0) + col * vec3(0.5, 0.08, 0.08), edge * red * 0.9);

  // goggles powering up/down: a bright scan line wipes down, static below it, brightness flicker
  if (uGoggle >= 0.0) {
    float line = 1.0 - uGoggle;
    float below = step(vUV.y, line);
    float n = hash(floor(vUV * vec2(200.0, 110.0)) + floor(uTime * 40.0));
    col = mix(col, vec3(0.05 + n * 0.22), below * 0.9);
    col += vec3(0.55, 0.85, 1.0) * exp(-abs(vUV.y - line) * 90.0) * 1.6;
    col *= 1.0 + (hash(vec2(floor(uTime * 45.0), 3.0)) - 0.5) * 0.7 * (1.0 - uGoggle);
  }
  FragColor = vec4(col, 1.0);
}";

    const int Levels = 3, Samples = 4;
    readonly GL gl;
    readonly Shader down, blur, ao, composite;
    readonly uint vao, msFbo, msColor, msDepth, sceneFbo, sceneTex;
    readonly uint depthFbo, depthTex, distFbo, distTex;
    readonly uint[] aoFbo = new uint[2], aoTex = new uint[2];
    readonly uint[] fbo = new uint[Levels * 2], tex = new uint[Levels * 2];   // ping-pong pair per level
    readonly int[] lw = new int[Levels], lh = new int[Levels];
    int w, h, hw, hh;
    public float Threshold = 0.72f;
    public uint DepthTexture => depthTex;
    public Vector2 HalfSize => new(hw, hh);

    public PostFx(GL gl)
    {
        this.gl = gl;
        down = new Shader(gl, VS, DownFS);
        blur = new Shader(gl, VS, BlurFS);
        ao = new Shader(gl, VS, AoFS);
        composite = new Shader(gl, VS, CompositeFS);
        vao = gl.GenVertexArray();
        msFbo = gl.GenFramebuffer();
        msColor = gl.GenRenderbuffer();
        msDepth = gl.GenRenderbuffer();
        sceneFbo = gl.GenFramebuffer(); sceneTex = gl.GenTexture();
        depthFbo = gl.GenFramebuffer(); depthTex = gl.GenTexture();
        distFbo = gl.GenFramebuffer(); distTex = gl.GenTexture();
        for (int i = 0; i < 2; i++) { aoFbo[i] = gl.GenFramebuffer(); aoTex[i] = gl.GenTexture(); }
        for (int i = 0; i < fbo.Length; i++) { fbo[i] = gl.GenFramebuffer(); tex[i] = gl.GenTexture(); }
    }

    void Resize(int nw, int nh)
    {
        w = nw; h = nh;
        hw = Math.Max(1, w / 2); hh = Math.Max(1, h / 2);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msColor);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, InternalFormat.R11fG11fB10f, (uint)w, (uint)h);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msDepth);
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, InternalFormat.DepthComponent24, (uint)w, (uint)h);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, msFbo);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msColor);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, msDepth);
        Check("scene (msaa)");

        AllocTex(sceneTex, w, h, InternalFormat.R11fG11fB10f, PixelFormat.Rgb);
        Attach(sceneFbo, sceneTex, "scene");
        AllocDepth(depthFbo, depthTex);
        AllocTex(distTex, hw, hh, InternalFormat.RG16f, PixelFormat.RG);
        Attach(distFbo, distTex, "distortion");
        for (int i = 0; i < 2; i++)
        {
            AllocTex(aoTex[i], hw, hh, InternalFormat.R11fG11fB10f, PixelFormat.Rgb);
            Attach(aoFbo[i], aoTex[i], "ssao");
        }
        for (int l = 0; l < Levels; l++)
        {
            lw[l] = Math.Max(1, w >> (l + 2));
            lh[l] = Math.Max(1, h >> (l + 2));
            for (int k = 0; k < 2; k++)
            {
                AllocTex(tex[l * 2 + k], lw[l], lh[l], InternalFormat.R11fG11fB10f, PixelFormat.Rgb);
                Attach(fbo[l * 2 + k], tex[l * 2 + k], "bloom");
            }
        }
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    void AllocTex(uint t, int tw, int th, InternalFormat fmt, PixelFormat pf)
    {
        gl.BindTexture(TextureTarget.Texture2D, t);
        gl.TexImage2D(TextureTarget.Texture2D, 0, fmt, (uint)tw, (uint)th, 0, pf, PixelType.HalfFloat, (void*)0);
        Params(TextureMinFilter.Linear);
    }

    void AllocDepth(uint f, uint t)
    {
        gl.BindTexture(TextureTarget.Texture2D, t);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, (uint)w, (uint)h, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, (void*)0);
        Params(TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, f);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, t, 0);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        Check("depth copy");
    }

    void Params(TextureMinFilter filter)
    {
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(filter == TextureMinFilter.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
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
        if (st != GLEnum.FramebufferComplete) throw new Exception($"PostFx {what} framebuffer incomplete: {st}");
    }

    /// <summary>Bind the offscreen scene target (call before drawing the 3D scene).</summary>
    public void Begin(int fbW, int fbH)
    {
        if (fbW != w || fbH != h) Resize(fbW, fbH);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, msFbo);
        gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        gl.Viewport(0, 0, (uint)w, (uint)h);
    }

    /// <summary>Resolve colour and depth, then bind the half-res distortion target for additive offset sprites.</summary>
    public void BeginDistortion()
    {
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msFbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, depthFbo);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, sceneFbo);
        gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, distFbo);
        gl.Viewport(0, 0, (uint)hw, (uint)hh);
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
    }

    /// <summary>Bloom, SSAO and the final composite into the default framebuffer, scaled up to outW x outH.</summary>
    public void Finish(PostParams p, int outW, int outH)
    {
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.DepthMask(false);
        gl.BindVertexArray(vao);
        gl.ActiveTexture(TextureUnit.Texture0);

        if (p.Bloom) BuildBloom();
        if (p.AO)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, aoFbo[0]);
            gl.Viewport(0, 0, (uint)hw, (uint)hh);
            ao.Use();
            ao.Set("uDepth", 0);
            ao.Set("uTanHalf", p.TanHalf);
            ao.Set("uNear", p.Near); ao.Set("uFar", p.Far);
            ao.Set("uRadius", 0.55f);
            gl.BindTexture(TextureTarget.Texture2D, depthTex);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            blur.Use();
            blur.Set("uTex", 0);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, aoFbo[1]);
            gl.BindTexture(TextureTarget.Texture2D, aoTex[0]);
            blur.Set("uDir", new Vector2(0.8f / hw, 0));
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, aoFbo[0]);
            gl.BindTexture(TextureTarget.Texture2D, aoTex[1]);
            blur.Set("uDir", new Vector2(0, 0.8f / hh));
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)outW, (uint)outH);
        composite.Use();
        composite.Set("uScene", 0); composite.Set("uB0", 1); composite.Set("uB1", 2); composite.Set("uB2", 3);
        composite.Set("uAO", 4); composite.Set("uDist", 5); composite.Set("uVM", 6);
        composite.Set("uBloom", p.Bloom ? p.BloomIntensity : 0f);
        composite.Set("uAOOn", p.AO ? 1f : 0f);
        composite.Set("uLift", p.Lift); composite.Set("uGain", p.Gain);
        composite.Set("uSat", p.Saturation); composite.Set("uContrast", p.Contrast);
        composite.Set("uVig", p.Vignette); composite.Set("uLens", p.Lens);
        composite.Set("uHurt", p.Hurt); composite.Set("uLow", p.LowHealth);
        composite.Set("uDesat", p.Desaturate); composite.Set("uChroma", p.Chroma);
        composite.Set("uGoggle", p.GoggleT); composite.Set("uTime", p.Time % 600f);
        composite.Set("uAspect", w / (float)Math.Max(1, h));
        Bind(0, sceneTex);
        for (int l = 0; l < Levels; l++) Bind(1 + l, tex[l * 2]);
        Bind(4, aoTex[0]);
        Bind(5, distTex);
        Bind(6, depthTex);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindVertexArray(0);
        gl.DepthMask(true);
    }

    void Bind(int unit, uint t)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(TextureTarget.Texture2D, t);
    }

    void BuildBloom()
    {
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
    }

    public void Dispose()
    {
        foreach (var f in new[] { msFbo, sceneFbo, depthFbo, distFbo }) gl.DeleteFramebuffer(f);
        gl.DeleteRenderbuffer(msColor); gl.DeleteRenderbuffer(msDepth);
        foreach (var t in new[] { sceneTex, depthTex, distTex }) gl.DeleteTexture(t);
        foreach (var f in aoFbo.Concat(fbo)) gl.DeleteFramebuffer(f);
        foreach (var t in aoTex.Concat(tex)) gl.DeleteTexture(t);
        gl.DeleteVertexArray(vao);
    }
}
