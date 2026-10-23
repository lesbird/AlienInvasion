using System.Numerics;
using Silk.NET.OpenAL;

namespace AlienInvasion.Engine;

/// <summary>OpenAL backed sound system. Fails gracefully to silence if no device is available.</summary>
public unsafe class SoundSystem : IDisposable
{
    AL al;
    ALContext alc;
    Device* device;
    Context* context;
    public bool Ok { get; private set; }

    readonly Dictionary<string, List<uint>> buffers = new();
    readonly uint[] sources = new uint[40];
    readonly float[] sourceStart = new float[40];
    uint musicSource, musicBuffer, loopSource;
    string loopName;
    readonly Random rng = new();
    float clock;
    public float SfxVolume = 0.9f, MusicVolume = 0.45f;

    public bool Init()
    {
        try
        {
            alc = ALContext.GetApi(true);
            al = AL.GetApi(true);
            device = alc.OpenDevice("");
            if (device == null) { Console.WriteLine("Audio: no device"); return false; }
            context = alc.CreateContext(device, null);
            alc.MakeContextCurrent(context);
            al.DistanceModel(DistanceModel.InverseDistanceClamped);
            for (int i = 0; i < sources.Length; i++) sources[i] = al.GenSource();
            musicSource = al.GenSource();
            al.SetSourceProperty(musicSource, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(musicSource, SourceBoolean.Looping, true);
            loopSource = al.GenSource();
            al.SetSourceProperty(loopSource, SourceBoolean.Looping, true);
            al.SetSourceProperty(loopSource, SourceFloat.ReferenceDistance, 6f);
            al.SetSourceProperty(loopSource, SourceFloat.RolloffFactor, 1.2f);
            Ok = true;
        }
        catch (Exception e)
        {
            Console.WriteLine("Audio disabled: " + e.Message);
            Ok = false;
        }
        return Ok;
    }

    public void Update(float dt) => clock += dt;

    /// <summary>Registers a variant for a sound name (random variant chosen at play time).</summary>
    public void Load(string name, float[] samples)
    {
        if (!Ok) return;
        uint b = al.GenBuffer();
        var pcm = Synth.ToPcm(samples);
        fixed (short* p = pcm)
            al.BufferData(b, BufferFormat.Mono16, p, pcm.Length * sizeof(short), Synth.Rate);
        if (!buffers.TryGetValue(name, out var list)) buffers[name] = list = new List<uint>();
        list.Add(b);
    }

    uint FreeSource()
    {
        int oldest = 0;
        for (int i = 0; i < sources.Length; i++)
        {
            al.GetSourceProperty(sources[i], GetSourceInteger.SourceState, out int st);
            if ((SourceState)st != SourceState.Playing) { sourceStart[i] = clock; return sources[i]; }
            if (sourceStart[i] < sourceStart[oldest]) oldest = i;
        }
        al.SourceStop(sources[oldest]);
        sourceStart[oldest] = clock;
        return sources[oldest];
    }

    /// <summary>Play a sound. pos == null plays it relative to the listener (2D).</summary>
    public void Play(string name, Vector3? pos = null, float gain = 1, float pitch = 1, float pitchJitter = 0.06f)
    {
        if (!Ok || !buffers.TryGetValue(name, out var list)) return;
        uint src = FreeSource();
        uint buf = list[rng.Next(list.Count)];
        al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        al.SetSourceProperty(src, SourceFloat.Gain, gain * SfxVolume);
        al.SetSourceProperty(src, SourceFloat.Pitch, pitch * (1 + ((float)rng.NextDouble() * 2 - 1) * pitchJitter));
        if (pos.HasValue)
        {
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, false);
            al.SetSourceProperty(src, SourceVector3.Position, pos.Value.X, pos.Value.Y, pos.Value.Z);
            al.SetSourceProperty(src, SourceFloat.ReferenceDistance, 4f);
            al.SetSourceProperty(src, SourceFloat.RolloffFactor, 1.1f);
            al.SetSourceProperty(src, SourceFloat.MaxDistance, 80f);
        }
        else
        {
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);
        }
        al.SourcePlay(src);
    }

    public void SetListener(Vector3 pos, Vector3 fwd, Vector3 up)
    {
        if (!Ok) return;
        al.SetListenerProperty(ListenerVector3.Position, pos.X, pos.Y, pos.Z);
        float* ori = stackalloc float[6] { fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z };
        al.SetListenerProperty(ListenerFloatArray.Orientation, ori);
    }

    public void PlayMusic(float[] samples)
    {
        if (!Ok) return;
        al.SourceStop(musicSource);
        al.SetSourceProperty(musicSource, SourceInteger.Buffer, 0);
        if (musicBuffer != 0) al.DeleteBuffer(musicBuffer);
        musicBuffer = al.GenBuffer();
        var pcm = Synth.ToPcm(samples);
        fixed (short* p = pcm)
            al.BufferData(musicBuffer, BufferFormat.Mono16, p, pcm.Length * sizeof(short), Synth.Rate);
        al.SetSourceProperty(musicSource, SourceInteger.Buffer, (int)musicBuffer);
        al.SetSourceProperty(musicSource, SourceFloat.Gain, MusicVolume);
        al.SourcePlay(musicSource);
    }

    public void SetMusicGain(float g)
    {
        if (Ok) al.SetSourceProperty(musicSource, SourceFloat.Gain, MusicVolume * g);
    }

    public void StopMusic()
    {
        if (Ok) al.SourceStop(musicSource);
    }

    /// <summary>One positional looping ambience (e.g., the portal hum).</summary>
    public void SetLoop(string name, Vector3 pos, float gain)
    {
        if (!Ok) return;
        if (name == null)
        {
            if (loopName != null) al.SourceStop(loopSource);
            loopName = null;
            return;
        }
        if (loopName != name && buffers.TryGetValue(name, out var list))
        {
            al.SourceStop(loopSource);
            al.SetSourceProperty(loopSource, SourceInteger.Buffer, (int)list[0]);
            al.SourcePlay(loopSource);
            loopName = name;
        }
        al.SetSourceProperty(loopSource, SourceVector3.Position, pos.X, pos.Y, pos.Z);
        al.SetSourceProperty(loopSource, SourceFloat.Gain, gain * SfxVolume);
    }

    public void Dispose()
    {
        if (!Ok) return;
        foreach (var s in sources) al.DeleteSource(s);
        al.DeleteSource(musicSource);
        al.DeleteSource(loopSource);
        foreach (var l in buffers.Values) foreach (var b in l) al.DeleteBuffer(b);
        alc.MakeContextCurrent(null);
        alc.DestroyContext(context);
        alc.CloseDevice(device);
        Ok = false;
    }
}

