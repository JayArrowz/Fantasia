using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

/// Builds visuals for world objects: a Higgsfield GLB if one exists, else a procedural stand-in.
public static class Props
{
    // ---------- shared primitive cache (lets MapView batch identical meshes into MultiMeshes) ----------
    static readonly Dictionary<string, Mesh> MeshCache = new();
    static readonly Dictionary<string, Material> MatCache = new();

    public static Material Mat(Color c, float rough = 0.85f, float metal = 0f, float emission = 0f)
    {
        string k = $"{c.ToHtml()}_{rough}_{metal}_{emission}";
        if (MatCache.TryGetValue(k, out var m)) return m;
        var sm = new StandardMaterial3D { AlbedoColor = c, Roughness = rough, Metallic = metal };
        if (emission > 0) { sm.EmissionEnabled = true; sm.Emission = c; sm.EmissionEnergyMultiplier = emission; }
        MatCache[k] = sm;
        return sm;
    }

    public static Material TexMat(string tex, Color fallback, float uv = 0.5f, Color? tint = null)
    {
        string k = $"tex_{tex}_{uv}_{tint?.ToHtml()}";
        if (MatCache.TryGetValue(k, out var m)) return m;
        var sm = Assets.TexturedMaterial(tex, fallback, uv);
        if (tint.HasValue) sm.AlbedoColor = tint.Value;
        MatCache[k] = sm;
        return sm;
    }

    static Mesh Cached(string k, Func<Mesh> make)
    {
        if (!MeshCache.TryGetValue(k, out var m)) { m = make(); MeshCache[k] = m; }
        return m;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = Cached($"box{size}", () => new BoxMesh { Size = size }), MaterialOverride = mat, Position = pos };
        if (rot.HasValue) mi.RotationDegrees = rot.Value;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Cyl(Node3D parent, float rTop, float rBot, float h, Vector3 pos, Material mat, int seg = 10, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = Cached($"cyl{rTop}_{rBot}_{h}_{seg}", () => new CylinderMesh { TopRadius = rTop, BottomRadius = rBot, Height = h, RadialSegments = seg, Rings = 1 }), MaterialOverride = mat, Position = pos };
        if (rot.HasValue) mi.RotationDegrees = rot.Value;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Sphere(Node3D parent, float r, Vector3 pos, Material mat, Vector3? scale = null, int seg = 10)
    {
        var mi = new MeshInstance3D { Mesh = Cached($"sph{r}_{seg}", () => new SphereMesh { Radius = r, Height = r * 2, RadialSegments = seg, Rings = seg / 2 }), MaterialOverride = mat, Position = pos };
        if (scale.HasValue) mi.Scale = scale.Value;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Prism(Node3D parent, Vector3 size, Vector3 pos, Material mat, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = Cached($"prism{size}", () => new PrismMesh { Size = size }), MaterialOverride = mat, Position = pos };
        if (rot.HasValue) mi.RotationDegrees = rot.Value;
        parent.AddChild(mi);
        return mi;
    }

    // ---------- palettes ----------
    static readonly Color Wood = new(0.42f, 0.28f, 0.16f), DarkWood = new(0.25f, 0.16f, 0.09f), Stone = new(0.55f, 0.54f, 0.52f),
        DarkStone = new(0.30f, 0.30f, 0.32f), Leaf = new(0.22f, 0.45f, 0.18f), Pine = new(0.12f, 0.32f, 0.16f), Bone = new(0.88f, 0.85f, 0.75f),
        Thatch = new(0.75f, 0.62f, 0.32f), Roof = new(0.55f, 0.22f, 0.15f), Plaster = new(0.90f, 0.87f, 0.78f), Iron = new(0.25f, 0.25f, 0.27f);

    public static string ModelKey(ObjKind k) => k switch
    {
        ObjKind.TreeOak => "tree_oak", ObjKind.TreePine => "tree_pine", ObjKind.TreeDead => "tree_dead", ObjKind.TreeWillow => "tree_willow",
        ObjKind.Bush => "bush", ObjKind.RockLarge => "rock_large", ObjKind.RockSmall => "rock_small",
        ObjKind.HouseTimber => "house_timber", ObjKind.HouseStone => "house_stone", ObjKind.Keep => "castle_keep", ObjKind.Tower => "castle_tower",
        ObjKind.Stall => "market_stall", ObjKind.BankBooth => "bank_booth", ObjKind.Well => "well", ObjKind.Fence => "fence",
        ObjKind.Barrel => "barrel", ObjKind.Crate => "crate", ObjKind.Haystack => "haystack", ObjKind.TorchPost => "torch_post",
        ObjKind.Brazier => "brazier", ObjKind.Tent => "tent", ObjKind.CaveEntrance => "cave_entrance", ObjKind.Mausoleum => "mausoleum",
        ObjKind.Gravestone => "gravestone", ObjKind.StandingStone => "standing_stone", ObjKind.Ladder => "ladder", ObjKind.Anvil => "anvil",
        ObjKind.Cart => "cart", ObjKind.Fountain => "fountain", ObjKind.Statue => "statue_knight", ObjKind.Altar => "altar",
        ObjKind.BonesPile => "bones_pile", ObjKind.Pillar => "dungeon_pillar", ObjKind.Chest => "chest", ObjKind.Throne => "throne",
        ObjKind.Cauldron => "cauldron", ObjKind.Campfire => "campfire", ObjKind.Signpost => "signpost", ObjKind.Banner => "banner",
        ObjKind.Bed => "bed", ObjKind.Table => "table", ObjKind.Chair => "chair", ObjKind.Stool => "stool", ObjKind.Bookshelf => "bookshelf",
        ObjKind.Fireplace => "fireplace", ObjKind.Counter => "counter", ObjKind.Furnace => "furnace", ObjKind.WeaponRack => "weapon_rack",
        ObjKind.ArmorStand => "armor_stand", ObjKind.Keg => "keg", ObjKind.ShelfPotions => "shelf_potions", ObjKind.Rug => "rug",
        ObjKind.Candelabra => "candelabra", ObjKind.RoyalThrone => "throne_royal", ObjKind.LongTable => "long_table", ObjKind.Wardrobe => "wardrobe",
        ObjKind.OreRock => "ore_rock", ObjKind.SpinningWheel => "spinning_wheel", ObjKind.Loom => "loom", ObjKind.EnchantingTable => "enchanting_table",
        ObjKind.Workbench => "workbench",
        _ => null,
    };

    /// Approximate visual height for picking.
    public static float PickHeight(WorldObject o) => o.Kind switch
    {
        ObjKind.TreeOak or ObjKind.TreeWillow => 5.5f, ObjKind.TreePine => 7f, ObjKind.TreeDead => 4.5f,
        ObjKind.Keep => 10f, ObjKind.Tower => 9f, ObjKind.HouseStone => 6f, ObjKind.HouseTimber => 5f, ObjKind.CastleWall => 3.6f,
        ObjKind.Mausoleum => 5f, ObjKind.CaveEntrance => 4f, ObjKind.Statue => 3.2f, ObjKind.StandingStone => 3f, ObjKind.Pillar => 3.5f,
        ObjKind.Throne => 2.6f, ObjKind.Ladder => 3f, ObjKind.Banner => 3.5f, ObjKind.Stall => 2.6f, ObjKind.TorchPost => 2.2f,
        ObjKind.Signpost => 2f, ObjKind.Fountain => 2f, ObjKind.Well => 2.5f,
        ObjKind.Bookshelf or ObjKind.Wardrobe => 2.1f, ObjKind.Fireplace or ObjKind.Furnace => 1.6f, ObjKind.RoyalThrone => 2.2f,
        ObjKind.ArmorStand or ObjKind.WeaponRack or ObjKind.Candelabra => 1.8f, ObjKind.Rug => 0.1f,
        ObjKind.FishingSpot => 0.6f, ObjKind.FarmPatch => 0.5f, ObjKind.SpinningWheel or ObjKind.Loom => 1.5f, ObjKind.SpiderWeb => 2f,
        _ => 1.2f,
    } * o.Scale;

