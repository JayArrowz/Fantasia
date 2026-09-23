using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Fantasia;

public static class GameConst
{
    public const float TickSeconds = 0.6f;
    public const int DefaultPort = 7777;
    public const int MaxPlayers = 64;
    public const int InventorySize = 28;
    public const int ViewDistance = 30;
    public const int GroundItemTicks = 300;
    public const string OverworldId = "overworld";
}

public enum Skill
{
    Prowess, Might, Fortitude, Archery, Sorcery, Vitality,
    Fishing, Cooking, Woodcutting, Farming, Mining, Smithing, Silkweaving, Enchanting,
}

public static class Skills
{
    public const int Count = 14;
    public static bool IsCombat(Skill s) => (int)s <= (int)Skill.Vitality;
    public static readonly Skill[] All = (Skill[])Enum.GetValues(typeof(Skill));

    public static string Blurb(Skill s) => s switch
    {
        Skill.Prowess => "Accuracy with melee weapons.",
        Skill.Might => "Melee damage.",
        Skill.Fortitude => "Defence against every kind of attack.",
        Skill.Archery => "Accuracy and damage with bows.",
        Skill.Sorcery => "Spellcasting accuracy and access to stronger spells.",
        Skill.Vitality => "Your health.",
        Skill.Fishing => "Net, bait and harpoon fish from rivers and lakes. Watch for frenzied shoals!",
        Skill.Cooking => "Cook raw food on fires and hearths; combine ingredients into hearty meals.",
        Skill.Woodcutting => "Fell trees for logs, then carve bows, staves and arrow shafts at a workbench.",
        Skill.Farming => "Plant seeds in allotments; water, protect and harvest crops that grow even while you're away.",
        Skill.Mining => "Mine ore, coal, gems and essence from rocks across the land and below it.",
        Skill.Smithing => "Smelt ore into bars at a furnace and forge weapons, armour and tools at an anvil.",
        Skill.Silkweaving => "Gather cocoons and spider silk, spin thread, weave cloth and sew enchanted robes.",
        Skill.Enchanting => "Craft sigils from essence, make jewellery and imbue it with magic.",
        _ => "",
    };
}

public enum EquipSlot { None = -1, Head, Cape, Neck, Ammo, Weapon, Body, Shield, Legs, Hands, Feet, Ring }

public enum CombatType { Melee, Ranged, Magic }

public enum WeaponKind { None, Unarmed, Sword, Scimitar, Warhammer, Greatsword, Shortbow, Longbow, Staff, Dagger, Spear }

public static class Xp
{
    static readonly int[] Table = BuildTable();

    static int[] BuildTable()
    {
        var t = new int[100];
        double points = 0;
        for (int lvl = 1; lvl < 100; lvl++)
        {
            t[lvl] = (int)Math.Floor(points / 4);
            points += Math.Floor(lvl + 300 * Math.Pow(2, lvl / 7.0));
        }
        return t;
    }

    public static int XpForLevel(int level) => Table[Math.Clamp(level, 1, 99)];

    public static int LevelForXp(int xp)
    {
        for (int lvl = 98; lvl >= 1; lvl--)
            if (xp >= Table[lvl + 1]) return lvl + 1;
        return 1;
    }

    public static int CombatLevel(int atk, int str, int def, int hp, int rng, int mag)
    {
        double b = 0.25 * (def + hp);
        double melee = 0.325 * (atk + str);
        double range = 0.325 * Math.Floor(rng * 1.5);
        double mage = 0.325 * Math.Floor(mag * 1.5);
        return (int)Math.Floor(b + Math.Max(melee, Math.Max(range, mage)));
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Opts = new() { IncludeFields = true };
    public static string Write<T>(T v) => JsonSerializer.Serialize(v, Opts);
    public static T Read<T>(string s) => JsonSerializer.Deserialize<T>(s, Opts);
}

/// Deterministic hashing so server and clients agree without syncing.
public static class Hash
{
    public static uint Of(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }
    public static float Unit(int x, int z, int salt = 0)
    {
        uint h = (uint)(x * 374761393 + z * 668265263 + salt * 144269504);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}

public struct Tile : IEquatable<Tile>
{
    public int X, Z;
    public Tile(int x, int z) { X = x; Z = z; }
    public bool Equals(Tile o) => X == o.X && Z == o.Z;
    public override bool Equals(object o) => o is Tile t && Equals(t);
    public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
    public static bool operator ==(Tile a, Tile b) => a.Equals(b);
    public static bool operator !=(Tile a, Tile b) => !a.Equals(b);
    public int Chebyshev(Tile o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Z - o.Z));
    public override string ToString() => $"({X},{Z})";
}
