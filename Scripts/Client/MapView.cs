using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fantasia.Client;

/// Renders a MapData: terrain, water, dungeon walls, buildings, props and atmosphere. The map is
/// streamed in regions of RegionSize x RegionSize tiles: only regions near the player exist in the
/// scene (built one per frame, the player's own neighbourhood at once), and far ones are freed, so
/// memory and load time depend on the view distance rather than the size of the map.
public partial class MapView : Node3D
{
    public const int RegionSize = 32;
    /// Regions kept around the player's region (Chebyshev), and the extra ring before unloading.
    public static int LoadRadius = 2, UnloadSlack = 1;

    /// One loaded region and everything that belongs to it.
    sealed partial class Region : Node3D
    {
        public int RX, RZ;
        public readonly List<(WorldObject obj, Aabb box)> Pickables = new();
        public readonly List<BuildingView> Buildings = new();
        public readonly List<(Node3D node, Aabb box)> WallChunks = new();
        public readonly List<int> Objects = new();
    }

    readonly Dictionary<(int, int), Region> regions = new();
    List<WorldObject>[,] objectsIn;
    List<Building>[,] buildingsIn;
    int regionsX, regionsZ;

    public MapData Map { get; private set; }
    Godot.Environment env;
    DirectionalLight3D sun;

    public override void _EnterTree() => Settings.Changed += ApplyGraphics;
    public override void _ExitTree() => Settings.Changed -= ApplyGraphics;

    void ApplyGraphics()
    {
        if (env == null) return;
        env.SsaoEnabled = Settings.Ssao;
        env.GlowEnabled = Settings.Glow;
        env.FogDensity = Map.FogDensity / Mathf.Max(0.25f, Settings.ViewDistance);
        if (sun != null)
        {
            sun.ShadowEnabled = Settings.Shadows > 0;
            sun.DirectionalShadowMaxDistance = Settings.Shadows >= 2 ? 80f : 40f;
            sun.DirectionalShadowMode = Settings.Shadows >= 2 ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
        }
        RenderingServer.DirectionalShadowAtlasSetSize(Settings.Shadows >= 2 ? 4096 : 2048, true);
    }
    /// Clickable objects in the loaded regions.
    public IEnumerable<(WorldObject obj, Aabb box)> Pickables => regions.Values.SelectMany(r => r.Pickables);

    public int LoadedRegions => regions.Count;

    /// Prepares the map: indexes objects and buildings by region and sets up the sky, skirt and
    /// materials. Regions are built by Stream() as the player moves.
    public void Build(MapData map)
    {
        Map = map;
        regionsX = (map.W + RegionSize - 1) / RegionSize;
        regionsZ = (map.H + RegionSize - 1) / RegionSize;
        objectsIn = new List<WorldObject>[regionsX, regionsZ];
        buildingsIn = new List<Building>[regionsX, regionsZ];
        for (int x = 0; x < regionsX; x++) for (int z = 0; z < regionsZ; z++) { objectsIn[x, z] = new(); buildingsIn[x, z] = new(); }
        foreach (var o in map.Objects) objectsIn[Math.Clamp(o.X / RegionSize, 0, regionsX - 1), Math.Clamp(o.Z / RegionSize, 0, regionsZ - 1)].Add(o);
        foreach (var b in map.Buildings) buildingsIn[Math.Clamp(b.X / RegionSize, 0, regionsX - 1), Math.Clamp(b.Z / RegionSize, 0, regionsZ - 1)].Add(b);
        PrepareTerrain();
        BuildSurroundings();
        BuildEnvironment();
    }

    // ================= streaming =================

    /// Loads regions around `focus` (world position) and frees far ones. Missing regions next to the
    /// focus are built immediately; the rest one per call.
    public void Stream(Vector3 focus)
    {
        if (Map == null) return;
        int fx = Math.Clamp((int)(focus.X / RegionSize), 0, regionsX - 1), fz = Math.Clamp((int)(focus.Z / RegionSize), 0, regionsZ - 1);
        foreach (var key in regions.Keys.Where(k => Math.Max(Math.Abs(k.Item1 - fx), Math.Abs(k.Item2 - fz)) > LoadRadius + UnloadSlack).ToList())
            Unload(key);
        (int, int)? next = null; int nextD = int.MaxValue;
        for (int x = fx - LoadRadius; x <= fx + LoadRadius; x++)
            for (int z = fz - LoadRadius; z <= fz + LoadRadius; z++)
            {
                if (x < 0 || z < 0 || x >= regionsX || z >= regionsZ || regions.ContainsKey((x, z))) continue;
                int d = Math.Max(Math.Abs(x - fx), Math.Abs(z - fz));
                if (d <= 1) { Load(x, z); continue; }
                if (d < nextD) { nextD = d; next = (x, z); }
            }
        if (next is var (nx, nz)) Load(nx, nz);
    }

