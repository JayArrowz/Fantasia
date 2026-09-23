using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia;

public sealed class CropDef
{
    public string Id, Name, Seed, Produce, ProduceName, Examine;
    public int Level, GrowSeconds, SeedValue, ProduceValue;
    public float PlantXp, HarvestXp;
    public Color Color;
}

/// One allotment's state for one player. Crops grow in real time, even while logged out.
public sealed class PatchState
{
    public int Obj;              // world object id of the allotment
    public string Crop;          // crop id, null when empty
    public long Planted;         // unix seconds (shifted forward when a disease is cured)
    public int DiseaseAt = -1;   // growth stage at which disease strikes (-1 never)
    public int WaterStage = -1;  // last growth stage that was watered
    public int Watered;          // number of stages watered (0-3)
    public bool Compost;
    public int Left;             // produce still to pick during a harvest
}

public enum PatchPhase { Empty, Growing, Diseased, Dead, Ready }

public static class FarmDb
{
    public const int Stages = 4;

    /// Loaded from res://data/crops.json.
    public static readonly CropDef[] Crops = GameData.Load<CropDef[]>("crops.json");

    static readonly Dictionary<string, CropDef> ById = new();
    static FarmDb() { foreach (var c in Crops) ById[c.Id] = c; }
    public static CropDef Get(string id) => id != null && ById.TryGetValue(id, out var c) ? c : null;
    public static CropDef BySeed(string seedItem) => Array.Find(Crops, c => c.Seed == seedItem);

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// Current growth stage (0..Stages) and phase at time `now`. Shared by server and client.
    public static (int stage, PatchPhase phase, float progress) Eval(PatchState p, long now)
    {
        var c = Get(p?.Crop);
        if (c == null) return (0, PatchPhase.Empty, 0);
        float stageLen = c.GrowSeconds / (float)Stages;
        float elapsed = now - p.Planted;
        if (p.DiseaseAt > 0)
        {
            float strike = p.DiseaseAt * stageLen;
            if (elapsed >= strike)
            {
                // A sick crop stops growing; left a whole stage it withers.
                if (elapsed >= strike + Math.Max(60f, stageLen)) return (p.DiseaseAt, PatchPhase.Dead, p.DiseaseAt / (float)Stages);
                return (p.DiseaseAt, PatchPhase.Diseased, p.DiseaseAt / (float)Stages);
            }
        }
        int stage = Math.Clamp((int)(elapsed / stageLen), 0, Stages);
        float prog = Math.Clamp(elapsed / c.GrowSeconds, 0f, 1f);
        return (stage, stage >= Stages ? PatchPhase.Ready : PatchPhase.Growing, prog);
    }

    public static string Describe(PatchState p, long now)
    {
        var c = Get(p?.Crop);
        var (stage, phase, prog) = Eval(p, now);
        return phase switch
        {
            PatchPhase.Empty => "The soil is bare and ready for seeds.",
            PatchPhase.Ready => $"Your {c.Name.ToLowerInvariant()} crop is ready to harvest!",
            PatchPhase.Diseased => $"Your {c.Name.ToLowerInvariant()} crop is diseased! Treat it with plant cure before it withers.",
            PatchPhase.Dead => $"Your {c.Name.ToLowerInvariant()} crop has withered. Clear the patch.",
            _ => $"{c.Name} growing: {prog * 100:0}% (stage {stage + 1} of {Stages}){(p.Watered > 0 ? $", watered {p.Watered}x" : "")}{(p.Compost ? ", composted" : "")}.",
        };
    }
}
