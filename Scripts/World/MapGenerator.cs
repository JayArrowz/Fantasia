using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia;

/// Deterministic map generation. Server and every client build identical maps from code plus
/// res://data/world.json (spawns, texts, skilling spots, dungeon stock), so terrain never needs to
/// be sent over the network.
public static class MapGenerator
{
    static readonly Dictionary<string, MapData> Cache = new();

    /// Hand-placed map content (res://data/world.json).
    public static readonly WorldFile World = GameData.Load<WorldFile>("world.json");

    public static readonly string[] MapIds = new[] { GameConst.OverworldId }.Concat(World.Dungeons.Select(d => d.Id)).ToArray();

    public static MapData Get(string id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var m)) return m;
            var dungeon = World.Dungeons.Find(d => d.Id == id);
            m = id == GameConst.OverworldId ? new OverworldBuilder().Build()
                : dungeon != null ? DungeonBuilder.Build(dungeon)
                : null;
            if (m != null) Cache[id] = m;
            return m;
        }
    }


}

sealed class OverworldBuilder
{
    const int W = 160, H = 160;
    static OverworldDef Def => MapGenerator.World.Overworld;
    readonly MapData m = new(GameConst.OverworldId, Def.Name, W, H);
    readonly Random rng = new(90210);
    readonly bool[,] reserved = new bool[W, H];
    readonly FastNoiseLite noise = new() { Seed = 7, Frequency = 0.035f, FractalOctaves = 3 };
    readonly FastNoiseLite detail = new() { Seed = 11, Frequency = 0.18f };

    float R() => (float)rng.NextDouble();
    int RI(int a, int b) => rng.Next(a, b + 1);

    public MapData Build()
    {
        m.Spawn = new Tile(Def.Spawn[0], Def.Spawn[1]);
        m.PvpZone = new Rect2I(Def.PvpZone[0], Def.PvpZone[1], Def.PvpZone[2], Def.PvpZone[3]);
        m.SkyTop = Def.SkyTop;
        m.SkyHorizon = Def.SkyHorizon;
        m.Ambient = Def.Ambient;
        m.Fog = Def.Fog;
        m.FogDensity = Def.FogDensity;

        Fill(0, 0, W, H, Ground.Grass);
        Fill(0, 0, W, 28, Ground.DarkGrass);
        for (int x = 0; x < W; x++)
            for (int z = 0; z < 28; z++)
                if (detail.GetNoise2D(x, z) > 0.35f) m.Ground[x, z] = Ground.Ash;

        River();
        Lake(28, 136, 9);
        Roads();
        Town();
        Farm();
        GoblinCamp();
        StoneCircle();
        Graveyard();
        BarbarianVillage();
        BanditCamp();
        Bloodmarch();
        SkillingAreas();
        Forest();
        Countryside();
        Border();
        TagResources();
        Heights();
        FlattenBuildings();
        Labels();
        Spawns("extra");
        var lore = Def.Lore;
        foreach (var o in m.Objects)
        {
            if (o.Kind == ObjKind.Bookshelf && o.Text == null) o.Text = lore[(o.X * 7 + o.Z * 13) % lore.Length];
            if (o.Text != null && o.Action == null) o.Action = "Read";
        }
        SkillingStations(m);
        m.BuildCollision();
        FixSpawns(m);
        return m;
    }

    // ---------- helpers ----------
    void Fill(int x0, int z0, int w, int h, Ground g)
    {
        for (int x = x0; x < x0 + w; x++)
            for (int z = z0; z < z0 + h; z++)
                if (m.InBounds(x, z)) m.Ground[x, z] = g;
    }

    void Reserve(int x0, int z0, int w, int h)
    {
        for (int x = x0; x < x0 + w; x++)
            for (int z = z0; z < z0 + h; z++)
                if (m.InBounds(x, z)) reserved[x, z] = true;
    }

    bool Free(int x0, int z0, int w, int h)
    {
        for (int x = x0; x < x0 + w; x++)
            for (int z = z0; z < z0 + h; z++)
            {
                if (!m.InBounds(x, z) || reserved[x, z]) return false;
                var g = m.Ground[x, z];
                if (g == Ground.Water || g == Ground.Dirt || g == Ground.Cobble || g == Ground.Sand) return false;
            }
        return true;
    }

    WorldObject Obj(ObjKind k, int x, int z, int w = 1, int d = 1, bool blocks = true, float rot = 0, bool reserve = true)
    {
        var o = m.AddObject(new WorldObject { Kind = k, X = x, Z = z, W = w, D = d, Blocks = blocks, Rot = rot });
        if (reserve) Reserve(x, z, w, d);
        return o;
    }

    void Scatter(ObjKind k, int x0, int z0, int w, int h, int count, bool blocks = true, int size = 1, float scaleMin = 0.85f, float scaleMax = 1.25f)
    {
        int tries = count * 8;
        while (count > 0 && tries-- > 0)
        {
            int x = RI(x0, x0 + w - size), z = RI(z0, z0 + h - size);
            if (!Free(x, z, size, size)) continue;
            var o = Obj(k, x, z, size, size, blocks, R() * 360f);
            o.Scale = Mathf.Lerp(scaleMin, scaleMax, R());
            count--;
        }
    }

    void Spawn(string npc, int x, int z) => m.Spawns.Add(new NpcSpawn(npc, x, z));

    /// Places the world.json spawns for an anchor: fixed ones at their tile, area ones scattered.
    void Spawns(string anchor)
    {
        if (!Def.Spawns.TryGetValue(anchor, out var list)) return;
        foreach (var s in list)
            if (s.Area is { Length: 4 } a) SpawnIn(s.Npc, a[0], a[1], a[2], a[3], s.Count);
            else Spawn(s.Npc, s.X, s.Z);
    }

    /// Anchors the generator places spawns at (world.json keys must be one of these).
    public static readonly string[] SpawnAnchors =
        { "town", "farm.cows", "farm.chickens", "farm.rats", "goblin_camp", "stone_circle", "barbarian_village", "bandit_camp", "bloodmarch", "skilling", "forest", "countryside", "extra" };

    static string Text(string key) => Def.Texts.TryGetValue(key, out var t) ? t : null;

    void SpawnIn(string npc, int x0, int z0, int w, int h, int count)
    {
        int tries = count * 20;
        while (count > 0 && tries-- > 0)
        {
            int x = RI(x0, x0 + w - 1), z = RI(z0, z0 + h - 1);
            if (!m.InBounds(x, z) || m.Ground[x, z] == Ground.Water || m.BuildingAt(new Tile(x, z)) != null) continue;
            if (m.Objects.Exists(o => o.Blocks && o.Covers(x, z))) continue;
            Spawn(npc, x, z);
            count--;
        }
    }