/// <summary>Builds the whole sound bank procedurally.</summary>
public static class SoundBank
{
    public static List<(string name, float[] data)> BuildAll()
    {
        var list = new List<(string, float[])>();
        void Add(string n, float[] d) { lock (list) list.Add((n, d)); }
        var jobs = new List<Action>
        {
            () => Add("knife", Synth.Knife(1)),
            () => Add("pistol", Synth.Gunshot(0.35f, 140, 5000, 14, 0.8f, 2)),
            () => Add("shotgun", Synth.Gunshot(0.8f, 90, 3500, 6, 1.2f, 3)),
            () => Add("rifle", Synth.Gunshot(0.22f, 160, 6000, 22, 0.6f, 4)),
            () => Add("rifle", Synth.Gunshot(0.22f, 170, 6500, 22, 0.6f, 5)),
            () => Add("gl", Synth.Launch(6, false)),
            () => Add("rocket", Synth.Launch(7, true)),
            () => Add("plasma", Synth.Plasma(8)),
            () => Add("acidfire", Synth.Acid(9)),
            () => Add("zap", Synth.Zap(10)),
            () => Add("zap", Synth.Zap(11)),
            () => Add("void", Synth.VoidCannon(12)),
            () => Add("sniper", Synth.Gunshot(1.1f, 60, 7500, 4, 1.4f, 16)),
            () => Add("rail", Synth.Railgun(17)),
            () => Add("explode", Synth.Explosion(13)),
            () => Add("explode", Synth.Explosion(14, 1.4f)),
            () => Add("clink", Synth.Clink(15)),
            () => Add("empty", Synth.Click(3000, 0.05f)),
            () => Add("switch", Synth.WeaponSwitch()),
            () => Add("ricochet", Synth.Click(4200, 0.08f)),
            () => Add("hitmark", Synth.Click(5200, 0.025f)),
            () => Add("steam", Synth.Steam(60)),
            () => Add("alarm", Synth.Klaxon()),
            () => Add("powerdown", Synth.PowerSweep(true)),
            () => Add("powerup", Synth.PowerSweep(false)),
            () => Add("killmark", Synth.Notes(new[] { 1200f, 900f }, 0.035f, 40, 0.1f, 0.4f)),
            // creatures
            () => Add("growl_small", Synth.Growl(20, 160, 0.6f, 1.2f, 0.6f, 9)),
            () => Add("growl_small", Synth.Growl(21, 180, 0.5f, 0.9f, 0.6f, 11)),
            () => Add("growl", Synth.Growl(22, 85, 0.9f, 0.8f, 0.5f, 6)),
            () => Add("growl", Synth.Growl(23, 95, 0.8f, 1.1f, 0.5f, 5)),
            () => Add("growl_big", Synth.Growl(24, 45, 1.3f, 0.7f, 0.7f, 3)),
            () => Add("screech", Synth.Screech(25, 900, 0.6f)),
            () => Add("screech", Synth.Screech(26, 1100, 0.5f)),
            () => Add("wail", Synth.Screech(27, 420, 1.0f)),
            () => Add("adeath", Synth.Growl(28, 120, 1.0f, 0.35f, 0.6f, 7)),
            () => Add("adeath", Synth.Growl(29, 100, 1.1f, 0.3f, 0.7f, 5)),
            () => Add("adeath_big", Synth.Growl(30, 50, 1.8f, 0.3f, 0.8f, 3)),
            () => Add("ahurt", Synth.Growl(31, 150, 0.3f, 1.3f, 0.5f, 12)),
            () => Add("ahurt", Synth.Growl(32, 170, 0.25f, 1.4f, 0.5f, 14)),
            () => Add("spit", Synth.Acid(33)),
            () => Add("eplasma", Synth.Plasma(34, 900, 0.3f)),
            () => Add("orb", Synth.Plasma(35, 500, 0.6f)),
            () => Add("swipe", Synth.Knife(36)),
            () => Add("splat", Synth.Splat(37)),
            () => Add("splat", Synth.Splat(38)),
            // player
            () => Add("hurt", Synth.PlayerGrunt(40, 0.25f, 140, 0.2f)),
            () => Add("hurt", Synth.PlayerGrunt(41, 0.22f, 155, 0.3f)),
            () => Add("pdeath", Synth.PlayerGrunt(42, 1.1f, 150, 0.6f)),
            () => Add("step", Synth.Footstep(43)),
            () => Add("step", Synth.Footstep(44)),
            () => Add("step", Synth.Footstep(45)),
            // pickups & world
            () => Add("pickup", Synth.Notes(new[] { 880f, 1320f }, 0.06f, 18, 0.2f, 0.2f)),
            () => Add("health", Synth.Notes(new[] { 523f, 659f, 784f }, 0.06f, 14, 0.3f)),
            () => Add("weapon", Synth.Notes(new[] { 392f, 523f, 659f, 1046f }, 0.07f, 10, 0.4f, 0.3f)),
            () => Add("key", Synth.Notes(new[] { 784f, 988f, 1175f, 1568f }, 0.11f, 4, 0.5f)),
            () => Add("door", Synth.Door(50)),
            () => Add("locked", Synth.Buzzer()),
            () => Add("elevator", Synth.Elevator(51)),
            () => Add("portal", Synth.PortalHum(52)),
            () => Add("shatter", Synth.Shatter(53)),
            () => Add("bigboom", Synth.Explosion(54, 3.0f)),
            () => Add("menu", Synth.Notes(new[] { 660f }, 0.05f, 30, 0.2f, 0.3f)),
            () => Add("secret", Synth.Notes(new[] { 523f, 784f, 1046f, 1568f, 2093f }, 0.08f, 6, 0.5f)),
        };
        Parallel.ForEach(jobs, j => j());
        return list;
    }
}
