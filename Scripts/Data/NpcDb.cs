using System.Collections.Generic;
using Godot;

namespace Fantasia;

public enum NpcBody { Humanoid, Quadruped, Bird, Spider, Dragon, Winged, Floating }

public sealed class Drop
{
    public string Item;
    public int Min = 1, Max = 1;
    public float Chance = 1f;
    public Drop() { }
    public Drop(string item, float chance, int min = 1, int max = 1) { Item = item; Chance = chance; Min = min; Max = max; }
}

public sealed class NpcDef
{
    public string Id;
    public string Name;
    public string Examine = "";
    public int CombatLevel;
    public int Hitpoints = 10;
    public int Attack = 1, Strength = 1, Defence = 1, Ranged = 1, Magic = 1;
    public int DefMelee, DefRanged, DefMagic;
    public CombatType Style = CombatType.Melee;
    public int MaxHit = 1;
    public int AttackSpeed = 4;
    public int AttackRange = 1;
    public bool Aggressive;
    public int AggroRange = 4;
    public int WanderRadius = 4;
    public int RespawnTicks = 30;
    public bool Attackable = true;
    public string[] Options = { "Attack" };   // for non-combat NPCs e.g. Talk-to, Trade, Bank
    public string ShopId;
    public string[] Dialogue;
    public List<Drop> Drops = new();

    // Visuals
    public NpcBody Body = NpcBody.Humanoid;
    public string Model;
    public float Height = 1.8f;
    public Color Tint = Colors.White;
    public string ProjectileColor = "#ffffff";
    public string WeaponModel;      // for rigged humanoids
    public Color WeaponTint = Colors.White;
    public bool Boss;
}

public static class NpcDb
{
    public static readonly Dictionary<string, NpcDef> All = new();
    public static NpcDef Get(string id) => id != null && All.TryGetValue(id, out var d) ? d : null;
    /// Loaded from res://data/npcs.json.
    static NpcDb()
    {
        foreach (var d in GameData.Load<List<NpcDef>>("npcs.json")) All[d.Id] = d;
    }
}