    void Road(params (int x, int z)[] pts)
    {
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var (x0, z0) = pts[i];
            var (x1, z1) = pts[i + 1];
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0));
            for (int s = 0; s <= steps; s++)
            {
                float t = steps == 0 ? 0 : s / (float)steps;
                int cx = (int)Mathf.Round(Mathf.Lerp(x0, x1, t));
                int cz = (int)Mathf.Round(Mathf.Lerp(z0, z1, t));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (m.InBounds(cx + dx, cz + dz) && m.Ground[cx + dx, cz + dz] != Ground.Water && m.Ground[cx + dx, cz + dz] != Ground.Cobble)
                            m.Ground[cx + dx, cz + dz] = Ground.Dirt;
            }
        }
    }

    int RiverX(int z) => 118 + (int)Mathf.Round(4f * Mathf.Sin(z * 0.07f) + 2f * Mathf.Sin(z * 0.023f + 1f));

    // ---------- features ----------
    void River()
    {
        for (int z = 0; z < H; z++)
        {
            int cx = RiverX(z);
            for (int x = cx - 3; x <= cx + 3; x++)
            {
                if (!m.InBounds(x, z)) continue;
                m.Ground[x, z] = Math.Abs(x - cx) <= 2 ? Ground.Water : Ground.Sand;
            }
        }
        Bridge(97);
        Bridge(18);
    }

    void Bridge(int z)
    {
        int cx = RiverX(z);
        int x0 = cx - 3, x1 = cx + 3;
        for (int x = x0; x <= x1; x++)
            for (int dz = 0; dz < 3; dz++)
                if (m.Ground[x, z - 1 + dz] == Ground.Sand) m.Ground[x, z - 1 + dz] = Ground.Dirt;
        var b = Obj(ObjKind.Bridge, x0, z - 1, x1 - x0 + 1, 3, false);
        b.Name = "Bridge";
    }

    void Lake(int cx, int cz, int r)
    {
        for (int x = cx - r - 2; x <= cx + r + 2; x++)
            for (int z = cz - r - 2; z <= cz + r + 2; z++)
            {
                if (!m.InBounds(x, z)) continue;
                float d = new Vector2(x - cx, z - cz).Length() + detail.GetNoise2D(x, z) * 2f;
                if (d < r) m.Ground[x, z] = Ground.Water;
                else if (d < r + 1.6f) m.Ground[x, z] = Ground.Sand;
            }
        Reserve(cx - r - 2, cz - r - 2, 2 * r + 5, 2 * r + 5);
    }

    void Roads()
    {
        Road((60, 97), (50, 97), (40, 90), (32, 78), (28, 70));        // west gate -> goblin camp
        Road((80, 116), (80, 124), (81, 134));                          // south gate -> barbarians
        Road((100, 97), (112, 97), (126, 97), (133, 97));               // east gate -> graveyard
        Road((92, 80), (92, 64), (90, 46), (92, 30), (92, 12));         // north gate -> wildlands
        Road((126, 97), (128, 80), (130, 60));                          // to stone circle
        Road((126, 97), (130, 115), (136, 130));                        // to bandit camp
        Road((92, 20), (112, 18), (126, 18), (136, 14));                // wild bridge -> dragons
    }

    void Town()
    {
        const int x0 = 60, z0 = 80, x1 = 100, z1 = 116;
        Fill(x0, z0, x1 - x0 + 1, z1 - z0 + 1, Ground.Grass);
        Reserve(x0 - 1, z0 - 1, x1 - x0 + 3, z1 - z0 + 3);

        // streets and market square
        Fill(78, 91, 5, z1 - 91, Ground.Cobble);
        Fill(x0, 96, x1 - x0 + 1, 3, Ground.Cobble);
        Fill(72, 93, 17, 15, Ground.Cobble);
        Fill(91, z0, 3, 16, Ground.Cobble);

        // walls with gates: west (z 96-98), east (z 96-98), south (x 79-81), north (x 91-93)
        for (int x = x0; x <= x1; x++)
        {
            if (x < 91 || x > 93) Wall(x, z0);
            if (x < 79 || x > 81) Wall(x, z1);
        }
        for (int z = z0 + 1; z < z1; z++)
        {
            if (z < 96 || z > 98) { Wall(x0, z); Wall(x1, z); }
        }
        Obj(ObjKind.Tower, x0 - 1, z0 - 1, 3, 3).BlocksLos = true;
        Obj(ObjKind.Tower, x1 - 1, z0 - 1, 3, 3).BlocksLos = true;
        Obj(ObjKind.Tower, x0 - 1, z1 - 1, 3, 3).BlocksLos = true;
        Obj(ObjKind.Tower, x1 - 1, z1 - 1, 3, 3).BlocksLos = true;
        foreach (var (gx, gz) in new[] { (x0 - 1, 95), (x0 - 1, 99), (x1 + 1, 95), (x1 + 1, 99), (78, z1 + 1), (82, z1 + 1), (90, z0 - 1), (94, z0 - 1) })
            Obj(ObjKind.TorchPost, gx, gz);

        // statues and banners before the castle doors
        Obj(ObjKind.Banner, 71, 91, 1, 1);
        Obj(ObjKind.Banner, 86, 91, 1, 1);
        var s1 = Obj(ObjKind.Statue, 76, 92, 1, 1, true, 180); s1.Name = "Statue of King Aldric";
        var s2 = Obj(ObjKind.Statue, 83, 92, 1, 1, true, 180); s2.Name = "Statue of Queen Maren";
        var f = Obj(ObjKind.Fountain, 79, 99, 3, 3); f.Name = "Fountain";

        // ---- Castle Aldmoor: throne room ----
        var castle = Bld("Castle Aldmoor", 71, 81, 16, 10, BuildingStyle.Castle, (78, 90), (79, 90));
        Furn(castle, ObjKind.RoyalThrone, 78, 82, 0, 2, 1, true, "Throne of Aldmoor");
        for (int z = 84; z <= 88; z += 2) Furn(castle, ObjKind.Rug, 78, z, 0, 2, 2, false, "Royal carpet");
        Furn(castle, ObjKind.Banner, 73, 82, 0); Furn(castle, ObjKind.Banner, 84, 82, 0);
        Furn(castle, ObjKind.LongTable, 73, 86, 0, 3, 1, true, "Feasting table");
        Furn(castle, ObjKind.LongTable, 82, 86, 0, 3, 1, true, "Feasting table");
        foreach (var x in new[] { 73, 75, 82, 84 }) { Furn(castle, ObjKind.Chair, x, 85, 0, 1, 1, false); Furn(castle, ObjKind.Chair, x, 87, 180, 1, 1, false); }
        foreach (var (cx, cz) in new[] { (72, 82), (85, 82), (72, 89), (85, 89) }) Furn(castle, ObjKind.Candelabra, cx, cz);
        Furn(castle, ObjKind.WeaponRack, 72, 84, 90, 1, 1, true, "Weapon rack");
        Furn(castle, ObjKind.ArmorStand, 85, 84, -90, 1, 1, true, "Suit of armour");
        Furn(castle, ObjKind.Bookshelf, 85, 86, -90, 1, 1, true, "Royal archives").Text = Text("castle_archives");

        // ---- Aldmoor Treasury (bank) ----
        var bank = Bld("Aldmoor Treasury", 94, 87, 6, 6, BuildingStyle.Stone, (96, 92));
        for (int x = 95; x <= 98; x++)
        {
            var booth = Furn(bank, ObjKind.BankBooth, x, 89, 0, 1, 1, true, "Teller's counter");
            booth.Action = "Bank";
        }
        Furn(bank, ObjKind.Chest, 95, 88, 0, 1, 1, true, "Vault chest");
        Furn(bank, ObjKind.Chest, 98, 88, 0, 1, 1, true, "Vault chest");

        // ---- Elara's Sigil Shop ----
        var magic = Bld("Elara's Sigil Shop", 61, 82, 8, 6, BuildingStyle.Stone, (65, 87));
        Furn(magic, ObjKind.Counter, 63, 84, 0, 3, 1, true, "Shop counter");
        Furn(magic, ObjKind.ShelfPotions, 62, 83, 0); Furn(magic, ObjKind.ShelfPotions, 67, 83, 0);
        Furn(magic, ObjKind.Bookshelf, 62, 85, 90, 1, 1, true, "Tomes of sigilcraft").Text = Text("magic_tomes");
        Furn(magic, ObjKind.Cauldron, 67, 85);
        Furn(magic, ObjKind.Candelabra, 62, 86);
        Furn(magic, ObjKind.EnchantingTable, 63, 86, 0, 1, 1, true, "Enchanting table");

        // ---- Brom's Forge ----
        var forge = Bld("Brom's Forge", 61, 99, 9, 7, BuildingStyle.Stone, (65, 99));
        Furn(forge, ObjKind.Furnace, 62, 103, 90, 2, 2, true, "Forge");
        Furn(forge, ObjKind.Anvil, 64, 102, 0, 1, 1, true, "Anvil");
        Furn(forge, ObjKind.WeaponRack, 68, 101, -90, 1, 1, true, "Weapon rack");
        Furn(forge, ObjKind.WeaponRack, 68, 103, -90, 1, 1, true, "Weapon rack");
        Furn(forge, ObjKind.ArmorStand, 62, 100, 90, 1, 1, true, "Armour stand");
        Furn(forge, ObjKind.Barrel, 68, 104); Furn(forge, ObjKind.Crate, 67, 104);

        // ---- The Gilded Tankard (inn) ----
        var inn = Bld("The Gilded Tankard", 61, 107, 11, 9, BuildingStyle.Timber, (71, 111));
        Furn(inn, ObjKind.Counter, 63, 109, 0, 4, 1, true, "Bar");
        Furn(inn, ObjKind.Keg, 62, 108); Furn(inn, ObjKind.Keg, 67, 108);
        Furn(inn, ObjKind.ShelfPotions, 65, 108, 0, 1, 1, true, "Bottles of cordial");
        Furn(inn, ObjKind.Fireplace, 65, 114, 180, 2, 1, true, "Hearth");
        Furn(inn, ObjKind.Table, 64, 112); Furn(inn, ObjKind.Stool, 63, 112, 90, 1, 1, false); Furn(inn, ObjKind.Stool, 64, 111, 0, 1, 1, false);
        Furn(inn, ObjKind.Table, 68, 110); Furn(inn, ObjKind.Stool, 68, 111, 180, 1, 1, false); Furn(inn, ObjKind.Stool, 69, 110, -90, 1, 1, false);
        Furn(inn, ObjKind.Table, 68, 113); Furn(inn, ObjKind.Stool, 67, 113, 90, 1, 1, false); Furn(inn, ObjKind.Stool, 69, 113, -90, 1, 1, false);
        Furn(inn, ObjKind.Candelabra, 62, 114); Furn(inn, ObjKind.Candelabra, 70, 108);

        // ---- Aldmoor General Store ----
        var store = Bld("Aldmoor General Store", 90, 100, 10, 7, BuildingStyle.Timber, (90, 103));
        Furn(store, ObjKind.Counter, 95, 102, 90, 1, 3, true, "Shop counter");
        Furn(store, ObjKind.ShelfPotions, 98, 101, -90); Furn(store, ObjKind.Bookshelf, 98, 103, -90, 1, 1, true, "Shelf of wares");
        Furn(store, ObjKind.Barrel, 98, 105); Furn(store, ObjKind.Crate, 97, 105); Furn(store, ObjKind.Crate, 91, 101); Furn(store, ObjKind.Barrel, 92, 101);
        Furn(store, ObjKind.Rug, 92, 103, 0, 2, 2, false);

        // ---- Lyra's Bows ----
        var bows = Bld("Lyra's Bows & Fletchings", 83, 109, 6, 7, BuildingStyle.Timber, (83, 112));
        Furn(bows, ObjKind.Counter, 85, 111, 90, 1, 3, true, "Shop counter");
        Furn(bows, ObjKind.WeaponRack, 87, 110, -90, 1, 1, true, "Rack of bows");
        Furn(bows, ObjKind.WeaponRack, 87, 114, -90, 1, 1, true, "Rack of bows");
        Furn(bows, ObjKind.Workbench, 84, 114, 0, 1, 1, true, "Workbench");
        Furn(bows, ObjKind.Barrel, 84, 114, 0, 1, 1, true, "Barrel of arrows");

        // ---- homes ----
        FurnishHome(Bld("Cottage", 61, 89, 8, 6, BuildingStyle.Timber, (64, 94)));
        FurnishHome(Bld("Cottage", 90, 109, 10, 7, BuildingStyle.Timber, (90, 112)));
        FurnishHome(Bld("Cottage", 72, 109, 6, 7, BuildingStyle.Timber, (77, 112)));
        FurnishHome(Bld("Stone house", 94, 81, 6, 5, BuildingStyle.Stone, (96, 85)));


        var st1 = Obj(ObjKind.Stall, 73, 104, 3, 2, true, 0); st1.Name = "Market stall"; st1.Tint = new Color(0.9f, 0.85f, 0.6f);
        var st2 = Obj(ObjKind.Stall, 85, 94, 3, 2, true, 180); st2.Name = "Market stall"; st2.Tint = new Color(0.4f, 0.6f, 0.9f);
        var well = Obj(ObjKind.Well, 70, 105, 2, 2); well.Name = "Well";
        Obj(ObjKind.Barrel, 89, 99); Obj(ObjKind.Crate, 89, 98, 1, 1);
        Obj(ObjKind.Cart, 72, 100, 2, 1);
        foreach (var (tx, tz) in new[] { (72, 93), (88, 93), (72, 107), (88, 107) })
            Obj(ObjKind.TorchPost, tx, tz);
        var sign = Obj(ObjKind.Signpost, 77, 103); sign.Name = "Signpost"; sign.Action = "Read";
        sign.Text = Text("town_sign");
        Spawns("town");
    }

    // ---------- enterable buildings ----------
    Building Bld(string name, int x, int z, int w, int d, BuildingStyle style, params (int x, int z)[] doors)
    {
        var b = new Building { Id = m.Buildings.Count, Name = name, X = x, Z = z, W = w, D = d, Style = style };
        foreach (var (dx, dz) in doors) b.Doors.Add(new Tile(dx, dz));
        m.Buildings.Add(b);
        Fill(x, z, w, d, style is BuildingStyle.Castle or BuildingStyle.Stone ? Ground.Stone : Ground.Wood);
        Reserve(x - 1, z - 1, w + 2, d + 2);
        return b;
    }

    /// Tiles just inside each door must stay clear so the building can be entered.
    static bool NearDoor(Building b, int x, int z, int w, int d)
    {
        foreach (var door in b.Doors)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int tx = door.X + dx, tz = door.Z + dz;
                    if (tx >= x && tx < x + w && tz >= z && tz < z + d) return true;
                }
        return false;
    }

    WorldObject Furn(Building b, ObjKind k, int x, int z, float rot = 0, int w = 1, int d = 1, bool blocks = true, string name = null)
    {
        var o = new WorldObject { Kind = k, X = x, Z = z, W = w, D = d, Rot = rot, Blocks = blocks, Name = name };
        if (blocks && NearDoor(b, x, z, w, d)) return o; // skipped
        foreach (var e in m.Objects) if (e.X < x + w && e.X + e.W > x && e.Z < z + d && e.Z + e.D > z && e.Blocks && blocks) return o;
        if (k == ObjKind.Bookshelf && name != null && o.Text == null) o.Action = null;
        m.AddObject(o);
        return o;
    }

    void FurnishHome(Building b)
    {
        int x0 = b.X + 1, z0 = b.Z + 1, x1 = b.X + b.W - 2, z1 = b.Z + b.D - 2;
        int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        Furn(b, ObjKind.Bed, x0, z0, 0, 1, 2, true, "Bed");
        Furn(b, ObjKind.Fireplace, Math.Min(x1 - 1, cx + 1), z0, 0, 2, 1, true, "Fireplace");
        Furn(b, ObjKind.Table, cx, cz + (z1 > z0 + 2 ? 1 : 0), 0, 1, 1, true, "Table");
        Furn(b, ObjKind.Chair, cx - 1, cz + (z1 > z0 + 2 ? 1 : 0), 90, 1, 1, false);
        Furn(b, ObjKind.Chair, cx + 1, cz + (z1 > z0 + 2 ? 1 : 0), -90, 1, 1, false);
        Furn(b, ObjKind.Wardrobe, x1, z1, -90, 1, 1, true, "Wardrobe");
        Furn(b, ObjKind.Bookshelf, x0, z1, 90, 1, 1, true, "Bookshelf");
        Furn(b, ObjKind.Candelabra, x1, z0);
        Furn(b, ObjKind.Rug, cx, cz - 1, 0, 2, 1, false);
    }

    void Wall(int x, int z)
    {
        var w = Obj(ObjKind.CastleWall, x, z);
        w.BlocksLos = true;
        w.Name = "Town wall";
    }

    void Farm()
    {
        // cow field
        FenceRect(32, 88, 18, 16, gapSide: 'E', gapAt: 97);
        Spawns("farm.cows");
        // chicken coop
        FenceRect(38, 108, 11, 9, gapSide: 'N', gapAt: 43);
        Spawns("farm.chickens");
        var farmhouse = Bld("Hob's Farmhouse", 51, 101, 6, 5, BuildingStyle.Timber, (53, 105));
        FurnishHome(farmhouse);
        Obj(ObjKind.Haystack, 51, 108); Obj(ObjKind.Haystack, 53, 109); Obj(ObjKind.Haystack, 36, 85);
        Obj(ObjKind.Cart, 55, 94, 2, 1, true, 90);
        Obj(ObjKind.Well, 52, 111, 2, 2).Name = "Well";
        var sign = Obj(ObjKind.Signpost, 57, 99); sign.Name = "Signpost"; sign.Action = "Read";
        sign.Text = Text("farm_sign");
        Spawns("farm.rats");
    }

    void FenceRect(int x0, int z0, int w, int d, char gapSide, int gapAt)
    {
        for (int x = x0; x < x0 + w; x++)
        {
            if (!(gapSide == 'N' && Math.Abs(x - gapAt) <= 1)) FenceAt(x, z0, 0);
            if (!(gapSide == 'S' && Math.Abs(x - gapAt) <= 1)) FenceAt(x, z0 + d - 1, 0);
        }
        for (int z = z0 + 1; z < z0 + d - 1; z++)
        {
            if (!(gapSide == 'W' && Math.Abs(z - gapAt) <= 1)) FenceAt(x0, z, 90);
            if (!(gapSide == 'E' && Math.Abs(z - gapAt) <= 1)) FenceAt(x0 + w - 1, z, 90);
        }
        Reserve(x0, z0, w, d);
    }

    void FenceAt(int x, int z, float rot)
    {
        if (m.Ground[x, z] == Ground.Dirt) return; // leave roads open
        var f = Obj(ObjKind.Fence, x, z, 1, 1, true, rot);
        f.Name = "Fence";
    }

    void GoblinCamp()
    {
        Fill(18, 60, 22, 20, Ground.Dirt);
        for (int x = 18; x < 40; x++) for (int z = 60; z < 80; z++)
            if (detail.GetNoise2D(x, z) > 0.1f) m.Ground[x, z] = Ground.Grass;
        var cave = Obj(ObjKind.CaveEntrance, 22, 60, 4, 3);
        cave.Name = "Cave entrance"; cave.Action = "Enter"; cave.TargetMap = "goblin_caves"; cave.BlocksLos = true;
        Obj(ObjKind.Campfire, 29, 70, 1, 1);
        foreach (var (tx, tz, r) in new[] { (20, 67, 30f), (33, 64, 200f), (36, 72, 120f), (22, 75, 300f), (30, 77, 20f) })
            Obj(ObjKind.Tent, tx, tz, 2, 2, true, r).Name = "Goblin tent";
        Obj(ObjKind.BonesPile, 26, 66, 1, 1, false);
        Obj(ObjKind.Crate, 34, 68); Obj(ObjKind.Barrel, 35, 68);
        Obj(ObjKind.TorchPost, 21, 64); Obj(ObjKind.TorchPost, 26, 64);
        Spawns("goblin_camp");
        Reserve(17, 58, 25, 24);
    }

    void StoneCircle()
    {
        int cx = 134, cz = 52;
        Fill(cx - 7, cz - 7, 15, 15, Ground.Moss);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.Tau;
            int x = cx + (int)Mathf.Round(Mathf.Cos(a) * 5.5f), z = cz + (int)Mathf.Round(Mathf.Sin(a) * 5.5f);
            var s = Obj(ObjKind.StandingStone, x, z, 1, 1, true, Mathf.RadToDeg(a) + 90);
            s.Name = "Standing stone";
        }
        Obj(ObjKind.Altar, cx, cz, 1, 1).Name = "Dark altar";
        Obj(ObjKind.Cauldron, cx + 2, cz + 1, 1, 1);
        Reserve(cx - 7, cz - 7, 15, 15);
        Spawns("stone_circle");
    }

    void Graveyard()
    {
        Fill(128, 86, 24, 24, Ground.Moss);
        for (int x = 128; x < 152; x++) for (int z = 86; z < 110; z++)
            if (detail.GetNoise2D(x, z) > 0.2f) m.Ground[x, z] = Ground.Grass;
        FenceRect(128, 86, 24, 24, gapSide: 'W', gapAt: 97);
        for (int x = 131; x < 150; x += 3)
            for (int z = 89; z < 108; z += 3)
            {
                if (x >= 139 && x <= 146 && z >= 95 && z <= 106) continue;
                if (R() < 0.8f) Obj(ObjKind.Gravestone, x, z, 1, 1, true, (R() - 0.5f) * 20f).Name = "Gravestone";
            }
        var maus = Obj(ObjKind.Mausoleum, 141, 97, 5, 5);
        maus.Name = "Crypt of the Fallen King"; maus.Action = "Enter"; maus.TargetMap = "crypt"; maus.BlocksLos = true;
        Obj(ObjKind.TreeDead, 133, 104); Obj(ObjKind.TreeDead, 148, 90); Obj(ObjKind.TreeDead, 147, 106);
        Obj(ObjKind.Brazier, 140, 103); Obj(ObjKind.Brazier, 146, 103);
        var sign = Obj(ObjKind.Signpost, 126, 99); sign.Name = "Signpost"; sign.Action = "Read";
        sign.Text = Text("graveyard_sign");
    }

    void BarbarianVillage()
    {
        Fill(68, 124, 28, 24, Ground.Dirt);
        for (int x = 68; x < 96; x++) for (int z = 124; z < 148; z++)
            if (detail.GetNoise2D(x, z) > 0.0f) m.Ground[x, z] = Ground.Grass;
        var hall = Bld("Longhall of Skarn", 75, 127, 12, 8, BuildingStyle.Longhall, (80, 134), (81, 134));
        Furn(hall, ObjKind.RoyalThrone, 80, 128, 0, 2, 1, true, "Jarl's high seat");
        Furn(hall, ObjKind.LongTable, 77, 130, 0, 3, 1, true, "Mead table");
        Furn(hall, ObjKind.LongTable, 82, 130, 0, 3, 1, true, "Mead table");
        foreach (var x in new[] { 77, 79, 82, 84 }) { Furn(hall, ObjKind.Stool, x, 129, 0, 1, 1, false); Furn(hall, ObjKind.Stool, x, 131, 180, 1, 1, false); }
        Furn(hall, ObjKind.Keg, 76, 128); Furn(hall, ObjKind.Keg, 85, 128);
        Furn(hall, ObjKind.WeaponRack, 76, 132, 90, 1, 1, true, "Axe rack");
        Furn(hall, ObjKind.WeaponRack, 85, 132, -90, 1, 1, true, "Axe rack");
        Furn(hall, ObjKind.Fireplace, 76, 130, 90, 1, 1, true, "Hearth");
        FurnishHome(Bld("Raider's hut", 69, 127, 5, 5, BuildingStyle.Longhall, (73, 129)));
        FurnishHome(Bld("Raider's hut", 88, 126, 5, 5, BuildingStyle.Longhall, (88, 128)));
        FurnishHome(Bld("Raider's hut", 70, 140, 5, 5, BuildingStyle.Longhall, (72, 140)));
        FurnishHome(Bld("Raider's hut", 88, 140, 5, 5, BuildingStyle.Longhall, (90, 140)));
        FurnishHome(Bld("Raider's hut", 79, 143, 5, 4, BuildingStyle.Longhall, (81, 143)));
        Obj(ObjKind.Campfire, 81, 137);
        Obj(ObjKind.Brazier, 78, 136); Obj(ObjKind.Brazier, 84, 136);
        Obj(ObjKind.Anvil, 76, 138);
        Obj(ObjKind.Barrel, 86, 137); Obj(ObjKind.Barrel, 87, 137); Obj(ObjKind.Crate, 86, 138);
        Reserve(67, 123, 30, 26);
        Spawns("barbarian_village");
    }

    void BanditCamp()
    {
        Fill(128, 128, 22, 20, Ground.Dirt);
        foreach (var (tx, tz, r) in new[] { (131, 131, 0f), (144, 130, 90f), (132, 143, 180f), (145, 142, 270f) })
            Obj(ObjKind.Tent, tx, tz, 2, 2, true, r).Name = "Bandit tent";
        Obj(ObjKind.Campfire, 138, 137);
        Obj(ObjKind.Crate, 136, 132); Obj(ObjKind.Crate, 137, 132); Obj(ObjKind.Barrel, 140, 143); Obj(ObjKind.Chest, 139, 131).Name = "Locked chest";
        Obj(ObjKind.TorchPost, 134, 136); Obj(ObjKind.TorchPost, 142, 136);
        Reserve(127, 127, 24, 22);
        Spawns("bandit_camp");
    }

    void Bloodmarch()
    {
        var sign = Obj(ObjKind.Signpost, 94, 30); sign.Name = "Warning sign"; sign.Action = "Read";
        sign.Text = Text("bloodmarch_sign");
        for (int x = 2; x < W - 2; x += 3)
            if (m.Ground[x, 28] != Ground.Water && m.Ground[x, 28] != Ground.Dirt && R() < 0.5f)
                Obj(ObjKind.RockSmall, x, 28, 1, 1, false, R() * 360f);
        Scatter(ObjKind.TreeDead, 2, 2, W - 4, 25, 70);
        Scatter(ObjKind.RockLarge, 2, 2, W - 4, 25, 25, true, 2);
        Scatter(ObjKind.BonesPile, 2, 2, W - 4, 25, 25, false);
        Obj(ObjKind.Altar, 60, 10).Name = "Ruined altar";
        Obj(ObjKind.Brazier, 58, 10); Obj(ObjKind.Brazier, 62, 10);
        Spawns("bloodmarch");
    }

    void Forest()
    {
        Scatter(ObjKind.TreePine, 3, 30, 115, 30, 380);
        Scatter(ObjKind.TreeOak, 3, 30, 115, 30, 90);
        Scatter(ObjKind.Bush, 3, 30, 115, 30, 70, false);
        Scatter(ObjKind.RockLarge, 3, 30, 115, 30, 12, true, 2);
        Spawns("forest");
        // east of river forest
        Scatter(ObjKind.TreePine, 124, 30, 34, 15, 60);
    }

    void Countryside()
    {
        Scatter(ObjKind.TreeOak, 3, 60, 112, 97, 110);
        Scatter(ObjKind.TreeWillow, 12, 118, 40, 35, 18);
        Scatter(ObjKind.TreeWillow, 105, 60, 26, 100, 20);
        Scatter(ObjKind.TreePine, 124, 60, 34, 25, 30);
        Scatter(ObjKind.TreeOak, 124, 110, 34, 48, 40);
        Scatter(ObjKind.Bush, 3, 60, 155, 97, 120, false);
        Scatter(ObjKind.RockSmall, 3, 60, 155, 97, 50, false);
        Scatter(ObjKind.RockLarge, 3, 60, 155, 97, 14, true, 2);
        Spawns("countryside");
    }

    void Border()
    {
        for (int i = 0; i < W; i++)
        {
            foreach (var (x, z) in new[] { (i, 0), (i, H - 1), (0, i), (W - 1, i), (i, 1), (i, H - 2), (1, i), (W - 2, i) })
            {
                if (!m.InBounds(x, z) || reserved[x, z]) continue;
                bool wild = z < 28;
                if (m.Ground[x, z] == Ground.Water) { m.Ground[x, z] = Ground.Sand; }
                var bt = Obj(wild ? ObjKind.TreeDead : ObjKind.TreePine, x, z, 1, 1, true, R() * 360f);
                bt.Scale = 1.1f + R() * 0.4f;
                bt.Group = "border";   // the edge of the world can't be felled
            }
        }
    }

    void Heights()
    {
        for (int x = 0; x <= W; x++)
            for (int z = 0; z <= H; z++)
            {
                float h = 1.2f + noise.GetNoise2D(x, z) * 3.2f + detail.GetNoise2D(x, z) * 0.25f;
                if (z < 28) h += 0.8f + Mathf.Abs(noise.GetNoise2D(x * 2, z * 2)) * 2.5f;
                // flatten town and villages
                h = Flatten(h, x, z, 57, 77, 103, 119, 1.5f);
                h = Flatten(h, x, z, 66, 122, 96, 150, 1.2f);
                h = Flatten(h, x, z, 126, 84, 153, 112, 1.3f);
                h = Flatten(h, x, z, 16, 57, 42, 82, 1.4f);
                h = Flatten(h, x, z, 126, 126, 152, 150, 1.1f);
                h = Flatten(h, x, z, 28, 106, 37, 117, 1.3f);   // allotments
                h = Flatten(h, x, z, 97, 61, 110, 75, 1.6f);    // quarry
                h = Flatten(h, x, z, 103, 102, 112, 109, 1.3f); // weaver's cottage
                m.Heights[x, z] = Mathf.Max(h, 0.35f);
            }
        for (int x = 0; x < W; x++)
            for (int z = 0; z < H; z++)
            {
                if (m.Ground[x, z] == Ground.Water)
                {
                    m.Heights[x, z] = m.Heights[x + 1, z] = m.Heights[x, z + 1] = m.Heights[x + 1, z + 1] = -0.7f;
                }
            }
        // Sand next to water gets a gentle bank
        for (int x = 0; x < W; x++)
            for (int z = 0; z < H; z++)
                if (m.Ground[x, z] == Ground.Sand)
                    for (int dx = 0; dx <= 1; dx++) for (int dz = 0; dz <= 1; dz++)
                        if (m.Heights[x + dx, z + dz] > 0.6f) m.Heights[x + dx, z + dz] = Mathf.Lerp(m.Heights[x + dx, z + dz], 0.45f, 0.6f);
        // Bridges are flat decks above the water
        foreach (var o in m.Objects)
            if (o.Kind == ObjKind.Bridge)
                for (int x = o.X; x <= o.X + o.W; x++)
                    for (int z = o.Z; z <= o.Z + o.D; z++)
                        if (m.Ground[Math.Min(x, W - 1), Math.Min(z, H - 1)] != Ground.Water)
                            m.Heights[x, z] = 0.5f;
    }

    void FlattenBuildings()
    {
        foreach (var b in m.Buildings)
        {
            float sum = 0; int n = 0;
            for (int x = b.X; x <= b.X + b.W; x++)
                for (int z = b.Z; z <= b.Z + b.D; z++) { sum += m.Heights[x, z]; n++; }
            float h = sum / n;
            for (int x = b.X - 1; x <= b.X + b.W + 1; x++)
                for (int z = b.Z - 1; z <= b.Z + b.D + 1; z++)
                    if (x >= 0 && z >= 0 && x <= W && z <= H)
                    {
                        bool inside = x >= b.X && x <= b.X + b.W && z >= b.Z && z <= b.Z + b.D;
                        m.Heights[x, z] = inside ? h : Mathf.Lerp(m.Heights[x, z], h, 0.6f);
                    }
        }
    }

    /// Moves any NPC spawn that ended up on a blocked tile to the nearest walkable one.
    public static void FixSpawns(MapData m)
    {
        foreach (var sp in m.Spawns)
        {
            if (m.Walkable(sp.X, sp.Z)) continue;
            for (int r = 1; r < 8; r++)
            {
                bool done = false;
                for (int dx = -r; dx <= r && !done; dx++)
                    for (int dz = -r; dz <= r && !done; dz++)
                        if (m.Walkable(sp.X + dx, sp.Z + dz)) { sp.X += dx; sp.Z += dz; done = true; }
                if (done) break;
            }
        }
    }

    static float Flatten(float h, int x, int z, int x0, int z0, int x1, int z1, float level)
    {
        float dx = Mathf.Max(Mathf.Max(x0 - x, x - x1), 0);
        float dz = Mathf.Max(Mathf.Max(z0 - z, z - z1), 0);
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float t = Mathf.Clamp(d / 6f, 0f, 1f);
        return Mathf.Lerp(level, h, t * t);
    }

    // ---------- gathering & crafting ----------
    void SkillingAreas()
    {
        // ---- Aldmoor Quarry (north-east of town) ----
        Fill(97, 61, 14, 15, Ground.Stone);
        for (int x = 97; x < 111; x++) for (int z = 61; z < 76; z++)
            if (detail.GetNoise2D(x, z) > 0.25f) m.Ground[x, z] = Ground.Dirt;
        Reserve(96, 60, 16, 17);
        foreach (var r in Def.QuarryRocks) Rock(r.X, r.Z, r.Res);
        Obj(ObjKind.Furnace, 105, 67, 1, 1, true, 180).Name = "Quarry furnace";
        Obj(ObjKind.Anvil, 103, 67).Name = "Anvil";
        Obj(ObjKind.Cart, 110, 66, 2, 1, true, 90);
        var qs = Obj(ObjKind.Signpost, 96, 70); qs.Name = "Signpost"; qs.Action = "Read";
        qs.Text = Text("quarry_sign");

        // ---- Allotments (west of the hen coop) ----
        Fill(28, 106, 10, 12, Ground.Grass);
        FenceRect(28, 106, 10, 12, gapSide: 'N', gapAt: 32);
        foreach (var at in Def.Allotments)
        {
            int px = at[0], pz = at[1];
            var patch = Obj(ObjKind.FarmPatch, px, pz, 3, 3, false);
            patch.Name = "Allotment"; patch.Action = "Tend";
            Fill(px, pz, 3, 3, Ground.Dirt);
        }
        var as_ = Obj(ObjKind.Signpost, 31, 104); as_.Name = "Signpost"; as_.Action = "Read";
        as_.Text = Text("allotments_sign");

        // ---- Ilse's weaving cottage and mulberry grove (east of town) ----
        var cot = Bld("Ilse's Weaving Cottage", 104, 103, 8, 6, BuildingStyle.Timber, (104, 106));
        Furn(cot, ObjKind.SpinningWheel, 106, 104, 0, 1, 1, true, "Spinning wheel");
        Furn(cot, ObjKind.Loom, 108, 104, 0, 2, 1, true, "Loom");
        Furn(cot, ObjKind.Bed, 110, 106, 0, 1, 1, true, "Bed");
        Furn(cot, ObjKind.Wardrobe, 110, 104, -90, 1, 1, true, "Silk wardrobe");
        Furn(cot, ObjKind.Candelabra, 105, 107);
        foreach (var at in Def.Mulberries)
        {
            int tx = at[0], tz = at[1];
            if (!Free(tx, tz, 1, 1)) continue;
            var t = Obj(ObjKind.TreeOak, tx, tz, 1, 1, true, R() * 360f);
            t.Res = "mulberry"; t.Name = "Mulberry tree"; t.Scale = 0.42f; t.Tint = new Color(0.9f, 1.05f, 0.8f);
        }

        // ---- Gerd's woodsman lodge (Darkwood's southern edge) ----
        var lodge = Bld("Woodsman's Lodge", 62, 61, 6, 5, BuildingStyle.Longhall, (65, 65));
        Furn(lodge, ObjKind.Fireplace, 63, 62, 0, 1, 1, true, "Hearth");
        Furn(lodge, ObjKind.Bed, 66, 62, 0, 1, 2, true, "Bed");
        Furn(lodge, ObjKind.WeaponRack, 63, 64, 90, 1, 1, true, "Axe rack");
        Obj(ObjKind.Workbench, 69, 64, 2, 1).Name = "Workbench";
        Obj(ObjKind.Crate, 69, 62); Obj(ObjKind.Barrel, 70, 62);
        // Elder trees deep in Darkwood, among the bears.
        int elders = 0;
        for (int tries = 0; tries < 200 && elders < 7; tries++)
        {
            int x = RI(96, 112), z = RI(34, 56);
            if (!Free(x, z, 1, 1)) continue;
            var t = Obj(ObjKind.TreeOak, x, z, 1, 1, true, R() * 360f);
            t.Res = "tree_elder"; t.Name = "Elder tree"; t.Scale = 1.35f; t.Tint = new Color(0.7f, 0.8f, 0.6f);
            elders++;
        }

        // ---- Lake Mirren: Old Maren, a cooking fire and shoals ----
        Obj(ObjKind.Campfire, 42, 137).Name = "Campfire";
        foreach (var f in Def.FishingGroups) FishingSpots(f.Group, f.Res, f.Area[0], f.Area[1], f.Area[2], f.Area[3], f.Count);

        // ---- The Bloodmarch: ember ore and moonwood, for the brave ----
        int placed = 0;
        for (int tries = 0; tries < 300 && placed < 6; tries++)
        {
            int x = RI(40, 80), z = RI(4, 22);
            if (!Free(x, z, 1, 1)) continue;
            Rock(x, z, "rock_ember"); placed++;
        }
        placed = 0;
        for (int tries = 0; tries < 300 && placed < 7; tries++)
        {
            int x = RI(128, 154), z = RI(4, 24);
            if (!Free(x, z, 1, 1)) continue;
            var t = Obj(ObjKind.TreeOak, x, z, 1, 1, true, R() * 360f);
            t.Res = "tree_moonwood"; t.Name = "Moonwood tree"; t.Scale = 1.2f; t.Tint = new Color(0.75f, 0.9f, 1.3f);
            placed++;
        }
        // Essence rocks by the stone circle (hexer territory).
        foreach (var r in Def.CircleRocks)
            if (Free(r.X, r.Z, 1, 1)) Rock(r.X, r.Z, r.Res);
        Spawns("skilling");
    }

    WorldObject Rock(int x, int z, string res)
    {
        var r = ResourceDb.Get(res);
        var o = Obj(ObjKind.OreRock, x, z, 1, 1, true, R() * 360f);
        o.Res = res; o.Name = r.Name; o.Action = "Mine"; o.Tint = r.Tint;
        return o;
    }

    /// Fishing spots on water tiles next to walkable shore; shoals wander between spots of a group.
    void FishingSpots(string group, string res, int x0, int z0, int x1, int z1, int count)
    {
        var cands = new List<Tile>();
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!m.InBounds(x, z) || m.Ground[x, z] != Ground.Water) continue;
                bool shore = false;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (m.InBounds(nx, nz) && m.Ground[nx, nz] != Ground.Water) shore = true;
                }
                if (shore && !m.Objects.Exists(o => o.Kind == ObjKind.Bridge && o.Covers(x, z))) cands.Add(new Tile(x, z));
            }
        var picked = new List<Tile>();
        for (int tries = 0; tries < 400 && picked.Count < count && cands.Count > 0; tries++)
        {
            var t = cands[rng.Next(cands.Count)];
            if (picked.Exists(p => p.Chebyshev(t) < 4)) continue;
            picked.Add(t);
        }
        var def = ResourceDb.Get(res);
        foreach (var t in picked)
        {
            var o = m.AddObject(new WorldObject { Kind = ObjKind.FishingSpot, X = t.X, Z = t.Z, Blocks = false, Name = def.Name, Action = def.Verb, Res = res, Group = group });
            o.Rot = 0;
        }
    }

    /// Makes the scattered trees choppable: pines are pine; broadleaf trees become ash, elm or hornbeam.
    void TagResources()
    {
        foreach (var o in m.Objects)
        {
            if (o.Res != null) { o.Action ??= ResourceDb.Get(o.Res)?.Verb; continue; }
            if (o.Group == "border") continue;
            switch (o.Kind)
            {
                case ObjKind.TreePine: o.Res = "tree_pine"; break;
                case ObjKind.TreeWillow: o.Res = "tree_willow"; break;
                case ObjKind.TreeOak:
                {
                    float h = Hash.Unit(o.X, o.Z, 77);
                    if (o.Z < 60) { o.Res = h < 0.55f ? "tree_ash" : "tree_elm"; }
                    else o.Res = h < 0.55f ? "tree_ash" : h < 0.85f ? "tree_elm" : "tree_hornbeam";
                    o.Tint = o.Res switch
                    {
                        "tree_elm" => new Color(0.92f, 1.0f, 0.85f),
                        "tree_hornbeam" => new Color(0.85f, 0.9f, 0.7f),
                        _ => Colors.White,
                    };
                    break;
                }
                default: continue;
            }
            var r = ResourceDb.Get(o.Res);
            o.Name = r.Name; o.Action = r.Verb;
        }
    }

    /// Station actions on furnaces, anvils, fires, altars and workshop furniture. Shared with dungeons.
    public static void SkillingStations(MapData m)
    {
        foreach (var o in m.Objects)
        {
            (string action, string station) = o.Kind switch
            {
                ObjKind.Furnace => ("Smelt", "smelt"),
                ObjKind.Anvil => ("Smith", "smith"),
                ObjKind.Campfire or ObjKind.Fireplace => ("Cook", "cook"),
                ObjKind.SpinningWheel => ("Spin", "spin"),
                ObjKind.Loom => ("Weave", "weave"),
                ObjKind.EnchantingTable => ("Enchant", "enchant"),
                ObjKind.Workbench => ("Craft", "workbench"),
                ObjKind.Altar when o.Name is "Dark altar" or "Ruined altar" => ("Enchant", "enchant"),
                _ => (null, null),
            };
            if (station == null) continue;
            o.Action = action; o.Station = station;
            o.Name ??= RecipeDb.StationName(station);
        }
    }

    void Labels()
    {
        foreach (var l in Def.Labels) m.Labels.Add(new(l.Text, l.X, l.Z));
    }
}