    /// Kinds with lights/particles are kept as individual nodes rather than batched.
    static bool IsFire(ObjKind k) => k is ObjKind.TorchPost or ObjKind.Brazier or ObjKind.Campfire or ObjKind.WallTorch
        or ObjKind.Fireplace or ObjKind.Candelabra or ObjKind.Furnace;

    static readonly System.Collections.Generic.Dictionary<(Material, Color), Material> tinted = new();

    /// Tints a model with shared per-(material, colour) overrides so tinted props still batch.
    static void TintBatchable(Node3D model, Color tint)
    {
        foreach (var mi in Assets.AllMeshes(model))
        {
            if (mi.Mesh == null || mi.Mesh.GetSurfaceCount() == 0) continue;
            var src = mi.GetActiveMaterial(0);
            if (src is not BaseMaterial3D bm) continue;
            if (!tinted.TryGetValue((src, tint), out var m))
            {
                var copy = (BaseMaterial3D)bm.Duplicate();
                copy.AlbedoColor = copy.AlbedoColor * tint;
                tinted[(src, tint)] = m = copy;
            }
            mi.MaterialOverride = m;
        }
    }

    /// Glinting crystals in the ore's colour so each rock type reads at a glance.
    static void OreVeins(Node3D root, WorldObject o)
    {
        var mat = Mat(o.Tint, 0.25f, o.Res == "rock_coal" ? 0.1f : 0.6f, o.Res is "rock_essence" or "rock_ember" or "rock_azurite" ? 0.9f : 0.25f);
        var holder = new Node3D { Name = "Veins" };
        root.AddChild(holder);
        // Clusters of crystals jutting from the boulder's shoulders and top.
        for (int i = 0; i < 7; i++)
        {
            float a = (i / 7f + Hash.Unit(o.X, o.Z, i) * 0.1f) * Mathf.Tau;
            bool top = i == 0;
            float rr = top ? 0.05f : 0.4f + Hash.Unit(o.X, o.Z, i + 9) * 0.1f;
            float h = top ? 1.0f : 0.45f + Hash.Unit(o.X, o.Z, i + 3) * 0.35f;
            float sz = 0.15f + Hash.Unit(o.X, o.Z, i + 7) * 0.08f;
            var tilt = top ? Vector3.Zero : new Vector3(Mathf.RadToDeg(Mathf.Sin(a)) * 0.6f, 0, -Mathf.RadToDeg(Mathf.Cos(a)) * 0.6f);
            Prism(holder, new Vector3(sz, sz * 2.2f, sz), new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr), mat, tilt + new Vector3(0, Mathf.RadToDeg(a), 0));
        }
    }

    public static bool IsDynamic(ObjKind k) => k is ObjKind.TorchPost or ObjKind.Brazier or ObjKind.Campfire or ObjKind.WallTorch
        or ObjKind.Fireplace or ObjKind.Candelabra or ObjKind.Furnace or ObjKind.FishingSpot or ObjKind.EnchantingTable or ObjKind.OreRock;

    public static Node3D Build(WorldObject o, MapData map)
    {
        var root = new Node3D { Name = $"{o.Kind}_{o.Id}" };
        float cx = o.X + o.W * 0.5f, cz = o.Z + o.D * 0.5f;
        float y = MinHeight(map, o);
        root.Position = new Vector3(cx, y, cz);
        root.RotationDegrees = new Vector3(0, o.Rot, 0);

        var model = TryModel(o);
        if (model != null && o.Tint != Colors.White && (o.Res != null || o.Kind == ObjKind.OreRock))
            TintBatchable(model, o.Kind == ObjKind.OreRock ? Colors.White.Lerp(o.Tint, 0.25f) : o.Tint);
        if (o.Kind == ObjKind.OreRock) OreVeins(root, o);
        if (model != null)
        {
            if (FrontFacesX(o.Kind)) model.RotationDegrees = new Vector3(0, -90, 0);
            else AlignLongAxis(o, model);
            root.AddChild(model);
        }
        else BuildProcedural(root, o);

        if (IsFire(o.Kind)) AddFire(root, o.Kind);
        if (o.Kind == ObjKind.EnchantingTable)
            root.AddChild(new OmniLight3D { LightColor = new Color(0.55f, 0.6f, 1f), LightEnergy = 1.2f, OmniRange = 3.5f, Position = new Vector3(0, 1.3f, 0) });
        return root;
    }

    /// Generated furniture models have their front on local +X; these kinds are turned so the front
    /// faces the object's +Z, i.e. Rot 0 = back against a north wall, facing into the room.
    static bool FrontFacesX(ObjKind k) => k is ObjKind.Bed or ObjKind.Chair or ObjKind.Bookshelf or ObjKind.Fireplace
        or ObjKind.Furnace or ObjKind.WeaponRack or ObjKind.ArmorStand or ObjKind.Keg or ObjKind.ShelfPotions
        or ObjKind.Wardrobe or ObjKind.RoyalThrone or ObjKind.Throne or ObjKind.BankBooth or ObjKind.Chest;

    /// Rotates elongated furniture models so their long side matches a non-square footprint.
    static void AlignLongAxis(WorldObject o, Node3D model)
    {
        if (o.Kind == ObjKind.Fence)
        {
            // Fence segments are laid out along their local X axis.
            var fb = Assets.ModelBounds(ModelKey(o.Kind));
            if (fb.Size.Z > fb.Size.X) model.RotationDegrees = new Vector3(0, 90, 0);
            return;
        }
        if (o.W == o.D || o.Kind == ObjKind.Rug) return;
        var key = ModelKey(o.Kind);
        var b = Assets.ModelBounds(key);
        if (b.Size.X <= 0 || b.Size.Z <= 0 || Mathf.Abs(b.Size.X - b.Size.Z) < 0.05f * Mathf.Max(b.Size.X, b.Size.Z)) return;
        bool worldLongZ = o.D > o.W;
        bool localLongX = b.Size.X >= b.Size.Z;
        bool quarter = Mathf.Abs(Mathf.Sin(Mathf.DegToRad(o.Rot))) > 0.7f;
        bool modelLongWorldZ = localLongX ? quarter : !quarter;
        if (modelLongWorldZ != worldLongZ) model.RotationDegrees = new Vector3(0, 90, 0);
    }

    static float MinHeight(MapData map, WorldObject o)
    {
        float min = float.MaxValue;
        for (int x = o.X; x <= o.X + o.W; x++)
            for (int z = o.Z; z <= o.Z + o.D; z++)
                if (x <= map.W && z <= map.H) min = Mathf.Min(min, map.Heights[Math.Min(x, map.W), Math.Min(z, map.H)]);
        return min == float.MaxValue ? 0 : min;
    }

    static Node3D TryModel(WorldObject o)
    {
        if (o.Kind is ObjKind.CastleWall or ObjKind.WallTorch or ObjKind.Bridge) return null;
        var key = ModelKey(o.Kind);
        // Broadleaf trees pick a model by species (the old oak model had a grass disc at its base).
        if (o.Kind == ObjKind.TreeOak)
        {
            var species = o.Res == "tree_ash" ? "tree_ash" : "tree_elm";
            if (Assets.HasModel(species)) key = species;
        }
        if (key == null || !Assets.HasModel(key)) return null;
        float s = o.Scale;
        Node3D m = o.Kind switch
        {
            ObjKind.TreeOak => Assets.Model(key, (key == "tree_ash" ? 6.2f : 5.6f) * s),
            ObjKind.OreRock => Assets.Model(key, 0, 1.15f * s),
            ObjKind.SpinningWheel => Assets.Model(key, 1.35f),
            ObjKind.Loom => Assets.Model(key, 0, Mathf.Max(o.W, o.D) * 0.95f),
            ObjKind.EnchantingTable => Assets.Model(key, 0, 1.0f),
            ObjKind.Workbench => Assets.Model(key, 0, Mathf.Max(o.W, o.D) * 0.95f),
            ObjKind.TreePine => Assets.Model(key, 7f * s),
            ObjKind.TreeDead => Assets.Model(key, 4.5f * s),
            ObjKind.TreeWillow => Assets.Model(key, 5.5f * s),
            ObjKind.Bush => Assets.Model(key, 1.1f * s),
            ObjKind.RockLarge => Assets.Model(key, 0, 2.1f * s),
            ObjKind.RockSmall => Assets.Model(key, 0, 0.8f * s),
            ObjKind.Fence => Assets.Model(key, 0, 1.05f),
            ObjKind.Barrel => Assets.Model(key, 1.0f),
            ObjKind.Crate => Assets.Model(key, 0.9f),
            ObjKind.Haystack => Assets.Model(key, 1.4f),
            ObjKind.TorchPost => Assets.Model(key, 2.2f),
            ObjKind.Brazier => Assets.Model(key, 1.1f),
            ObjKind.Gravestone => Assets.Model(key, 1.1f),
            ObjKind.StandingStone => Assets.Model(key, 3.0f),
            ObjKind.Ladder => Assets.Model(key, 3.0f),
            ObjKind.Anvil => Assets.Model(key, 0.8f),
            ObjKind.Statue => Assets.Model(key, 3.2f),
            ObjKind.Altar => Assets.Model(key, 1.1f),
            ObjKind.Pillar => Assets.Model(key, 3.6f),
            ObjKind.Chest => Assets.Model(key, 0.8f),
            ObjKind.Throne => Assets.Model(key, 2.6f),
            ObjKind.Cauldron => Assets.Model(key, 0.9f),
            ObjKind.Signpost => Assets.Model(key, 2.0f),
            ObjKind.Banner => Assets.Model(key, 3.5f),
            ObjKind.BankBooth => Assets.Model(key, 0, 1.0f),
            ObjKind.Campfire => Assets.Model(key, 0, 1.0f),
            ObjKind.Tower => Assets.Model(key, 0, 3.2f),
            ObjKind.Bed => Assets.Model(key, 0, 2.0f),
            ObjKind.Table => Assets.Model(key, 0.8f),
            ObjKind.Chair => Assets.Model(key, 1.0f),
            ObjKind.Stool => Assets.Model(key, 0.5f),
            ObjKind.Bookshelf => Assets.Model(key, 2.1f),
            ObjKind.Wardrobe => Assets.Model(key, 2.0f),
            ObjKind.Fireplace => Assets.Model(key, 0, Mathf.Max(o.W, o.D) * 0.95f),
            ObjKind.Counter => Assets.Model(key, 0, Mathf.Max(o.W, o.D) * 1.0f),
            ObjKind.Furnace => Assets.Model(key, 0, 1.9f),
            ObjKind.WeaponRack => Assets.Model(key, 1.7f),
            ObjKind.ArmorStand => Assets.Model(key, 1.8f),
            ObjKind.Keg => Assets.Model(key, 1.1f),
            ObjKind.ShelfPotions => Assets.Model(key, 0, 1.0f),
            ObjKind.Rug => Assets.ModelFlat(key, Mathf.Max(o.W, o.D) * 0.95f),
            ObjKind.Candelabra => Assets.Model(key, 1.7f),
            ObjKind.RoyalThrone => Assets.Model(key, 2.2f),
            ObjKind.LongTable => Assets.Model(key, 0, Mathf.Max(o.W, o.D) * 1.0f),
            _ => Assets.Model(key, 0, Mathf.Min(o.W, o.D) * 1.0f),
        };
        return m;
    }

    // ---------- procedural fallbacks ----------
    static void BuildProcedural(Node3D r, WorldObject o)
    {
        float s = o.Scale;
        float w = o.W, d = o.D;
        switch (o.Kind)
        {
            case ObjKind.TreeOak:
            case ObjKind.TreeWillow:
                Cyl(r, 0.18f * s, 0.3f * s, 2.4f * s, new Vector3(0, 1.2f * s, 0), TexMat("bark", Wood, 1f));
                var lm = TexMat("leaves", Leaf, 0.6f, o.Kind == ObjKind.TreeWillow ? new Color(0.75f, 0.95f, 0.6f) : null);
                Sphere(r, 1.6f * s, new Vector3(0, 3.4f * s, 0), lm, new Vector3(1, 0.8f, 1));
                Sphere(r, 1.1f * s, new Vector3(0.9f * s, 3.0f * s, 0.3f * s), lm);
                Sphere(r, 1.0f * s, new Vector3(-0.8f * s, 3.2f * s, -0.4f * s), lm);
                break;
            case ObjKind.TreePine:
                Cyl(r, 0.12f * s, 0.22f * s, 2f * s, new Vector3(0, 1f * s, 0), TexMat("bark", Wood, 1f));
                var pm = TexMat("leaves", Pine, 0.6f, new Color(0.55f, 0.75f, 0.6f));
                for (int i = 0; i < 3; i++)
                    Cyl(r, 0.05f, (1.5f - i * 0.4f) * s, 2.2f * s, new Vector3(0, (2.2f + i * 1.4f) * s, 0), pm, 8);
                break;
            case ObjKind.TreeDead:
                Cyl(r, 0.1f * s, 0.28f * s, 3.5f * s, new Vector3(0, 1.75f * s, 0), Mat(DarkWood));
                Cyl(r, 0.04f, 0.1f * s, 1.6f * s, new Vector3(0.5f * s, 3f * s, 0), Mat(DarkWood), 6, new Vector3(0, 0, -45));
                Cyl(r, 0.04f, 0.08f * s, 1.3f * s, new Vector3(-0.4f * s, 2.5f * s, 0.2f * s), Mat(DarkWood), 6, new Vector3(20, 0, 50));
                break;
            case ObjKind.Bush:
                Sphere(r, 0.6f * s, new Vector3(0, 0.45f * s, 0), TexMat("leaves", Leaf, 0.8f), new Vector3(1.2f, 0.8f, 1.1f), 8);
                break;
            case ObjKind.RockLarge:
                Sphere(r, 1f * s, new Vector3(0, 0.5f * s, 0), TexMat("rock", Stone, 0.5f), new Vector3(1.1f, 0.9f, 0.95f), 7);
                break;
            case ObjKind.RockSmall:
                Sphere(r, 0.35f * s, new Vector3(0, 0.15f, 0), TexMat("rock", Stone, 0.5f), new Vector3(1.2f, 0.6f, 1f), 6);
                break;
            case ObjKind.CastleWall:
            {
                var sm = TexMat("stone_wall", Stone, 0.35f);
                Box(r, new Vector3(1.02f, 3.2f, 1.02f), new Vector3(0, 1.1f, 0), sm);
                Box(r, new Vector3(0.4f, 0.6f, 1.02f), new Vector3(-0.28f, 3.0f, 0), sm);
                Box(r, new Vector3(0.4f, 0.6f, 1.02f), new Vector3(0.28f, 3.0f, 0), sm);
                break;
            }
            case ObjKind.Tower:
            {
                var sm = TexMat("stone_wall", Stone, 0.35f);
                Cyl(r, 1.5f, 1.7f, 7f, new Vector3(0, 3f, 0), sm, 12);
                Cyl(r, 1.8f, 1.8f, 0.6f, new Vector3(0, 6.8f, 0), sm, 12);
                Cyl(r, 0.02f, 2.0f, 3f, new Vector3(0, 8.6f, 0), TexMat("roof_tiles", Roof, 0.5f, new Color(0.5f, 0.6f, 1.0f)), 12);
                Cyl(r, 0.03f, 0.03f, 1.5f, new Vector3(0, 10.5f, 0), Mat(DarkWood));
                Box(r, new Vector3(0.8f, 0.5f, 0.03f), new Vector3(0.4f, 10.9f, 0), Mat(new Color(0.7f, 0.1f, 0.1f)));
                break;
            }
            case ObjKind.Keep:
            {
                var sm = TexMat("stone_wall", Stone, 0.3f);
                Box(r, new Vector3(w - 2f, 8f, d - 2f), new Vector3(0, 3.5f, 0), sm);
                for (float x = -w / 2 + 1.2f; x <= w / 2 - 1.2f; x += 1f)
                {
                    Box(r, new Vector3(0.5f, 0.7f, 0.5f), new Vector3(x, 7.85f, -d / 2 + 1.2f), sm);
                    Box(r, new Vector3(0.5f, 0.7f, 0.5f), new Vector3(x, 7.85f, d / 2 - 1.2f), sm);
                }
                foreach (var (tx, tz) in new[] { (-w / 2 + 1.2f, -d / 2 + 1.2f), (w / 2 - 1.2f, -d / 2 + 1.2f), (-w / 2 + 1.2f, d / 2 - 1.2f), (w / 2 - 1.2f, d / 2 - 1.2f) })
                {
                    Cyl(r, 1.3f, 1.4f, 10f, new Vector3(tx, 4.5f, tz), sm, 12);
                    Cyl(r, 0.02f, 1.6f, 2.6f, new Vector3(tx, 10.8f, tz), TexMat("roof_tiles", Roof, 0.5f, new Color(0.45f, 0.55f, 1f)), 12);
                }
                Box(r, new Vector3(2.4f, 3.2f, 0.3f), new Vector3(0, 1.1f, d / 2 - 0.9f), Mat(DarkWood));
                Cyl(r, 0.04f, 0.04f, 3f, new Vector3(0, 9f, 0), Mat(DarkWood));
                Box(r, new Vector3(1.6f, 1f, 0.04f), new Vector3(0.8f, 10f, 0), Mat(new Color(0.75f, 0.12f, 0.1f)));
                break;
            }
            case ObjKind.HouseTimber:
            case ObjKind.HouseStone:
            {
                bool stone = o.Kind == ObjKind.HouseStone;
                float hh = stone ? 4f : 2.8f;
                var wall = stone ? TexMat("stone_wall", Stone, 0.4f) : TexMat("plaster", Plaster, 0.4f);
                Box(r, new Vector3(w - 0.3f, hh, d - 0.3f), new Vector3(0, hh / 2 - 0.2f, 0), wall);
                if (!stone)
                {
                    var beam = Mat(DarkWood);
                    foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                        Box(r, new Vector3(0.18f, hh, 0.18f), new Vector3(sx * (w / 2 - 0.2f), hh / 2 - 0.2f, sz * (d / 2 - 0.2f)), beam);
                    Box(r, new Vector3(w - 0.2f, 0.16f, d - 0.2f), new Vector3(0, hh * 0.55f, 0), beam);
                }
                var roof = stone ? TexMat("roof_tiles", Roof, 0.5f) : TexMat("thatch_roof", Thatch, 0.5f);
                Prism(r, new Vector3(w + 0.4f, 2.2f, d + 0.3f), new Vector3(0, hh + 0.9f, 0), roof);
                Box(r, new Vector3(0.9f, 1.8f, 0.1f), new Vector3(0, 0.7f, d / 2 - 0.12f), Mat(DarkWood));
                Box(r, new Vector3(0.6f, 0.6f, 0.08f), new Vector3(w / 4, hh * 0.6f, d / 2 - 0.12f), Mat(new Color(0.9f, 0.75f, 0.35f), 0.5f, 0, 0.3f));
                Box(r, new Vector3(0.5f, 1.5f, 0.5f), new Vector3(w / 2 - 0.8f, hh + 1.2f, 0), TexMat("stone_wall", Stone, 0.5f));
                break;
            }
            case ObjKind.Stall:
            {
                var post = Mat(Wood);
                foreach (var (px, pz) in new[] { (-1.3f, -0.8f), (1.3f, -0.8f), (-1.3f, 0.8f), (1.3f, 0.8f) })
                    Cyl(r, 0.06f, 0.06f, 2.3f, new Vector3(px, 1.15f, pz), post, 6);
                Box(r, new Vector3(2.7f, 0.9f, 0.8f), new Vector3(0, 0.45f, 0.5f), TexMat("wood_planks", Wood, 0.8f));
                Prism(r, new Vector3(2.9f, 0.6f, 1.9f), new Vector3(0, 2.55f, 0), Mat(o.Tint));
                Box(r, new Vector3(2.9f, 0.05f, 1.9f), new Vector3(0, 2.25f, 0), Mat(new Color(0.95f, 0.92f, 0.85f)));
                for (int i = 0; i < 4; i++)
                    Sphere(r, 0.14f, new Vector3(-0.9f + i * 0.6f, 0.98f, 0.5f), Mat(new Color(0.8f - i * 0.15f, 0.3f + i * 0.1f, 0.2f)), null, 6);
                break;
            }
            case ObjKind.BankBooth:
                Box(r, new Vector3(1f, 1.1f, 0.7f), new Vector3(0, 0.55f, 0), TexMat("wood_planks", DarkWood, 0.8f));
                Box(r, new Vector3(1.04f, 0.08f, 0.78f), new Vector3(0, 1.12f, 0), Mat(new Color(0.85f, 0.68f, 0.25f), 0.35f, 0.8f));
                Box(r, new Vector3(0.06f, 0.9f, 0.06f), new Vector3(-0.45f, 1.6f, -0.2f), Mat(new Color(0.85f, 0.68f, 0.25f), 0.35f, 0.8f));
                Box(r, new Vector3(0.06f, 0.9f, 0.06f), new Vector3(0.45f, 1.6f, -0.2f), Mat(new Color(0.85f, 0.68f, 0.25f), 0.35f, 0.8f));
                Box(r, new Vector3(1f, 0.1f, 0.1f), new Vector3(0, 2.05f, -0.2f), Mat(new Color(0.85f, 0.68f, 0.25f), 0.35f, 0.8f));
                break;
            case ObjKind.Well:
                Cyl(r, 0.8f, 0.85f, 0.9f, new Vector3(0, 0.45f, 0), TexMat("stone_wall", Stone, 0.6f), 12);
                Cyl(r, 0.62f, 0.62f, 0.05f, new Vector3(0, 0.8f, 0), Mat(new Color(0.1f, 0.2f, 0.3f), 0.1f));
                Box(r, new Vector3(0.1f, 1.8f, 0.1f), new Vector3(-0.75f, 1.3f, 0), Mat(Wood));
                Box(r, new Vector3(0.1f, 1.8f, 0.1f), new Vector3(0.75f, 1.3f, 0), Mat(Wood));
                Prism(r, new Vector3(2f, 0.7f, 1.4f), new Vector3(0, 2.45f, 0), TexMat("roof_tiles", Roof, 0.5f));
                break;
            case ObjKind.Fence:
                Box(r, new Vector3(0.12f, 1.1f, 0.12f), new Vector3(-0.45f, 0.55f, 0), Mat(Wood));
                Box(r, new Vector3(0.12f, 1.1f, 0.12f), new Vector3(0.45f, 0.55f, 0), Mat(Wood));
                Box(r, new Vector3(1.05f, 0.1f, 0.06f), new Vector3(0, 0.8f, 0), Mat(Wood));
                Box(r, new Vector3(1.05f, 0.1f, 0.06f), new Vector3(0, 0.45f, 0), Mat(Wood));
                break;
            case ObjKind.Barrel:
                Cyl(r, 0.35f, 0.35f, 0.95f, new Vector3(0, 0.48f, 0), TexMat("wood_planks", Wood, 1f), 10);
                Cyl(r, 0.37f, 0.37f, 0.06f, new Vector3(0, 0.2f, 0), Mat(Iron, 0.5f, 0.6f), 10);
                Cyl(r, 0.37f, 0.37f, 0.06f, new Vector3(0, 0.76f, 0), Mat(Iron, 0.5f, 0.6f), 10);
                break;
            case ObjKind.Crate:
                Box(r, new Vector3(0.85f, 0.85f, 0.85f), new Vector3(0, 0.43f, 0), TexMat("wood_planks", Wood, 1f));
                break;
            case ObjKind.Haystack:
                Sphere(r, 0.8f, new Vector3(0, 0.45f, 0), TexMat("thatch_roof", Thatch, 0.8f), new Vector3(1, 0.8f, 1), 8);
                break;
            case ObjKind.TorchPost:
                Cyl(r, 0.06f, 0.08f, 2f, new Vector3(0, 1f, 0), Mat(DarkWood), 6);
                Cyl(r, 0.12f, 0.06f, 0.25f, new Vector3(0, 2.05f, 0), Mat(Iron, 0.5f, 0.6f), 6);
                break;
            case ObjKind.WallTorch:
                Box(r, new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0, 1.8f, -0.42f), Mat(Iron, 0.5f, 0.6f), new Vector3(-25, 0, 0));
                break;
            case ObjKind.Brazier:
                Cyl(r, 0.4f, 0.2f, 0.4f, new Vector3(0, 0.9f, 0), Mat(Iron, 0.5f, 0.7f), 8);
                Cyl(r, 0.05f, 0.05f, 0.8f, new Vector3(0, 0.4f, 0), Mat(Iron, 0.5f, 0.7f), 6);
                break;
            case ObjKind.Campfire:
                for (int i = 0; i < 4; i++)
                    Cyl(r, 0.07f, 0.07f, 0.9f, new Vector3(0, 0.1f, 0), Mat(DarkWood), 6, new Vector3(80, i * 45, 0));
                for (int i = 0; i < 8; i++)
                    Sphere(r, 0.12f, new Vector3(Mathf.Cos(i * 0.785f) * 0.5f, 0.06f, Mathf.Sin(i * 0.785f) * 0.5f), Mat(DarkStone), null, 6);
                break;
            case ObjKind.Tent:
                Prism(r, new Vector3(1.9f, 1.6f, 1.9f), new Vector3(0, 0.8f, 0), Mat(new Color(0.45f, 0.35f, 0.22f)));
                Box(r, new Vector3(0.5f, 0.9f, 0.02f), new Vector3(0, 0.45f, 0.96f), Mat(new Color(0.1f, 0.08f, 0.05f)));
                break;
            case ObjKind.CaveEntrance:
            {
                var rm = TexMat("rock", DarkStone, 0.4f);
                Sphere(r, 1.8f, new Vector3(-1.2f, 1.3f, 0), rm, new Vector3(1, 1.3f, 1), 8);
                Sphere(r, 1.8f, new Vector3(1.2f, 1.3f, 0), rm, new Vector3(1, 1.3f, 1), 8);
                Sphere(r, 2.0f, new Vector3(0, 2.8f, -0.3f), rm, new Vector3(1.3f, 0.7f, 1), 8);
                Box(r, new Vector3(1.4f, 2.0f, 0.1f), new Vector3(0, 1.0f, 1.5f), Mat(new Color(0.02f, 0.02f, 0.02f), 1f));
                break;
            }
            case ObjKind.Mausoleum:
            {
                var sm = TexMat("stone_wall", DarkStone, 0.4f, new Color(0.7f, 0.7f, 0.75f));
                Box(r, new Vector3(4.2f, 3.2f, 4.2f), new Vector3(0, 1.6f, 0), sm);
                Prism(r, new Vector3(4.6f, 1.4f, 4.6f), new Vector3(0, 3.9f, 0), sm);
                foreach (var px in new[] { -1.6f, -0.6f, 0.6f, 1.6f })
                    Cyl(r, 0.18f, 0.2f, 3.2f, new Vector3(px, 1.6f, 2.3f), sm, 8);
                Box(r, new Vector3(1.2f, 2.2f, 0.1f), new Vector3(0, 1.1f, 2.12f), Mat(new Color(0.02f, 0.02f, 0.03f), 1f));
                break;
            }
            case ObjKind.Gravestone:
                Box(r, new Vector3(0.6f, 0.9f, 0.18f), new Vector3(0, 0.45f, 0), TexMat("rock", Stone, 0.8f));
                Box(r, new Vector3(0.6f, 0.05f, 1.2f), new Vector3(0, 0.02f, 0.6f), Mat(new Color(0.35f, 0.3f, 0.22f)));
                break;
            case ObjKind.StandingStone:
                Box(r, new Vector3(0.9f, 3f, 0.6f), new Vector3(0, 1.4f, 0), TexMat("rock", Stone, 0.5f), new Vector3(0, 0, 3));
                break;
            case ObjKind.Ladder:
                Box(r, new Vector3(0.08f, 3f, 0.08f), new Vector3(-0.3f, 1.5f, 0), Mat(Wood), new Vector3(-12, 0, 0));
                Box(r, new Vector3(0.08f, 3f, 0.08f), new Vector3(0.3f, 1.5f, 0), Mat(Wood), new Vector3(-12, 0, 0));
                for (int i = 0; i < 7; i++)
                    Box(r, new Vector3(0.6f, 0.06f, 0.06f), new Vector3(0, 0.3f + i * 0.4f, -0.06f - i * 0.085f), Mat(Wood));
                break;
            case ObjKind.Anvil:
                Box(r, new Vector3(0.3f, 0.45f, 0.3f), new Vector3(0, 0.22f, 0), Mat(DarkWood));
                Box(r, new Vector3(0.7f, 0.25f, 0.3f), new Vector3(0, 0.57f, 0), Mat(Iron, 0.4f, 0.8f));
                break;
            case ObjKind.Cart:
                Box(r, new Vector3(1.8f, 0.5f, 1f), new Vector3(0, 0.75f, 0), TexMat("wood_planks", Wood, 1f));
                Cyl(r, 0.45f, 0.45f, 0.1f, new Vector3(0, 0.45f, 0.55f), Mat(DarkWood), 10, new Vector3(90, 0, 0));
                Cyl(r, 0.45f, 0.45f, 0.1f, new Vector3(0, 0.45f, -0.55f), Mat(DarkWood), 10, new Vector3(90, 0, 0));
                break;
            case ObjKind.Fountain:
                Cyl(r, 1.4f, 1.5f, 0.6f, new Vector3(0, 0.3f, 0), TexMat("stone_wall", Stone, 0.6f), 14);
                Cyl(r, 1.25f, 1.25f, 0.05f, new Vector3(0, 0.55f, 0), Mat(new Color(0.25f, 0.45f, 0.65f), 0.05f));
                Cyl(r, 0.2f, 0.25f, 1.6f, new Vector3(0, 1.2f, 0), TexMat("stone_wall", Stone, 0.6f), 8);
                Cyl(r, 0.6f, 0.3f, 0.2f, new Vector3(0, 2f, 0), TexMat("stone_wall", Stone, 0.6f), 10);
                break;
            case ObjKind.Statue:
                Box(r, new Vector3(1f, 0.8f, 1f), new Vector3(0, 0.4f, 0), TexMat("stone_wall", Stone, 0.6f));
                Cyl(r, 0.25f, 0.3f, 1.2f, new Vector3(0, 1.4f, 0), Mat(Stone));
                Sphere(r, 0.2f, new Vector3(0, 2.2f, 0), Mat(Stone));
                Box(r, new Vector3(0.08f, 1.4f, 0.04f), new Vector3(0.35f, 1.7f, 0.1f), Mat(Stone));
                break;
            case ObjKind.Altar:
                Box(r, new Vector3(1f, 0.9f, 0.7f), new Vector3(0, 0.45f, 0), TexMat("rock", DarkStone, 0.8f));
                Cyl(r, 0.04f, 0.04f, 0.2f, new Vector3(-0.3f, 1f, 0), Mat(new Color(0.95f, 0.9f, 0.8f)), 6);
                Cyl(r, 0.04f, 0.04f, 0.2f, new Vector3(0.3f, 1f, 0), Mat(new Color(0.95f, 0.9f, 0.8f)), 6);
                break;
            case ObjKind.BonesPile:
                for (int i = 0; i < 4; i++)
                    Cyl(r, 0.04f, 0.04f, 0.5f, new Vector3((i - 1.5f) * 0.12f, 0.05f, 0), Mat(Bone), 5, new Vector3(90, i * 40, 0));
                Sphere(r, 0.12f, new Vector3(0.1f, 0.1f, 0.15f), Mat(Bone), null, 6);
                break;
            case ObjKind.Pillar:
                Cyl(r, 0.35f, 0.4f, 3.6f, new Vector3(0, 1.8f, 0), TexMat("dungeon_wall", DarkStone, 0.6f), 10);
                break;
            case ObjKind.Chest:
                Box(r, new Vector3(0.8f, 0.5f, 0.5f), new Vector3(0, 0.25f, 0), TexMat("wood_planks", Wood, 1f));
                Box(r, new Vector3(0.82f, 0.2f, 0.52f), new Vector3(0, 0.6f, 0), Mat(new Color(0.85f, 0.68f, 0.25f), 0.35f, 0.8f));
                break;
            case ObjKind.Throne:
                Box(r, new Vector3(1.2f, 0.6f, 1f), new Vector3(0, 0.3f, 0), Mat(DarkStone));
                Box(r, new Vector3(1.2f, 2.4f, 0.25f), new Vector3(0, 1.2f, -0.45f), Mat(DarkStone));
                Sphere(r, 0.18f, new Vector3(-0.45f, 2.5f, -0.45f), Mat(Bone), null, 6);
                Sphere(r, 0.18f, new Vector3(0.45f, 2.5f, -0.45f), Mat(Bone), null, 6);
                break;
            case ObjKind.Cauldron:
                Sphere(r, 0.45f, new Vector3(0, 0.45f, 0), Mat(Iron, 0.4f, 0.7f), new Vector3(1, 0.8f, 1), 10);
                Cyl(r, 0.35f, 0.35f, 0.04f, new Vector3(0, 0.78f, 0), Mat(new Color(0.3f, 0.9f, 0.3f), 0.3f, 0, 1.5f), 10);
                break;
            case ObjKind.Signpost:
                Cyl(r, 0.06f, 0.07f, 2f, new Vector3(0, 1f, 0), Mat(Wood), 6);
                Box(r, new Vector3(0.9f, 0.25f, 0.05f), new Vector3(0.3f, 1.7f, 0), TexMat("wood_planks", Wood, 1f));
                Box(r, new Vector3(0.8f, 0.25f, 0.05f), new Vector3(-0.25f, 1.35f, 0), TexMat("wood_planks", Wood, 1f), new Vector3(0, 30, 0));
                break;
            case ObjKind.Banner:
                Cyl(r, 0.05f, 0.05f, 3.5f, new Vector3(0, 1.75f, 0), Mat(DarkWood), 6);
                Box(r, new Vector3(0.8f, 1.4f, 0.03f), new Vector3(0, 2.6f, 0.05f), Mat(new Color(0.7f, 0.1f, 0.1f)));
                Box(r, new Vector3(0.3f, 0.3f, 0.035f), new Vector3(0, 2.7f, 0.06f), Mat(new Color(0.95f, 0.8f, 0.2f), 0.4f, 0.6f));
                break;
            case ObjKind.Bridge:
            {
                // Planks spanning the footprint along X, handled in world space by MapView.
                var plank = TexMat("wood_planks", Wood, 0.8f);
                Box(r, new Vector3(w, 0.2f, d), new Vector3(0, 0f, 0), plank);
                Box(r, new Vector3(w, 0.1f, 0.1f), new Vector3(0, 0.9f, -d / 2 + 0.1f), Mat(Wood));
                Box(r, new Vector3(w, 0.1f, 0.1f), new Vector3(0, 0.9f, d / 2 - 0.1f), Mat(Wood));
                for (float x = -w / 2 + 0.2f; x <= w / 2; x += 1.2f)
                {
                    Box(r, new Vector3(0.12f, 1f, 0.12f), new Vector3(x, 0.45f, -d / 2 + 0.1f), Mat(Wood));
                    Box(r, new Vector3(0.12f, 1f, 0.12f), new Vector3(x, 0.45f, d / 2 - 0.1f), Mat(Wood));
                }
                break;
            }
            case ObjKind.Bed:
                Box(r, new Vector3(0.95f, 0.45f, 1.9f), new Vector3(0, 0.25f, 0), TexMat("wood_planks", Wood, 1f));
                Box(r, new Vector3(0.9f, 0.12f, 1.4f), new Vector3(0, 0.52f, 0.2f), Mat(new Color(0.6f, 0.15f, 0.12f)));
                Box(r, new Vector3(0.7f, 0.12f, 0.35f), new Vector3(0, 0.55f, -0.7f), Mat(new Color(0.92f, 0.9f, 0.85f)));
                Box(r, new Vector3(0.95f, 0.9f, 0.08f), new Vector3(0, 0.45f, -0.95f), Mat(DarkWood));
                break;
            case ObjKind.Table:
            case ObjKind.LongTable:
            case ObjKind.Counter:
            {
                float tw = Mathf.Max(o.W, o.D) - 0.1f;
                bool alongZ = o.D > o.W && o.Kind != ObjKind.Table;
                var size = o.Kind == ObjKind.Counter ? new Vector3(alongZ ? 0.7f : tw, 1.0f, alongZ ? tw : 0.7f) : new Vector3(alongZ ? 0.9f : tw, 0.08f, alongZ ? tw : 0.9f);
                if (o.Kind == ObjKind.Counter) Box(r, size, new Vector3(0, 0.5f, 0), TexMat("wood_planks", Wood, 1f));
                else
                {
                    Box(r, size, new Vector3(0, 0.76f, 0), TexMat("wood_planks", Wood, 1f));
                    foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                        Box(r, new Vector3(0.08f, 0.74f, 0.08f), new Vector3(lx * (size.X / 2 - 0.08f), 0.37f, lz * (size.Z / 2 - 0.08f)), Mat(DarkWood));
                }
                break;
            }
            case ObjKind.Chair:
            case ObjKind.Stool:
                Box(r, new Vector3(0.45f, 0.06f, 0.45f), new Vector3(0, 0.45f, 0), Mat(Wood));
                foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Box(r, new Vector3(0.05f, 0.45f, 0.05f), new Vector3(lx * 0.18f, 0.22f, lz * 0.18f), Mat(DarkWood));
                if (o.Kind == ObjKind.Chair) Box(r, new Vector3(0.45f, 0.55f, 0.05f), new Vector3(0, 0.75f, -0.2f), Mat(Wood));
                break;
            case ObjKind.Bookshelf:
            case ObjKind.Wardrobe:
                Box(r, new Vector3(0.95f, 2.0f, 0.4f), new Vector3(0, 1.0f, -0.25f), TexMat("wood_planks", DarkWood, 1f));
                if (o.Kind == ObjKind.Bookshelf)
                    for (int i = 0; i < 4; i++)
                        Box(r, new Vector3(0.85f, 0.3f, 0.3f), new Vector3(0, 0.3f + i * 0.45f, -0.18f), Mat(new Color(0.3f + i * 0.12f, 0.2f, 0.15f + i * 0.08f)));
                break;
            case ObjKind.Fireplace:
            case ObjKind.Furnace:
                Box(r, new Vector3(Mathf.Max(o.W, o.D) * 0.9f, 1.4f, 0.6f), new Vector3(0, 0.7f, -0.2f), TexMat("stone_wall", Stone, 0.6f));
                Box(r, new Vector3(Mathf.Max(o.W, o.D) * 0.5f, 0.6f, 0.1f), new Vector3(0, 0.4f, 0.12f), Mat(new Color(0.05f, 0.03f, 0.02f), 1f));
                break;
            case ObjKind.WeaponRack:
                Box(r, new Vector3(0.9f, 0.08f, 0.2f), new Vector3(0, 1.2f, -0.3f), Mat(Wood));
                for (int i = 0; i < 4; i++) Box(r, new Vector3(0.04f, 1.4f, 0.04f), new Vector3(-0.3f + i * 0.2f, 0.75f, -0.25f), Mat(Iron, 0.4f, 0.8f));
                break;
            case ObjKind.ArmorStand:
                Cyl(r, 0.05f, 0.05f, 1.1f, new Vector3(0, 0.55f, 0), Mat(Wood), 6);
                Box(r, new Vector3(0.5f, 0.6f, 0.3f), new Vector3(0, 1.3f, 0), Mat(new Color(0.7f, 0.72f, 0.75f), 0.35f, 0.8f));
                Sphere(r, 0.14f, new Vector3(0, 1.75f, 0), Mat(new Color(0.7f, 0.72f, 0.75f), 0.35f, 0.8f), null, 8);
                break;
            case ObjKind.Keg:
                Cyl(r, 0.35f, 0.35f, 0.7f, new Vector3(0, 0.6f, 0), TexMat("wood_planks", Wood, 1f), 10, new Vector3(0, 0, 90));
                Box(r, new Vector3(0.7f, 0.25f, 0.6f), new Vector3(0, 0.12f, 0), Mat(DarkWood));
                break;
            case ObjKind.ShelfPotions:
                Box(r, new Vector3(0.9f, 0.05f, 0.3f), new Vector3(0, 1.0f, -0.3f), Mat(Wood));
                for (int i = 0; i < 4; i++) Sphere(r, 0.07f, new Vector3(-0.3f + i * 0.2f, 1.1f, -0.3f), Mat(new Color(0.2f + i * 0.2f, 0.8f - i * 0.15f, 0.4f + i * 0.1f), 0.1f, 0f, 0.8f), null, 6);
                break;
            case ObjKind.Rug:
                Box(r, new Vector3(Mathf.Max(o.W, o.D) * 0.9f, 0.02f, Mathf.Min(o.W, o.D) * 0.9f), new Vector3(0, 0.01f, 0), Mat(new Color(0.55f, 0.12f, 0.12f)));
                break;
            case ObjKind.Candelabra:
                Cyl(r, 0.04f, 0.12f, 1.5f, new Vector3(0, 0.75f, 0), Mat(Iron, 0.4f, 0.8f), 6);
                break;
            case ObjKind.RoyalThrone:
                Box(r, new Vector3(1.3f, 0.6f, 0.9f), new Vector3(0, 0.3f, 0), Mat(new Color(0.85f, 0.65f, 0.2f), 0.35f, 0.8f));
                Box(r, new Vector3(1.3f, 2.0f, 0.2f), new Vector3(0, 1.0f, -0.4f), Mat(new Color(0.85f, 0.65f, 0.2f), 0.35f, 0.8f));
                Box(r, new Vector3(1.0f, 0.1f, 0.7f), new Vector3(0, 0.65f, 0.05f), Mat(new Color(0.6f, 0.08f, 0.1f)));
                break;
            case ObjKind.FishingSpot:
                FishingRipples(r);
                break;
            case ObjKind.FarmPatch:
            {
                var soil = Mat(new Color(0.30f, 0.20f, 0.12f), 1f);
                var plank = Mat(new Color(0.42f, 0.28f, 0.15f));
                Box(r, new Vector3(w - 0.1f, 0.12f, d - 0.1f), new Vector3(0, 0.06f, 0), soil);
                for (int i = -1; i <= 1; i += 2)
                {
                    Box(r, new Vector3(w, 0.2f, 0.1f), new Vector3(0, 0.1f, i * d / 2f), plank);
                    Box(r, new Vector3(0.1f, 0.2f, d), new Vector3(i * w / 2f, 0.1f, 0), plank);
                }
                for (int i = 0; i < 3; i++)
                    Box(r, new Vector3(w - 0.4f, 0.05f, 0.25f), new Vector3(0, 0.13f, (i - 1) * d / 3.2f), Mat(new Color(0.24f, 0.16f, 0.09f), 1f));
                break;
            }
            case ObjKind.SpiderWeb:
            {
                var silk = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 1f, 0.75f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
                for (int i = 0; i < 6; i++)
                    Box(r, new Vector3(0.02f, 2.0f, 0.02f), new Vector3(0, 1.1f, 0), silk, new Vector3(0, 0, i * 30));
                for (int ring = 1; ring <= 3; ring++)
                    Cyl(r, ring * 0.28f, ring * 0.28f, 0.02f, new Vector3(0, 1.1f, 0), silk, 12, new Vector3(90, 0, 0));
                break;
            }
            default:
                Box(r, new Vector3(0.6f, 0.6f, 0.6f), new Vector3(0, 0.3f, 0), Mat(Stone));
                break;
        }
    }

    /// Circling ripples and bubbles marking a shoal.
    static void FishingRipples(Node3D r)
    {
        r.Position = r.Position with { Y = 0f };
        var ring = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var ripples = new Ripples { Name = "Ripples" };
        r.AddChild(ripples);
        for (int i = 0; i < 3; i++)
        {
            var mi = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.28f, OuterRadius = 0.34f, Rings = 16, RingSegments = 4 }, MaterialOverride = ring, Position = new Vector3(0, -0.12f, 0) };
            ripples.AddChild(mi);
        }
        var pm = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 25, InitialVelocityMin = 0.4f, InitialVelocityMax = 0.9f, Gravity = new Vector3(0, -1.2f, 0), EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.3f, ScaleMin = 0.5f, ScaleMax = 1f };
        var quad = new QuadMesh { Size = new Vector2(0.07f, 0.07f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = new Color(0.9f, 0.97f, 1f, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
        r.AddChild(new GpuParticles3D { Name = "Bubbles", Amount = 10, Lifetime = 0.8f, ProcessMaterial = pm, DrawPass1 = quad, Position = new Vector3(0, -0.15f, 0) });
    }

    /// Adds golden glitter over a frenzied shoal (or removes it).
    public static void SetFrenzy(Node3D spot, bool on)
    {
        var old = spot.GetNodeOrNull<Node3D>("Frenzy");
        if (!on) { old?.QueueFree(); return; }
        if (old != null) return;
        var fz = new Node3D { Name = "Frenzy" };
        spot.AddChild(fz);
        var pm = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 60, InitialVelocityMin = 0.8f, InitialVelocityMax = 2f, Gravity = new Vector3(0, -2f, 0), EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.6f, ScaleMin = 0.6f, ScaleMax = 1.3f };
        var quad = new QuadMesh { Size = new Vector2(0.1f, 0.1f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = new Color(1f, 0.85f, 0.25f), BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
        fz.AddChild(new GpuParticles3D { Amount = 40, Lifetime = 1f, ProcessMaterial = pm, DrawPass1 = quad, Position = new Vector3(0, -0.2f, 0) });
        fz.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.3f), LightEnergy = 2f, OmniRange = 4f, Position = new Vector3(0, 0.5f, 0) });
    }

    // ---------- fire ----------
    public static void AddFire(Node3D root, ObjKind k)
    {
        float y = k switch { ObjKind.TorchPost => 2.25f, ObjKind.Brazier => 1.15f, ObjKind.Campfire => 0.25f, ObjKind.WallTorch => 2.1f, ObjKind.Fireplace => 0.35f, ObjKind.Furnace => 0.6f, ObjKind.Candelabra => 1.6f, _ => 1f };
        float z = k switch { ObjKind.WallTorch => -0.3f, ObjKind.Fireplace or ObjKind.Furnace => 0.1f, _ => 0f };
        float size = k switch { ObjKind.Campfire => 1.3f, ObjKind.Brazier => 1.0f, ObjKind.Fireplace or ObjKind.Furnace => 1.1f, ObjKind.Candelabra => 0.3f, _ => 0.6f };
        var fire = new Flame { Position = new Vector3(0, y, z), Size = size, LightRange = k switch { ObjKind.Campfire => 9f, ObjKind.Candelabra => 5f, ObjKind.Fireplace => 7f, _ => 7f } };
        root.AddChild(fire);
    }
}

