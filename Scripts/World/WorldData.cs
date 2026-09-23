using System.Collections.Generic;
using Godot;

namespace Fantasia;

/// res://data/world.json: the hand-placed content of the maps. The generator (MapGenerator.cs) still
/// lays out terrain, rivers, roads, buildings and dungeon rooms procedurally; this file says who lives
/// where, what the signs and books say, where the skilling spots are, and how each dungeon is stocked.
public sealed class WorldFile
{
    public OverworldDef Overworld = new();
    public List<DungeonDef> Dungeons = new();
}

public sealed class OverworldDef
{
    public string Name = "";
    public int[] Spawn = { 0, 0 };                  // x, z
    public int[] PvpZone = { 0, 0, 0, 0 };          // x, z, w, h
    public Color SkyTop, SkyHorizon, Ambient, Fog;
    public float FogDensity;
    public List<LabelDef> Labels = new();
    public string[] Lore = System.Array.Empty<string>();          // random bookshelf texts
    public Dictionary<string, string> Texts = new();              // signposts and named books, by key
    /// NPC spawns by anchor: each anchor is a fixed point in the generator (e.g. "farm.cows") where its
    /// spawns are placed, so area spawns draw from the world's random sequence exactly where they always
    /// have. "extra" is placed after everything else, for new spawns.
    public Dictionary<string, List<SpawnDef>> Spawns = new();
    public List<RockDef> QuarryRocks = new();
    public List<RockDef> CircleRocks = new();                     // placed only if the tile is free
    public List<int[]> Allotments = new();                        // x, z of each 3x3 patch
    public List<int[]> Mulberries = new();                        // x, z (skipped if the tile is taken)
    public List<FishingGroupDef> FishingGroups = new();
}

public sealed class DungeonDef
{
    public string Id, Name;
    public int Width, Height, Seed;
    public DungeonTheme Theme;
    public int Rooms = 9;
    public string Music = "cave";
    public Color Ambient, Fog;
    public float FogDensity;
    public int[] Exit = { 0, 0 };                                 // overworld tile the ladder leads to
    public string[] Monsters = System.Array.Empty<string>();      // room spawn pool (later entries rarer early on)
    public string[] Ores = System.Array.Empty<string>();          // resource ids placed round the rooms, in turn
    public List<SpawnDef> Boss = new();                           // x, z are offsets from the boss room centre
    public string BossLabel = "";
}

/// A fixed spawn at (x, z), or `count` spawns scattered in `area` = [x, z, w, h].
public sealed class SpawnDef
{
    public string Npc;
    public int X, Z;
    public int[] Area;
    public int Count = 1;
}

public sealed class LabelDef
{
    public string Text;
    public int X, Z;
}

public sealed class RockDef
{
    public int X, Z;
    public string Res;
}

public sealed class FishingGroupDef
{
    public string Group, Res;
    public int[] Area = { 0, 0, 0, 0 };                           // x0, z0, x1, z1 (inclusive)
    public int Count;
}