public enum DungeonTheme { Goblin, Crypt }

static class DungeonBuilder
{
    struct Room { public int X, Z, W, D; public Tile Center => new(X + W / 2, Z + D / 2); }

    /// Rooms, corridors and decor come from the theme and seed; lighting, stock and bosses from world.json.
    public static MapData Build(DungeonDef def)
    {
        string id = def.Id;
        int w = def.Width, h = def.Height;
        var theme = def.Theme;
        var m = new MapData(id, def.Name, w, h) { Underground = true, Music = def.Music };
        var rng = new Random(def.Seed);
        m.Ambient = def.Ambient; m.Fog = def.Fog; m.FogDensity = def.FogDensity;
        for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) m.Ground[x, z] = Ground.Void;

        var rooms = new List<Room>();
        int target = def.Rooms;
        for (int tries = 0; tries < 400 && rooms.Count < target; tries++)
        {
            var r = new Room { W = rng.Next(7, 13), D = rng.Next(7, 13) };
            r.X = rng.Next(2, w - r.W - 2); r.Z = rng.Next(2, h - r.D - 2);
            bool overlap = false;
            foreach (var o in rooms)
                if (r.X - 3 < o.X + o.W && r.X + r.W + 3 > o.X && r.Z - 3 < o.Z + o.D && r.Z + r.D + 3 > o.Z) { overlap = true; break; }
            if (overlap) continue;
            rooms.Add(r);
        }
        // Order rooms along a chain by nearest neighbour so the last room is far from the entrance.
        var ordered = new List<Room> { rooms[0] };
        var left = new List<Room>(rooms.GetRange(1, rooms.Count - 1));
        while (left.Count > 0)
        {
            var last = ordered[^1].Center;
            int bi = 0; int bd = int.MaxValue;
            for (int i = 0; i < left.Count; i++) { int d = left[i].Center.Chebyshev(last); if (d < bd) { bd = d; bi = i; } }
            ordered.Add(left[bi]); left.RemoveAt(bi);
        }
        rooms = ordered;
        // make boss room big
        var bossRoom = rooms[^1];
        foreach (var r in rooms) Carve(m, r.X, r.Z, r.W, r.D);
        for (int i = 0; i + 1 < rooms.Count; i++) Corridor(m, rooms[i].Center, rooms[i + 1].Center, rng);
        // a couple of loops
        if (rooms.Count > 4) Corridor(m, rooms[1].Center, rooms[4].Center, rng);