    void Load(int rx, int rz)
    {
        var r = new Region { RX = rx, RZ = rz, Name = $"Region_{rx}_{rz}" };
        AddChild(r);
        regions[(rx, rz)] = r;
        int x0 = rx * RegionSize, z0 = rz * RegionSize;
        int x1 = Math.Min(x0 + RegionSize, Map.W), z1 = Math.Min(z0 + RegionSize, Map.H);
        BuildTerrain(r, x0, z0, x1, z1);
        if (!Map.Underground) BuildWater(r, x0, z0, x1, z1);
        BuildWalls(r, x0, z0, x1, z1);
        BuildBuildings(r, buildingsIn[rx, rz]);
        BuildProps(r, objectsIn[rx, rz]);
        // Server state that arrived while the region wasn't loaded.
        foreach (var id in r.Objects)
        {
            if (depletedNow.Contains(id)) ShowResource(id, false);
            if (frenzyNow.Contains(id) && resNodes.TryGetValue(id, out var fn)) Props.SetFrenzy(fn, true);
        }
        RefreshCrops();
    }

    void Unload((int, int) key)
    {
        var r = regions[key];
        regions.Remove(key);
        foreach (var id in r.Objects)
        {
            resInstances.Remove(id); resNodes.Remove(id); stumps.Remove(id); patchNodes.Remove(id);
            crops.Remove(id);
        }
        foreach (var b in r.Buildings) buildingViews.Remove(b);
        foreach (var w in r.WallChunks) wallChunks.Remove(w);
        r.QueueFree();
    }

    Region RegionOf(WorldObject o) => regions.TryGetValue((Math.Clamp(o.X / RegionSize, 0, regionsX - 1), Math.Clamp(o.Z / RegionSize, 0, regionsZ - 1)), out var r) ? r : null;

    // ================= terrain =================

    static (string tex, Color tint, float uv) GroundLook(Ground g) => g switch
    {
        Ground.Grass => ("grass", new Color(1f, 1f, 1f), 0.22f),
        Ground.DarkGrass => ("grass", new Color(0.55f, 0.55f, 0.42f), 0.22f),
        Ground.Moss => ("grass", new Color(0.62f, 0.68f, 0.55f), 0.22f),
        Ground.Dirt => ("dirt_path", new Color(1f, 1f, 1f), 0.3f),
        Ground.Cobble => ("cobblestone", new Color(1f, 1f, 1f), 0.35f),
        Ground.Sand => ("sand", new Color(1f, 1f, 1f), 0.3f),
        Ground.Water => ("sand", new Color(0.45f, 0.45f, 0.4f), 0.3f),
        Ground.Stone => ("cave_floor", new Color(1f, 1f, 1f), 0.3f),
        Ground.Wood => ("wood_planks", new Color(1f, 1f, 1f), 0.4f),
        Ground.Ash => ("dirt_path", new Color(0.45f, 0.42f, 0.4f), 0.3f),
        _ => ("rock", new Color(0.2f, 0.2f, 0.2f), 0.3f),
    };

    static Color GroundFallback(Ground g) => g switch
    {
        Ground.Grass => new Color(0.30f, 0.55f, 0.22f), Ground.DarkGrass => new Color(0.22f, 0.28f, 0.16f),
        Ground.Moss => new Color(0.28f, 0.38f, 0.22f), Ground.Dirt => new Color(0.50f, 0.38f, 0.24f),
        Ground.Cobble => new Color(0.52f, 0.50f, 0.48f), Ground.Sand => new Color(0.80f, 0.72f, 0.50f),
        Ground.Stone => new Color(0.35f, 0.33f, 0.31f), Ground.Wood => new Color(0.45f, 0.30f, 0.18f),
        Ground.Ash => new Color(0.25f, 0.23f, 0.22f), _ => new Color(0.1f, 0.1f, 0.1f),
    };

