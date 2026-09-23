using System.Collections.Generic;
using System.Linq;

namespace Fantasia;

public enum SpellKind { Combat, Teleport, Heal }

public sealed class SpellDef
{
    public string Id;
    public string Name;
    public int Level;
    public int MaxHit;
    public float BaseXp;
    public string Color = "#ffffff";
    public string Description;
    public SpellKind Kind = SpellKind.Combat;
    public string TeleportMap;
    public int TeleportX, TeleportZ;
    public int HealPercent;
    public int CooldownTicks;
    public RuneCost[] Runes;
    [System.Text.Json.Serialization.JsonIgnore] public string Icon => "spell_" + Id;
}

public static class SpellDb
{
    /// Loaded from res://data/spells.json.
    public static readonly List<SpellDef> List = GameData.Load<List<SpellDef>>("spells.json");

    public static SpellDef Get(string id) => List.FirstOrDefault(s => s.Id == id);
}

public sealed class ShopDef
{
    public string Id;
    public string Name;
    public string[] Stock;
}

public static class ShopDb
{
    /// Loaded from res://data/shops.json.
    public static readonly Dictionary<string, ShopDef> All = GameData.Load<List<ShopDef>>("shops.json").ToDictionary(s => s.Id);
}