        // Entrance
        var start = rooms[0];
        var ladder = m.AddObject(new WorldObject { Kind = ObjKind.Ladder, X = start.Center.X, Z = start.Z + 1, Name = "Ladder", Action = "Climb-up", TargetMap = GameConst.OverworldId });
        ladder.TargetX = def.Exit[0]; ladder.TargetZ = def.Exit[1];
        m.Spawn = new Tile(start.Center.X, start.Z + 3);

        // Decor + torches
        foreach (var r in rooms)
        {
            TorchesAround(m, r, rng);
            int decor = rng.Next(1, 4);
            for (int i = 0; i < decor; i++)
            {
                int x = rng.Next(r.X + 1, r.X + r.W - 1), z = rng.Next(r.Z + 1, r.Z + r.D - 1);
                if (Math.Abs(x - r.Center.X) <= 1 && Math.Abs(z - r.Center.Z) <= 1) continue;
                if (m.Objects.Exists(o => o.Covers(x, z))) continue;
                var k = theme == DungeonTheme.Goblin
                    ? (rng.Next(3) switch { 0 => ObjKind.Crate, 1 => ObjKind.Barrel, _ => ObjKind.BonesPile })
                    : (rng.Next(3) switch { 0 => ObjKind.BonesPile, 1 => ObjKind.Gravestone, _ => ObjKind.RockSmall });
                m.AddObject(new WorldObject { Kind = k, X = x, Z = z, Rot = rng.Next(360), Blocks = k != ObjKind.BonesPile && k != ObjKind.RockSmall });
            }
            if (theme == DungeonTheme.Crypt && r.W >= 9 && r.D >= 9)
            {
                foreach (var (px, pz) in new[] { (r.X + 2, r.Z + 2), (r.X + r.W - 3, r.Z + 2), (r.X + 2, r.Z + r.D - 3), (r.X + r.W - 3, r.Z + r.D - 3) })
                    if (!m.Objects.Exists(o => o.Covers(px, pz)))
                        m.AddObject(new WorldObject { Kind = ObjKind.Pillar, X = px, Z = pz, Name = "Pillar", BlocksLos = true });
            }
        }