/// Flickering light plus a small particle flame.
public partial class Flame : Node3D
{
    public float Size = 0.6f;
    public float LightRange = 7f;
    OmniLight3D light;
    float t;
    float baseEnergy = 1.6f;

    public override void _Ready()
    {
        t = GD.Randf() * 10f;
        light = new OmniLight3D { LightColor = new Color(1f, 0.62f, 0.28f), LightEnergy = baseEnergy, OmniRange = LightRange, Position = new Vector3(0, 0.3f, 0), ShadowEnabled = false };
        AddChild(light);

        var pm = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0), Spread = 12f,
            InitialVelocityMin = 0.4f * Size, InitialVelocityMax = 0.9f * Size,
            Gravity = new Vector3(0, 0.8f, 0),
            ScaleMin = 0.6f, ScaleMax = 1.0f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.08f * Size,
        };
        var grad = new Gradient();
        grad.SetColor(0, new Color(1f, 0.85f, 0.4f, 1f));
        grad.SetColor(1, new Color(0.9f, 0.2f, 0.05f, 0f));
        pm.ColorRamp = new GradientTexture1D { Gradient = grad };
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 1)); curve.AddPoint(new Vector2(1, 0.2f));
        pm.ScaleCurve = new CurveTexture { Curve = curve };
        var quad = new QuadMesh { Size = new Vector2(0.25f, 0.25f) * Size };
        quad.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = FlameTexture(),
        };
        var p = new GpuParticles3D { Amount = 24, Lifetime = 0.7f, ProcessMaterial = pm, DrawPass1 = quad, LocalCoords = false, Explosiveness = 0 };
        AddChild(p);
    }

    static Texture2D flameTex;
    static Texture2D FlameTexture()
    {
        if (flameTex != null) return flameTex;
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.SetColor(1, new Color(1, 1, 1, 0));
        flameTex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 32, Height = 32 };
        return flameTex;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        light.LightEnergy = baseEnergy * (0.85f + 0.15f * Mathf.Sin(t * 13f) * Mathf.Sin(t * 7.3f + 1f) + 0.05f * Mathf.Sin(t * 29f));
    }
}

/// Expanding, fading rings on the water surface.
public partial class Ripples : Node3D
{
    float t;
    public override void _Process(double delta)
    {
        t += (float)delta;
        int i = 0;
        foreach (var c in GetChildren())
        {
            if (c is not MeshInstance3D mi) continue;
            float p = (t * 0.5f + i / 3f) % 1f;
            mi.Scale = new Vector3(0.5f + p * 1.6f, 1, 0.5f + p * 1.6f);
            if (mi.MaterialOverride is StandardMaterial3D m && i == 0) m.AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.5f);
            mi.Transparency = p;
            i++;
        }
    }
}
