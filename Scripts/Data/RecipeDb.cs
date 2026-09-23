using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia;

/// A crafting recipe. Stations: smelt (furnace), smith (anvil + hammer), cook (fire or hearth),
/// spin (spinning wheel), weave (loom), sew (needle, anywhere), enchant (enchanting table or
/// the dark altar), workbench (carving and fletching).
public sealed class Recipe
{
    public string Id;
    public Skill Skill;
    public int Level;
    public float Xp;
    public string Station;
    public ItemCount[] In;
    public string Out;
    public int OutCount = 1;
    public string Unlock;       // unlock key required, null = known to everyone
    public string Group = "";   // heading in the crafting window
    public float FailChance;    // smelting: chance the ore crumbles at the recipe's level
    public int BurnStop;        // cooking: level at which you stop burning it
    public bool Sigil;          // enchanting: more sigils per essence as you level

    [System.Text.Json.Serialization.JsonIgnore] public string Name => ItemDb.Get(Out)?.Name ?? Out;
}

public sealed class UnlockDef
{
    public string Key, Name, Scroll, Hint;
    public int Value;
    public Color Color;
}

/// recipes.json: crafting stations' display names, recipe unlocks and the recipes themselves.
public sealed class RecipeFile
{
    public Dictionary<string, string> Stations = new();
    public UnlockDef[] Unlocks = System.Array.Empty<UnlockDef>();
    public List<Recipe> Recipes = new();
}

public static class RecipeDb
{
    static readonly RecipeFile Data = GameData.Load<RecipeFile>("recipes.json");

    /// Everything in recipes.json (for tools that write it back out).
    public static RecipeFile File => Data;

    public static readonly UnlockDef[] Unlocks = Data.Unlocks;

    public static UnlockDef GetUnlock(string key) => Array.Find(Unlocks, u => u.Key == key);

    public static readonly List<Recipe> All = new();
    static readonly Dictionary<string, Recipe> ById = new();
    public static Recipe Get(string id) => id != null && ById.TryGetValue(id, out var r) ? r : null;
    public static IEnumerable<Recipe> ForStation(string station) => All.Where(r => r.Station == station);

    static RecipeDb()
    {
        foreach (var r in Data.Recipes) { r.Id ??= r.Station + ":" + r.Out; All.Add(r); ById[r.Id] = r; }
    }

    /// Sigils made from one essence stone at this level (doubled at the dark altar).
    public static int SigilYield(Recipe r, int level, bool altar) => (1 + Math.Max(0, level - r.Level) / 12) * (altar ? 2 : 1);

    /// Chance to burn a cooking recipe. Hearths burn less; the Cook's band halves it.
    public static float BurnChance(Recipe r, int level, bool hearth, bool band)
    {
        if (r.BurnStop <= 0 || level >= r.BurnStop) return 0f;
        float t = (r.BurnStop - level) / (float)Math.Max(1, r.BurnStop - r.Level + 4);
        float c = 0.55f * t * (hearth ? 0.7f : 1f);
        return band ? c * 0.5f : c;
    }

    public static float SmeltFail(Recipe r, int level) => Math.Max(0f, r.FailChance - (level - r.Level) * 0.02f);

    public static string StationName(string s) => s != null && Data.Stations.TryGetValue(s, out var n) ? n : s;
}