        // Monsters
        string[] pool = def.Monsters;
        for (int i = 1; i < rooms.Count - 1; i++)
        {
            var r = rooms[i];
            int count = rng.Next(2, 5);
            // later rooms are a bit harder
            for (int c = 0; c < count; c++)
            {
                int x = rng.Next(r.X + 1, r.X + r.W - 1), z = rng.Next(r.Z + 1, r.Z + r.D - 1);
                int pi = Math.Min(pool.Length - 1, rng.Next(0, pool.Length - (i < rooms.Count / 2 ? 2 : 0)));
                m.Spawns.Add(new NpcSpawn(pool[pi], x, z));
            }
        }

        // Ore veins and giant webs: the best materials lie below ground.
        string[] ores = def.Ores;
        int oreIdx = 0, webs = 0;
        for (int i = 1; i < rooms.Count - 1; i++)
        {
            var r = rooms[i];
            for (int k = 0; k < 2; k++)
            {
                // Hug a wall so rocks don't block the room.
                bool alongX = rng.Next(2) == 0;
                int x = alongX ? rng.Next(r.X + 1, r.X + r.W - 1) : (rng.Next(2) == 0 ? r.X : r.X + r.W - 1);
                int z = alongX ? (rng.Next(2) == 0 ? r.Z : r.Z + r.D - 1) : rng.Next(r.Z + 1, r.Z + r.D - 1);
                if (m.Objects.Exists(o => o.Covers(x, z))) continue;
                // Never plug a corridor mouth: skip edge tiles with open ground outside the room.
                bool mouth = false;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    bool outside = nx < r.X || nx >= r.X + r.W || nz < r.Z || nz >= r.Z + r.D;
                    if (outside && m.InBounds(nx, nz) && m.Ground[nx, nz] != Ground.Void) mouth = true;
                }
                if (mouth) continue;
                if (k == 1 && webs < 4 && rng.Next(3) == 0)
                {
                    m.AddObject(new WorldObject { Kind = ObjKind.SpiderWeb, X = x, Z = z, Res = "spider_web", Name = "Giant web", Action = "Collect", Blocks = true });
                    webs++;
                    continue;
                }
                var res = ResourceDb.Get(ores[oreIdx++ % ores.Length]);
                m.AddObject(new WorldObject { Kind = ObjKind.OreRock, X = x, Z = z, Res = res.Id, Name = res.Name, Action = "Mine", Tint = res.Tint, Rot = rng.Next(360) });
            }
        }

        // Boss room
        var bc = bossRoom.Center;
        if (theme == DungeonTheme.Goblin)
        {
            m.AddObject(new WorldObject { Kind = ObjKind.Campfire, X = bc.X, Z = bc.Z - 2, Name = "Campfire" });
            m.AddObject(new WorldObject { Kind = ObjKind.Chest, X = bossRoom.X + 1, Z = bossRoom.Z + 1, Name = "Grukk's hoard" });
        }
        else
        {
            m.AddObject(new WorldObject { Kind = ObjKind.Throne, X = bc.X, Z = bossRoom.Z + 1, Name = "Bone throne", BlocksLos = true });
            m.AddObject(new WorldObject { Kind = ObjKind.Brazier, X = bc.X - 2, Z = bossRoom.Z + 1, Name = "Brazier" });
            m.AddObject(new WorldObject { Kind = ObjKind.Brazier, X = bc.X + 2, Z = bossRoom.Z + 1, Name = "Brazier" });
            m.AddObject(new WorldObject { Kind = ObjKind.Altar, X = bossRoom.X + 1, Z = bossRoom.Z + bossRoom.D - 2, Name = "Altar" });
        }
        foreach (var b in def.Boss) m.Spawns.Add(new NpcSpawn(b.Npc, bc.X + b.X, bc.Z + b.Z));

        m.Labels.Add(new MapLabel("Entrance", start.Center.X, start.Center.Z));
        m.Labels.Add(new MapLabel(def.BossLabel, bc.X, bc.Z));
        OverworldBuilder.SkillingStations(m);
        m.BuildCollision();
        OverworldBuilder.FixSpawns(m);
        return m;
    }

    static void Carve(MapData m, int x0, int z0, int w, int d)
    {
        for (int x = x0; x < x0 + w; x++)
            for (int z = z0; z < z0 + d; z++)
                if (m.InBounds(x, z)) m.Ground[x, z] = Ground.Stone;
    }

    static void Corridor(MapData m, Tile a, Tile b, Random rng)
    {
        bool xFirst = rng.Next(2) == 0;
        var corner = xFirst ? new Tile(b.X, a.Z) : new Tile(a.X, b.Z);
        Line(m, a, corner); Line(m, corner, b);
    }

    static void Line(MapData m, Tile a, Tile b)
    {
        int x = a.X, z = a.Z;
        while (true)
        {
            Carve(m, x, z, 2, 2);
            if (x == b.X && z == b.Z) break;
            x += Math.Sign(b.X - x); z += Math.Sign(b.Z - z);
        }
    }

    static void TorchesAround(MapData m, Room r, Random rng)
    {
        // Torch objects sit on floor tiles adjacent to walls; they don't block.
        foreach (var (x, z) in new[] { (r.X, r.Z + r.D / 2), (r.X + r.W - 1, r.Z + r.D / 2), (r.X + r.W / 2, r.Z), (r.X + r.W / 2, r.Z + r.D - 1) })
        {
            if (rng.Next(3) == 0) continue;
            if (m.Objects.Exists(o => o.Covers(x, z))) continue;
            float rot = x == r.X ? 90 : x == r.X + r.W - 1 ? 270 : z == r.Z ? 0 : 180;
            m.AddObject(new WorldObject { Kind = ObjKind.WallTorch, X = x, Z = z, Blocks = false, Rot = rot });
        }
    }
}
