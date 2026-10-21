using System.Numerics;
using AlienInvasion.Engine;
using Silk.NET.OpenGL;
using Shader = AlienInvasion.Engine.Shader;
using Texture = AlienInvasion.Engine.Texture;

namespace AlienInvasion.Game;

public unsafe class Renderer
{
    const string LitVS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;
uniform mat4 uModel, uView, uProj;
out vec3 vWorld; out vec3 vNormal; out vec2 vUV; out vec4 vColor;
void main(){
  vec4 w = uModel * vec4(aPos, 1.0);
  vWorld = w.xyz;
  vNormal = mat3(uModel) * aNormal;
  vUV = aUV; vColor = aColor;
  gl_Position = uProj * uView * w;
}";

    const string LitFS = @"#version 330 core
in vec3 vWorld; in vec3 vNormal; in vec2 vUV; in vec4 vColor;
uniform sampler2D uTex;
uniform sampler2D uNormalMap; uniform float uBump;
uniform vec4 uTint; uniform float uGlow; uniform float uTexGlow;
uniform int uLightCount;
uniform vec3 uLightPos[32]; uniform vec3 uLightCol[32]; uniform float uLightRad[32];
uniform vec4 uLightSpot[32];   // rotating beacons: xyz = beam dir (mirrored for the 2nd beam), w = cos(cutoff); w <= 0: omni
uniform int uShadowCount;      // lights [0, uShadowCount) have a distance cube map in uShadow0..3
uniform samplerCube uShadow0, uShadow1, uShadow2, uShadow3;
uniform vec3 uAmbient; uniform vec3 uCamPos;
uniform vec3 uFlashPos; uniform vec3 uFlashDir; uniform float uFlashOn;
uniform mat4 uFlashVP; uniform sampler2D uFlashShadow; uniform float uFlashFar; uniform float uFlashShadowOn;
uniform vec3 uFogColor; uniform float uFogDensity;
uniform float uBlink; uniform float uTime;
uniform int uVision;    // 0 normal, 1 night vision, 2 x-ray
uniform float uHot;     // >0.5: flat emissive silhouette in uTint (heat-glowing aliens, x-ray see-through)
uniform vec2 uScreen;
uniform float uPulse;                  // >0.5: glow breathes in waves across the surface (hive growth)
uniform float uXrayWorld;              // >0.5 while drawing level geometry (x-ray blueprint + lenses)
uniform int uLensCount; uniform vec4 uLens[12];   // x-ray: hidden targets, xyz = world pos, w = angular radius
out vec4 FragColor;

float grain() {
  return fract(sin(dot(floor(gl_FragCoord.xy * 0.5) + floor(uTime * 24.0) * vec2(17.0, 31.0), vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
}
// goggle views: night vision = amplified green monochrome with grain and vignette; x-ray = cold blue monochrome
vec3 visionGrade(vec3 col, vec3 albedo) {
  vec2 sc = gl_FragCoord.xy / uScreen - 0.5;
  float vig = smoothstep(0.8, 0.3, length(sc * vec2(uScreen.x / uScreen.y, 1.0)) * 0.9);
  float lum = dot(col, vec3(0.3, 0.59, 0.11));
  if (uVision == 1) {
    lum = lum * 3.4 + dot(albedo, vec3(0.3, 0.59, 0.11)) * 0.2;
    lum = lum / (1.0 + lum) * 1.3;
    return vec3(0.14, 1.0, 0.24) * lum * (0.3 + 0.7 * vig) + vec3(0.1, 0.3, 0.1) * grain() * 0.35;
  }
  lum = lum * 1.7 + 0.03;
  float scan = 0.9 + 0.1 * sin(gl_FragCoord.y * 1.6 + uTime * 8.0);
  return vec3(0.25, 0.5, 1.0) * lum * scan * (0.45 + 0.55 * vig);
}

// grid lines on the surface's dominant plane, antialiased; spacing in metres
float gridLines(vec3 N, float spacing) {
  vec3 a = abs(N);
  vec2 q = (a.y > 0.5 ? vWorld.xz : a.x > 0.5 ? vWorld.zy : vWorld.xy) / spacing;
  vec2 d = abs(fract(q - 0.5) - 0.5) / max(fwidth(q), vec2(1e-4));
  return 1.0 - min(min(d.x, d.y), 1.0);
}
// x-ray goggles on level geometry: faint blueprint grid, an outward sonar ping, and a glowing lens
// cut into any surface that stands between the camera and a hidden alien or keycard
vec3 xrayWorld(vec3 col, vec3 N, float camDist) {
  float near = exp(-camDist * 0.05);
  col += vec3(0.1, 0.28, 0.7) * gridLines(N, 1.0) * 0.32 * near;
  float ping = mod(uTime * 16.0, 64.0);
  col += vec3(0.25, 0.55, 1.0) * exp(-abs(camDist - ping) * 1.2) * (1.0 - ping / 64.0) * 0.55;
  vec3 dirF = (vWorld - uCamPos) / max(camDist, 1e-4);
  float inside = 0.0, rim = 0.0, kMin = 9.0;
  for (int i = 0; i < uLensCount; i++) {
    vec3 T = uLens[i].xyz - uCamPos;
    float dT = length(T);
    if (camDist > dT - 0.4) continue;          // only surfaces in front of the target
    float th = sqrt(max(0.0, 2.0 * (1.0 - dot(dirF, T / dT))));
    float k = th / uLens[i].w;
    if (k > 1.3) continue;
    inside = max(inside, 1.0 - smoothstep(0.82, 1.0, k));
    rim = max(rim, exp(-pow((k - 1.0) * 10.0, 2.0)));
    kMin = min(kMin, k);
  }
  if (kMin < 9.0) {
    col *= mix(1.0, 0.18, inside);                                   // the wall thins out
    col += vec3(0.25, 0.65, 1.0) * gridLines(N, 0.2) * inside * 0.5;  // fine holo mesh
    col += vec3(0.2, 0.5, 1.0) * (0.5 + 0.5 * sin(kMin * 22.0 - uTime * 7.0)) * inside * kMin * 0.22;
    col += vec3(0.45, 0.85, 1.0) * rim * 1.3;                          // bright rim (bloom picks it up)
  }
  return col;
}

float shadowSample(int k, vec3 d) {
  if (k == 0) return textureLod(uShadow0, d, 0.0).r;
  if (k == 1) return textureLod(uShadow1, d, 0.0).r;
  if (k == 2) return textureLod(uShadow2, d, 0.0).r;
  return textureLod(uShadow3, d, 0.0).r;
}
// 5-tap PCF against the stored (distance / far) of the nearest caster
float shadowFactor(int k, vec3 fromLight, float far) {
  float dist = length(fromLight);
  if (dist >= far) return 1.0;
  vec3 n = fromLight / dist;
  vec3 t1 = normalize(cross(n, abs(n.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
  vec3 t2 = cross(n, t1);
  float cur = (dist - 0.07) / far;
  float r = 0.022;
  float s = step(cur, shadowSample(k, n));
  s += step(cur, shadowSample(k, n + t1 * r));
  s += step(cur, shadowSample(k, n - t1 * r));
  s += step(cur, shadowSample(k, n + t2 * r));
  s += step(cur, shadowSample(k, n - t2 * r));
  return s * 0.2;
}

// flashlight: perspective shadow map holding (distance / far) of the nearest caster
float flashShadow(float dist) {
  if (uFlashShadowOn < 0.5) return 1.0;
  vec4 c = uFlashVP * vec4(vWorld, 1.0);
  if (c.w <= 0.0) return 1.0;
  vec2 uv = c.xy / c.w * 0.5 + 0.5;
  if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 1.0;
  float cur = (dist - 0.06 - dist * 0.004) / uFlashFar;
  vec2 tx = vec2(1.5 / 1024.0);
  float s = step(cur, textureLod(uFlashShadow, uv, 0.0).r);
  s += step(cur, textureLod(uFlashShadow, uv + vec2(tx.x, 0.0), 0.0).r);
  s += step(cur, textureLod(uFlashShadow, uv - vec2(tx.x, 0.0), 0.0).r);
  s += step(cur, textureLod(uFlashShadow, uv + vec2(0.0, tx.y), 0.0).r);
  s += step(cur, textureLod(uFlashShadow, uv - vec2(0.0, tx.y), 0.0).r);
  return s * 0.2;
}

void main(){
  vec4 tex = texture(uTex, vUV);
  float texGlow = 0.0;
  if (uTexGlow > 0.5) { texGlow = 1.0 - tex.a; tex.a = 1.0; }
  vec4 base = vec4(tex.rgb * vColor.rgb, tex.a) * uTint;
  if (base.a < 0.02) discard;
  if (uBlink > 0.5) {
    if (vUV.y >= 10.0) {
      // screens: slow breathing, rolling scanlines, the occasional glitch
      float k = 0.86 + 0.07 * sin(uTime * 2.3 + vUV.y * 7.0) + 0.07 * sin(vWorld.y * 70.0 - uTime * 4.0);
      if (fract(sin(floor(uTime * 9.0) * 3.1 + vUV.y * 31.7) * 43758.5453) > 0.985) k *= 0.35;
      base.rgb *= k;
    } else {
      float on;
      if (vUV.x >= 0.0) on = step(0.5, fract(uTime * vUV.x + vUV.y));
      else on = step(0.3, fract(sin(floor(uTime * -vUV.x) * 12.9898 + vUV.y * 78.233) * 43758.5453));
      base.rgb *= mix(0.1, 1.15, on);
    }
  }
  vec3 N = normalize(vNormal);
  vec3 V = uCamPos - vWorld;
  float camDist = length(V);
  V /= max(camDist, 1e-4);
  if (dot(N, V) < 0.0) N = -N;
  if (uHot > 0.5) {
    float rim = 1.0 - abs(dot(N, V));
    vec3 hc = uTint.rgb * (0.55 + 0.9 * rim * rim);
    if (uVision == 1) hc += vec3(0.1, 0.3, 0.1) * grain() * 0.3;
    FragColor = vec4(hc, uTint.a);
    return;
  }
  if (uBump > 0.0) {
    // cotangent frame from screen-space derivatives (no per-vertex tangents needed)
    vec3 dp1 = dFdx(vWorld), dp2 = dFdy(vWorld);
    vec2 duv1 = dFdx(vUV), duv2 = dFdy(vUV);
    vec3 dp2perp = cross(dp2, N), dp1perp = cross(N, dp1);
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = inversesqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
    vec3 tn = texture(uNormalMap, vUV).xyz * 2.0 - 1.0;
    tn.xy *= uBump;
    N = normalize(mat3(T * invmax, B * invmax, N) * tn);
  }
  vec3 light = uAmbient;
  for (int i = 0; i < uLightCount; i++) {
    vec3 L = uLightPos[i] - vWorld;
    float d = length(L);
    float att = clamp(1.0 - d / uLightRad[i], 0.0, 1.0);
    if (att <= 0.0) continue;
    att *= att;
    L /= max(d, 1e-4);
    vec4 sp = uLightSpot[i];
    if (sp.w > 0.0) {
      // two back-to-back reflectors sweeping around the beacon
      float c = max(dot(-L, sp.xyz), dot(-L, vec3(-sp.x, sp.y, -sp.z)));
      att *= 0.08 + 2.6 * smoothstep(sp.w, sp.w + (1.0 - sp.w) * 0.45, c);
    }
    if (i < uShadowCount) {
      att *= shadowFactor(i, vWorld - uLightPos[i], uLightRad[i]);
      if (att <= 0.0) continue;
    }
    float ndl = max(dot(N, L), 0.0) * 0.85 + 0.15;
    vec3 H = normalize(L + V);
    float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.3;
    light += uLightCol[i] * (ndl + spec) * att;
  }
  if (uFlashOn > 0.0) {
    vec3 L = uFlashPos - vWorld;
    float d = length(L);
    L /= max(d, 1e-4);
    float spot = smoothstep(0.82, 0.95, dot(-L, uFlashDir));
    float att = 1.0 / (1.0 + 0.008 * d * d);
    float sh = spot > 0.0 ? flashShadow(d) : 1.0;
    light += vec3(1.0, 0.95, 0.85) * (spot * 1.4 * sh + 0.08) * att * max(dot(N, L), 0.0) * uFlashOn;
  }
  float glow = max(max(vColor.a, uGlow), texGlow);
  if (uPulse > 0.5) glow *= 0.3 + 0.7 * pow(0.5 + 0.5 * sin(uTime * 2.1 - dot(vWorld.xz, vec2(0.45, 0.3)) - vWorld.y * 0.8), 2.0);
  vec3 col = base.rgb * mix(light, vec3(1.0), glow);
  col += base.rgb * texGlow * 0.6;
  col += base.rgb * glow * 0.35;   // emissive surfaces go past 1.0 so the bloom picks them up
  if (uVision != 0) {
    float fogV = exp(-uFogDensity * (uVision == 1 ? 0.35 : 0.8) * camDist);
    col = visionGrade(mix(vec3(0.0), col, clamp(fogV, 0.0, 1.0)), base.rgb);
    if (uVision == 2 && uXrayWorld > 0.5) col = xrayWorld(col, N, camDist);
    FragColor = vec4(col, base.a);
    return;
  }
  float fog = exp(-uFogDensity * camDist);
  col = mix(uFogColor, col, clamp(fog, 0.0, 1.0));
  FragColor = vec4(col, base.a);
}";

    const string PartVS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;
uniform mat4 uView, uProj;
out vec2 vUV; out vec4 vColor; out float vDist;
void main(){
  vec4 v = uView * vec4(aPos, 1.0);
  vDist = length(v.xyz);
  vUV = aUV; vColor = aColor;
  gl_Position = uProj * v;
}";

    const string PartFS = @"#version 330 core
in vec2 vUV; in vec4 vColor; in float vDist;
uniform sampler2D uTex; uniform float uFogDensity; uniform int uVision;
out vec4 FragColor;
void main(){
  float a = texture(uTex, vUV).a * vColor.a * exp(-uFogDensity * vDist * 0.7);
  if (a < 0.003) discard;
  vec3 c = vColor.rgb;
  float lum = dot(c, vec3(0.3, 0.59, 0.11));
  if (uVision == 1) c = vec3(0.3, 1.0, 0.35) * min(1.5, lum * 1.6);
  else if (uVision == 2) c = vec3(0.35, 0.6, 1.0) * lum;
  FragColor = vec4(c, a);
}";

    // screen-distortion sprites, drawn additively into the half-res offset buffer.
    // aNormal = (kind, strength, ignoreDepth); aColor.x = phase (age), aColor.w = fade.
    // kind 0: shockwave ring, 1: heat haze, 2: swirl (portal / teleport)
    const string DistVS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;
uniform mat4 uView, uProj;
out vec2 vUV; out vec3 vP; out vec4 vC; out float vZ;
void main(){
  vec4 v = uView * vec4(aPos, 1.0);
  vZ = -v.z; vUV = aUV; vP = aNormal; vC = aColor;
  gl_Position = uProj * v;
}";

    const string DistFS = @"#version 330 core
in vec2 vUV; in vec3 vP; in vec4 vC; in float vZ;
uniform sampler2D uDepth; uniform vec2 uTarget; uniform float uNear, uFar, uTime;
out vec4 FragColor;
float linZ(float d) { float z = (d - 0.01) / 0.99 * 2.0 - 1.0; return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear)); }
void main(){
  vec2 p = vUV * 2.0 - 1.0;
  float r = length(p);
  if (r > 1.0) discard;
  if (vP.z < 0.5) {
    float sz = linZ(texture(uDepth, gl_FragCoord.xy / uTarget).r);
    if (vZ > sz + 0.25) discard;                        // behind a wall
  }
  vec2 off;
  int kind = int(vP.x + 0.5);
  if (kind == 0) {
    float band = exp(-pow((r - vC.x) * 7.0, 2.0));
    off = p / max(r, 1e-3) * band * (1.0 - vC.x);
  } else if (kind == 1) {
    off = vec2(sin(p.y * 9.0 + uTime * 11.0 + vC.x * 6.0), cos(p.x * 8.0 + uTime * 9.0 + vC.x * 5.0)) * pow(1.0 - r, 2.0);
  } else {
    vec2 t = vec2(-p.y, p.x);
    off = (t * (1.0 + 0.4 * sin(r * 12.0 - uTime * 5.0)) - p * 0.35) * r * (1.0 - r) * 4.0;
  }
  FragColor = vec4(off * vP.y * vC.w, 0.0, 1.0);
}";

    const string ShadowVS = @"#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uModel, uView, uProj;
out vec3 vWorld;
void main(){
  vec4 w = uModel * vec4(aPos, 1.0);
  vWorld = w.xyz;
  gl_Position = uProj * uView * w;
}";

    const string ShadowFS = @"#version 330 core
in vec3 vWorld;
uniform vec3 uLightPos; uniform float uFar;
void main(){
  gl_FragDepth = clamp(length(vWorld - uLightPos) / uFar, 0.0, 1.0);
}";

    const int MaxLights = 32;
    const int MaxShadows = 4, ShadowRes = 512;
    const float BeaconTilt = 0.34f;   // downward slope of the beacon beams
    readonly GL gl;
    readonly Shader lit, part, shadow;
    Shader cur;   // shader the enemy model parts are drawn with (lit, or shadow while filling cube maps)
    readonly uint[] shadowCube = new uint[MaxShadows];
    readonly uint shadowFbo;
    const int FlashRes = 1024;
    const float FlashFar = 45f, FlashFov = 80f;
    readonly uint flashTex;
    bool flashShadowOn;
    Matrix4x4 flashVP;
    Vector3 flashPos;
    int shadowCount;
    readonly bool[] lShadow = new bool[MaxLights];
    readonly List<(Vector3 pos, float yaw, Vector3 col)> beacons = new();
    readonly Texture white, particleTex, smokeTex, skin, metal, portalTex;
    readonly Texture[] decalTex = new Texture[3];
    Texture wallT, floorT, ceilT, crateT, doorT, elevT, wallN, floorN, ceilN;
    Mesh walls, floors, ceils, crates, fixtures, elevFloor, pipes, props, propGlass, propBlink;
    readonly Mesh cube, sphere, cyl, cone, torus, crystal, quad, lowSphere, barrelMesh;
    readonly Mesh[] weaponView = new Mesh[Weapons.Count], weaponWorld = new Mesh[Weapons.Count];
    readonly Vector3[] muzzle = new Vector3[Weapons.Count];
    readonly Dictionary<PickupKind, Mesh> pickupMesh = new();
    readonly Mesh dyn;
    readonly List<Vertex> pv = new();
    readonly List<uint> pi = new();
    readonly Vector3[] lPos = new Vector3[MaxLights], lCol = new Vector3[MaxLights];
    readonly float[] lRad = new float[MaxLights];
    readonly Vector4[] lSpot = new Vector4[MaxLights];
    readonly List<(Vector3 pos, Vector3 col, float rad, float score, Vector4 spot, bool shadow)> lightCands = new();
    int lCount;
    float powerLevel = 1;
    Vector2 screen = Vector2.One;
    Level builtFor;
    Matrix4x4 view, proj;
    Vector3 cam, camRight, camUp;
    float time, deathAnim;
    public float Fov = 72f;
    public bool Shadows = true;
    public bool BloomOn = true, AO = true;
    readonly PostFx post;
    readonly PostParams pp = new();
    readonly Shader dist;
    readonly Texture hiveBlobT, hiveVeinT;
    Mesh hiveBlobs, hiveVeins;
    Vector3 hiveColor;
    float hiveDensity;
    readonly List<(Vector3 pos, Vector3 col, int i)> shafts = new();
    int lastVision = -1;
    float goggleStart = -10;
    Vector3 lastMuzzle; bool muzzleValid;
    Matrix4x4 lastVProj;
    public int TextureSeedOverride = -1;

    public Renderer(GL gl)
    {
        this.gl = gl;
        lit = new Shader(gl, LitVS, LitFS);
        part = new Shader(gl, PartVS, PartFS);
        shadow = new Shader(gl, ShadowVS, ShadowFS);
        post = new PostFx(gl);
        dist = new Shader(gl, DistVS, DistFS);
        cur = lit;
        shadowFbo = gl.GenFramebuffer();
        flashTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, flashTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, FlashRes, FlashRes, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, (void*)0);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        for (int k = 0; k < MaxShadows; k++)
        {
            shadowCube[k] = gl.GenTexture();
            gl.BindTexture(TextureTarget.TextureCubeMap, shadowCube[k]);
            for (int f = 0; f < 6; f++)
                gl.TexImage2D((TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + f), 0, InternalFormat.DepthComponent24,
                    ShadowRes, ShadowRes, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, (void*)0);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
        }
        gl.BindTexture(TextureTarget.TextureCubeMap, 0);
        white = new Texture(gl, 1, 1, TexGen.White(), false, true);
        particleTex = new Texture(gl, 64, 64, TexGen.Particle(), true);
        smokeTex = new Texture(gl, 64, 64, TexGen.Smoke(3), true);
        skin = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.AlienSkin(11));
        metal = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.Metal(12));
        portalTex = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.Portal(13), true, false, true);
        for (int k = 0; k < 3; k++) decalTex[k] = new Texture(gl, 64, 64, DecalPixels(k), true, false, true);
        hiveBlobT = new Texture(gl, 128, 128, HivePixels(false), true, false, true);
        hiveVeinT = new Texture(gl, 128, 128, HivePixels(true), true, false, true);

        var b = new MeshBuilder();
        b.BoxC(Vector3.Zero, Vector3.One); cube = b.Build(gl); b.Clear();
        b.Sphere(Vector3.Zero, new Vector3(0.5f), 16, 12); sphere = b.Build(gl); b.Clear();
        b.Sphere(Vector3.Zero, new Vector3(0.5f), 8, 6); lowSphere = b.Build(gl); b.Clear();
        b.Cylinder(Vector3.Zero, 0.5f, 0.5f, 1f, 12); cyl = b.Build(gl); b.Clear();
        b.Cylinder(Vector3.Zero, 0.5f, 0f, 1f, 10); cone = b.Build(gl); b.Clear();
        b.Torus(1f, 0.1f, 40, 10); torus = b.Build(gl); b.Clear();
        crystal = Models.Crystal().Build(gl);
        barrelMesh = Models.ExplosiveBarrel().Build(gl);
        b.Quad(new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0), Vector3.UnitZ); quad = b.Build(gl);
        for (int i = 0; i < Weapons.Count; i++)
        {
            weaponView[i] = Models.Weapon(i, out muzzle[i], true).Build(gl);
            weaponWorld[i] = Models.Weapon(i, out _, false).Build(gl);
        }
        foreach (PickupKind k in Enum.GetValues<PickupKind>())
            if (k != PickupKind.Weapon) pickupMesh[k] = Models.Pickup(k).Build(gl);
        dyn = new Mesh(gl);
    }

    // =====================================================================  level geometry
    void BuildLevel(Level L)
    {
        builtFor = L;
        foreach (var m in new[] { walls, floors, ceils, crates, fixtures, elevFloor, pipes, props, propGlass, propBlink, hiveBlobs, hiveVeins }) m?.Dispose();
        foreach (var t in new[] { wallT, floorT, ceilT, crateT, doorT, elevT, wallN, floorN, ceilN }) t?.Dispose();
        var theme = TexGen.ThemeFor(L.Number);
        int seed = L.Seed;
        int sz = TexGen.Size;
        var wallPx = TexGen.Wall(theme, seed);
        var floorPx = TexGen.Floor(theme, seed + 1);
        var ceilPx = TexGen.Ceiling(theme, seed + 2);
        wallT = new Texture(gl, sz, sz, wallPx);
        floorT = new Texture(gl, sz, sz, floorPx);
        ceilT = new Texture(gl, sz, sz, ceilPx);
        wallN = new Texture(gl, sz, sz, TexGen.NormalMap(wallPx, sz, 4f));
        floorN = new Texture(gl, sz, sz, TexGen.NormalMap(floorPx, sz, 5f));
        ceilN = new Texture(gl, sz, sz, TexGen.NormalMap(ceilPx, sz, 4f));
        crateT = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.Crate(seed + 3));
        doorT = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.Door(seed + 4));
        elevT = new Texture(gl, TexGen.Size, TexGen.Size, TexGen.Elevator(seed + 5));

        float S = Level.CS, H = Level.WH;
        var wb = new MeshBuilder(); var fb = new MeshBuilder(); var cb = new MeshBuilder();
        var kb = new MeshBuilder(); var lb = new MeshBuilder(); var eb = new MeshBuilder(); var pb = new MeshBuilder();
        var rng = new Random(seed);
        for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                if (!L.Floor(x, y)) continue;
                float x0 = x * S, x1 = x0 + S, z0 = y * S, z1 = z0 + S;
                bool isElev = !L.IsFinal && x == L.ExitX && y == L.ExitY;
                var floorB = isElev ? eb : fb;
                floorB.Quad(new(x0, 0, z1), new(x1, 0, z1), new(x1, 0, z0), new(x0, 0, z0), Vector3.UnitY, new(0, 1), new(1, 1), new(1, 0), new(0, 0));
                cb.Quad(new(x0, H, z0), new(x1, H, z0), new(x1, H, z1), new(x0, H, z1), -Vector3.UnitY, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
                // walls (seen from inside this cell)
                if (!L.Floor(x + 1, y)) wb.Quad(new(x1, 0, z1), new(x1, 0, z0), new(x1, H, z0), new(x1, H, z1), -Vector3.UnitX, new(0, 1), new(1, 1), new(1, 0), new(0, 0));
                if (!L.Floor(x - 1, y)) wb.Quad(new(x0, 0, z0), new(x0, 0, z1), new(x0, H, z1), new(x0, H, z0), Vector3.UnitX, new(0, 1), new(1, 1), new(1, 0), new(0, 0));
                if (!L.Floor(x, y + 1)) wb.Quad(new(x0, 0, z1), new(x1, 0, z1), new(x1, H, z1), new(x0, H, z1), -Vector3.UnitZ, new(0, 1), new(1, 1), new(1, 0), new(0, 0));
                if (!L.Floor(x, y - 1)) wb.Quad(new(x1, 0, z0), new(x0, 0, z0), new(x0, H, z0), new(x1, H, z0), Vector3.UnitZ, new(0, 1), new(1, 1), new(1, 0), new(0, 0));

                if (L.Crate[L.Idx(x, y)])
                {
                    int stacks = 1 + (int)(TexGen.Hash(x, y, seed) * 2.5f);
                    float cs = 3.6f, ch = 1.8f;
                    for (int s = 0; s < stacks; s++)
                    {
                        float shrink = s * 0.4f;
                        float rot = (TexGen.Hash(x, y + s, seed + 1) - 0.5f) * 0.3f;
                        kb.Transform = Matrix4x4.CreateRotationY(rot) * Matrix4x4.CreateTranslation(x0 + S / 2, s * ch + ch / 2, z0 + S / 2);
                        kb.BoxC(Vector3.Zero, new(cs - shrink, ch, cs - shrink));
                    }
                    kb.Transform = Matrix4x4.Identity;
                }

                // ceiling pipes along some corridors
                if (!L.RoomCell[L.Idx(x, y)] && !L.SecretCell[L.Idx(x, y)] && L.DoorAt[L.Idx(x, y)] < 0 && !isElev)
                {
                    pb.Col(0.45f, 0.42f, 0.38f);
                    float py = H - 0.35f;
                    if (L.Floor(x - 1, y) || L.Floor(x + 1, y))
                    {
                        pb.Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(x1, py, z0 + 0.4f);
                        pb.Cylinder(Vector3.Zero, 0.15f, 0.15f, S, 8, false);
                    }
                    if (L.Floor(x, y - 1) || L.Floor(x, y + 1))
                    {
                        pb.Transform = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(x1 - 0.4f, py, z0);
                        pb.Cylinder(Vector3.Zero, 0.15f, 0.15f, S, 8, false);
                    }
                    pb.Transform = Matrix4x4.Identity;
                }
            }

        // light fixtures
        foreach (var l in L.Lights)
        {
            if (l.Beacon)
            {
                // ceiling mount for the rotating beacon (dome + reflector are drawn per frame)
                lb.Col(0.16f, 0.16f, 0.18f);
                lb.Cylinder(new(l.Pos.X, H - 0.08f, l.Pos.Z), 0.42f, 0.42f, 0.08f, 16);
                lb.Cylinder(new(l.Pos.X, l.Pos.Y + 0.12f, l.Pos.Z), 0.07f, 0.07f, H - l.Pos.Y - 0.2f, 8);
                lb.Cylinder(new(l.Pos.X, l.Pos.Y - 0.02f, l.Pos.Z), 0.3f, 0.28f, 0.14f, 16);
                continue;
            }
            if (MathF.Abs(l.Pos.Y - (H - 0.2f)) > 0.01f) continue;
            lb.Col(l.Color * 1.3f, 1);
            lb.BoxC(new(l.Pos.X, H - 0.07f, l.Pos.Z), new(1.5f, 0.1f, 0.45f));
            lb.Col(0.2f, 0.2f, 0.22f);
            lb.BoxC(new(l.Pos.X, H - 0.03f, l.Pos.Z), new(1.7f, 0.06f, 0.6f));
        }
        // door frames & lock lights
        foreach (var d in L.Doors)
        {
            if (d.Secret) continue;
            var c = Level.Center(d.X, d.Y);
            var col = d.Key >= 0 ? Level.KeyColors[d.Key] : new Vector3(0.9f, 0.6f, 0.2f);
            lb.Col(0.25f, 0.25f, 0.27f);
            if (d.AlongX)
            {
                lb.BoxC(new(c.X, H - 0.3f, c.Z), new(0.8f, 0.6f, S));
                lb.Col(col, 1); lb.BoxC(new(c.X - 0.42f, H - 0.3f, c.Z), new(0.05f, 0.12f, S * 0.6f)); lb.BoxC(new(c.X + 0.42f, H - 0.3f, c.Z), new(0.05f, 0.12f, S * 0.6f));
            }
            else
            {
                lb.BoxC(new(c.X, H - 0.3f, c.Z), new(S, 0.6f, 0.8f));
                lb.Col(col, 1); lb.BoxC(new(c.X, H - 0.3f, c.Z - 0.42f), new(S * 0.6f, 0.12f, 0.05f)); lb.BoxC(new(c.X, H - 0.3f, c.Z + 0.42f), new(S * 0.6f, 0.12f, 0.05f));
            }
        }
        // elevator alcove decoration
        if (!L.IsFinal)
        {
            var c = Level.Center(L.ExitX, L.ExitY);
            var into = new Vector3(L.ElevDirX, 0, L.ElevDirY);
            var side = new Vector3(-into.Z, 0, into.X);
            var mouth = c + into * (S / 2);
            lb.Col(0.3f, 0.3f, 0.32f);
            lb.BoxC(mouth + side * (S / 2 - 0.2f) + new Vector3(0, H / 2, 0), new Vector3(0.4f) + new Vector3(0, H - 0.4f, 0));
            lb.BoxC(mouth - side * (S / 2 - 0.2f) + new Vector3(0, H / 2, 0), new Vector3(0.4f) + new Vector3(0, H - 0.4f, 0));
            lb.Col(0.2f, 1f, 0.4f, 1);
            lb.BoxC(mouth + side * (S / 2 - 0.42f) + new Vector3(0, H / 2, 0), new Vector3(0.06f, H - 1f, 0.06f));
            lb.BoxC(mouth - side * (S / 2 - 0.42f) + new Vector3(0, H / 2, 0), new Vector3(0.06f, H - 1f, 0.06f));
            lb.BoxC(mouth + new Vector3(0, H - 0.5f, 0), new Vector3(S * 0.5f, 0.3f, 0.3f) * (Vector3.One - Vector3.Abs(into)) + Vector3.Abs(into) * 0.1f + new Vector3(0, 0.3f, 0));
            // control panel on back wall
            var back = c - into * (S / 2 - 0.1f);
            lb.Col(0.15f, 0.15f, 0.17f);
            lb.BoxC(back + new Vector3(0, 1.4f, 0) + side * 1.2f, new Vector3(0.6f, 1.0f, 0.6f) * (Vector3.One - Vector3.Abs(into)) + Vector3.Abs(into) * 0.15f + new Vector3(0, 0.4f, 0));
            lb.Col(1f, 0.3f, 0.2f, 1);
            lb.BoxC(back + new Vector3(0, 1.6f, 0) + side * 1.2f + into * 0.1f, new Vector3(0.12f));
            lb.Col(0.2f, 1f, 0.3f, 1);
            lb.BoxC(back + new Vector3(0, 1.3f, 0) + side * 1.2f + into * 0.1f, new Vector3(0.12f));
        }
        // hazards: steam grates, hanging live cables, acid pools
        foreach (var h in L.Hazards)
        {
            switch (h.Kind)
            {
                case HazardKind.Steam:
                    lb.Col(0.14f, 0.14f, 0.15f); lb.BoxC(h.Pos + new Vector3(0, 0.015f, 0), new(1.3f, 0.03f, 1.3f));
                    lb.Col(0.32f, 0.32f, 0.34f);
                    for (int k = -2; k <= 2; k++) lb.BoxC(h.Pos + new Vector3(k * 0.24f, 0.035f, 0), new(0.06f, 0.03f, 1.15f));
                    lb.Col(0.75f, 0.6f, 0.1f); lb.BoxC(h.Pos + new Vector3(0, 0.02f, 0.68f), new(1.3f, 0.02f, 0.06f)); lb.BoxC(h.Pos + new Vector3(0, 0.02f, -0.68f), new(1.3f, 0.02f, 0.06f));
                    break;
                case HazardKind.Cable:
                {
                    var anchor = new Vector3(h.Pos.X, H - 0.5f, h.Pos.Z) - h.N * 0.4f;
                    lb.Col(0.2f, 0.2f, 0.22f); lb.BoxC(anchor + new Vector3(0, 0.1f, 0), new(0.35f, 0.25f, 0.35f));
                    lb.Col(0.06f, 0.06f, 0.06f);
                    var prev = anchor;
                    for (int k = 1; k <= 7; k++)
                    {
                        float f = k / 7f;
                        var p = Vector3.Lerp(anchor, h.Pos, f) + h.N * MathF.Sin(f * MathF.PI) * 0.35f;
                        lb.Transform = Seg(prev, p, 0.12f);
                        lb.Cylinder(Vector3.Zero, 0.5f, 0.5f, 1, 6, false);
                        prev = p;
                    }
                    lb.Transform = Matrix4x4.Identity;
                    lb.Col(1f, 0.6f, 0.25f, 0.8f);
                    for (int k = 0; k < 4; k++) { lb.Transform = Seg(h.Pos, h.Pos + new Vector3(MathF.Cos(k * 1.7f), -0.6f, MathF.Sin(k * 1.7f)) * 0.18f, 0.02f); lb.Cylinder(Vector3.Zero, 0.5f, 0.5f, 1, 4, false); }
                    lb.Transform = Matrix4x4.Identity;
                    break;
                }
                case HazardKind.Acid:
                    lb.Col(0.15f, 0.2f, 0.1f); lb.Cylinder(h.Pos, h.Radius + 0.12f, h.Radius + 0.12f, 0.012f, 22);
                    lb.Col(0.35f, 0.95f, 0.2f, 0.85f); lb.Cylinder(h.Pos + new Vector3(0, 0.004f, 0), h.Radius, h.Radius, 0.014f, 22);
                    break;
            }
        }

        var prb = new MeshBuilder();
        var glb = new MeshBuilder();
        var blb = new MeshBuilder();
        foreach (var pr in L.Props) Models.Prop(prb, glb, blb, pr, L.Number);
        props = prb.Build(gl);
        propGlass = glb.Build(gl);
        propBlink = blb.Build(gl);
        walls = wb.Build(gl); floors = fb.Build(gl); ceils = cb.Build(gl); crates = kb.Build(gl);
        fixtures = lb.Build(gl); elevFloor = eb.Build(gl); pipes = pb.Build(gl);
        BuildHive(L);
    }

    /// <summary>Deep levels: glowing biolume patches on floors and ceilings and pulsing veins up the walls.</summary>
    void BuildHive(Level L)
    {
        hiveDensity = L.Number < 14 ? 0 : MathF.Min(1, (L.Number - 13) / 14f);
        float k = Math.Clamp((L.Number - 14) / 16f, 0, 1);
        hiveColor = Vector3.Lerp(new Vector3(0.35f, 1f, 0.55f), new Vector3(0.85f, 0.35f, 1f), k);
        var bb = new MeshBuilder(); var vb = new MeshBuilder();
        float S = Level.CS, H = Level.WH;
        var rng = new Random(L.Seed * 7 + 3);
        if (hiveDensity > 0)
            for (int y = 0; y < L.H; y++)
                for (int x = 0; x < L.W; x++)
                {
                    if (!L.Floor(x, y) || L.Crate[L.Idx(x, y)] || (x == L.ExitX && y == L.ExitY) || (x == L.StartX && y == L.StartY)) continue;
                    var c = Level.Center(x, y);
                    var tint = hiveColor * (0.8f + (float)rng.NextDouble() * 0.4f);
                    if (rng.NextDouble() < hiveDensity * 0.3)
                        Patch(bb, c + new Vector3(((float)rng.NextDouble() - 0.5f) * 2, 0.015f, ((float)rng.NextDouble() - 0.5f) * 2), Vector3.UnitY, 1.6f + (float)rng.NextDouble() * 1.8f, tint, 0.9f, rng);
                    if (rng.NextDouble() < hiveDensity * 0.15)
                        Patch(bb, c + new Vector3(((float)rng.NextDouble() - 0.5f) * 2, H - 0.015f, ((float)rng.NextDouble() - 0.5f) * 2), -Vector3.UnitY, 1.5f + (float)rng.NextDouble() * 1.5f, tint, 0.9f, rng);
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (L.Floor(nx, ny) || rng.NextDouble() >= hiveDensity * 0.35) continue;
                        var n = new Vector3(x - nx, 0, y - ny);              // wall normal, pointing into this cell
                        var side = new Vector3(-n.Z, 0, n.X);
                        float wdt = 2.4f + (float)rng.NextDouble() * 1.4f, hgt = 2f + (float)rng.NextDouble() * 1.6f;
                        var foot = c - n * (S / 2 - 0.025f) + side * ((float)rng.NextDouble() - 0.5f) * (S - wdt);
                        bool flip = rng.NextDouble() < 0.5;
                        vb.Col(tint, 0.8f);
                        var a = foot - side * wdt / 2; var b = foot + side * wdt / 2;
                        vb.Quad(a, b, b + Vector3.UnitY * hgt, a + Vector3.UnitY * hgt, n,
                            new(flip ? 1 : 0, 1), new(flip ? 0 : 1, 1), new(flip ? 0 : 1, 0), new(flip ? 1 : 0, 0));
                    }
                }
        hiveBlobs = bb.Build(gl);
        hiveVeins = vb.Build(gl);
    }

    static void Patch(MeshBuilder b, Vector3 c, Vector3 n, float size, Vector3 col, float glow, Random rng)
    {
        float r = (float)rng.NextDouble() * MathF.Tau;
        var t1 = new Vector3(MathF.Cos(r), 0, MathF.Sin(r)) * size / 2;
        var t2 = Vector3.Cross(n, t1);
        b.Col(col, glow);
        b.Quad(c - t1 - t2, c + t1 - t2, c + t1 + t2, c - t1 + t2, n, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
    }

    /// <summary>Hive textures: clustered glowing pods with membrane (patches), or branching veins (walls).</summary>
    static byte[] HivePixels(bool veins)
    {
        const int N = 128;
        var lum = new float[N * N]; var alpha = new float[N * N];
        var r = new Random(veins ? 91 : 57);
        if (!veins)
        {
            var pods = Enumerable.Range(0, 14).Select(_ =>
            {
                float a = (float)r.NextDouble() * MathF.Tau, d = (float)Math.Sqrt(r.NextDouble()) * 0.3f;
                return (x: 0.5f + MathF.Cos(a) * d, y: 0.5f + MathF.Sin(a) * d, s: 0.03f + (float)r.NextDouble() * 0.06f);
            }).ToArray();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float d = MathF.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                    float ang = MathF.Atan2(v - 0.5f, u - 0.5f);
                    float n = TexGen.Hash(x / 3, y / 3, 7) * 0.5f + TexGen.Hash(x / 9, y / 9, 8) * 0.5f;
                    float edge = 0.36f + 0.07f * MathF.Sin(ang * 5 + 1) + 0.04f * MathF.Sin(ang * 11) - n * 0.05f;
                    float a = Math.Clamp((edge - d) / 0.1f, 0, 1) * 0.75f;
                    float l = 0.18f + n * 0.12f;
                    foreach (var p in pods)
                    {
                        float pd = MathF.Sqrt((u - p.x) * (u - p.x) + (v - p.y) * (v - p.y)) / p.s;
                        if (pd < 1.25f) { a = MathF.Max(a, Math.Clamp((1.25f - pd) / 0.25f, 0, 1)); l = MathF.Max(l, 1 - pd * pd * 0.75f); }
                    }
                    lum[y * N + x] = l; alpha[y * N + x] = a;
                }
        }
        else
        {
            // random-walk branches growing up from the floor (v = 1 is the bottom edge)
            var stack = new Stack<(float x, float y, float dir, float w, int len)>();
            for (int i = 0; i < 3; i++) stack.Push((0.2f + i * 0.3f + (float)r.NextDouble() * 0.1f, 1f, -MathF.PI / 2, 0.03f, 160));
            while (stack.Count > 0)
            {
                var (x, y, dir, w, len) = stack.Pop();
                for (int k = 0; k < len && w > 0.006f; k++)
                {
                    dir += ((float)r.NextDouble() - 0.5f) * 0.22f + MathF.Sin(k * 0.15f) * 0.03f;
                    dir = Math.Clamp(dir, -MathF.PI + 0.35f, -0.35f);
                    x += MathF.Cos(dir) * 0.005f; y += MathF.Sin(dir) * 0.005f;
                    w *= 0.994f;
                    if (x < 0.02f || x > 0.98f || y < 0.02f) break;
                    int cx = (int)(x * N), cy = (int)(y * N), rad = (int)MathF.Ceiling(w * N) + 1;
                    for (int dy = -rad; dy <= rad; dy++)
                        for (int dx = -rad; dx <= rad; dx++)
                        {
                            int px = cx + dx, py = cy + dy;
                            if (px < 0 || py < 0 || px >= N || py >= N) continue;
                            float dd = MathF.Sqrt(dx * dx + dy * dy) / (w * N);
                            if (dd > 1) continue;
                            int idx = py * N + px;
                            alpha[idx] = MathF.Max(alpha[idx], Math.Clamp((1 - dd) * 2.5f, 0, 1));
                            lum[idx] = MathF.Max(lum[idx], 0.35f + 0.65f * (1 - dd) * (1 - dd));
                        }
                    if (r.NextDouble() < 0.022) stack.Push((x, y, dir + (r.NextDouble() < 0.5 ? -0.75f : 0.75f), w * 0.7f, len - k));
                    if (r.NextDouble() < 0.008) { int nx = (int)(x * N), ny = (int)(y * N); for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++) { int px = nx + dx, py = ny + dy; if (px >= 0 && py >= 0 && px < N && py < N && dx * dx + dy * dy <= 9) { alpha[py * N + px] = 1; lum[py * N + px] = 1; } } }
                }
            }
        }
        var px2 = new byte[N * N * 4];
        for (int i = 0; i < N * N; i++)
        {
            byte l = (byte)(Math.Clamp(lum[i], 0, 1) * 255);
            px2[i * 4] = px2[i * 4 + 1] = px2[i * 4 + 2] = l;
            px2[i * 4 + 3] = (byte)(Math.Clamp(alpha[i], 0, 1) * 255);
        }
        return px2;
    }

    // =====================================================================  lights
    void GatherLights(World w)
    {
        lightCands.Clear();
        shafts.Clear();
        beacons.Clear();
        var L = w.L;
        powerLevel = w.PowerLevel;
        for (int i = 0; i < L.Lights.Count; i++)
        {
            var l = L.Lights[i];
            float d = Vector3.Distance(cam, l.Pos);
            if (d - l.Radius > 40) continue;
            bool ceiling = l.Pos.Y > Level.WH - 0.8f;
            if (l.Beacon)
            {
                float yaw = l.Phase + time * l.Spin;
                var dir = Vector3.Normalize(new Vector3(MathF.Cos(yaw), -BeaconTilt, MathF.Sin(yaw)));
                lightCands.Add((l.Pos, l.Color, l.Radius, d - l.Radius * 0.3f, new Vector4(dir, 0.88f), true));
                if (d < 60) beacons.Add((l.Pos, yaw, l.Color));
                continue;
            }
            float k = 1;
            if (powerLevel < 1)
            {
                // blackout: most lights die, every fourth becomes a pulsing red emergency lamp
                if (i % 4 == 0)
                {
                    var emer = new Vector3(0.9f, 0.08f, 0.04f) * (0.55f + 0.45f * MathF.Sin(time * 3 + i));
                    lightCands.Add((l.Pos, Vector3.Lerp(emer, l.Color, powerLevel), l.Radius, d - l.Radius * 0.3f, Vector4.Zero, ceiling));
                    continue;
                }
                k = MathF.Max(powerLevel, 0.02f);
            }
            if (l.Flicker > 0)
            {
                float h = TexGen.Hash((int)(time * 14) + i * 17, i, 5);
                float h2 = TexGen.Hash((int)(time * 3) + i * 5, i, 9);
                k = h2 < l.Flicker * 0.5f ? (h < 0.5f ? 0.1f : 0.9f) : 1f;
            }
            lightCands.Add((l.Pos, l.Color * k, l.Radius, d - l.Radius * 0.3f, Vector4.Zero, ceiling));
            if (d < 38 && MathF.Abs(l.Pos.Y - (Level.WH - 0.2f)) < 0.01f) shafts.Add((l.Pos, l.Color * k, i));
        }
        if (w.ActiveLockdown != null)
        {
            var r = L.Rooms[w.ActiveLockdown.Room];
            var c = Level.Center(r.CX, r.CY, Level.WH - 0.6f);
            lightCands.Add((c, new Vector3(1.4f, 0.1f, 0.05f) * (0.5f + 0.5f * MathF.Sin(time * 6)), 14, Vector3.Distance(cam, c) - 20, Vector4.Zero, false));
        }
        foreach (var dl in w.DynLights)
            lightCands.Add((dl.Pos, dl.Color * (dl.Life / dl.MaxLife), dl.Radius, Vector3.Distance(cam, dl.Pos) - 20, Vector4.Zero, false));
        foreach (var p in w.Projs)
        {
            Vector3 c = p.Kind switch
            {
                ProjKind.Plasma => new(0.3f, 0.7f, 1.2f),
                ProjKind.Acid or ProjKind.EnemyAcid => new(0.4f, 1f, 0.2f),
                ProjKind.Void => new(0.8f, 0.3f, 1.5f),
                ProjKind.EnemyPlasma => new(1f, 0.3f, 0.9f),
                ProjKind.EnemyOrb => new(0.7f, 0.4f, 1.2f),
                ProjKind.BruteOrb => new(1.2f, 0.5f, 0.3f),
                ProjKind.Rocket => new(1.2f, 0.6f, 0.2f),
                _ => Vector3.Zero
            };
            if (c != Vector3.Zero) lightCands.Add((p.Pos, c, p.Kind == ProjKind.Void ? 9 : 5, Vector3.Distance(cam, p.Pos) - 15, Vector4.Zero, false));
        }
        foreach (var pk in w.Pickups)
            if (pk.Kind == PickupKind.DamageAmp)
                lightCands.Add((pk.Pos + Vector3.UnitY * 0.5f, new Vector3(1f, 0.5f, 0.15f), 5, Vector3.Distance(cam, pk.Pos) - 5, Vector4.Zero, false));
            else if (pk.Kind == PickupKind.Key)
                lightCands.Add((pk.Pos + Vector3.UnitY * 0.5f, Level.KeyColors[pk.Arg] * 0.9f, 5, Vector3.Distance(cam, pk.Pos) - 5, Vector4.Zero, false));
            else if (pk.Kind == PickupKind.Upgrade)
                lightCands.Add((pk.Pos + Vector3.UnitY * 0.5f, new Vector3(1f, 0.75f, 0.25f), 4.5f, Vector3.Distance(cam, pk.Pos) - 5, Vector4.Zero, false));
        foreach (var e in w.Enemies)
            if (!e.Dead && (e.Type == EnemyDefs.Wraith || e.Type == EnemyDefs.Pylon || e.Type == EnemyDefs.Overseer))
                lightCands.Add((e.Mid, e.D.Color * 0.8f, 5, Vector3.Distance(cam, e.Pos), Vector4.Zero, false));
        lightCands.Sort((a, b) => a.score.CompareTo(b.score));
        lCount = Math.Min(MaxLights, lightCands.Count);
        for (int i = 0; i < lCount; i++)
        {
            var c = lightCands[i];
            lPos[i] = c.pos; lCol[i] = c.col; lRad[i] = c.rad; lSpot[i] = c.spot; lShadow[i] = c.shadow;
        }
        PickShadowLights(w);
    }

    /// <summary>Moves up to MaxShadows ceiling lights (nearest first) that have an enemy within reach
    /// to the front of the light arrays; those get cube shadow maps this frame.</summary>
    void PickShadowLights(World w)
    {
        shadowCount = 0;
        if (!Shadows) return;
        for (int i = 0; i < lCount && shadowCount < MaxShadows; i++)
        {
            if (!lShadow[i] || lRad[i] < 6 || Vector3.DistanceSquared(lPos[i], cam) > 45 * 45) continue;
            bool any = false;
            foreach (var e in w.Enemies)
            {
                if (e.Type == EnemyDefs.Stalker || (e.Dead && e.D.Stationary)) continue;
                if (Vector3.DistanceSquared(e.Mid, lPos[i]) < (lRad[i] + e.D.Radius) * (lRad[i] + e.D.Radius)) { any = true; break; }
            }
            if (!any) continue;
            int j = shadowCount++;
            (lPos[i], lPos[j]) = (lPos[j], lPos[i]);
            (lCol[i], lCol[j]) = (lCol[j], lCol[i]);
            (lRad[i], lRad[j]) = (lRad[j], lRad[i]);
            (lSpot[i], lSpot[j]) = (lSpot[j], lSpot[i]);
            (lShadow[i], lShadow[j]) = (lShadow[j], lShadow[i]);
        }
    }

    // =====================================================================  shadow maps
    /// <summary>Spot shadow map from the flashlight: enemies, props and crates as casters.</summary>
    void RenderFlashShadow(World w)
    {
        var P = w.P;
        flashPos = P.Eye + P.Right * 0.42f - Vector3.UnitY * 0.4f;   // shoulder lamp, off-axis so shadows show
        flashShadowOn = Shadows && P.Flashlight && !P.Dead;
        if (!flashShadowOn) return;
        var dir = P.Forward;
        var up = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var fv = Matrix4x4.CreateLookAt(flashPos, flashPos + dir, up);
        flashVP = fv * GMath.Perspective(FlashFov * MathF.PI / 180, 1, 0.05f, FlashFar + 5);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, flashTex, 0);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        gl.Viewport(0, 0, FlashRes, FlashRes);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        gl.Clear(ClearBufferMask.DepthBufferBit);
        shadow.Use();
        shadow.Set("uView", fv);
        shadow.Set("uProj", GMath.Perspective(FlashFov * MathF.PI / 180, 1, 0.05f, FlashFar + 5));
        shadow.Set("uLightPos", flashPos);
        shadow.Set("uFar", FlashFar);
        shadow.Set("uModel", Matrix4x4.Identity);
        props.Draw();
        crates.Draw();
        DrawBarrels(w, true);
        cur = shadow;
        foreach (var e in w.Enemies)
        {
            if (e.Type == EnemyDefs.Stalker || (e.Dead && e.D.Stationary)) continue;
            var v = e.Mid - flashPos;
            float dd = v.Length();
            if (dd > FlashFar + 3) continue;
            if (Vector3.Dot(v, dir) < -(e.D.Radius + e.D.Height)) continue;
            DrawEnemy(e, w, false);
        }
        cur = lit;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    static readonly (Vector3 dir, Vector3 up)[] CubeFaces =
    {
        (Vector3.UnitX, -Vector3.UnitY), (-Vector3.UnitX, -Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitY, -Vector3.UnitZ),
        (Vector3.UnitZ, -Vector3.UnitY), (-Vector3.UnitZ, -Vector3.UnitY),
    };
    readonly List<Enemy> casters = new();

    /// <summary>Renders the distance-to-light of every nearby enemy into each shadow light's cube map.</summary>
    void RenderShadowMaps(World w)
    {
        if (shadowCount == 0) return;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFbo);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        gl.Viewport(0, 0, ShadowRes, ShadowRes);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        shadow.Use();
        var faceProj = GMath.Perspective(MathF.PI / 2, 1, 0.05f, 60f);
        shadow.Set("uProj", faceProj);
        cur = shadow;
        for (int k = 0; k < shadowCount; k++)
        {
            var lp = lPos[k];
            float far = lRad[k];
            shadow.Set("uLightPos", lp);
            shadow.Set("uFar", far);
            casters.Clear();
            foreach (var e in w.Enemies)
            {
                if (e.Type == EnemyDefs.Stalker || (e.Dead && e.D.Stationary)) continue;
                if (Vector3.DistanceSquared(e.Mid, lp) < (far + e.D.Radius + 1) * (far + e.D.Radius + 1)) casters.Add(e);
            }
            for (int f = 0; f < 6; f++)
            {
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + f), shadowCube[k], 0);
                gl.Clear(ClearBufferMask.DepthBufferBit);
                var (dir, up) = CubeFaces[f];
                shadow.Set("uView", Matrix4x4.CreateLookAt(lp, lp + dir, up));
                foreach (var e in casters)
                {
                    // rough frustum test: enemy bounding sphere vs. this 90 degree face
                    var v = e.Mid - lp;
                    float r = e.D.Radius + e.D.Height * 0.6f + 0.5f;
                    float depth = Vector3.Dot(v, dir);
                    if (depth + r <= 0) continue;
                    var side = v - dir * depth;
                    if (MathF.Max(MathF.Abs(side.X), MathF.Max(MathF.Abs(side.Y), MathF.Abs(side.Z))) - r * 1.42f > depth + r) continue;
                    DrawEnemy(e, w, false);
                }
            }
        }
        cur = lit;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // =====================================================================  main render
    public void Render(World w, int fbW, int fbH, float t, float dt)
    {
        time = t;
        var L = w.L;
        var P = w.P;
        if (builtFor != L) BuildLevel(L);

        deathAnim = P.Dead ? MathF.Min(1, deathAnim + dt * 1.5f) : 0;
        var eye = P.Eye;
        var fwd = P.Forward;
        var up = Vector3.UnitY;
        if (P.Shake > 0)
        {
            float s = P.Shake * P.Shake * 0.12f;
            eye += new Vector3(MathF.Sin(t * 47) * s, MathF.Sin(t * 53) * s, MathF.Cos(t * 41) * s);
            float a = P.Shake * P.Shake * 0.035f;
            fwd = Vector3.Normalize(fwd + P.Right * MathF.Sin(t * 37) * a + Vector3.UnitY * MathF.Sin(t * 43 + 1) * a);
            up = Vector3.Normalize(Vector3.UnitY + P.Right * MathF.Sin(t * 29) * a * 0.8f);
        }
        if (deathAnim > 0)
        {
            eye.Y = GMath.Lerp(eye.Y, 0.35f, deathAnim);
            up = Vector3.Normalize(Vector3.UnitY + P.Right * deathAnim * 0.8f);
        }
        cam = eye;
        float aspect = fbW / (float)Math.Max(1, fbH);
        view = Matrix4x4.CreateLookAt(eye, eye + fwd, up);
        proj = GMath.Perspective(Fov * MathF.PI / 180, aspect, 0.05f, 250f);
        camRight = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
        camUp = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));

        vision = P.NVOn ? 1 : P.XRayOn ? 2 : 0;
        GatherLights(w);
        RenderShadowMaps(w);
        RenderFlashShadow(w);
        post.Begin(fbW, fbH);
        if (lastVision < 0) lastVision = vision;
        if (vision != lastVision) { lastVision = vision; goggleStart = t; }
        gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        var clear = vision == 1 ? new Vector3(0.0f, 0.03f, 0.005f) : vision == 2 ? new Vector3(0.0f, 0.01f, 0.03f) : L.FogColor;
        gl.ClearColor(clear.X, clear.Y, clear.Z, 1);
        part.Use();
        part.Set("uVision", vision);
        screen = new Vector2(fbW, fbH);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        gl.DepthRange(0.01, 1);   // [0, 0.01] is kept for the first-person weapon (see PostFx)
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);

        lit.Use();
        SetCommon(view, proj, P);

        // level
        SetXrayLenses(w);
        lit.Set("uXrayWorld", 1f);
        lit.Set("uModel", Matrix4x4.Identity);
        Tint(Vector4.One, 0);
        lit.Set("uTexGlow", 1f);
        lit.Set("uBump", 1f);
        wallN.Bind(1); wallT.Bind(); walls.Draw();
        floorN.Bind(1); floorT.Bind(); floors.Draw();
        ceilN.Bind(1); ceilT.Bind(); ceils.Draw();
        lit.Set("uBump", 0f);
        elevT.Bind(); elevFloor.Draw();
        lit.Set("uTexGlow", 0f);
        crateT.Bind(); crates.Draw();
        metal.Bind(); pipes.Draw();
        white.Bind();
        if (powerLevel < 1) Tint(new Vector4(new Vector3(0.12f + 0.88f * powerLevel), 1), 0);   // dead light panels in a blackout
        fixtures.Draw();
        Tint(Vector4.One, 0);
        metal.Bind(); props.Draw();
        white.Bind();
        lit.Set("uBlink", 1f);
        propBlink.Draw();
        lit.Set("uBlink", 0f);
        DrawBeacons();
        DrawBarrels(w, false);
        DrawVents(w);

        // secret wall panels: the wall texture on a full-cell block, a shade off
        lit.Set("uTexGlow", 1f);
        lit.Set("uBump", 1f);
        wallN.Bind(1); wallT.Bind();
        foreach (var d in L.Doors)
        {
            if (!d.Secret || d.Open >= 0.999f) continue;
            var c = Level.Center(d.X, d.Y);
            lit.Set("uModel", Matrix4x4.CreateScale(Level.CS, Level.WH, Level.CS) * Matrix4x4.CreateTranslation(c.X, Level.WH / 2 + d.Open * (Level.WH - 0.05f), c.Z));
            Tint(new Vector4(0.86f, 0.84f, 0.8f, 1), 0);
            cube.Draw();
        }
        lit.Set("uBump", 0f);
        lit.Set("uTexGlow", 0f);

        if (hiveDensity > 0)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false);
            lit.Set("uModel", Matrix4x4.Identity);
            Tint(Vector4.One, 0);
            lit.Set("uPulse", 1f);
            hiveBlobT.Bind(); hiveBlobs.Draw();
            hiveVeinT.Bind(); hiveVeins.Draw();
            lit.Set("uPulse", 0f);
            gl.DepthMask(true);
            gl.Disable(EnableCap.Blend);
        }

        DrawDecals(w);

        // doors
        doorT.Bind();
        lit.Set("uTexGlow", 1f);
        foreach (var d in L.Doors)
        {
            if (d.Open >= 0.999f) continue;
            var c = Level.Center(d.X, d.Y);
            float H = Level.WH;
            var size = d.AlongX ? new Vector3(0.35f, H, Level.CS) : new Vector3(Level.CS, H, 0.35f);
            var pos = new Vector3(c.X, H / 2 + d.Open * (H - 0.1f), c.Z);
            var col = d.Key >= 0 ? Level.KeyColors[d.Key] * 0.85f + new Vector3(0.15f) : new Vector3(0.62f, 0.64f, 0.66f);
            lit.Set("uModel", Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(pos));
            Tint(new Vector4(col, 1), 0);
            cube.Draw();
        }
        lit.Set("uTexGlow", 0f);
        lit.Set("uXrayWorld", 0f);

        // pickups
        foreach (var pk in w.Pickups)
            if (Vector3.DistanceSquared(pk.Pos, cam) <= 70 * 70) DrawPickup(pk, null);

        // enemies (opaque); night vision shows them as glowing heat silhouettes
        skin.Bind();
        if (vision == 1) { lit.Set("uHot", 1f); hotTint = new Vector4(0.55f, 1f, 0.45f, 0.6f); }
        foreach (var e in w.Enemies)
        {
            if (Vector3.DistanceSquared(e.Pos, cam) > 90 * 90) continue;
            if (e.Type == EnemyDefs.Stalker) continue;
            DrawEnemy(e, w, false);
        }
        lit.Set("uHot", 0f); hotTint = null;
        DrawChunks(w);

        // projectile bodies
        white.Bind();
        foreach (var p in w.Projs)
        {
            if (p.Kind == ProjKind.Grenade)
            {
                lit.Set("uModel", Matrix4x4.CreateScale(0.22f) * Matrix4x4.CreateTranslation(p.Pos));
                Tint(new Vector4(0.25f, 0.35f, 0.2f, 1), 0);
                sphere.Draw();
                lit.Set("uModel", Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(p.Pos + Vector3.UnitY * 0.1f));
                Tint(new Vector4(1f, 0.2f, 0.1f, 1), 1);
                sphere.Draw();
            }
            else if (p.Kind == ProjKind.Rocket)
            {
                var dir = Vector3.Normalize(p.Vel);
                lit.Set("uModel", Seg(p.Pos - dir * 0.4f, p.Pos + dir * 0.2f, 0.14f));
                Tint(new Vector4(0.35f, 0.38f, 0.22f, 1), 0);
                cyl.Draw();
                lit.Set("uModel", Seg(p.Pos + dir * 0.2f, p.Pos + dir * 0.45f, 0.14f));
                Tint(new Vector4(0.7f, 0.15f, 0.1f, 1), 0);
                cone.Draw();
            }
            else if (p.Kind == ProjKind.Void)
            {
                lit.Set("uModel", Matrix4x4.CreateScale(0.45f) * Matrix4x4.CreateTranslation(p.Pos));
                Tint(new Vector4(0.02f, 0f, 0.05f, 1), 0);
                sphere.Draw();
            }
        }

        // ---------- transparent pass ----------
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        skin.Bind();
        if (vision == 1) { lit.Set("uHot", 1f); hotTint = new Vector4(0.55f, 1f, 0.45f, 0.6f); }   // night vision beats the cloak
        foreach (var e in w.Enemies)
            if (e.Type == EnemyDefs.Stalker) DrawEnemy(e, w, true);
        lit.Set("uHot", 0f); hotTint = null;

        // specimen tank glass
        white.Bind();
        lit.Set("uModel", Matrix4x4.Identity);
        Tint(new Vector4(1, 1, 1, 0.22f), 0);
        gl.DepthMask(false);
        propGlass.Draw();
        DrawBarriers(w);
        // beacon domes
        foreach (var (bp, _, bc) in beacons)
        {
            lit.Set("uModel", Matrix4x4.CreateScale(0.62f, 0.5f, 0.62f) * Matrix4x4.CreateTranslation(bp + new Vector3(0, -0.12f, 0)));
            Tint(new Vector4(Vector3.Normalize(bc) * 1.1f, 0.45f), 0.8f);
            sphere.Draw();
        }

        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        DrawPortalFx(w);

        // billboards
        part.Use();
        part.Set("uView", view);
        part.Set("uProj", proj);
        part.Set("uTex", 0);
        part.Set("uFogDensity", L.FogDensity);

        // alpha particles (blood, gibs)
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        foreach (var p in w.Particles)
            if (!p.Additive && !p.Smoke) Billboard(p.Pos, p.Size, p.Color * new Vector4(1, 1, 1, MathF.Min(1, p.Life / p.MaxLife * 2)));
        Flush(particleTex);
        foreach (var p in w.Particles)
            if (p.Smoke) Billboard(p.Pos, p.Size, p.Color * new Vector4(1, 1, 1, p.Life / p.MaxLife));
        Flush(smokeTex);

        // additive
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        foreach (var p in w.Particles)
            if (p.Additive) Billboard(p.Pos, p.Size, p.Color * new Vector4(1, 1, 1, p.Life / p.MaxLife));
        foreach (var b in w.Beams)
            BeamQuad(b.A, b.B, b.Width, b.Color * new Vector4(1, 1, 1, b.Life / b.MaxLife));
        Flush(particleTex);
        if (vision != 2)
        {
            foreach (var (sp, sc, _) in shafts)
            {
                LightShaft(sp, sc, 0.7f, 0.2f, 1.6f, 1.05f, 0.075f);
                LightShaft(sp, sc, 0.75f, 0.22f, 2.3f, 1.6f, 0.04f);
            }
            Flush(white);
            DrawDust();
        }
        DrawSwirls(w);
        foreach (var (bp, yaw, bc) in beacons)
        {
            var col = Vector3.Normalize(bc);
            var toCam = cam - bp; toCam.Y = 0;
            float tl = toCam.Length();
            for (int s = 0; s < 2; s++)
            {
                float a = yaw + s * MathF.PI;
                var dir = Vector3.Normalize(new Vector3(MathF.Cos(a), -BeaconTilt, MathF.Sin(a)));
                BeamCone(bp + dir * 0.25f, dir, 7.5f, 1.7f, new Vector4(col, 0.16f));
                if (tl > 0.5f)
                {
                    float facing = MathF.Max(0, Vector3.Dot(new Vector3(dir.X, 0, dir.Z) / new Vector2(dir.X, dir.Z).Length(), toCam / tl));
                    float g = MathF.Pow(facing, 10);
                    if (g > 0.02f) Billboard(bp + dir * 0.3f, 0.6f + g * 2.6f, new Vector4(col * 0.6f + new Vector3(0.4f), g * 0.9f));
                }
            }
            Billboard(bp, 1.0f, new Vector4(col, 0.35f));
        }
        foreach (var p in w.Projs)
        {
            switch (p.Kind)
            {
                case ProjKind.Plasma:
                    Billboard(p.Pos, 0.5f, new Vector4(0.3f, 0.7f, 1f, 0.9f)); Billboard(p.Pos, 0.2f, new Vector4(0.9f, 1f, 1f, 1f));
                    BeamQuad(p.Pos, p.Pos - Vector3.Normalize(p.Vel) * 0.8f, 0.15f, new Vector4(0.3f, 0.7f, 1f, 0.5f)); break;
                case ProjKind.Acid:
                case ProjKind.EnemyAcid:
                    Billboard(p.Pos, 0.55f, new Vector4(0.4f, 1f, 0.2f, 0.8f)); Billboard(p.Pos, 0.25f, new Vector4(0.8f, 1f, 0.6f, 1f)); break;
                case ProjKind.Void:
                    Billboard(p.Pos, 1.8f + MathF.Sin(t * 30) * 0.2f, new Vector4(0.6f, 0.2f, 1f, 0.8f)); break;
                case ProjKind.EnemyPlasma:
                    Billboard(p.Pos, 0.45f, new Vector4(1f, 0.3f, 0.8f, 0.9f)); Billboard(p.Pos, 0.18f, new Vector4(1f, 0.9f, 1f, 1f));
                    BeamQuad(p.Pos, p.Pos - Vector3.Normalize(p.Vel) * 0.7f, 0.14f, new Vector4(1f, 0.3f, 0.8f, 0.5f)); break;
                case ProjKind.EnemyOrb:
                    Billboard(p.Pos, 0.8f + MathF.Sin(t * 25) * 0.1f, new Vector4(0.7f, 0.4f, 1f, 0.9f)); Billboard(p.Pos, 0.3f, Vector4.One); break;
                case ProjKind.BruteOrb:
                    Billboard(p.Pos, 1.3f + MathF.Sin(t * 20) * 0.15f, new Vector4(1f, 0.45f, 0.2f, 0.9f)); Billboard(p.Pos, 0.5f, new Vector4(1, 0.9f, 0.6f, 1)); break;
                case ProjKind.Rocket:
                    Billboard(p.Pos - Vector3.Normalize(p.Vel) * 0.45f, 0.6f, new Vector4(1f, 0.7f, 0.3f, 1)); break;
            }
        }
        foreach (var pk in w.Pickups)
        {
            if (pk.Kind == PickupKind.Key) Billboard(pk.Pos + Vector3.UnitY * (0.5f + MathF.Sin(pk.Phase * 2.2f) * 0.1f), 1.4f, new Vector4(Level.KeyColors[pk.Arg], 0.5f));
            if (pk.Kind == PickupKind.Weapon && Weapons.All[pk.Arg].Alien) Billboard(pk.Pos + Vector3.UnitY * 0.3f, 1.2f, new Vector4(Weapons.All[pk.Arg].Flash, 0.35f));
            if (pk.Kind == PickupKind.Upgrade) Billboard(pk.Pos + Vector3.UnitY * 0.25f, 1.3f + MathF.Sin(t * 4) * 0.15f, new Vector4(1f, 0.75f, 0.2f, 0.4f));
            if (pk.Kind == PickupKind.DamageAmp) Billboard(pk.Pos + Vector3.UnitY * (0.1f + MathF.Sin(pk.Phase * 2.2f) * 0.1f), 1.6f + MathF.Sin(t * 6) * 0.2f, new Vector4(1f, 0.5f, 0.1f, 0.5f));
            if (pk.Kind == PickupKind.MegaHealth || pk.Kind == PickupKind.Cells) Billboard(pk.Pos + Vector3.UnitY * 0.1f, 0.9f, new Vector4(0.3f, 0.9f, 1f, 0.3f));
        }
        foreach (var e in w.Enemies)
        {
            if (e.Dead) continue;
            if (e.SpawnT > 0) Billboard(e.Mid, 3.5f * e.SpawnT, new Vector4(0.7f, 0.3f, 1f, e.SpawnT));
            if (e.State == EState.Windup && e.RangedWindup)
            {
                var facing = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
                float k = 1 - Math.Clamp(e.StateT / w.Windup, 0, 1);
                var c = e.D.Ranged switch { ProjKind.EnemyAcid => new Vector3(0.4f, 1f, 0.2f), ProjKind.EnemyPlasma => new Vector3(1f, 0.3f, 0.8f), ProjKind.BruteOrb => new Vector3(1f, 0.5f, 0.2f), _ => new Vector3(0.7f, 0.4f, 1f) };
                Billboard(e.EyePos + facing * (e.D.Radius + 0.2f) - Vector3.UnitY * 0.2f, 0.3f + k * 0.6f, new Vector4(c, 0.8f));
            }
        }
        Flush(particleTex);
        gl.DepthMask(true);

        if (vision == 2) DrawXRay(w);

        // ---------- first person weapon ----------
        muzzleValid = false;
        if (!P.Dead && w.VictoryT < 0) DrawViewModel(w, aspect);
        gl.Disable(EnableCap.Blend);
        gl.DepthRange(0, 1);
        DrawDistortion(w);
        SetPostParams(w, fbW, fbH);
        post.Finish(pp);
    }

    /// <summary>Colour grade per area: (shadow lift, gain, saturation, contrast).</summary>
    static (Vector3 lift, Vector3 gain, float sat, float con) GradeFor(int n) => n switch
    {
        <= 4 => (new(0, 0.008f, 0.012f), new(0.98f, 1f, 1.02f), 1f, 1.05f),                // surface, security, barracks, mess
        <= 6 => (new(0.012f, 0.006f, 0), new(1.06f, 1f, 0.9f), 0.95f, 1.08f),              // armory, motor pool: warm industrial
        <= 8 => (new(0, 0.005f, 0.02f), new(0.92f, 1f, 1.1f), 1f, 1.06f),                  // supply, comms, power: cool tech
        <= 10 => (new(0, 0.012f, 0.01f), new(0.9f, 1.04f, 1f), 0.9f, 1.05f),               // water reclamation, tunnels: teal
        11 => (new(0, 0.004f, 0.016f), new(0.95f, 1f, 1.08f), 1f, 1.06f),                  // command center
        <= 13 => (new(0.004f, 0.01f, 0.012f), new(0.97f, 1.02f, 1.04f), 0.78f, 1f),        // medical, detention: clinical
        14 => (new(0.01f, 0.007f, 0.002f), new(1.03f, 1f, 0.92f), 0.85f, 1.05f),           // deep storage: dusty
        15 => (new(0.02f, 0.006f, 0), new(1.12f, 0.98f, 0.82f), 1.05f, 1.08f),             // geothermal: hot orange
        <= 19 => (new(0, 0.006f, 0.02f), new(0.9f, 1f, 1.12f), 0.92f, 1.06f),              // research labs: cold blue
        20 => (new(0.008f, 0.014f, 0), new(1.02f, 1.06f, 0.82f), 0.9f, 1.06f),             // quarantine: sickly yellow-green
        21 => (new(0.02f, 0.008f, 0), new(1.12f, 1f, 0.8f), 1.08f, 1.1f),                  // reactor: amber
        <= 23 => (new(0.014f, 0.008f, 0.002f), new(1.08f, 0.98f, 0.85f), 0.85f, 1.08f),    // excavation, mines: sepia
        <= 27 => (new(0.01f, 0, 0.016f), new(0.98f, 1.06f, 0.95f), 1.1f, 1.12f),           // infested, nest, hive, brood
        <= 29 => (new(0.016f, 0, 0.022f), new(1.04f, 0.92f, 1.1f), 1.1f, 1.12f),           // cathedral, breach: purple
        _ => (new(0.02f, 0, 0.016f), new(1.1f, 0.9f, 1.05f), 1.15f, 1.15f),                // the portal
    };

    void SetPostParams(World w, int fbW, int fbH)
    {
        var P = w.P;
        float aspect = fbW / (float)Math.Max(1, fbH);
        float th = MathF.Tan(Fov * MathF.PI / 360);
        pp.Bloom = BloomOn; pp.AO = AO && vision == 0;
        pp.TanHalf = new Vector2(th * aspect, th);
        pp.Near = 0.05f; pp.Far = 250f;
        if (vision == 0)
        {
            var g = GradeFor(w.L.Number);
            pp.Lift = g.lift; pp.Gain = g.gain; pp.Saturation = g.sat; pp.Contrast = g.con;
        }
        else { pp.Lift = Vector3.Zero; pp.Gain = Vector3.One; pp.Saturation = 1; pp.Contrast = 1; }
        pp.Vignette = 0.35f; pp.Lens = 0.06f;
        float low = P.Health < 35 && !P.Dead ? (35 - P.Health) / 35f : 0;
        // heartbeat: a strong beat and a softer echo, faster as health drops
        float ph = time * (1.1f + low * 0.7f) % 1f;
        float beat = MathF.Exp(-ph * 14) + 0.6f * (ph > 0.2f ? MathF.Exp(-(ph - 0.2f) * 14) : 0);
        pp.Hurt = MathF.Min(1, P.HurtFlash * 0.85f);
        pp.LowHealth = low * (0.3f + 0.55f * beat);
        pp.Desaturate = P.Dead ? 1 : low * 0.75f;
        pp.Chroma = P.HurtFlash * 0.014f + P.Shake * P.Shake * 0.006f;
        float gt = (time - goggleStart) / 0.45f;
        pp.GoggleT = gt >= 0 && gt < 1 ? gt : -1;
        pp.Time = time;
    }

    /// <summary>Additive soft frustum of light below a ceiling fixture, fading out toward the floor.</summary>
    void LightShaft(Vector3 fixture, Vector3 col, float tx, float tz, float bx, float bz, float a)
    {
        float top = Level.WH - 0.13f, bottom = 0.2f;
        var c = new Vector4(Vector3.Normalize(col + new Vector3(0.001f)) * MathF.Min(1.2f, col.Length()), a);
        var c0 = new Vector4(c.X, c.Y, c.Z, 0);
        Vector3 T(float x, float z) => new(fixture.X + x, top, fixture.Z + z);
        Vector3 B(float x, float z) => new(fixture.X + x, bottom, fixture.Z + z);
        var tc = new[] { T(-tx, -tz), T(tx, -tz), T(tx, tz), T(-tx, tz) };
        var bc = new[] { B(-bx, -bz), B(bx, -bz), B(bx, bz), B(-bx, bz) };
        for (int k = 0; k < 4; k++)
        {
            int k2 = (k + 1) % 4;
            uint i = (uint)pv.Count;
            pv.Add(new Vertex(tc[k], Vector3.Zero, new(0.5f, 0.5f), c));
            pv.Add(new Vertex(tc[k2], Vector3.Zero, new(0.5f, 0.5f), c));
            pv.Add(new Vertex(bc[k2], Vector3.Zero, new(0.5f, 0.5f), c0));
            pv.Add(new Vertex(bc[k], Vector3.Zero, new(0.5f, 0.5f), c0));
            pi.Add(i); pi.Add(i + 1); pi.Add(i + 2); pi.Add(i); pi.Add(i + 2); pi.Add(i + 3);
        }
    }

    /// <summary>Dust motes drifting down through the light shafts, and glowing spores on hive levels.</summary>
    void DrawDust()
    {
        float H = Level.WH;
        foreach (var (sp, sc, li) in shafts)
        {
            if (Vector3.DistanceSquared(sp, cam) > 16 * 16) continue;
            var col = Vector3.Normalize(sc + new Vector3(0.001f)) * MathF.Min(1, sc.Length());
            for (int m = 0; m < 7; m++)
            {
                float h0 = TexGen.Hash(li, m, 1), h1 = TexGen.Hash(li, m, 2), h2 = TexGen.Hash(li, m, 3), h3 = TexGen.Hash(li, m, 4);
                float f = (h0 + time * 0.012f * (0.5f + h1)) % 1f;
                float ex = GMath.Lerp(0.6f, 1.5f, f), ez = GMath.Lerp(0.2f, 1.0f, f);
                var p = new Vector3(sp.X + (h2 * 2 - 1) * ex + MathF.Sin(time * 0.6f + h3 * 6) * 0.12f,
                                    H - 0.25f - f * (H - 0.5f),
                                    sp.Z + (h3 * 2 - 1) * ez + MathF.Cos(time * 0.5f + h2 * 6) * 0.12f);
                Billboard(p, 0.035f, new Vector4(col, 0.55f * MathF.Sin(f * MathF.PI)));
            }
        }
        if (hiveDensity > 0)
        {
            const float Cell = 2.2f;
            int cx = (int)MathF.Floor(cam.X / Cell), cz = (int)MathF.Floor(cam.Z / Cell);
            for (int z = cz - 6; z <= cz + 6; z++)
                for (int x = cx - 6; x <= cx + 6; x++)
                {
                    if (TexGen.Hash(x, z, 61) > hiveDensity * 0.7f) continue;
                    float h1 = TexGen.Hash(x, z, 62), h2 = TexGen.Hash(x, z, 63), h3 = TexGen.Hash(x, z, 64);
                    var p = new Vector3((x + h1) * Cell + MathF.Sin(time * 0.4f + h2 * 9) * 0.4f,
                                        0.3f + (h3 + time * 0.02f * (0.5f + h1)) % 1f * (H - 0.6f),
                                        (z + h2) * Cell + MathF.Cos(time * 0.35f + h1 * 9) * 0.4f);
                    if (!builtFor.Floor((int)(p.X / Level.CS), (int)(p.Z / Level.CS))) continue;
                    float tw = 0.5f + 0.5f * MathF.Sin(time * 3 + h1 * 20);
                    Billboard(p, 0.07f + h2 * 0.05f, new Vector4(hiveColor, 0.35f + 0.5f * tw));
                }
        }
        Flush(particleTex);
    }

    /// <summary>Particles spiralling into the portal and around aliens teleporting in.</summary>
    void DrawSwirls(World w)
    {
        var portal = w.PortalE;
        if (portal != null && !portal.Dead)
        {
            var center = portal.Pos + new Vector3(0, 2.55f, 0);
            var right = Vector3.Transform(Vector3.UnitX, Matrix4x4.CreateRotationY(portal.Yaw));
            var nrm = Vector3.Cross(right, Vector3.UnitY);
            for (int i = 0; i < 48; i++)
            {
                float f = (time * 0.22f + i * 0.37f) % 1f;
                float r = (1 - f) * 5.2f, a = i * 2.4f + f * 7f;
                var p = center + right * MathF.Cos(a) * r + Vector3.UnitY * MathF.Sin(a) * r + nrm * MathF.Sin(i * 1.3f) * (1 - f) * 0.8f;
                Billboard(p, 0.12f + (1 - f) * 0.14f, new Vector4(0.8f, 0.4f, 1f, f * 0.9f));
            }
        }
        foreach (var e in w.Enemies)
        {
            if (e.Dead || e.SpawnT <= 0) continue;
            for (int i = 0; i < 16; i++)
            {
                float a = i * 0.4f + time * 7, r = 0.3f + e.SpawnT * 1.8f;
                float y = (i / 15f - 0.5f) * MathF.Max(1, e.D.Height) * (1.2f - e.SpawnT * 0.4f);
                Billboard(e.Mid + new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r), 0.16f, new Vector4(0.75f, 0.35f, 1f, e.SpawnT));
            }
        }
        Flush(particleTex);
    }

    /// <summary>Screen-space offset sprite for the distortion buffer (see DistFS).</summary>
    void DistSprite(Vector3 p, float size, int kind, float strength, float phase, float fade, bool ignoreDepth = false)
    {
        var r = camRight * size * 0.5f;
        var u = camUp * size * 0.5f;
        var prm = new Vector3(kind, strength, ignoreDepth ? 1 : 0);
        var c = new Vector4(phase, 0, 0, fade);
        uint b = (uint)pv.Count;
        pv.Add(new Vertex(p - r - u, prm, new(0, 0), c));
        pv.Add(new Vertex(p + r - u, prm, new(1, 0), c));
        pv.Add(new Vertex(p + r + u, prm, new(1, 1), c));
        pv.Add(new Vertex(p - r + u, prm, new(0, 1), c));
        pi.Add(b); pi.Add(b + 1); pi.Add(b + 2); pi.Add(b); pi.Add(b + 2); pi.Add(b + 3);
    }

    /// <summary>Shockwave rings, heat haze over hot projectiles, burning barrels and an overheated barrel,
    /// and the swirl of the portal and of teleporting aliens.</summary>
    void DrawDistortion(World w)
    {
        post.BeginDistortion();
        dist.Use();
        dist.Set("uView", view);
        dist.Set("uProj", proj);
        dist.Set("uDepth", 1);
        dist.Set("uTarget", post.HalfSize);
        dist.Set("uNear", 0.05f); dist.Set("uFar", 250f);
        dist.Set("uTime", time % 600f);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, post.DepthTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        foreach (var s in w.Shockwaves)
        {
            float k = s.Age / s.Life;
            DistSprite(s.Pos, s.Size * (0.3f + 0.7f * k), 0, s.Strength, k, 1 - k * k);
        }
        foreach (var p in w.Projs)
        {
            if (p.Kind is ProjKind.Plasma or ProjKind.EnemyPlasma) DistSprite(p.Pos, 1.0f, 1, 0.004f, p.Pos.X, 1);
            else if (p.Kind is ProjKind.Void or ProjKind.BruteOrb or ProjKind.EnemyOrb) DistSprite(p.Pos, p.Kind == ProjKind.Void ? 3.2f : 1.6f, 1, 0.006f, p.Pos.X, 1);
            else if (p.Kind == ProjKind.Rocket) DistSprite(p.Pos - Vector3.Normalize(p.Vel) * 0.8f, 1.2f, 1, 0.004f, p.Pos.X, 1);
        }
        foreach (var b in w.Barrels)
            if (!b.Dead && b.Fuse >= 0) DistSprite(b.Pos + new Vector3(0, 1.8f, 0), 1.8f, 1, 0.005f, b.Pos.Z, 1);
        var portal = w.PortalE;
        if (portal != null && !portal.Dead)
        {
            float shrink = w.VictoryT >= 0 ? MathF.Max(0, 1 - w.VictoryT / 3f) : 1;
            DistSprite(portal.Pos + new Vector3(0, 2.55f, 0), 11f * shrink, 2, 0.025f, 0, 1);
        }
        foreach (var e in w.Enemies)
            if (!e.Dead && e.SpawnT > 0) DistSprite(e.Mid, 2.5f + e.D.Height, 2, 0.035f, 0, e.SpawnT);
        Flush(white);
        if (muzzleValid && w.P.Heat > 0.25f)
        {
            dist.Set("uProj", lastVProj);
            DistSprite(lastMuzzle + camUp * 0.06f, 0.22f, 1, 0.005f * w.P.Heat, 0, 1, true);
            Flush(white);
        }
        gl.Disable(EnableCap.Blend);
    }

    void SetCommon(Matrix4x4 v, Matrix4x4 p, Player P)
    {
        var L = builtFor;
        lit.Set("uView", v);
        lit.Set("uProj", p);
        lit.Set("uTex", 0);
        lit.Set("uNormalMap", 1);
        lit.Set("uBump", 0f);
        lit.Set("uCamPos", cam);
        lit.Set("uAmbient", L.Ambient * (0.35f + 0.65f * powerLevel));
        lit.Set("uFogColor", L.FogColor);
        lit.Set("uFogDensity", L.FogDensity);
        lit.Set("uLightCount", lCount);
        lit.SetArray("uLightPos[0]", lPos, lCount);
        lit.SetArray("uLightCol[0]", lCol, lCount);
        lit.SetArray("uLightRad[0]", lRad, lCount);
        lit.SetArray("uLightSpot[0]", lSpot, lCount);
        lit.Set("uShadowCount", shadowCount);
        for (int k = 0; k < MaxShadows; k++)
        {
            lit.Set("uShadow" + k, 2 + k);
            gl.ActiveTexture(TextureUnit.Texture2 + k);
            gl.BindTexture(TextureTarget.TextureCubeMap, shadowCube[k]);
        }
        lit.Set("uFlashShadow", 6);
        gl.ActiveTexture(TextureUnit.Texture6);
        gl.BindTexture(TextureTarget.Texture2D, flashTex);
        gl.ActiveTexture(TextureUnit.Texture0);
        lit.Set("uFlashShadowOn", flashShadowOn ? 1f : 0f);
        lit.Set("uFlashVP", flashVP);
        lit.Set("uFlashFar", FlashFar);
        lit.Set("uBlink", 0f);
        lit.Set("uTime", time % 3600f);
        lit.Set("uVision", vision);
        lit.Set("uHot", 0f);
        lit.Set("uXrayWorld", 0f);
        lit.Set("uPulse", 0f);
        lit.Set("uScreen", screen);
        lit.Set("uFlashOn", P.Flashlight && !P.Dead ? 1f : 0f);
        lit.Set("uFlashPos", flashPos);
        lit.Set("uFlashDir", P.Forward);
        lit.Set("uTexGlow", 0f);
    }

    void Tint(Vector4 c, float glow)
    {
        lit.Set("uTint", c);
        lit.Set("uGlow", glow);
    }

    // =====================================================================  enemy models
    static Matrix4x4 Seg(Vector3 a, Vector3 b, float th)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-5f) return Matrix4x4.CreateScale(0);
        var dir = d / len;
        Quaternion q;
        float dot = Vector3.Dot(Vector3.UnitY, dir);
        if (dot > 0.9999f) q = Quaternion.Identity;
        else if (dot < -0.9999f) q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        else q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir)), MathF.Acos(dot));
        return Matrix4x4.CreateScale(th, len, th) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(a);
    }

    static Matrix4x4 SP(Vector3 scale, Vector3 pos) => Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(pos);
    static Matrix4x4 SRP(Vector3 scale, Matrix4x4 rot, Vector3 pos) => Matrix4x4.CreateScale(scale) * rot * Matrix4x4.CreateTranslation(pos);

    Matrix4x4 root;
    float flash, alpha;
    Vector4? hotTint;   // overrides part colors (goggle silhouettes); w = minimum alpha
    int vision;         // 0 normal, 1 night vision, 2 x-ray

    void DrawPickup(Pickup pk, Vector4? overrideTint)
    {
        float bob = MathF.Sin(pk.Phase * 2.2f) * 0.1f;
        var rot = Matrix4x4.CreateRotationY(pk.Phase * 1.3f);
        var pos = pk.Pos + new Vector3(0, bob, 0);
        if (pk.Kind == PickupKind.Weapon)
        {
            metal.Bind();
            lit.Set("uModel", Matrix4x4.CreateScale(1.7f) * Matrix4x4.CreateRotationZ(0.2f) * rot * Matrix4x4.CreateTranslation(pos + Vector3.UnitY * 0.25f));
            Tint(overrideTint ?? Vector4.One, 0.15f);
            weaponWorld[pk.Arg].Draw();
        }
        else
        {
            white.Bind();
            var tint = pk.Kind == PickupKind.Key ? new Vector4(Level.KeyColors[pk.Arg], 1) : Vector4.One;
            var scale = pk.Kind == PickupKind.Key ? Matrix4x4.CreateScale(1.3f) : Matrix4x4.Identity;
            lit.Set("uModel", scale * rot * Matrix4x4.CreateTranslation(pos + (pk.Kind == PickupKind.Key ? Vector3.UnitY * 0.5f : Vector3.Zero)));
            Tint(overrideTint ?? tint, 0);
            pickupMesh[pk.Kind].Draw();
        }
    }

    readonly Vector4[] lenses = new Vector4[12];
    readonly List<(float D, Vector4 L)> lensCands = new();

    /// <summary>X-ray goggles: one lens per alien or keycard hidden behind level geometry, nearest first.</summary>
    void SetXrayLenses(World w)
    {
        int n = 0;
        if (vision == 2)
        {
            const float Range = 48;
            lensCands.Clear();
            void Add(Vector3 pos, float r)
            {
                float d = Vector3.Distance(pos, cam);
                if (d > Range || d < 1f || w.L.LineOfSight(cam, pos)) return;
                lensCands.Add((d, new Vector4(pos, MathF.Atan(r / d))));
            }
            foreach (var e in w.Enemies)
                if (!e.Dead) Add(e.Mid, MathF.Max(e.D.Radius, e.D.Height * 0.5f) * 1.25f + 0.35f);
            foreach (var pk in w.Pickups)
                if (pk.Kind == PickupKind.Key) Add(pk.Pos, 0.7f);
            lensCands.Sort((a, b) => a.D.CompareTo(b.D));
            for (; n < lensCands.Count && n < lenses.Length; n++) lenses[n] = lensCands[n].L;
        }
        lit.Set("uLensCount", n);
        if (n > 0) lit.SetArray("uLens", lenses, n);
    }

    /// <summary>X-ray goggles: aliens and items behind walls drawn as glowing silhouettes
    /// (depth test GREATER, so only the hidden parts light up).</summary>
    void DrawXRay(World w)
    {
        const float Range = 48;
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        gl.DepthMask(false);
        gl.DepthFunc(DepthFunction.Greater);
        lit.Use();
        lit.Set("uHot", 1f);
        foreach (var pk in w.Pickups)
        {
            if (Vector3.DistanceSquared(pk.Pos, cam) > Range * Range) continue;
            var c = pk.Kind == PickupKind.Key ? new Vector4(Level.KeyColors[pk.Arg] * 1.2f, 0.9f) : new Vector4(1f, 0.85f, 0.3f, 0.6f);
            DrawPickup(pk, c);
        }
        skin.Bind();
        hotTint = new Vector4(1f, 0.42f, 0.15f, 0.75f);
        foreach (var e in w.Enemies)
        {
            if (e.Dead || Vector3.DistanceSquared(e.Pos, cam) > Range * Range) continue;
            DrawEnemy(e, w, e.Type == EnemyDefs.Stalker);
        }
        hotTint = null;
        lit.Set("uHot", 0f);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(true);
    }

    void Part(Mesh m, Matrix4x4 local, Vector3 col, float glow = 0)
    {
        if (capture != null)
        {
            capture.Add((new Ragdoll.Part { Mesh = m, M0 = local * root, Col = col, Glow = glow }, pendingLimb != null));
            if (pendingLimb is var (a, b, th)) { captureLimbs.Add((Vector3.Transform(a, root), Vector3.Transform(b, root), th)); pendingLimb = null; }
            return;
        }
        cur.Set("uModel", local * root);
        if (cur == lit && hotTint is Vector4 h)
            lit.Set("uTint", new Vector4(h.X, h.Y, h.Z, MathF.Max(alpha, h.W)));
        else if (cur == lit)
        {
            var c = Vector3.Lerp(col, new Vector3(1f, 0.9f, 0.9f), flash);
            lit.Set("uTint", new Vector4(c, alpha));
            lit.Set("uGlow", MathF.Max(glow, flash * 0.6f));
        }
        m.Draw();
    }

    void Limb(Vector3 a, Vector3 b, float th, Vector3 col, float glow = 0)
    {
        if (capture != null && Vector3.DistanceSquared(a, b) > 1e-8f) pendingLimb = (a, b, th);
        Part(cyl, Seg(a, b, th), col, glow);
    }

    // ragdoll capture: while set, Part() records world-space parts instead of drawing
    List<(Ragdoll.Part P, bool IsLimb)> capture;
    readonly List<(Vector3 A, Vector3 B, float Th)> captureLimbs = new();
    (Vector3 A, Vector3 B, float Th)? pendingLimb;
    public bool Ragdolls = true;

    /// <summary>Record the alien's pose at death and turn it into a ragdoll launched along the killing blow.</summary>
    void CaptureRagdoll(Enemy e, World w)
    {
        capture = new();
        captureLimbs.Clear();
        pendingLimb = null;
        var keep = (root, flash, alpha, hotTint);
        hotTint = null;
        DrawEnemy(e, w, true);
        var parts = capture;
        capture = null;
        (root, flash, alpha, hotTint) = keep;
        if (parts.Count == 0) return;
        var up = e.OnCeiling ? -Vector3.UnitY : Vector3.UnitY;
        var fwd = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
        e.Rag = new Ragdoll(new(captureLimbs), parts, up, fwd);
        e.Rag.Push(e.DeathVel, new Random((int)(e.Seed * 1000)));
    }

    void DrawRagdoll(Enemy e)
    {
        root = Matrix4x4.Identity;
        flash = 0; alpha = 1;
        float sink = e.DeathT > 10 ? MathF.Min(1, (e.DeathT - 10) * 0.1f) : 0;
        var down = Matrix4x4.CreateTranslation(0, -sink * 0.8f, 0);
        foreach (var p in e.Rag.Parts) Part(p.Mesh, e.Rag.Transform(p) * down, p.Col, p.Glow);
    }
    void Spike(Vector3 a, Vector3 b, float th, Vector3 col, float glow = 0) => Part(cone, Seg(a, b, th), col, glow);

    void DrawEnemy(Enemy e, World w, bool transparentPass)
    {
        var D = e.D;
        float t = e.Anim + e.Seed;
        float death = e.Dead && capture == null ? MathF.Min(1, e.DeathT * 2.2f) : 0;
        if (e.Dead && e.D.Stationary) return;
        if (e.Dead && e.Gibbed) return;
        if (e.Dead && capture == null && Ragdolls && !e.Hidden)
        {
            if (e.Rag == null) CaptureRagdoll(e, w);
            if (e.Rag != null)
            {
                if (e.Type == EnemyDefs.Stalker && !transparentPass) return;
                DrawRagdoll(e);
                return;
            }
        }
        if (e.Hidden)
        {
            // burrowed: a mound of earth ploughing along the floor
            if (transparentPass) return;
            root = Matrix4x4.CreateRotationY(-e.Yaw) * Matrix4x4.CreateTranslation(e.Pos);
            flash = 0; alpha = 1;
            float bump = 0.85f + 0.15f * MathF.Sin(e.Anim * 9 + e.Seed);
            Part(sphere, SP(new(1.0f, 0.32f * bump, 1.3f), Vector3.Zero), new(0.3f, 0.24f, 0.18f));
            Part(sphere, SP(new(0.55f, 0.24f, 0.6f), new(0.25f, 0.04f, 0.35f)), new(0.25f, 0.2f, 0.15f));
            Part(sphere, SP(new(0.5f, 0.2f, 0.5f), new(-0.2f, 0.03f, -0.4f)), new(0.34f, 0.27f, 0.2f));
            return;
        }
        var pos = e.Pos;
        if (e.Dead && D.FlyHeight > 0) pos.Y = MathF.Max(0.1f, D.FlyHeight * (1 - MathF.Min(1, e.DeathT * 2f)));
        root = Matrix4x4.CreateRotationY(-e.Yaw) * Matrix4x4.CreateTranslation(pos);
        // clinging to the ceiling: hang upside down from it
        if (e.OnCeiling) root = Matrix4x4.CreateScale(1, -1, 1) * Matrix4x4.CreateRotationY(-e.Yaw) * Matrix4x4.CreateTranslation(pos.X, Level.WH, pos.Z);
        if (death > 0)
        {
            float sink = e.DeathT > 10 ? MathF.Min(1, (e.DeathT - 10) * 0.1f) : 0;
            root = Matrix4x4.CreateScale(1, 1 - death * 0.45f, 1) * Matrix4x4.CreateRotationX(death * 1.2f) * Matrix4x4.CreateTranslation(0, -sink * 0.6f, 0) * root;
        }
        if (e.SpawnT > 0) root = Matrix4x4.CreateScale(1 - e.SpawnT * 0.9f) * root;
        flash = e.PainFlash > 0 ? 0.7f : 0;
        alpha = 1;
        float walk = e.Anim * (1.5f + e.MoveSpeed * 1.6f) + e.Seed;
        float stride = Math.Clamp(e.MoveSpeed / 3f, 0, 1.2f);
        float wind = e.State == EState.Windup ? 1 - Math.Clamp(e.StateT / (e.RangedWindup ? w.Windup : 0.3f), 0, 1) : 0;
        float breathe = 1 + MathF.Sin(t * 2.2f) * 0.025f;
        var c = D.Color;
        float lv = (w.L.Number - 1) / 29f;

        switch (e.Type)
        {
            case EnemyDefs.Skitter:
            {
                Part(sphere, SP(new(0.75f, 0.42f * breathe, 0.95f), new(0, 0.42f, 0.05f)), c);
                Part(sphere, SP(new(0.48f, 0.36f, 0.45f), new(0, 0.48f, -0.48f)), c * 0.85f);
                Part(sphere, SP(new(0.09f), new(0.12f, 0.56f, -0.66f)), new(1f, 0.15f, 0.1f), 1);
                Part(sphere, SP(new(0.09f), new(-0.12f, 0.56f, -0.66f)), new(1f, 0.15f, 0.1f), 1);
                float bite = MathF.Sin(t * 12) * 0.05f + wind * 0.15f;
                Spike(new(0.1f, 0.42f, -0.62f), new(0.04f + bite, 0.32f, -0.85f), 0.07f, c * 0.6f);
                Spike(new(-0.1f, 0.42f, -0.62f), new(-0.04f - bite, 0.32f, -0.85f), 0.07f, c * 0.6f);
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ph = walk * 2 + i * 2.1f + (s > 0 ? MathF.PI : 0);
                        float sw = MathF.Sin(ph) * 0.22f * stride;
                        float lift = MathF.Max(0, MathF.Cos(ph)) * 0.15f * stride;
                        var hip = new Vector3(s * 0.28f, 0.42f, -0.3f + i * 0.3f);
                        var knee = new Vector3(s * 0.62f, 0.68f + lift, -0.3f + i * 0.32f + sw * 0.5f);
                        var foot = new Vector3(s * 0.85f, lift * 0.5f, -0.35f + i * 0.36f + sw);
                        Limb(hip, knee, 0.07f, c * 0.75f);
                        Limb(knee, foot, 0.05f, c * 0.6f);
                    }
                break;
            }
            case EnemyDefs.Grunt:
            case EnemyDefs.Trooper:
            {
                bool trooper = e.Type == EnemyDefs.Trooper;
                var armor = trooper ? new Vector3(0.25f, 0.22f, 0.32f) : c;
                for (int s = -1; s <= 1; s += 2)
                {
                    float ph = walk + (s > 0 ? 0 : MathF.PI);
                    float sw = MathF.Sin(ph) * 0.35f * stride;
                    float lift = MathF.Max(0, MathF.Cos(ph)) * 0.2f * stride;
                    var hip = new Vector3(s * 0.18f, 0.98f, 0.02f);
                    var foot = new Vector3(s * 0.2f, lift, -sw);
                    var knee = new Vector3(s * 0.21f, 0.52f + lift * 0.5f, (hip.Z + foot.Z) * 0.5f + (trooper ? -0.1f : 0.16f));
                    Limb(hip, knee, 0.17f, trooper ? armor : c * 0.9f);
                    Limb(knee, foot, 0.13f, c * 0.75f);
                    if (trooper) Part(cube, SP(new(0.16f, 0.14f, 0.3f), foot + new Vector3(0, 0.07f, -0.06f)), armor * 0.8f);
                    else Spike(foot + new Vector3(0, 0.05f, 0), foot + new Vector3(0, 0.02f, -0.25f), 0.1f, c * 0.6f);
                }
                if (trooper)
                {
                    Part(cube, SP(new(0.62f, 0.62f * breathe, 0.4f), new(0, 1.35f, 0)), armor);
                    Part(sphere, SP(new(0.16f), new(0, 1.42f, -0.2f)), new(0.3f, 0.9f, 1f), 1);
                    Part(cube, SP(new(0.28f, 0.14f, 0.36f), new(0.36f, 1.66f, 0)), armor * 1.2f);
                    Part(cube, SP(new(0.28f, 0.14f, 0.36f), new(-0.36f, 1.66f, 0)), armor * 1.2f);
                    Part(sphere, SP(new(0.4f, 0.44f, 0.44f), new(0, 1.88f, 0)), armor * 1.1f);
                    Part(cube, SP(new(0.3f, 0.08f, 0.1f), new(0, 1.9f, -0.2f)), new(0.3f, 0.9f, 1f), 1);
                    // arms holding a rifle
                    var gun = new Vector3(0.18f, 1.3f, -0.45f - wind * 0.1f);
                    Limb(new(0.33f, 1.55f, 0), new(0.38f, 1.25f, -0.15f), 0.12f, c);
                    Limb(new(0.38f, 1.25f, -0.15f), gun + new Vector3(0.05f, 0, 0.15f), 0.1f, c);
                    Limb(new(-0.33f, 1.55f, 0), new(-0.3f, 1.25f, -0.25f), 0.12f, c);
                    Limb(new(-0.3f, 1.25f, -0.25f), gun + new Vector3(0, 0, -0.2f), 0.1f, c);
                    Part(cube, SP(new(0.12f, 0.16f, 0.85f), gun), new(0.18f, 0.16f, 0.22f));
                    Part(sphere, SP(new(0.1f, 0.1f, 0.3f), gun + new Vector3(0, 0.1f, -0.1f)), new(1f, 0.3f, 0.8f), 0.6f + wind * 0.4f);
                }
                else
                {
                    Part(sphere, SP(new(0.6f, 0.78f * breathe, 0.46f), new(0, 1.3f, 0)), c);
                    Part(sphere, SP(new(0.34f, 0.34f, 0.62f), new(0, 1.76f, -0.16f)), c * 1.05f);
                    Spike(new(0, 1.8f, 0.05f), new(0, 1.95f, 0.45f), 0.22f, c * 0.8f);
                    Part(sphere, SP(new(0.08f, 0.06f, 0.08f), new(0.1f, 1.8f, -0.42f)), new(1f, 0.85f, 0.2f), 1);
                    Part(sphere, SP(new(0.08f, 0.06f, 0.08f), new(-0.1f, 1.8f, -0.42f)), new(1f, 0.85f, 0.2f), 1);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float sw = MathF.Sin(walk + (s > 0 ? MathF.PI : 0)) * 0.25f * stride;
                        var sh = new Vector3(s * 0.33f, 1.55f, 0);
                        Vector3 el, hand;
                        if (wind > 0) { el = new(s * 0.45f, 1.7f, -0.25f); hand = new(s * 0.3f, 1.95f - wind * 1.0f, -0.55f - wind * 0.2f); }
                        else { el = new(s * 0.46f, 1.15f, -0.05f + sw * 0.3f); hand = new(s * 0.42f, 0.85f, -0.3f + sw * 0.6f); }
                        Limb(sh, el, 0.12f, c * 0.9f);
                        Limb(el, hand, 0.1f, c * 0.85f);
                        for (int k = -1; k <= 1; k++)
                            Spike(hand, hand + Vector3.Normalize(hand - el) * 0.22f + new Vector3(k * 0.05f, 0, 0), 0.04f, new Vector3(0.85f, 0.8f, 0.7f));
                    }
                }
                break;
            }
            case EnemyDefs.Spitter:
            {
                float pulse = 1 + MathF.Sin(t * 4) * 0.06f + wind * 0.25f;
                Part(sphere, SP(new(0.95f, 0.8f * breathe, 0.95f), new(0, 0.78f, 0)), c);
                Part(sphere, SP(new(0.62f * pulse), new(0, 1.12f, 0.3f)), new(0.4f, 1f, 0.2f), 0.55f + wind * 0.4f);
                Part(sphere, SP(new(0.5f, 0.42f, 0.55f), new(0, 1.05f, -0.42f)), c * 0.9f);
                Part(sphere, SP(new(0.3f, 0.12f + wind * 0.15f, 0.2f), new(0, 0.98f, -0.66f)), new(0.1f, 0.02f, 0.02f));
                for (int k = -1; k <= 1; k++) Part(sphere, SP(new(0.07f), new(k * 0.13f, 1.18f, -0.65f)), new(0.6f, 1f, 0.3f), 1);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i < 2 ? -1 : 1, sz = i % 2 == 0 ? -1 : 1;
                    float sw = MathF.Sin(walk * 1.6f + i * 1.6f) * 0.15f * stride;
                    Limb(new(sx * 0.35f, 0.6f, sz * 0.25f), new(sx * 0.55f, 0, sz * 0.35f + sw), 0.13f, c * 0.75f);
                }
                break;
            }
            case EnemyDefs.Stalker:
            {
                if (!transparentPass) return;
                float dist = Vector3.Distance(e.Pos, w.P.Pos);
                float cloak = e.Dead ? 1 : MathF.Max(e.State == EState.Windup || e.PainFlash > 0 ? 1 : 0, GMath.Clamp01(1.2f - dist / 7f));
                alpha = GMath.Lerp(0.12f + 0.05f * MathF.Sin(t * 9), 1f, cloak);
                Part(sphere, SP(new(0.42f, 0.42f * breathe, 1.25f), new(0, 1.0f, 0.05f)), c);
                Spike(new(0, 1.05f, -0.5f), new(0, 0.95f, -1.15f), 0.34f, c * 1.2f);
                Part(cube, SP(new(0.2f, 0.02f, 0.05f), new(0.07f, 1.07f, -0.85f)), new(1f, 0.1f, 0.1f), 1);
                Part(cube, SP(new(0.2f, 0.02f, 0.05f), new(-0.07f, 1.07f, -0.85f)), new(1f, 0.1f, 0.1f), 1);
                for (int k = 0; k < 4; k++) Part(cube, SP(new(0.05f, 0.03f, 0.18f), new(0, 1.2f, -0.35f + k * 0.25f)), new(1f, 0.15f, 0.1f), 0.8f);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i < 2 ? -1 : 1, sz = i % 2 == 0 ? -1 : 1;
                    float ph = walk * 1.4f + (i == 0 || i == 3 ? 0 : MathF.PI);
                    float sw = MathF.Sin(ph) * 0.4f * stride;
                    float lift = MathF.Max(0, MathF.Cos(ph)) * 0.25f * stride;
                    var hip = new Vector3(sx * 0.2f, 1.0f, sz * 0.45f);
                    var foot = new Vector3(sx * 0.4f, lift, sz * 0.7f - sw);
                    var knee = new Vector3(sx * 0.45f, 1.45f + lift, (hip.Z + foot.Z) * 0.5f + sz * 0.1f);
                    if (sz < 0 && wind > 0) foot = new Vector3(sx * 0.3f, 1.2f - wind * 0.6f, -1.2f);
                    Limb(hip, knee, 0.09f, c);
                    Limb(knee, foot, 0.07f, c * 0.8f);
                }
                var tail = new Vector3(0, 1.0f, 0.65f);
                for (int k = 0; k < 4; k++)
                {
                    var next = tail + new Vector3(MathF.Sin(t * 3 + k) * 0.15f, 0.12f - k * 0.05f, 0.3f);
                    Spike(tail, next, 0.14f - k * 0.025f, c);
                    tail = next;
                }
                break;
            }
            case EnemyDefs.Wraith:
            {
                Part(sphere, SP(new(1.05f, 0.75f * breathe, 1.05f), new(0, 1.0f, 0)), c, 0.25f);
                Part(sphere, SP(new(0.42f + wind * 0.15f), new(0, 0.9f, 0)), new(0.9f, 0.6f, 1f), 1);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * MathF.Tau / 6;
                    Part(sphere, SP(new(0.08f), new(MathF.Cos(a) * 0.47f, 1.05f, MathF.Sin(a) * 0.47f)), new(1f, 0.9f, 0.4f), 1);
                    var p0 = new Vector3(MathF.Cos(a) * 0.35f, 0.7f, MathF.Sin(a) * 0.35f);
                    for (int j = 0; j < 3; j++)
                    {
                        var p1 = p0 + new Vector3(MathF.Sin(t * 2 + k + j) * 0.15f, -0.4f, MathF.Cos(t * 1.7f + k * 2 + j) * 0.15f);
                        Limb(p0, p1, 0.08f - j * 0.02f, c * 0.8f, 0.2f);
                        p0 = p1;
                    }
                }
                break;
            }
            case EnemyDefs.Brute:
            case EnemyDefs.Warlord:
            {
                bool lord = e.Type == EnemyDefs.Warlord;
                if (lord) root = Matrix4x4.CreateScale(1.28f) * root;
                for (int s = -1; s <= 1; s += 2)
                {
                    float ph = walk * 0.8f + (s > 0 ? 0 : MathF.PI);
                    float sw = MathF.Sin(ph) * 0.4f * stride;
                    float lift = MathF.Max(0, MathF.Cos(ph)) * 0.25f * stride;
                    var hip = new Vector3(s * 0.42f, 1.35f, 0.1f);
                    var foot = new Vector3(s * 0.5f, lift, -sw);
                    var knee = new Vector3(s * 0.52f, 0.72f + lift * 0.5f, (hip.Z + foot.Z) * 0.5f - 0.1f);
                    Limb(hip, knee, 0.36f, c * 0.9f);
                    Limb(knee, foot, 0.3f, c * 0.8f);
                    Part(cube, SP(new(0.38f, 0.2f, 0.5f), foot + new Vector3(0, 0.1f, -0.1f)), c * 0.6f);
                }
                Part(sphere, SP(new(1.45f, 1.25f * breathe, 1.05f), new(0, 1.95f, 0.05f)), c);
                Part(sphere, SP(new(0.35f + wind * 0.2f), new(0, 1.95f, -0.45f)), new(1f, 0.5f, 0.15f), 1);
                Part(sphere, SP(new(0.5f, 0.45f, 0.55f), new(0, 2.6f, -0.4f)), c * 0.9f);
                Part(sphere, SP(new(0.1f), new(0.12f, 2.65f, -0.65f)), new(1f, 0.6f, 0.1f), 1);
                Part(sphere, SP(new(0.1f), new(-0.12f, 2.65f, -0.65f)), new(1f, 0.6f, 0.1f), 1);
                for (int k = 0; k < 5; k++) Spike(new(-0.4f + k * 0.2f, 2.3f, 0.4f), new(-0.5f + k * 0.25f, 2.9f, 0.7f), 0.2f, new Vector3(0.75f, 0.7f, 0.6f));
                for (int s = -1; s <= 1; s += 2)
                {
                    float sw = MathF.Sin(walk * 0.8f + (s > 0 ? MathF.PI : 0)) * 0.3f * stride;
                    var sh = new Vector3(s * 0.78f, 2.3f, 0);
                    Vector3 el, fist;
                    if (wind > 0 && !e.RangedWindup) { el = new(s * 0.9f, 2.7f, -0.3f); fist = new(s * 0.6f, 3.0f - wind * 2.2f, -0.9f); }
                    else { el = new(s * 1.05f, 1.6f, -0.15f + sw * 0.3f); fist = new(s * 0.95f, 1.0f, -0.5f + sw * 0.6f); }
                    Limb(sh, el, 0.32f, c * 0.95f);
                    Limb(el, fist, 0.28f, c * 0.9f);
                    if (lord && s > 0)
                    {
                        // arm cannon
                        var dir = Vector3.Normalize(fist - el);
                        Part(cyl, Seg(fist - dir * 0.2f, fist + dir * 0.7f, 0.42f), new Vector3(0.22f, 0.2f, 0.22f));
                        Part(sphere, SP(new(0.3f), fist + dir * 0.72f), new(1f, 0.45f, 0.15f), 0.5f + (e.RangedWindup ? wind * 0.5f : 0));
                    }
                    else Part(sphere, SP(new(0.5f), fist), c * 0.7f);
                }
                if (lord)
                {
                    // armour plating, helmet, horns
                    var plate = new Vector3(0.24f, 0.22f, 0.25f);
                    Part(cube, SP(new(1.35f, 0.95f, 0.5f), new(0, 2.05f, -0.45f)), plate);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Part(cube, SP(new(0.65f, 0.3f, 0.75f), new(s * 0.85f, 2.55f, 0)), plate * 1.2f);
                        for (int k = 0; k < 3; k++) Spike(new(s * (0.65f + k * 0.15f), 2.68f, -0.2f + k * 0.2f), new(s * (0.75f + k * 0.18f), 3.2f, -0.1f + k * 0.2f), 0.12f, new Vector3(0.8f, 0.75f, 0.65f));
                        Spike(new(s * 0.25f, 2.85f, -0.35f), new(s * 0.65f, 3.45f, -0.1f), 0.18f, new Vector3(0.85f, 0.8f, 0.7f));
                    }
                    Part(sphere, SP(new(0.62f, 0.5f, 0.62f), new(0, 2.68f, -0.38f)), plate * 1.1f);
                    Part(cube, SP(new(0.42f, 0.07f, 0.05f), new(0, 2.68f, -0.69f)), new(1f, 0.2f, 0.1f), 1);
                    if (e.Mode == 1 || e.Mode == 2) Part(sphere, SP(new(0.9f), new(0, 2.05f, -0.75f)), new(1f, 0.35f, 0.1f), 1);
                    if (e.Mode == 3) for (int k = 0; k < 4; k++) Part(sphere, SP(new(0.15f), new(MathF.Cos(t * 6 + k * 1.57f) * 0.6f, 3.1f, MathF.Sin(t * 6 + k * 1.57f) * 0.6f)), new(1f, 0.9f, 0.3f), 1);
                }
                break;
            }
            case EnemyDefs.Bloater:
            {
                float fuse = e.SpecialT > 0 ? 1 - e.SpecialT / 0.7f : 0;
                float sw = (1 + MathF.Sin(t * 3) * 0.04f) * (1 + fuse * 0.5f);
                var sac = Vector3.Lerp(c, new Vector3(0.8f, 0.9f, 0.3f), fuse);
                var mid = new Vector3(0, 0.9f, 0.05f);
                Part(sphere, SP(new Vector3(1.15f, 1.05f, 1.15f) * sw, mid), sac);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 2.4f + e.Seed, el = -0.2f + (k % 3) * 0.45f;
                    var p = new Vector3(MathF.Cos(a) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(a) * MathF.Cos(el)) * 0.52f * sw + mid;
                    float pulse = 0.5f + 0.5f * MathF.Sin(t * (4 + fuse * 30) + k);
                    Part(sphere, SP(new Vector3(0.18f + pulse * 0.06f) * sw, p), new(0.6f, 1f, 0.25f), 0.4f + 0.6f * MathF.Max(pulse * 0.6f, fuse));
                }
                Part(sphere, SP(new(0.42f, 0.36f, 0.42f), new(0, 0.7f, -0.55f * sw)), c * 0.75f);
                Part(sphere, SP(new(0.08f), new(0.12f, 0.78f, -0.72f * sw)), new(1f, 0.85f, 0.2f), 1);
                Part(sphere, SP(new(0.08f), new(-0.12f, 0.78f, -0.72f * sw)), new(1f, 0.85f, 0.2f), 1);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i < 2 ? -1 : 1, sz = i % 2 == 0 ? -1 : 1;
                    float ph = walk * 2 + i * 1.6f;
                    float lift = MathF.Max(0, MathF.Sin(ph)) * 0.12f * stride;
                    Limb(new(sx * 0.35f, 0.55f, sz * 0.3f), new(sx * 0.5f, lift, sz * 0.4f + MathF.Cos(ph) * 0.1f * stride), 0.14f, c * 0.7f);
                }
                break;
            }
            case EnemyDefs.Burrower:
            {
                var under = c * 0.6f + new Vector3(0.1f, 0.05f, 0.03f);
                for (int k = 5; k >= 0; k--)
                {
                    float f = k / 5f;
                    float wav = MathF.Sin(t * 4 - k * 0.9f) * 0.08f * (0.3f + stride);
                    var p = new Vector3(wav, 0.25f + (1 - f) * (0.95f + wind * 0.2f), -0.55f + f * 1.6f - (1 - f) * wind * 0.25f);
                    float sz = 0.62f - f * 0.25f;
                    Part(sphere, SP(new(sz, sz * 0.8f, sz * 0.9f), p), k % 2 == 0 ? c : c * 0.85f);
                    Spike(p + new Vector3(0, sz * 0.3f, 0.05f), p + new Vector3(0, sz * 0.7f, 0.22f), 0.12f, new Vector3(0.75f, 0.65f, 0.5f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (p.Y > 0.8f) continue;
                        float ph = walk * 2.5f + k * 1.1f + (s > 0 ? MathF.PI : 0);
                        var foot = new Vector3(s * (0.5f + sz * 0.3f), MathF.Max(0, MathF.Cos(ph)) * 0.1f * stride, p.Z + MathF.Sin(ph) * 0.12f * stride);
                        Limb(p + new Vector3(s * sz * 0.3f, -0.05f, 0), foot, 0.06f, under);
                    }
                }
                var head = new Vector3(0, 1.3f + wind * 0.15f, -0.6f - wind * 0.3f);
                Part(sphere, SP(new(0.5f, 0.42f, 0.55f), head), c * 1.1f);
                float snap = MathF.Sin(t * 10) * 0.08f + wind * 0.25f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Spike(head + new Vector3(s * 0.15f, -0.08f, -0.2f), head + new Vector3(s * (0.06f + snap), -0.25f, -0.62f), 0.12f, new Vector3(0.85f, 0.8f, 0.65f));
                    Part(sphere, SP(new(0.08f), head + new Vector3(s * 0.14f, 0.1f, -0.24f)), new(1f, 0.25f, 0.1f), 1);
                }
                Part(sphere, SP(new(0.07f), head + new Vector3(0, 0.16f, -0.25f)), new(1f, 0.25f, 0.1f), 1);
                break;
            }
            case EnemyDefs.Crawler:
            {
                Part(sphere, SP(new(0.75f, 0.38f * breathe, 0.85f), new(0, 0.5f, 0.15f)), c);
                Part(sphere, SP(new(0.5f, 0.32f, 0.45f), new(0, 0.48f, -0.4f)), c * 1.15f);
                for (int k = 0; k < 4; k++) Part(sphere, SP(new(0.07f), new(-0.12f + k * 0.08f, 0.6f, -0.6f)), new(0.3f, 0.95f, 1f), 1);
                float bite = wind * 0.2f;
                Spike(new(0.1f, 0.4f, -0.55f), new(0.05f + bite, 0.25f, -0.85f), 0.07f, new Vector3(0.8f, 0.8f, 0.75f));
                Spike(new(-0.1f, 0.4f, -0.55f), new(-0.05f - bite, 0.25f, -0.85f), 0.07f, new Vector3(0.8f, 0.8f, 0.75f));
                for (int i = 0; i < 4; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ph = walk * 2.2f + i * 1.7f + (s > 0 ? MathF.PI : 0);
                        float sw = MathF.Sin(ph) * 0.25f * stride, lift = MathF.Max(0, MathF.Cos(ph)) * 0.18f * stride;
                        var hip = new Vector3(s * 0.3f, 0.5f, -0.3f + i * 0.22f);
                        var knee = new Vector3(s * 0.85f, 1.05f + lift, -0.45f + i * 0.3f + sw * 0.5f);
                        var foot = new Vector3(s * 1.15f, lift * 0.4f, -0.6f + i * 0.4f + sw);
                        Limb(hip, knee, 0.06f, c * 0.8f);
                        Limb(knee, foot, 0.045f, c * 0.65f);
                    }
                break;
            }
            case EnemyDefs.Queen:
            {
                float pulse = 1 + MathF.Sin(t * 2.5f) * 0.05f;
                var sac = new Vector3(0.62f, 0.6f, 0.3f);
                Part(sphere, SP(new Vector3(2.0f, 1.6f, 2.6f) * pulse, new(0, 1.15f, 1.35f)), sac);
                for (int k = 0; k < 7; k++)
                    Part(sphere, SP(new(0.34f), new(MathF.Cos(k * 0.9f) * 0.8f, 1.25f + MathF.Sin(k * 2.1f) * 0.45f, 1.4f + MathF.Sin(k * 0.9f) * 0.95f)), new(0.7f, 1f, 0.3f), 0.5f + 0.4f * MathF.Sin(t * 3 + k));
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ph = walk * 1.2f + i * 2.1f + (s > 0 ? MathF.PI : 0);
                        float sw = MathF.Sin(ph) * 0.35f * stride, lift = MathF.Max(0, MathF.Cos(ph)) * 0.3f * stride;
                        var hip = new Vector3(s * 0.6f, 1.1f, -0.4f + i * 0.6f);
                        var knee = new Vector3(s * 1.7f, 1.8f + lift, -0.5f + i * 0.75f + sw * 0.5f);
                        var foot = new Vector3(s * 2.1f, lift * 0.3f, -0.7f + i * 0.85f + sw);
                        Limb(hip, knee, 0.16f, c * 0.8f);
                        Limb(knee, foot, 0.12f, c * 0.65f);
                    }
                Part(sphere, SP(new(1.3f, 1.6f * breathe, 1.1f), new(0, 2.0f, -0.25f)), c);
                var head = new Vector3(0, 3.0f, -0.6f);
                Part(sphere, SP(new(0.7f, 0.6f, 0.9f), head), c * 1.1f);
                for (int k = -2; k <= 2; k++)
                    Spike(head + new Vector3(k * 0.15f, 0.2f, 0.2f), head + new Vector3(k * 0.35f, 0.95f - MathF.Abs(k) * 0.15f, 0.75f), 0.16f, new Vector3(0.8f, 0.75f, 0.55f));
                for (int s = -1; s <= 1; s += 2) Part(sphere, SP(new(0.12f), head + new Vector3(s * 0.2f, 0.06f, -0.42f)), new(1f, 0.55f, 0.1f), 1);
                Part(sphere, SP(new(0.22f, 0.12f + (e.RangedWindup ? wind * 0.12f : 0), 0.1f), head + new Vector3(0, -0.2f, -0.45f)), new(0.6f, 1f, 0.2f), 0.5f + (e.RangedWindup ? wind * 0.5f : 0));
                for (int s = -1; s <= 1; s += 2)
                {
                    var sh = new Vector3(s * 0.65f, 2.5f, -0.45f);
                    float sw = MathF.Sin(t * 1.5f + s) * 0.15f;
                    var el = wind > 0 && !e.RangedWindup ? new Vector3(s * 1.0f, 3.3f, -0.9f) : new Vector3(s * 1.2f, 2.0f + sw, -0.8f);
                    var tip = wind > 0 && !e.RangedWindup ? el + new Vector3(-s * 0.3f, -1.6f * wind, -0.9f) : el + new Vector3(s * 0.1f, 0.9f, -0.7f);
                    Limb(sh, el, 0.22f, c * 0.9f);
                    Spike(el, tip, 0.28f, new Vector3(0.85f, 0.8f, 0.62f));
                }
                break;
            }
            case EnemyDefs.Overseer:
            case EnemyDefs.Mind:
            {
                bool mind = e.Type == EnemyDefs.Mind;
                if (mind)
                {
                    root = Matrix4x4.CreateScale(1.3f) * root;
                    // orbiting crystals and a glowing core
                    for (int k = 0; k < 4; k++)
                    {
                        float a = t * 1.2f + k * MathF.PI / 2;
                        Part(crystal, SRP(new(0.35f, 0.8f, 0.35f), Matrix4x4.CreateRotationY(t * 3), new(MathF.Cos(a) * 1.8f, 1.4f + MathF.Sin(t * 2 + k) * 0.3f, MathF.Sin(a) * 1.8f)), new(0.9f, 0.5f, 1f), 0.85f);
                    }
                    Part(sphere, SP(new(0.9f + MathF.Sin(t * 5) * 0.08f), new(0, 0.85f, 0)), new(1f, 0.45f, 0.9f), 0.9f);
                    if (e.Mode == 4) root = Matrix4x4.CreateScale(MathF.Max(0.05f, e.SpecialT / 0.5f), 1 + (1 - e.SpecialT / 0.5f) * 0.8f, MathF.Max(0.05f, e.SpecialT / 0.5f)) * root;
                }
                Part(sphere, SP(new(1.9f, 1.6f * breathe, 1.9f), new(0, 1.3f, 0)), c);
                for (int k = 0; k < 6; k++) Part(sphere, SP(new(0.7f, 0.5f, 1.2f), new(MathF.Cos(k) * 0.55f, 1.75f, MathF.Sin(k) * 0.55f)), c * 1.1f);
                Part(sphere, SP(new(0.85f), new(0, 1.2f, -0.65f)), new(0.95f, 0.95f, 0.9f));
                var look = Vector3.Zero;
                Part(sphere, SP(new(0.45f + wind * 0.1f), new(0, 1.2f, -1.0f)), new(1f, 0.2f, 0.1f), 1);
                Part(sphere, SP(new(0.2f), new(0, 1.2f, -1.14f)), new(0.02f, 0.02f, 0.02f));
                for (int k = 0; k < 8; k++)
                {
                    float a = k * MathF.Tau / 8;
                    var p0 = new Vector3(MathF.Cos(a) * 0.6f, 0.6f, MathF.Sin(a) * 0.6f);
                    for (int j = 0; j < 4; j++)
                    {
                        var p1 = p0 + new Vector3(MathF.Sin(t * 1.5f + k + j) * 0.2f, -0.35f, MathF.Cos(t * 1.3f + k * 2 + j) * 0.2f);
                        Limb(p0, p1, 0.12f - j * 0.025f, c * 0.75f);
                        p0 = p1;
                    }
                }
                break;
            }
            case EnemyDefs.Pylon:
            {
                white.Bind();
                root = Matrix4x4.CreateTranslation(pos);
                Part(cyl, SP(new(1.4f, 0.4f, 1.4f), Vector3.Zero), new(0.2f, 0.2f, 0.24f));
                Part(cyl, SP(new(0.9f, 0.3f, 0.9f), new(0, 0.4f, 0)), new(0.3f, 0.25f, 0.35f));
                float hp = e.Hp / e.MaxHp;
                var cr = Vector3.Lerp(new Vector3(1f, 0.2f, 0.2f), D.Color, hp);
                float bob = MathF.Sin(t * 2) * 0.15f;
                Part(crystal, SRP(new(0.9f, 2.4f, 0.9f), Matrix4x4.CreateRotationY(t), new(0, 2.1f + bob, 0)), cr, 0.85f);
                Part(torus, SRP(new(0.75f), Matrix4x4.CreateRotationX(MathF.PI / 2 + MathF.Sin(t) * 0.4f) * Matrix4x4.CreateRotationY(t * 1.5f), new(0, 2.1f + bob, 0)), cr, 0.7f);
                Part(torus, SRP(new(0.6f), Matrix4x4.CreateRotationX(MathF.PI / 2 + MathF.Cos(t * 1.3f) * 0.6f) * Matrix4x4.CreateRotationY(-t * 2f), new(0, 2.1f + bob, 0)), cr, 0.7f);
                skin.Bind();
                break;
            }
            case EnemyDefs.Portal:
            {
                white.Bind();
                root = Matrix4x4.CreateTranslation(pos);
                float ry = e.Yaw;
                Part(cyl, SP(new(5.5f, 0.3f, 5.5f), Vector3.Zero), new(0.15f, 0.12f, 0.18f));
                Part(cyl, SP(new(4.2f, 0.2f, 4.2f), new(0, 0.3f, 0)), new(0.3f, 0.15f, 0.4f), 0.3f);
                var ringRot = Matrix4x4.CreateRotationY(ry);
                Part(torus, SRP(new(2.1f, 2.1f, 2.6f), ringRot, new(0, 2.55f, 0)), new(0.25f, 0.2f, 0.3f));
                for (int k = 0; k < 12; k++)
                {
                    float a = k * MathF.Tau / 12;
                    var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
                    var p0 = Vector3.Transform(dir * 2.2f, ringRot) + new Vector3(0, 2.55f, 0);
                    var p1 = Vector3.Transform(dir * 2.9f, ringRot) + new Vector3(0, 2.55f, 0);
                    if (p1.Y < 0.3f || p1.Y > 4.9f) p1 = Vector3.Lerp(p0, p1, 0.4f);
                    Spike(p0, p1, 0.25f, new Vector3(0.5f, 0.3f, 0.6f), 0.2f);
                }
                skin.Bind();
                break;
            }
        }
    }

    void DrawPortalFx(World w)
    {
        var portal = w.PortalE;
        if (portal == null) return;
        float shrink = w.VictoryT >= 0 ? MathF.Max(0, 1 - w.VictoryT / 3f) : 1;
        if (shrink <= 0) return;
        lit.Use();
        portalTex.Bind();
        var center = portal.Pos + new Vector3(0, 2.55f, 0);
        var ringRot = Matrix4x4.CreateRotationY(portal.Yaw);
        float hpK = portal.Hp / portal.MaxHp;
        for (int layer = 0; layer < 3; layer++)
        {
            float rot = time * (layer % 2 == 0 ? 0.8f : -1.3f) * (1 + layer * 0.3f);
            float sc = (4.3f - layer * 0.6f) * shrink;
            lit.Set("uModel", Matrix4x4.CreateScale(sc) * Matrix4x4.CreateRotationZ(rot) * ringRot * Matrix4x4.CreateTranslation(center));
            lit.Set("uTint", new Vector4(Vector3.Lerp(new Vector3(1f, 0.4f, 0.3f), Vector3.One, hpK), 0.8f));
            lit.Set("uGlow", 1f);
            quad.Draw();
        }
        white.Bind();
        if (portal.Invulnerable && !portal.Dead)
        {
            float pulse = 0.12f + 0.05f * MathF.Sin(time * 3);
            lit.Set("uModel", Matrix4x4.CreateScale(6.2f) * Matrix4x4.CreateTranslation(portal.Pos + new Vector3(0, 2.4f, 0)));
            lit.Set("uTint", new Vector4(0.6f, 0.3f, 1f, pulse));
            lit.Set("uGlow", 1f);
            sphere.Draw();
            // pylon beams
            part.Use();
            part.Set("uView", view); part.Set("uProj", proj); part.Set("uTex", 0); part.Set("uFogDensity", w.L.FogDensity);
            foreach (var e in w.Enemies)
                if (e.Type == EnemyDefs.Pylon && !e.Dead)
                {
                    var a = e.Pos + new Vector3(0, 2.2f, 0);
                    BeamQuad(a, center, 0.35f + MathF.Sin(time * 20 + e.Seed) * 0.1f, new Vector4(0.8f, 0.4f, 1f, 0.6f));
                    BeamQuad(a, center, 0.1f, new Vector4(1f, 0.9f, 1f, 0.9f));
                }
            Flush(particleTex);
        }
    }

    // =====================================================================  billboards
    void Billboard(Vector3 p, float size, Vector4 c)
    {
        var r = camRight * size * 0.5f;
        var u = camUp * size * 0.5f;
        uint b = (uint)pv.Count;
        pv.Add(new Vertex(p - r - u, Vector3.Zero, new(0, 0), c));
        pv.Add(new Vertex(p + r - u, Vector3.Zero, new(1, 0), c));
        pv.Add(new Vertex(p + r + u, Vector3.Zero, new(1, 1), c));
        pv.Add(new Vertex(p - r + u, Vector3.Zero, new(0, 1), c));
        pi.Add(b); pi.Add(b + 1); pi.Add(b + 2); pi.Add(b); pi.Add(b + 2); pi.Add(b + 3);
    }

    void BeamQuad(Vector3 a, Vector3 b, float width, Vector4 c)
    {
        var dir = b - a;
        if (dir.LengthSquared() < 1e-6f) return;
        var toCam = cam - (a + b) * 0.5f;
        var side = Vector3.Cross(dir, toCam);
        if (side.LengthSquared() < 1e-8f) return;
        side = Vector3.Normalize(side) * width * 0.5f;
        uint i = (uint)pv.Count;
        pv.Add(new Vertex(a - side, Vector3.Zero, new(0, 0.5f), c));
        pv.Add(new Vertex(a + side, Vector3.Zero, new(1, 0.5f), c));
        pv.Add(new Vertex(b + side, Vector3.Zero, new(1, 0.5f), c));
        pv.Add(new Vertex(b - side, Vector3.Zero, new(0, 0.5f), c));
        pi.Add(i); pi.Add(i + 1); pi.Add(i + 2); pi.Add(i); pi.Add(i + 2); pi.Add(i + 3);
    }

    /// <summary>Soft additive light cone: bright at the apex, fading to nothing at the rim.</summary>
    void BeamCone(Vector3 apex, Vector3 dir, float len, float radius, Vector4 c)
    {
        var t1 = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var t2 = Vector3.Cross(dir, t1);
        var end = apex + dir * len;
        const int seg = 16;
        var mid = new Vector2(0.5f, 0.5f);
        uint a0 = (uint)pv.Count;
        pv.Add(new Vertex(apex, Vector3.Zero, mid, c));
        for (int i = 0; i < seg; i++)
        {
            float ang = i * MathF.Tau / seg;
            pv.Add(new Vertex(end + (t1 * MathF.Cos(ang) + t2 * MathF.Sin(ang)) * radius, Vector3.Zero, mid, new Vector4(c.X, c.Y, c.Z, 0)));
        }
        for (int i = 0; i < seg; i++)
        {
            pi.Add(a0); pi.Add(a0 + 1 + (uint)i); pi.Add(a0 + 1 + (uint)((i + 1) % seg));
        }
    }

    /// <summary>Procedural decal sprites (white, tinted per decal): 0 bullet hole, 1 scorch, 2 blood splat.</summary>
    static byte[] DecalPixels(int kind)
    {
        const int N = 64;
        var px = new byte[N * N * 4];
        var r = new Random(77 + kind);
        var drops = Enumerable.Range(0, 9).Select(_ => (x: (float)r.NextDouble() * 0.8f - 0.4f, y: (float)r.NextDouble() * 0.8f - 0.4f, s: 0.03f + (float)r.NextDouble() * 0.07f)).ToArray();
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float d = MathF.Sqrt(u * u + v * v);
                float n = TexGen.Hash(x, y, 31 + kind) * 0.5f + TexGen.Hash(x / 4, y / 4, 41 + kind) * 0.5f;
                float a, lum;
                switch (kind)
                {
                    case 0: // dark crater with a scuffed ring
                        a = d < 0.12f ? 1 : MathF.Max(0, 1 - (d - 0.12f) / 0.2f) * (0.4f + 0.6f * n);
                        lum = d < 0.08f ? 0.02f : 0.12f + n * 0.1f;
                        break;
                    case 1: // soot
                        float ang = MathF.Atan2(v, u);
                        float edge = 0.32f + 0.1f * MathF.Sin(ang * 5 + n) + 0.05f * n;
                        a = Math.Clamp((edge - d) / 0.18f, 0, 1) * (0.55f + 0.45f * n);
                        lum = 0.03f + n * 0.05f;
                        break;
                    default: // splat: blob plus droplets
                        float ang2 = MathF.Atan2(v, u);
                        float blob = 0.2f + 0.06f * MathF.Sin(ang2 * 7) + 0.04f * MathF.Sin(ang2 * 13 + 1);
                        a = Math.Clamp((blob - d) / 0.04f, 0, 1);
                        foreach (var dr in drops) a = MathF.Max(a, Math.Clamp((dr.s - MathF.Sqrt((u - dr.x) * (u - dr.x) + (v - dr.y) * (v - dr.y))) / 0.015f, 0, 1));
                        lum = 0.75f + n * 0.25f;
                        a *= 0.85f + 0.15f * n;
                        break;
                }
                int i = (y * N + x) * 4;
                byte l = (byte)(Math.Clamp(lum, 0, 1) * 255);
                px[i] = px[i + 1] = px[i + 2] = l;
                px[i + 3] = (byte)(Math.Clamp(a, 0, 1) * 255);
            }
        return px;
    }

    void DrawDecals(World w)
    {
        if (w.Decals.Count == 0) return;
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(-1, -2);
        int bound = -1;
        foreach (var d in w.Decals)
        {
            if (Vector3.DistanceSquared(d.Pos, cam) > 45 * 45) continue;
            if (d.Kind != bound) { decalTex[d.Kind].Bind(); bound = d.Kind; }
            var n = d.N;
            var t0 = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var b0 = Vector3.Cross(n, t0);
            var t1 = t0 * MathF.Cos(d.Rot) + b0 * MathF.Sin(d.Rot);
            var b1 = Vector3.Cross(n, t1);
            var m = new Matrix4x4(t1.X * d.Size, t1.Y * d.Size, t1.Z * d.Size, 0, b1.X * d.Size, b1.Y * d.Size, b1.Z * d.Size, 0, n.X, n.Y, n.Z, 0, d.Pos.X, d.Pos.Y, d.Pos.Z, 1);
            lit.Set("uModel", m);
            float fade = MathF.Min(1, d.Life / 4f);
            Tint(new Vector4(d.Color.X, d.Color.Y, d.Color.Z, d.Color.W * fade), 0);
            quad.Draw();
        }
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
    }

    void DrawChunks(World w)
    {
        if (w.Chunks.Count == 0) return;
        skin.Bind();
        foreach (var c in w.Chunks)
        {
            if (Vector3.DistanceSquared(c.Pos, cam) > 60 * 60) continue;
            float sink = c.Life < 2 ? (2 - c.Life) * 0.15f : 0;
            var rot = Matrix4x4.CreateFromAxisAngle(c.Axis, c.Angle);
            if (c.Shape == 0) lit.Set("uModel", Matrix4x4.CreateScale(c.Scale) * rot * Matrix4x4.CreateTranslation(c.Pos - Vector3.UnitY * sink));
            else lit.Set("uModel", Matrix4x4.CreateTranslation(0, -0.5f, 0) * Matrix4x4.CreateScale(c.Scale) * rot * Matrix4x4.CreateTranslation(c.Pos - Vector3.UnitY * sink));
            Tint(new Vector4(c.Color, 1), 0);
            (c.Shape == 0 ? sphere : cyl).Draw();
        }
    }

    static Matrix4x4 WallFrame(Vector3 pos, Vector3 n)
    {
        var right = new Vector3(n.Z, 0, -n.X);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, 0, 1, 0, 0, n.X, n.Y, n.Z, 0, pos.X, pos.Y, pos.Z, 1);
    }

    /// <summary>Wall vents: frame, dark duct and slats (or a wrecked opening). Something sometimes stares out.</summary>
    void DrawVents(World w)
    {
        metal.Bind();
        foreach (var v in w.L.Vents)
        {
            if (Vector3.DistanceSquared(v.Pos, cam) > 60 * 60) continue;
            var f = WallFrame(v.Pos, v.N);
            void Box(Vector3 at, Vector3 size, Vector3 col, float glow = 0)
            {
                lit.Set("uModel", Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * f);
                Tint(new Vector4(col, 1), glow);
                cube.Draw();
            }
            Box(new(0, 0, 0.0f), new(1.0f, 0.64f, 0.06f), new(0.3f, 0.3f, 0.32f));
            Box(new(0, 0, 0.032f), new(0.84f, 0.48f, 0.01f), new(0.02f, 0.02f, 0.02f));
            if (!v.Broken)
            {
                for (int k = 0; k < 4; k++) Box(new(0, -0.18f + k * 0.12f, 0.05f), new(0.86f, 0.05f, 0.025f), new(0.38f, 0.38f, 0.4f));
                if (v.Spawns > 0 && MathF.Sin(time * 0.6f + v.X * 1.7f + v.Y) > 0.93f)
                    for (int s = -1; s <= 1; s += 2) Box(new(s * 0.08f, 0.02f, 0.036f), new(0.05f, 0.03f, 0.005f), new(1f, 0.2f, 0.1f), 1);
            }
            else
            {
                lit.Set("uModel", Matrix4x4.CreateScale(0.86f, 0.05f, 0.025f) * Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(0.15f, -0.25f, 0.1f) * f);
                Tint(new Vector4(0.25f, 0.25f, 0.27f, 1), 0);
                cube.Draw();
            }
        }
    }

    /// <summary>Red energy walls across the entrances of a room in lockdown.</summary>
    void DrawBarriers(World w)
    {
        var ld = w.ActiveLockdown;
        if (ld == null) return;
        white.Bind();
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        lit.Set("uHot", 1f);
        foreach (var (x, y, dx, dy) in ld.Barriers)
        {
            var n = new Vector3(dx, 0, dy);
            var c = Level.Center(x, y, Level.WH / 2) + n * (Level.CS / 2 - 0.05f);
            for (int layer = 0; layer < 2; layer++)
            {
                float flick = 0.22f + 0.08f * MathF.Sin(time * 23 + x * 3 + layer) + (layer == 1 ? 0.1f * MathF.Sin(time * 4) : 0);
                lit.Set("uModel", Matrix4x4.CreateScale(Level.CS, Level.WH, 1) * WallFrame(c + n * layer * 0.06f, n));
                lit.Set("uTint", new Vector4(1f, 0.18f, 0.1f, flick));
                quad.Draw();
            }
        }
        lit.Set("uHot", 0f);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    /// <summary>Explosive barrels are drawn individually (they get destroyed). A lit fuse makes them throb.</summary>
    void DrawBarrels(World w, bool shadowPass)
    {
        var sh = shadowPass ? shadow : lit;
        if (!shadowPass) metal.Bind();
        foreach (var b in w.Barrels)
        {
            if (b.Dead || Vector3.DistanceSquared(b.Pos, cam) > 80 * 80) continue;
            sh.Set("uModel", Matrix4x4.CreateRotationY(b.Yaw) * Matrix4x4.CreateTranslation(b.Pos));
            if (!shadowPass)
            {
                float hot = b.Fuse >= 0 ? 0.6f + 0.4f * MathF.Sin(time * 60) : b.Hp < Prop.ExplosiveHp ? 0.15f + 0.1f * MathF.Sin(time * 12) : 0;
                Tint(new Vector4(1f + hot * 0.6f, 1f + hot * 0.3f, 1f, 1), hot);
            }
            barrelMesh.Draw();
        }
    }

    /// <summary>Opaque beacon internals: bulb and the spinning reflector.</summary>
    void DrawBeacons()
    {
        white.Bind();
        foreach (var (bp, yaw, bc) in beacons)
        {
            var col = Vector3.Normalize(bc);
            var c = bp + new Vector3(0, -0.12f, 0);
            lit.Set("uModel", Matrix4x4.CreateScale(0.16f) * Matrix4x4.CreateTranslation(c));
            Tint(new Vector4(col * 0.5f + new Vector3(0.5f), 1), 1);
            sphere.Draw();
            // mirror plate behind the bulb, turning with the beams
            lit.Set("uModel", Matrix4x4.CreateScale(0.04f, 0.32f, 0.4f) * Matrix4x4.CreateRotationY(-yaw) * Matrix4x4.CreateTranslation(c));
            Tint(new Vector4(0.8f, 0.8f, 0.85f, 1), 0.3f);
            cube.Draw();
        }
    }

    void Flush(Texture tex)
    {
        if (pi.Count == 0) return;
        tex.Bind();
        dyn.Upload(pv, pi, true);
        dyn.Draw();
        pv.Clear();
        pi.Clear();
    }

    // =====================================================================  view model
    void DrawViewModel(World w, float aspect)
    {
        var P = w.P;
        gl.DepthRange(0, 0.01);   // always in front of the world, without clearing the depth the post pass needs
        gl.Disable(EnableCap.Blend);
        var vproj = GMath.Perspective(62 * MathF.PI / 180, aspect, 0.01f, 20f);
        lit.Use();
        lit.Set("uProj", vproj);
        lit.Set("uFogDensity", 0f);
        lit.Set("uFlashOn", 0f);
        lit.Set("uShadowCount", 0);
        lit.Set("uAmbient", builtFor.Ambient + new Vector3(0.12f));
        Matrix4x4.Invert(view, out var camWorld);
        int id = P.Weapon;
        var basePos = id switch
        {
            Weapons.Knife => new Vector3(0.24f, -0.25f, -0.45f),
            Weapons.Pistol => new Vector3(0.17f, -0.17f, -0.42f),
            Weapons.RocketLauncher => new Vector3(0.24f, -0.27f, -0.36f),
            Weapons.VoidCannon => new Vector3(0.22f, -0.24f, -0.46f),
            _ => new Vector3(0.19f, -0.19f, -0.4f)
        };
        float amt = P.BobAmount;
        var bob = new Vector3(MathF.Sin(P.Bob) * 0.014f * amt, -MathF.Abs(MathF.Cos(P.Bob)) * 0.012f * amt, 0);
        var idle = new Vector3(MathF.Sin(time * 1.1f) * 0.003f, MathF.Sin(time * 1.6f) * 0.003f, 0);
        var off = basePos + bob + idle + new Vector3(0, -P.SwitchT * 0.4f, P.Recoil * 0.3f);
        var rot = Matrix4x4.CreateRotationY(0.05f) * Matrix4x4.CreateRotationX(P.Recoil * 0.9f - P.SwitchT * 0.7f);
        if (id == Weapons.Knife)
        {
            float k = MathF.Sin(P.KnifeT * MathF.PI);
            off += new Vector3(-k * 0.15f, k * 0.05f, -k * 0.3f);
            rot = Matrix4x4.CreateRotationZ(k * 0.9f) * Matrix4x4.CreateRotationX(-0.4f - k * 0.6f) * rot;
        }
        var model = Matrix4x4.CreateScale(0.72f) * rot * Matrix4x4.CreateTranslation(off + new Vector3(0, 0.035f, 0)) * camWorld;
        metal.Bind();
        lit.Set("uModel", model);
        var w8 = Weapons.All[id];
        float pulse = w8.Alien ? 0.5f + 0.5f * MathF.Sin(time * 4) : 0;
        bool amp = P.AmpT > 0;
        lit.Set("uTint", amp ? new Vector4(1f, 0.75f, 0.55f, 1) : P.Upgraded[id] ? new Vector4(1.08f, 0.98f, 0.82f, 1) : Vector4.One);
        lit.Set("uGlow", amp ? 0.12f + 0.08f * MathF.Sin(time * 8) : 0f);
        weaponView[id].Draw();

        if (id != Weapons.Knife)
        {
            lastMuzzle = Vector3.Transform(muzzle[id], model);
            lastVProj = vproj;
            muzzleValid = true;
        }
        if (P.Heat > 0.2f && id != Weapons.Knife)
        {
            // overheated barrel: an orange glow at the tip that fades as it cools
            float hk = (P.Heat - 0.2f) / 0.8f;
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            gl.DepthMask(false);
            part.Use();
            part.Set("uView", view);
            part.Set("uProj", vproj);
            part.Set("uFogDensity", 0f);
            Billboard(lastMuzzle, 0.07f + 0.06f * hk, new Vector4(1f, 0.35f, 0.08f, hk * 0.9f));
            Billboard(lastMuzzle, 0.035f + 0.03f * hk, new Vector4(1f, 0.75f, 0.4f, hk * 0.7f));
            Flush(particleTex);
            gl.DepthMask(true);
            lit.Use();
        }

        if (P.FireFlash > 0 && id != Weapons.Knife)
        {
            var mz = Vector3.Transform(muzzle[id], model);
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            gl.DepthMask(false);
            part.Use();
            part.Set("uView", view);
            part.Set("uProj", vproj);
            part.Set("uFogDensity", 0f);
            float s = id == Weapons.Shotgun || id == Weapons.RocketLauncher ? 0.5f : 0.32f;
            Billboard(mz, s * (0.8f + (float)Random.Shared.NextDouble() * 0.4f), new Vector4(w8.Flash, 1));
            Billboard(mz, s * 0.45f, new Vector4(1, 1, 0.9f, 1));
            Flush(particleTex);
            gl.DepthMask(true);
        }
        _ = pulse;
    }
}
