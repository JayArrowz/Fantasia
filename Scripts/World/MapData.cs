using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia;

public enum Ground : byte { Grass, Dirt, Cobble, Sand, Water, Stone, Wood, Void, DarkGrass, Ash, Moss }

public enum ObjKind
{
    TreeOak, TreePine, TreeDead, TreeWillow, Bush, RockLarge, RockSmall,
    HouseTimber, HouseStone, Keep, Tower, CastleWall, Stall, BankBooth, Well, Fence,
    Barrel, Crate, Haystack, TorchPost, Brazier, Tent, CaveEntrance, Mausoleum, Gravestone,
    StandingStone, Ladder, Anvil, Cart, Fountain, Statue, Altar, BonesPile, Pillar, Chest,
    Throne, Cauldron, Campfire, Signpost, Banner, WallTorch, Bridge,
    // Interior furniture
    Bed, Table, Chair, Stool, Bookshelf, Fireplace, Counter, Furnace, WeaponRack, ArmorStand, Keg,
    ShelfPotions, Rug, Candelabra, RoyalThrone, LongTable, Wardrobe,
    // Skilling
    FishingSpot, OreRock, FarmPatch, SpinningWheel, Loom, EnchantingTable, Workbench, SpiderWeb,
}

public enum BuildingStyle { Timber, Stone, Castle, Longhall }

/// An enterable building: perimeter tiles are walls except for doors; the interior is walkable.
public sealed class Building
{
    public int Id;
    public string Name;
    public int X, Z, W, D;
    public BuildingStyle Style;
    public readonly List<Tile> Doors = new();

    public bool Contains(Tile t) => t.X >= X && t.X < X + W && t.Z >= Z && t.Z < Z + D;
    public bool IsPerimeter(int x, int z) => x >= X && x < X + W && z >= Z && z < Z + D && (x == X || x == X + W - 1 || z == Z || z == Z + D - 1);
    public bool IsDoor(int x, int z) => Doors.Contains(new Tile(x, z));
    public bool IsWall(int x, int z) => IsPerimeter(x, z) && !IsDoor(x, z);
    public float WallHeight => Style switch { BuildingStyle.Castle => 5.2f, BuildingStyle.Stone => 3.3f, BuildingStyle.Longhall => 3.2f, _ => 2.9f };
}

public sealed class WorldObject
{
    public int Id;
    public ObjKind Kind;
    public int X, Z;            // min corner tile
    public int W = 1, D = 1;    // footprint in tiles
    public float Rot;           // yaw degrees
    public float Scale = 1f;
    public bool Blocks = true;
    public bool BlocksLos;
    public string Name;
    public string Action;       // e.g. "Climb-down", "Enter", "Bank", "Read"
    public string TargetMap;
    public int TargetX, TargetZ;
    public string Text;         // signpost text etc
    public Color Tint = Colors.White;
    public string Res;          // ResourceDb id for gatherable objects
    public string Group;        // fishing spots: shoals move between spots of the same group
    public string Station;      // crafting station id (RecipeDb stations) opened by Action

    public Tile Center => new(X + W / 2, Z + D / 2);
    public bool Covers(int x, int z) => x >= X && x < X + W && z >= Z && z < Z + D;

    public int DistanceTo(Tile t)
    {
        int dx = t.X < X ? X - t.X : t.X >= X + W ? t.X - (X + W - 1) : 0;
        int dz = t.Z < Z ? Z - t.Z : t.Z >= Z + D ? t.Z - (Z + D - 1) : 0;
        return Math.Max(dx, dz);
    }
}

public sealed class NpcSpawn
{
    public string NpcId;
    public int X, Z;
    public NpcSpawn(string id, int x, int z) { NpcId = id; X = x; Z = z; }
}

public sealed class MapLabel
{
    public string Text;
    public int X, Z;
    public MapLabel(string t, int x, int z) { Text = t; X = x; Z = z; }
}

public sealed class MapData
{
    public string Id;
    public string Name;
    public int W, H;
    public Ground[,] Ground;
    public float[,] Heights;       // (W+1) x (H+1) vertex heights
    public bool[,] Blocked;
    public bool[,] LosBlocked;
    public int[,] ObjectAt;        // object id occupying tile (-1 none)
    public readonly List<WorldObject> Objects = new();
    public readonly List<NpcSpawn> Spawns = new();
    public readonly List<MapLabel> Labels = new();
    public readonly List<Building> Buildings = new();

