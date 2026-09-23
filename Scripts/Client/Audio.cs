using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

static class AudioFiles
{
    static readonly Dictionary<string, AudioStream> Cache = new();

    public static AudioStream Load(string baseName)
    {
        if (Cache.TryGetValue(baseName, out var s)) return s;
        foreach (var ext in new[] { ".ogg", ".mp3", ".wav" })
        {
            string p = $"res://assets/audio/{baseName}{ext}";
            if (ResourceLoader.Exists(p)) { s = ResourceLoader.Load<AudioStream>(p); break; }
        }
        Cache[baseName] = s;
        return s;
    }
}

/// Background music with crossfades. Tracks: assets/audio/music_<name>.(ogg|mp3|wav)
public partial class Music : Node
{
    public static Music I { get; private set; }
    AudioStreamPlayer a, b;
    string current;

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        a = new AudioStreamPlayer { VolumeDb = -80 };
        b = new AudioStreamPlayer { VolumeDb = -80 };
        AddChild(a); AddChild(b);
        a.Finished += () => a.Play();
        b.Finished += () => b.Play();
    }

    float TargetDb => Settings.MusicVolume <= 0.001f ? -80 : Mathf.LinearToDb(Settings.MusicVolume);

    public void Play(string name)
    {
        if (name == current) return;
        var stream = AudioFiles.Load("music_" + name) ?? AudioFiles.Load("music_overworld");
        current = name;
        if (stream == null && !a.Playing && !b.Playing) return;
        (a, b) = (b, a);
        var tw = CreateTween().SetParallel();
        if (b.Playing) tw.TweenProperty(b, "volume_db", -80f, 2.0f);
        else tw.TweenInterval(0.01);
        if (stream == null) return;
        a.Stream = stream;
        a.VolumeDb = -60;
        a.Play();
        tw.TweenProperty(a, "volume_db", TargetDb, 2.0f);
        tw.Chain().TweenCallback(Callable.From(() => b.Stop()));
    }

    public void ApplyVolume()
    {
        if (a.Playing) a.VolumeDb = TargetDb;
    }
}

/// Positional one-shot sound effects. Files: assets/audio/sfx_<name>.(ogg|mp3|wav)
public partial class Sfx : Node
{
    public static Sfx I { get; private set; }
    readonly List<AudioStreamPlayer3D> pool = new();

    public override void _EnterTree() => I = this;

    public void Play(string name, Vector3 pos)
    {
        var stream = AudioFiles.Load("sfx_" + name);
        if (stream == null || Settings.SfxVolume <= 0.001f) return;
        AudioStreamPlayer3D p = null;
        foreach (var x in pool) if (!x.Playing) { p = x; break; }
        if (p == null)
        {
            if (pool.Count >= 24) return;
            p = new AudioStreamPlayer3D { UnitSize = 8, MaxDistance = 45 };
            AddChild(p);
            pool.Add(p);
        }
        p.Stream = stream;
        p.GlobalPosition = pos;
        p.VolumeDb = Mathf.LinearToDb(Settings.SfxVolume);
        p.PitchScale = (float)GD.RandRange(0.92, 1.08);
        p.Play();
    }
}
