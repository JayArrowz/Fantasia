using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia;

public enum IconKind
{
    Coins, Sword, Scimitar, Warhammer, Greatsword, Dagger, Kiteshield, Roundshield, FullHelm, Platebody, Platelegs,
    Shortbow, Longbow, Arrows, Staff, Battlestaff, WizardHat, Robe, RobeBottom, LeatherBody, LeatherChaps, Coif,
    Boots, Gloves, Cape, Amulet, Ring, Rune, Fish, Bread, Bones, Misc,
    Pickaxe, Hatchet, FishingRod, FishingNet, Harpoon, Hammer, Needle, WateringCan, Bait,
    Log, Ore, Bar, Gem, Scroll, Seed, Produce, Thread, Cloth, Nest, Meat, Feather, Essence, Shafts, Arrowheads, Vial,
}

/// Tools used by gathering and crafting skills (held in the pack or wielded).
public enum ToolKind { None, Pickaxe, Hatchet, Rod, Net, Harpoon, Hammer, Needle, WateringCan }

public sealed class ItemDef
{
    public string Id;
    public string Name;
    public string Examine = "";
    public EquipSlot Slot = EquipSlot.None;
    public bool Stackable;
    public int Value = 1;
    public IconKind Icon = IconKind.Misc;
    public Color Tint = Colors.White;
    public string Model;          // GLB key (assets/models/<key>.glb)
    public bool TintModel;        // model is untextured and should use Tint as metal color

    // Equipment bonuses
    public int AtkMelee, AtkRanged, AtkMagic;
    public int DefMelee, DefRanged, DefMagic;
    public int StrMelee, StrRanged, MagicDmg;

    // Weapon data
    public WeaponKind Weapon = WeaponKind.None;
    public int Speed = 4;
    public bool TwoHanded;
    public int Range = 1;

    // Requirements
    public Skill ReqSkill = Skill.Prowess;
    public int ReqLevel = 1;

    // Food
    public int Heal;

    // Runes / elemental staff
    public string ProvidesRune;

    // Skilling
    public ToolKind Tool;
    public int ToolPower;         // tier of a tool: speeds up gathering
    public string Use;            // extra pack verb: Read, Open, Sew
    public string Teaches;        // unlock key learnt by reading this item
    public Skill BoostSkill;      // skilling jewellery: skill it helps
    public int BoostPct;
    public bool ModulateModel;    // multiply a textured model by Tint (ores, bars, gems, cloth)

    [System.Text.Json.Serialization.JsonIgnore] public bool Equippable => Slot != EquipSlot.None;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsFood => Heal > 0;
    [System.Text.Json.Serialization.JsonIgnore] public CombatType AttackType => Weapon switch
    {
        WeaponKind.Shortbow or WeaponKind.Longbow => CombatType.Ranged,
        _ => CombatType.Melee,
    };
}

public static class ItemDb
{
    public static readonly Dictionary<string, ItemDef> All = new();
    public static ItemDef Get(string id) => id != null && All.TryGetValue(id, out var d) ? d : null;

    /// Loaded from res://data/items.json (in file order).
    static ItemDb()
    {
        foreach (var d in GameData.Load<List<ItemDef>>("items.json")) All[d.Id] = d;
    }
}