    public Building BuildingAt(Tile t)
    {
        foreach (var b in Buildings) if (b.Contains(t)) return b;
        return null;
    }
    public Tile Spawn;
    public bool Underground;
    public Color SkyTop = new(0.35f, 0.55f, 0.85f), SkyHorizon = new(0.75f, 0.80f, 0.85f);
    public Color Ambient = new(0.55f, 0.55f, 0.6f);
    public Color Fog = new(0.7f, 0.75f, 0.8f);
    public float FogDensity = 0.004f;
    public Rect2I PvpZone = new(0, 0, 0, 0);
    public string Music = "overworld";

    public MapData(string id, string name, int w, int h)
    {
        Id = id; Name = name; W = w; H = h;
        Ground = new Ground[w, h];
        Heights = new float[w + 1, h + 1];
        Blocked = new bool[w, h];
        LosBlocked = new bool[w, h];
        ObjectAt = new int[w, h];
        for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) ObjectAt[x, z] = -1;
    }

    public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < W && z < H;
    public bool Walkable(int x, int z) => InBounds(x, z) && !Blocked[x, z];
    public bool Walkable(Tile t) => Walkable(t.X, t.Z);
    public bool IsPvp(Tile t) => PvpZone.HasPoint(new Vector2I(t.X, t.Z));

    public float TileHeight(int x, int z)
    {
        if (!InBounds(x, z)) return 0;
        return (Heights[x, z] + Heights[x + 1, z] + Heights[x, z + 1] + Heights[x + 1, z + 1]) * 0.25f;
    }

    public float HeightAt(float fx, float fz)
    {
        fx = Mathf.Clamp(fx, 0, W - 0.001f);
        fz = Mathf.Clamp(fz, 0, H - 0.001f);
        int x = (int)fx, z = (int)fz;
        float tx = fx - x, tz = fz - z;
        float a = Mathf.Lerp(Heights[x, z], Heights[x + 1, z], tx);
        float b = Mathf.Lerp(Heights[x, z + 1], Heights[x + 1, z + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    public Vector3 TileCenter(Tile t) => new(t.X + 0.5f, TileHeight(t.X, t.Z), t.Z + 0.5f);

    public WorldObject AddObject(WorldObject o)
    {
        o.Id = Objects.Count;
        Objects.Add(o);
        return o;
    }

    public WorldObject GetObject(int id) => id >= 0 && id < Objects.Count ? Objects[id] : null;

    /// Recompute collision from ground and objects. Called once after generation.
    public void BuildCollision()
    {
        for (int x = 0; x < W; x++)
            for (int z = 0; z < H; z++)
            {
                var g = Ground[x, z];
                Blocked[x, z] = g == Fantasia.Ground.Water || g == Fantasia.Ground.Void;
                LosBlocked[x, z] = g == Fantasia.Ground.Void;
                ObjectAt[x, z] = -1;
            }
        foreach (var o in Objects)
        {
            for (int x = o.X; x < o.X + o.W; x++)
                for (int z = o.Z; z < o.Z + o.D; z++)
                {
                    if (!InBounds(x, z)) continue;
                    if (o.Kind == ObjKind.Bridge) { Blocked[x, z] = false; continue; }
                    ObjectAt[x, z] = o.Id;
                    if (o.Blocks) Blocked[x, z] = true;
                    if (o.BlocksLos) LosBlocked[x, z] = true;
                }
        }
        foreach (var b in Buildings)
            for (int x = b.X; x < b.X + b.W; x++)
                for (int z = b.Z; z < b.Z + b.D; z++)
                {
                    if (!InBounds(x, z) || !b.IsPerimeter(x, z)) continue;
                    bool wall = !b.IsDoor(x, z);
                    Blocked[x, z] = wall;
                    LosBlocked[x, z] = wall;
                }
    }

    public bool HasLineOfSight(Tile a, Tile b)
    {
        int x0 = a.X, z0 = a.Z, x1 = b.X, z1 = b.Z;
        int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;
        while (!(x0 == x1 && z0 == z1))
        {
            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 < dx) { err += dx; z0 += sz; }
            if (x0 == x1 && z0 == z1) break;
            if (!InBounds(x0, z0) || LosBlocked[x0, z0]) return false;
        }
        return true;
    }
}