    Vector3 Normal(int x, int z)
    {
        float hl = Map.Heights[Math.Max(x - 1, 0), z], hr = Map.Heights[Math.Min(x + 1, Map.W), z];
        float hd = Map.Heights[x, Math.Max(z - 1, 0)], hu = Map.Heights[x, Math.Min(z + 1, Map.H)];
        return new Vector3(hl - hr, 2f, hd - hu).Normalized();
    }

    const string TerrainShader = @"
shader_type spatial;
render_mode cull_back;
uniform sampler2D mask0 : filter_linear, repeat_disable;
uniform sampler2D mask1 : filter_linear, repeat_disable;
uniform sampler2D mask2 : filter_linear, repeat_disable;
uniform sampler2D t_grass : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_dirt : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_cobble : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_sand : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_stone : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_wood : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec2 mask_origin;
uniform vec2 mask_size;
varying vec3 wpos;

void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
    vec2 uv = (wpos.xz - mask_origin) / mask_size;
    vec4 m0 = texture(mask0, uv);
    vec4 m1 = texture(mask1, uv);
    vec4 m2 = texture(mask2, uv);
    vec2 p = wpos.xz;
    vec3 grass = texture(t_grass, p * 0.22).rgb;
    vec3 dirt = texture(t_dirt, p * 0.3).rgb;
    vec3 cobble = texture(t_cobble, p * 0.35).rgb;
    vec3 sand = texture(t_sand, p * 0.3).rgb;
    vec3 stone = texture(t_stone, p * 0.3).rgb;
    vec3 wood = texture(t_wood, p * 0.4).rgb;
    // Organic, slightly jagged borders: perturb weights with texture-derived noise, then sharpen.
    float n = (texture(t_grass, p * 0.071).g + texture(t_dirt, p * 0.19).r - 1.0) * 0.45;
    vec4 w0 = pow(clamp(m0 + vec4(n, -n, n * 0.6, -n * 0.8), 0.0, 1.0), vec4(4.0));
    vec4 w1 = pow(clamp(m1 + vec4(-n, n * 0.5, -n, n * 0.7), 0.0, 1.0), vec4(4.0));
    vec4 w2 = pow(clamp(m2 + vec4(n * 0.4, -n * 0.4, 0.0, 0.0), 0.0, 1.0), vec4(4.0));
    float total = dot(w0, vec4(1.0)) + dot(w1, vec4(1.0)) + dot(w2, vec4(1.0)) + 0.0001;
    vec3 c = grass * w0.r
        + grass * vec3(0.55, 0.55, 0.42) * w0.g
        + grass * vec3(0.62, 0.68, 0.55) * w0.b
        + dirt * w0.a
        + dirt * vec3(0.45, 0.42, 0.40) * w1.r
        + cobble * w1.g
        + sand * w1.b
        + stone * w1.a
        + wood * w2.r
        + sand * vec3(0.45, 0.45, 0.40) * w2.g;
    ALBEDO = c / total * COLOR.rgb;
    ROUGHNESS = 0.92;
    SPECULAR = 0.25;
}";

    static int MaskIndex(Ground g) => g switch
    {
        Ground.Grass => 0, Ground.DarkGrass => 1, Ground.Moss => 2, Ground.Dirt => 3,
        Ground.Ash => 4, Ground.Cobble => 5, Ground.Sand => 6, Ground.Stone => 7,
        Ground.Wood => 8, Ground.Water => 9, _ => 7,
    };

    static Texture2D TexOr(string name, Color fallback)
    {
        var t = Assets.Tex(name);
        if (t != null) return t;
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.SetPixel(0, 0, fallback);
        return ImageTexture.CreateFromImage(img);
    }

    Shader terrainShader;
    readonly Dictionary<string, Texture2D> groundTex = new();

    void PrepareTerrain()
    {
        terrainShader = new Shader { Code = TerrainShader };
        groundTex["t_grass"] = TexOr("grass", GroundFallback(Ground.Grass));
        groundTex["t_dirt"] = TexOr("dirt_path", GroundFallback(Ground.Dirt));
        groundTex["t_cobble"] = TexOr("cobblestone", GroundFallback(Ground.Cobble));
        groundTex["t_sand"] = TexOr("sand", GroundFallback(Ground.Sand));
        groundTex["t_stone"] = TexOr("cave_floor", GroundFallback(Ground.Stone));
        groundTex["t_wood"] = TexOr("wood_planks", GroundFallback(Ground.Wood));
    }

    /// Ground-type masks for one region, with a one-tile border so blending is seamless across regions.
    ShaderMaterial TerrainMaterial(int x0, int z0, int x1, int z1)
    {
        int ox = x0 - 1, oz = z0 - 1, w = x1 - x0 + 2, h = z1 - z0 + 2;
        var imgs = new Image[3];
        for (int i = 0; i < 3; i++) imgs[i] = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
            {
                int mx = Math.Clamp(ox + x, 0, Map.W - 1), mz = Math.Clamp(oz + z, 0, Map.H - 1);
                int idx = MaskIndex(Map.Ground[mx, mz]);
                var c = new Color(0, 0, 0, 0);
                c[idx % 4] = 1f;
                imgs[idx / 4].SetPixel(x, z, c);
            }
        var mat = new ShaderMaterial { Shader = terrainShader };
        for (int i = 0; i < 3; i++) mat.SetShaderParameter($"mask{i}", ImageTexture.CreateFromImage(imgs[i]));
        foreach (var (k, t) in groundTex) mat.SetShaderParameter(k, t);
        mat.SetShaderParameter("mask_origin", new Vector2(ox, oz));
        mat.SetShaderParameter("mask_size", new Vector2(w, h));
        return mat;
    }

    void BuildTerrain(Region r, int x0, int z0, int x1, int z1)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int quads = 0;
        for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
            {
                if (Map.Ground[x, z] == Ground.Void) continue;
                AddQuad(st, x, z);
                quads++;
            }
        if (quads == 0) return;
        st.SetMaterial(TerrainMaterial(x0, z0, x1, z1));
        r.AddChild(new MeshInstance3D { Mesh = st.Commit(), Name = "Terrain" });
    }

    /// Map-wide pieces: the grass skirt beyond the edges (overworld) or the black void (dungeons).
    void BuildSurroundings()
    {
        if (Map.Underground)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(Map.W + 40, Map.H + 40) },
                Position = new Vector3(Map.W / 2f, -0.02f, Map.H / 2f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f) },
                Name = "Void",
            });
            return;
        }
        // Skirt beyond the map edges so the horizon isn't a cliff: a frame of four strips around
        // the map (a single plane under it would sit above the rivers and lakes).
        var skirtMat = Assets.TexturedMaterial("grass", GroundFallback(Ground.Grass), 0.22f);
        ((StandardMaterial3D)skirtMat).AlbedoColor = new Color(0.6f, 0.65f, 0.5f);
        float m = Map.W * 1.5f;
        foreach (var (pos, size) in new[]
        {
            (new Vector3(Map.W / 2f, 0.2f, -m / 2f), new Vector2(Map.W + 2 * m, m)),
            (new Vector3(Map.W / 2f, 0.2f, Map.H + m / 2f), new Vector2(Map.W + 2 * m, m)),
            (new Vector3(-m / 2f, 0.2f, Map.H / 2f), new Vector2(m, Map.H)),
            (new Vector3(Map.W + m / 2f, 0.2f, Map.H / 2f), new Vector2(m, Map.H)),
        })
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = size }, Position = pos, MaterialOverride = skirtMat, Name = "Skirt" });
    }

    void AddQuad(SurfaceTool st, int x, int z)
    {
        void V(int vx, int vz)
        {
            float shade = 0.88f + 0.14f * Hash.Unit(vx, vz, 3);
            st.SetColor(new Color(shade, shade, shade));
            st.SetNormal(Normal(vx, vz));
            st.SetUV(new Vector2(vx, vz) * 0.25f);
            st.AddVertex(new Vector3(vx, Map.Heights[vx, vz], vz));
        }
        V(x, z); V(x + 1, z); V(x + 1, z + 1);
        V(x, z); V(x + 1, z + 1); V(x, z + 1);
    }

    // ================= water =================

    const string WaterShader = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque;
uniform vec4 deep : source_color = vec4(0.10, 0.28, 0.42, 0.85);
uniform vec4 shallow : source_color = vec4(0.25, 0.55, 0.65, 0.75);
void vertex() {
    VERTEX.y += sin(TIME * 1.2 + VERTEX.x * 0.7 + VERTEX.z * 0.4) * 0.04;
}
void fragment() {
    vec2 p = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz;
    float w = sin(p.x * 1.7 + TIME * 1.1) * cos(p.y * 1.3 - TIME * 0.9) * 0.5 + 0.5;
    float w2 = sin(p.x * 1.3 - TIME * 0.8 + p.y * 1.7) * cos(p.y * 0.9 + TIME * 0.6) * 0.5 + 0.5;
    ALBEDO = mix(deep.rgb, shallow.rgb, w * 0.6 + w2 * 0.2);
    ALPHA = mix(deep.a, shallow.a, w);
    ROUGHNESS = 0.08;
    METALLIC = 0.1;
    SPECULAR = 0.7;
    NORMAL_MAP = normalize(vec3(0.5 + (w - 0.5) * 0.12, 0.5 + (w2 - 0.5) * 0.12, 1.0));
}";

    ShaderMaterial waterMat;

    void BuildWater(Region r, int x0, int z0, int x1, int z1)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int count = 0;
        const float y = -0.15f;
        for (int x = x0; x < x1; x++)
        for (int z = z0; z < z1; z++)
        {
            bool near = false;
            for (int dx = -1; dx <= 1 && !near; dx++)
                for (int dz = -1; dz <= 1 && !near; dz++)
                    if (Map.InBounds(x + dx, z + dz) && Map.Ground[x + dx, z + dz] == Ground.Water) near = true;
            if (!near) continue;
            st.SetNormal(Vector3.Up);
            st.AddVertex(new Vector3(x, y, z)); st.AddVertex(new Vector3(x + 1, y, z)); st.AddVertex(new Vector3(x + 1, y, z + 1));
            st.AddVertex(new Vector3(x, y, z)); st.AddVertex(new Vector3(x + 1, y, z + 1)); st.AddVertex(new Vector3(x, y, z + 1));
            count++;
        }
        if (count == 0) return;
        waterMat ??= new ShaderMaterial { Shader = new Shader { Code = WaterShader } };
        r.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = waterMat, Name = "Water", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    // ================= dungeon walls =================

    Material wallMat;

    void BuildWalls(Region r, int x0, int z0, int x1, int z1)
    {
        if (!Map.Underground) return;
        var xforms = new List<Transform3D>();
        for (int x = x0; x < x1; x++)
        for (int z = z0; z < z1; z++)
        {
            if (Map.Ground[x, z] != Ground.Void) continue;
            bool edge = false;
            for (int dx = -1; dx <= 1 && !edge; dx++)
                for (int dz = -1; dz <= 1 && !edge; dz++)
                    if (Map.InBounds(x + dx, z + dz) && Map.Ground[x + dx, z + dz] != Ground.Void) edge = true;
            if (!edge) continue;
            float h = 3.2f + Hash.Unit(x, z, 9) * 0.8f;
            xforms.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1, h, 1)), new Vector3(x + 0.5f, h / 2f, z + 0.5f)));
        }
        if (xforms.Count == 0) return;
        wallMat ??= Assets.TexturedMaterial("dungeon_wall", new Color(0.3f, 0.28f, 0.27f), 0.4f);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = Vector3.One }, InstanceCount = xforms.Count };
        for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        r.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = wallMat, Name = "Walls" });
    }

    // ================= buildings =================

    readonly List<BuildingView> buildingViews = new();

    void BuildBuildings(Region r, List<Building> list)
    {
        foreach (var b in list)
        {
            var v = new BuildingView { Name = $"Building_{b.Id}" };
            r.AddChild(v);
            v.Build(b, Map);
            buildingViews.Add(v);
            r.Buildings.Add(v);
        }
    }

    public override void _Process(double delta)
    {
        var focus = GameWorld.I?.Me;
        if (focus != null) Stream(focus.GlobalPosition);
        TickCrops(delta);
        var me = GameWorld.I?.Me;
        var cam = GameWorld.I?.Rig?.Cam;
        if (me == null || cam == null || buildingViews.Count == 0) return;
        var inside = Map.BuildingAt(me.Tile);
        // Anything the camera looks through to see the player gets cut away, like classic isometric RPGs.
        var from = cam.GlobalPosition;
        var targets = new[] { me.GlobalPosition + Vector3.Up * 0.3f, me.GlobalPosition + Vector3.Up * 1.6f };
        foreach (var v in buildingViews)
        {
            bool cut = v.B == inside;
            if (!cut)
                foreach (var t in targets)
                    if (SegmentHits(from, t, v.Bounds)) { cut = true; break; }
            v.SetCutaway(cut);
        }
        float step = (float)delta * 3f;
        // Town walls also drop when they hide the ground around the player, not just the player.
        var p0 = me.GlobalPosition;
        var around = new[]
        {
            targets[0], targets[1],
            p0 + new Vector3(3, 0.4f, 0), p0 + new Vector3(-3, 0.4f, 0), p0 + new Vector3(0, 0.4f, 3), p0 + new Vector3(0, 0.4f, -3),
        };
        foreach (var (node, box) in wallChunks)
        {
            bool hide = false;
            foreach (var t in around) if (SegmentHits(from, t, box)) { hide = true; break; }
            float want = hide ? 0.3f : 1f;
            var sc = node.Scale;
            if (Mathf.IsEqualApprox(sc.Y, want)) continue;
            float y = Mathf.MoveToward(sc.Y, want, step);
            // Scale about the chunk's base height so the wall sinks rather than floats.
            float baseY = box.Position.Y + 0.5f;
            node.Scale = new Vector3(1, y, 1);
            node.Position = new Vector3(0, baseY * (1 - y), 0);
        }
    }

    static bool SegmentHits(Vector3 a, Vector3 b, Aabb box)
    {
        var d = b - a;
        float tmin = 0f, tmax = 1f;
        for (int i = 0; i < 3; i++)
        {
            float o = a[i], di = d[i], mn = box.Position[i], mx = box.End[i];
            if (Mathf.Abs(di) < 1e-6f) { if (o < mn || o > mx) return false; continue; }
            float t1 = (mn - o) / di, t2 = (mx - o) / di;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        return true;
    }

    // ================= props =================

    // Town wall runs grouped into chunks that can be lowered when they hide the player.
    readonly List<(Node3D node, Aabb box)> wallChunks = new();

    // Gatherable objects: batched instances (trees) or standalone nodes (rocks, fishing spots).
    readonly Dictionary<int, List<(MultiMesh mm, int index, Transform3D xf)>> resInstances = new();
    readonly Dictionary<int, Node3D> resNodes = new();
    readonly Dictionary<int, Node3D> stumps = new();
    readonly HashSet<int> depletedNow = new(), frenzyNow = new();
    void BuildProps(Region r, List<WorldObject> objects)
    {
        var batches = new Dictionary<(Mesh, Material), List<(Transform3D xf, int obj)>>();
        var holder = new Node3D { Name = "Props" };
        r.AddChild(holder);
        var chunks = new Dictionary<(int, int), (Node3D node, Aabb box)>();
        foreach (var o in objects)
        {
            r.Objects.Add(o.Id);
            if (o.Kind == ObjKind.CastleWall)
            {
                var key = (o.X / 5, o.Z / 5);
                var wn = Props.Build(o, Map);
                var wb = new Aabb(new Vector3(o.X, wn.Position.Y - 0.5f, o.Z), new Vector3(1, 4.5f, 1));
                if (!chunks.TryGetValue(key, out var ch))
                {
                    var n = new Node3D { Name = $"WallChunk_{key.Item1}_{key.Item2}" };
                    holder.AddChild(n);
                    ch = (n, wb);
                }
                // Parent pivots at ground level so scaling Y shrinks walls toward the ground.
                wn.Position -= new Vector3(0, 0, 0);
                ch.node.AddChild(wn);
                chunks[key] = (ch.node, ch.box.Merge(wb));
                continue;
            }
            var node = Props.Build(o, Map);
            float h = Props.PickHeight(o);
            var basePos = node.Position;
            if (o.Kind != ObjKind.Bridge && o.Kind != ObjKind.WallTorch && o.Kind != ObjKind.BonesPile)
                r.Pickables.Add((o, new Aabb(new Vector3(o.X, basePos.Y, o.Z), new Vector3(o.W, h, o.D))));

            if (o.Kind == ObjKind.Bridge)
            {
                node.Position = new Vector3(basePos.X, 0.4f, basePos.Z);
                holder.AddChild(node);
                continue;
            }
            if (Props.IsDynamic(o.Kind) || !Batchable(node))
            {
                holder.AddChild(node);
                if (o.Res != null) resNodes[o.Id] = node;
                if (o.Kind == ObjKind.FarmPatch) patchNodes[o.Id] = node;
                continue;
            }
            // Flatten static props into multimesh batches.
            var rootXf = node.Transform;
            Collect(node, rootXf, batches, true, o.Res != null ? o.Id : -1);
            node.Free();
            if (o.Kind == ObjKind.FarmPatch) patchNodes[o.Id] = null;
        }
        foreach (var c in chunks.Values) { wallChunks.Add(c); r.WallChunks.Add(c); }
        foreach (var ((mesh, mat), list) in batches)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++)
            {
                mm.SetInstanceTransform(i, list[i].xf);
                if (list[i].obj < 0) continue;
                if (!resInstances.TryGetValue(list[i].obj, out var li)) resInstances[list[i].obj] = li = new();
                li.Add((mm, i, list[i].xf));
            }
            var mmi = new MultiMeshInstance3D { Multimesh = mm };
            if (mat != null) mmi.MaterialOverride = mat;
            holder.AddChild(mmi);
        }
    }

    static bool Batchable(Node n)
    {
        if (n is Light3D || n is GpuParticles3D || n is AnimationPlayer || n is Skeleton3D) return false;
        if (n is MeshInstance3D mi)
        {
            for (int s = 0; s < mi.GetSurfaceOverrideMaterialCount(); s++)
                if (mi.GetSurfaceOverrideMaterial(s) != null) return false;
        }
        foreach (var c in n.GetChildren()) if (!Batchable(c)) return false;
        return true;
    }

    static void Collect(Node n, Transform3D xf, Dictionary<(Mesh, Material), List<(Transform3D, int)>> batches, bool isRoot, int obj)
    {
        var local = isRoot ? xf : (n is Node3D n3 ? xf * n3.Transform : xf);
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            var key = (mi.Mesh, mi.MaterialOverride);
            if (!batches.TryGetValue(key, out var list)) batches[key] = list = new List<(Transform3D, int)>();
            list.Add((local, obj));
        }
        foreach (var c in n.GetChildren()) Collect(c, local, batches, false, obj);
    }

    // ================= resources =================

    /// Applies the server's depleted / frenzied lists for objects in view.
    public void SetResourceState(int[] dep, int[] frz)
    {
        var d = dep != null ? new HashSet<int>(dep) : new HashSet<int>();
        foreach (var id in depletedNow.Where(i => !d.Contains(i)).ToList()) { ShowResource(id, true); depletedNow.Remove(id); }
        foreach (var id in d.Where(i => !depletedNow.Contains(i))) { ShowResource(id, false); depletedNow.Add(id); }
        var f = frz != null ? new HashSet<int>(frz) : new HashSet<int>();
        foreach (var id in frenzyNow.Where(i => !f.Contains(i)).ToList()) { if (resNodes.TryGetValue(id, out var n)) Props.SetFrenzy(n, false); frenzyNow.Remove(id); }
        foreach (var id in f.Where(i => !frenzyNow.Contains(i))) { if (resNodes.TryGetValue(id, out var n)) Props.SetFrenzy(n, true); frenzyNow.Add(id); }
    }

    public bool IsDepleted(int objId) => depletedNow.Contains(objId);

    void ShowResource(int id, bool present)
    {
        var o = Map.GetObject(id);
        if (o == null || RegionOf(o) == null) return;   // applied when its region loads
        if (resInstances.TryGetValue(id, out var list))
            foreach (var (mm, i, xf) in list)
                mm.SetInstanceTransform(i, present ? xf : new Transform3D(xf.Basis.Scaled(Vector3.One * 0.0001f), xf.Origin));
        if (resNodes.TryGetValue(id, out var node))
        {
            if (o.Kind == ObjKind.FishingSpot) node.Visible = present;
            else
            {
                var veins = node.GetNodeOrNull<Node3D>("Veins");
                if (veins != null) veins.Visible = present;
                foreach (var c in node.GetChildren()) if (c is Node3D n3 && c.Name != "Veins") n3.Scale = Vector3.One * (present ? 1f : 0.8f);
            }
        }
        bool tree = o.Kind is ObjKind.TreeOak or ObjKind.TreePine or ObjKind.TreeWillow or ObjKind.TreeDead;
        if (!tree) return;
        if (present) { if (stumps.Remove(id, out var s)) s.QueueFree(); return; }
        var stump = new Node3D { Name = $"Stump{id}", Position = new Vector3(o.X + 0.5f, Map.TileHeight(o.X, o.Z), o.Z + 0.5f) };
        var bark = Props.Mat(new Color(0.35f, 0.24f, 0.14f));
        Props.Cyl(stump, 0.26f * o.Scale, 0.34f * o.Scale, 0.45f, new Vector3(0, 0.22f, 0), bark, 9);
        Props.Cyl(stump, 0.25f * o.Scale, 0.25f * o.Scale, 0.02f, new Vector3(0, 0.46f, 0), Props.Mat(new Color(0.78f, 0.62f, 0.40f)), 9);
        (RegionOf(o) ?? (Node)this).AddChild(stump);
        stumps[id] = stump;
    }

    // ================= farming =================

    readonly Dictionary<int, Node3D> patchNodes = new();
    readonly Dictionary<int, (Node3D node, string key)> crops = new();
    List<PatchState> patches;
    long patchNow;
    double patchStamp, patchTimer;

    /// The player's own crops, shown on their allotments (everyone sees only their own).
    public void UpdatePatches(List<PatchState> list, long serverNow)
    {
        patches = list;
        patchNow = serverNow;
        patchStamp = Time.GetTicksMsec() / 1000.0;
        RefreshCrops();
    }

    public long ServerNow => patchNow + (long)(Time.GetTicksMsec() / 1000.0 - patchStamp);

    public PatchState PatchFor(int obj) => patches?.FirstOrDefault(p => p.Obj == obj);

    void RefreshCrops()
    {
        if (Map == null) return;
        long now = ServerNow;
        foreach (var id in patchNodes.Keys.ToList())
        {
            var o = Map.GetObject(id);
            if (o == null) continue;
            var pt = PatchFor(o.Id);
            var (stage, phase, _) = FarmDb.Eval(pt, now);
            string key = phase == PatchPhase.Empty ? "" : $"{pt.Crop}:{stage}:{phase}";
            if (crops.TryGetValue(o.Id, out var cur) && cur.key == key) continue;
            cur.node?.QueueFree();
            crops.Remove(o.Id);
            if (key == "") continue;
            var n = CropVisual.Build(FarmDb.Get(pt.Crop), stage, phase, o.W, o.D);
            n.Position = new Vector3(o.X + o.W / 2f, Map.TileHeight(o.X + o.W / 2, o.Z + o.D / 2) + 0.12f, o.Z + o.D / 2f);
            (RegionOf(o) ?? (Node)this).AddChild(n);
            crops[o.Id] = (n, key);
        }
    }

    void TickCrops(double delta)
    {
        patchTimer += delta;
        if (patchTimer < 1.0 || patches == null) return;
        patchTimer = 0;
        RefreshCrops();
    }

    // ================= atmosphere =================

    void BuildEnvironment()
    {
        env = new Godot.Environment();
        if (Map.Underground)
        {
            env.BackgroundMode = Godot.Environment.BGMode.Color;
            env.BackgroundColor = new Color(0.01f, 0.01f, 0.015f);
            env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor = Map.Ambient;
            env.AmbientLightEnergy = 1.0f;
        }
        else
        {
            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor = Map.SkyTop, SkyHorizonColor = Map.SkyHorizon,
                GroundBottomColor = new Color(0.2f, 0.18f, 0.15f), GroundHorizonColor = Map.SkyHorizon,
                SunAngleMax = 20f,
            };
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.Sky = new Sky { SkyMaterial = sky };
            env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            env.AmbientLightEnergy = 0.55f;
        }
        env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = Map.Underground ? 1.2f : 1.0f;
        env.FogEnabled = true;
        env.FogLightColor = Map.Fog;
        env.FogDensity = Map.FogDensity;
        env.FogSkyAffect = 0.15f;
        env.SsaoEnabled = true;
        env.SsaoIntensity = 1.2f;
        env.GlowEnabled = true;
        env.GlowIntensity = 0.5f;
        env.GlowBloom = 0.0f;
        env.GlowHdrThreshold = 1.1f;
        env.AdjustmentEnabled = true;
        env.AdjustmentSaturation = 1.08f;
        env.AdjustmentContrast = 1.05f;
        AddChild(new WorldEnvironment { Environment = env });

        if (!Map.Underground)
        {
            sun = new DirectionalLight3D
            {
                LightColor = new Color(1f, 0.92f, 0.78f), LightEnergy = 1.45f, ShadowEnabled = true,
                RotationDegrees = new Vector3(-50, -35, 0),
                DirectionalShadowMaxDistance = 70f,
            };
            AddChild(sun);
        }
        ApplyGraphics();
    }
}
