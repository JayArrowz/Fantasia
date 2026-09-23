using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia.Client;

/// Box/prism geometry appended to a SurfaceTool (materials are world-triplanar, so no UVs needed).
static class MeshKit
{
    public static void Box(SurfaceTool st, Vector3 min, Vector3 max)
    {
        Vector3 a = min, b = max;
        Quad(st, new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z), Vector3.Back);
        Quad(st, new(b.X, a.Y, a.Z), new(a.X, a.Y, a.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), Vector3.Forward);
        Quad(st, new(b.X, a.Y, b.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z), Vector3.Right);
        Quad(st, new(a.X, a.Y, a.Z), new(a.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(a.X, b.Y, a.Z), Vector3.Left);
        Quad(st, new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z), Vector3.Up);
        Quad(st, new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z), Vector3.Down);
    }

    public static void Quad(SurfaceTool st, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
    {
        // Godot treats clockwise triangles as front faces; quads are specified counter-clockwise.
        st.SetNormal(n);
        st.AddVertex(p0); st.AddVertex(p2); st.AddVertex(p1);
        st.AddVertex(p0); st.AddVertex(p3); st.AddVertex(p2);
    }

    public static void Tri(SurfaceTool st, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var n = (p1 - p0).Cross(p2 - p0).Normalized();
        st.SetNormal(n);
        st.AddVertex(p0); st.AddVertex(p2); st.AddVertex(p1);
    }
}

/// Renders an enterable building: walls with doorways and windows, trim, and a roof that
/// hides (and walls that lower) while the local player is inside.
public partial class BuildingView : Node3D
{
    public Building B;
    /// World-space bounds of walls and roof, used to detect when the building hides the player.
    public Aabb Bounds;
    Node3D walls, roof;
    OmniLight3D light;
    float wallK = 1f, roofK = 1f;
    bool inside;
    readonly List<GeometryInstance3D> roofParts = new();

    const float Thick = 0.6f;
    /// Trim sits this far proud of the wall tops so coplanar faces never z-fight (visible when walls are cut down).
    const float Lift = 0.04f;

    static Material WallMat(BuildingStyle s) => s switch
    {
        BuildingStyle.Timber => Props.TexMat("plaster", new Color(0.9f, 0.87f, 0.78f), 0.4f),
        BuildingStyle.Longhall => Props.TexMat("wood_planks", new Color(0.45f, 0.3f, 0.18f), 0.5f, new Color(0.85f, 0.75f, 0.65f)),
        _ => Props.TexMat("stone_wall", new Color(0.55f, 0.54f, 0.52f), 0.35f),
    };

    static Material RoofMat(BuildingStyle s)
    {
        var m = (StandardMaterial3D)(s switch
        {
            BuildingStyle.Timber => Assets.TexturedMaterial("thatch_roof", new Color(0.75f, 0.62f, 0.32f), 0.45f),
            BuildingStyle.Longhall => Assets.TexturedMaterial("thatch_roof", new Color(0.55f, 0.45f, 0.3f), 0.45f),
            _ => Assets.TexturedMaterial("roof_tiles", new Color(0.55f, 0.22f, 0.15f), 0.45f),
        });
        if (s == BuildingStyle.Longhall) m.AlbedoColor = new Color(0.75f, 0.68f, 0.55f);
        m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return m;
    }

    public void Build(Building b, MapData map)
    {
        B = b;
        float floor = map.TileHeight(b.X + b.W / 2, b.Z + b.D / 2);
        Position = new Vector3(0, floor, 0);
        float h = b.WallHeight;

        walls = new Node3D { Name = "Walls" };
        AddChild(walls);
        var wall = new SurfaceTool(); wall.Begin(Mesh.PrimitiveType.Triangles);
        var trim = new SurfaceTool(); trim.Begin(Mesh.PrimitiveType.Triangles);
        var glass = new SurfaceTool(); glass.Begin(Mesh.PrimitiveType.Triangles);
        bool hasTrim = false, hasGlass = false;

        for (int x = b.X; x < b.X + b.W; x++)
        for (int z = b.Z; z < b.Z + b.D; z++)
        {
            if (!b.IsPerimeter(x, z)) continue;
            bool north = z == b.Z, south = z == b.Z + b.D - 1, west = x == b.X, east = x == b.X + b.W - 1;
            bool corner = (north || south) && (west || east);
            bool door = b.IsDoor(x, z);
            // Wall slab(s) for this tile: along X for north/south edges, along Z for west/east.
            var slabs = new List<(Vector3 min, Vector3 max, bool alongX)>();
            float t0 = 0.5f - Thick / 2, t1 = 0.5f + Thick / 2;
            if (north || south) slabs.Add((new Vector3(x, 0, z + t0), new Vector3(x + 1, h, z + t1), true));
            if (west || east) slabs.Add((new Vector3(x + t0, 0, z), new Vector3(x + t1, h, z + 1), false));
            foreach (var (mn, mx, alongX) in slabs)
            {
                if (door && !corner)
                {
                    float dh = Mathf.Min(2.4f, h - 0.4f);
                    // posts + lintel
                    if (alongX)
                    {
                        MeshKit.Box(trim, new Vector3(mn.X, 0, mn.Z - 0.05f), new Vector3(mn.X + 0.15f, dh, mx.Z + 0.05f));
                        MeshKit.Box(trim, new Vector3(mx.X - 0.15f, 0, mn.Z - 0.05f), new Vector3(mx.X, dh, mx.Z + 0.05f));
                        MeshKit.Box(trim, new Vector3(mn.X, dh - 0.15f, mn.Z - 0.05f), new Vector3(mx.X, dh, mx.Z + 0.05f));
                    }
                    else
                    {
                        MeshKit.Box(trim, new Vector3(mn.X - 0.05f, 0, mn.Z), new Vector3(mx.X + 0.05f, dh, mn.Z + 0.15f));
                        MeshKit.Box(trim, new Vector3(mn.X - 0.05f, 0, mx.Z - 0.15f), new Vector3(mx.X + 0.05f, dh, mx.Z));
                        MeshKit.Box(trim, new Vector3(mn.X - 0.05f, dh - 0.15f, mn.Z), new Vector3(mx.X + 0.05f, dh, mx.Z));
                    }
                    hasTrim = true;
                    MeshKit.Box(wall, new Vector3(mn.X, dh, mn.Z), mx);
                    continue;
                }
                bool window = !corner && !door && b.Style != BuildingStyle.Castle && ((x * 3 + z * 5) % 3 == 0)
                              && !b.IsDoor(x - 1, z) && !b.IsDoor(x + 1, z) && !b.IsDoor(x, z - 1) && !b.IsDoor(x, z + 1);
                if (b.Style == BuildingStyle.Castle && !corner && (x + z) % 4 == 0) window = true;
                if (window)
                {
                    float w0 = b.Style == BuildingStyle.Castle ? 2.2f : 1.0f, w1 = b.Style == BuildingStyle.Castle ? 3.4f : 2.0f;
                    MeshKit.Box(wall, mn, new Vector3(mx.X, w0, mx.Z));
                    MeshKit.Box(wall, new Vector3(mn.X, w1, mn.Z), mx);
                    var c = (mn + mx) / 2;
                    if (alongX)
                    {
                        MeshKit.Box(trim, new Vector3(mn.X, w0 - 0.08f, mn.Z - 0.04f), new Vector3(mx.X, w0 + Lift, mx.Z + 0.04f));
                        MeshKit.Box(glass, new Vector3(mn.X + 0.1f, w0, c.Z - 0.03f), new Vector3(mx.X - 0.1f, w1, c.Z + 0.03f));
                        MeshKit.Box(trim, new Vector3(c.X - 0.04f, w0, c.Z - 0.05f), new Vector3(c.X + 0.04f, w1, c.Z + 0.05f));
                    }
                    else
                    {
                        MeshKit.Box(trim, new Vector3(mn.X - 0.04f, w0 - 0.08f, mn.Z), new Vector3(mx.X + 0.04f, w0 + Lift, mx.Z));
                        MeshKit.Box(glass, new Vector3(c.X - 0.03f, w0, mn.Z + 0.1f), new Vector3(c.X + 0.03f, w1, mx.Z - 0.1f));
                        MeshKit.Box(trim, new Vector3(c.X - 0.05f, w0, c.Z - 0.04f), new Vector3(c.X + 0.05f, w1, c.Z + 0.04f));
                    }
                    hasTrim = hasGlass = true;
                    continue;
                }
                MeshKit.Box(wall, mn, mx);
            }
            // Castle battlements
            if (b.Style == BuildingStyle.Castle && (x + z) % 2 == 0)
                MeshKit.Box(wall, new Vector3(x + 0.2f, h, z + 0.2f), new Vector3(x + 0.8f, h + 0.7f, z + 0.8f));
            // Timber frame: dark corner posts and a mid-height beam.
            if (b.Style == BuildingStyle.Timber)
            {
                hasTrim = true;
                if (corner) MeshKit.Box(trim, new Vector3(x + t0 - 0.06f, 0, z + t0 - 0.06f), new Vector3(x + t1 + 0.06f, h + Lift * 2, z + t1 + 0.06f));
                foreach (var (mn, mx, alongX) in slabs)
                {
                    if (door && !corner) continue;
                    var e = alongX ? new Vector3(0, 0, 0.04f) : new Vector3(0.04f, 0, 0);
                    MeshKit.Box(trim, new Vector3(mn.X, h * 0.52f, mn.Z) - e, new Vector3(mx.X, h * 0.52f + 0.14f, mx.Z) + e);
                    MeshKit.Box(trim, new Vector3(mn.X, h - 0.14f, mn.Z) - e, new Vector3(mx.X, h + Lift, mx.Z) + e);
                }
            }
            // Stone plinth for timber & longhall
            if (b.Style is BuildingStyle.Timber or BuildingStyle.Longhall)
                foreach (var (mn, mx, alongX) in slabs)
                {
                    if (door && !corner) continue;
                    var e = alongX ? new Vector3(0, 0, 0.05f) : new Vector3(0.05f, 0, 0);
                    MeshKit.Box(trim, new Vector3(mn.X, -0.2f, mn.Z) - e, new Vector3(mx.X, 0.35f, mx.Z) + e);
                }
        }

        wall.SetMaterial(WallMat(b.Style));
        walls.AddChild(new MeshInstance3D { Mesh = wall.Commit(), Name = "WallMesh" });
        if (hasTrim)
        {
            trim.SetMaterial(b.Style == BuildingStyle.Castle || b.Style == BuildingStyle.Stone
                ? Props.TexMat("stone_wall", new Color(0.45f, 0.44f, 0.42f), 0.5f, new Color(0.75f, 0.72f, 0.7f))
                : Props.TexMat("wood_planks", new Color(0.25f, 0.16f, 0.09f), 0.8f, new Color(0.45f, 0.32f, 0.22f)));
            walls.AddChild(new MeshInstance3D { Mesh = trim.Commit(), Name = "TrimMesh" });
        }
        if (hasGlass)
        {
            glass.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.45f, 0.55f), Roughness = 0.1f, Metallic = 0.3f, EmissionEnabled = true, Emission = new Color(0.9f, 0.7f, 0.4f), EmissionEnergyMultiplier = 0.25f });
            walls.AddChild(new MeshInstance3D { Mesh = glass.Commit(), Name = "Glass" });
        }

        BuildRoof(h);
        float roofTop = b.Style == BuildingStyle.Castle ? h + 7f : h + Mathf.Min(b.W, b.D) * 0.6f + 1f;
        Bounds = new Aabb(new Vector3(b.X - 0.5f, floor - 0.5f, b.Z - 0.5f), new Vector3(b.W + 1f, roofTop + 0.5f, b.D + 1f));

        light = new OmniLight3D { Position = new Vector3(b.X + b.W / 2f, h - 0.6f, b.Z + b.D / 2f), LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = 0.9f, OmniRange = Mathf.Max(b.W, b.D) * 0.9f, ShadowEnabled = false };
        AddChild(light);
    }

    void BuildRoof(float h)
    {
        roof = new Node3D { Name = "Roof" };
        AddChild(roof);
        const float o = 0.45f;
        float x0 = B.X - o + 0.2f, x1 = B.X + B.W + o - 0.2f, z0 = B.Z - o + 0.2f, z1 = B.Z + B.D + o - 0.2f;
        if (B.Style == BuildingStyle.Castle)
        {
            var slab = new SurfaceTool(); slab.Begin(Mesh.PrimitiveType.Triangles);
            MeshKit.Box(slab, new Vector3(B.X + 0.8f, h - 0.3f, B.Z + 0.8f), new Vector3(B.X + B.W - 0.8f, h - 0.05f, B.Z + B.D - 0.8f));
            // Central tower block with a pointed roof.
            float cx = B.X + B.W / 2f, cz = B.Z + B.D / 2f;
            MeshKit.Box(slab, new Vector3(cx - 2.5f, h - 0.1f, cz - 2f), new Vector3(cx + 2.5f, h + 3.2f, cz + 2f));
            slab.SetMaterial(WallMat(B.Style));
            var mi = new MeshInstance3D { Mesh = slab.Commit() };
            roof.AddChild(mi); roofParts.Add(mi);
            var spire = new SurfaceTool(); spire.Begin(Mesh.PrimitiveType.Triangles);
            var top = new Vector3(cx, h + 6.5f, cz);
            var c0 = new Vector3(cx - 2.8f, h + 3.2f, cz - 2.3f); var c1 = new Vector3(cx + 2.8f, h + 3.2f, cz - 2.3f);
            var c2 = new Vector3(cx + 2.8f, h + 3.2f, cz + 2.3f); var c3 = new Vector3(cx - 2.8f, h + 3.2f, cz + 2.3f);
            MeshKit.Tri(spire, c1, c0, top); MeshKit.Tri(spire, c2, c1, top); MeshKit.Tri(spire, c3, c2, top); MeshKit.Tri(spire, c0, c3, top);
            var spireMat = (StandardMaterial3D)Assets.TexturedMaterial("roof_tiles", new Color(0.3f, 0.35f, 0.6f), 0.45f);
            spireMat.AlbedoColor = new Color(0.55f, 0.65f, 1f);
            spireMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            spire.SetMaterial(spireMat);
            var smi = new MeshInstance3D { Mesh = spire.Commit() };
            roof.AddChild(smi); roofParts.Add(smi);
            // Flag
            var pole = Props.Cyl(roof, 0.05f, 0.05f, 2.5f, top + new Vector3(0, 1.2f, 0), Props.Mat(new Color(0.2f, 0.14f, 0.08f)), 6);
            var flag = Props.Box(roof, new Vector3(1.4f, 0.8f, 0.04f), top + new Vector3(0.7f, 2.1f, 0), Props.Mat(new Color(0.7f, 0.1f, 0.1f)));
            roofParts.Add(pole); roofParts.Add(flag);
            return;
        }
        bool alongX = B.W >= B.D;
        float span = alongX ? (z1 - z0) : (x1 - x0);
        float rise = span * (B.Style == BuildingStyle.Timber ? 0.55f : 0.45f);
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        var gable = new SurfaceTool(); gable.Begin(Mesh.PrimitiveType.Triangles);
        if (alongX)
        {
            float zm = (z0 + z1) / 2;
            var r0 = new Vector3(x0, h + rise, zm); var r1 = new Vector3(x1, h + rise, zm);
            MeshKit.Quad(st, new Vector3(x0, h - 0.1f, z0), new Vector3(x1, h - 0.1f, z0), r1, r0, new Vector3(0, 1, -1).Normalized());
            MeshKit.Quad(st, new Vector3(x1, h - 0.1f, z1), new Vector3(x0, h - 0.1f, z1), r0, r1, new Vector3(0, 1, 1).Normalized());
            float gx0 = B.X + 0.2f, gx1 = B.X + B.W - 0.2f;
            MeshKit.Tri(gable, new Vector3(gx0, h, B.Z + 0.2f), new Vector3(gx0, h, B.Z + B.D - 0.2f), new Vector3(gx0, h + rise * 0.92f, zm));
            MeshKit.Tri(gable, new Vector3(gx1, h, B.Z + B.D - 0.2f), new Vector3(gx1, h, B.Z + 0.2f), new Vector3(gx1, h + rise * 0.92f, zm));
        }
        else
        {
            float xm = (x0 + x1) / 2;
            var r0 = new Vector3(xm, h + rise, z0); var r1 = new Vector3(xm, h + rise, z1);
            MeshKit.Quad(st, new Vector3(x0, h - 0.1f, z1), new Vector3(x0, h - 0.1f, z0), r0, r1, new Vector3(-1, 1, 0).Normalized());
            MeshKit.Quad(st, new Vector3(x1, h - 0.1f, z0), new Vector3(x1, h - 0.1f, z1), r1, r0, new Vector3(1, 1, 0).Normalized());
            float gz0 = B.Z + 0.2f, gz1 = B.Z + B.D - 0.2f;
            MeshKit.Tri(gable, new Vector3(B.X + B.W - 0.2f, h, gz0), new Vector3(B.X + 0.2f, h, gz0), new Vector3(xm, h + rise * 0.92f, gz0));
            MeshKit.Tri(gable, new Vector3(B.X + 0.2f, h, gz1), new Vector3(B.X + B.W - 0.2f, h, gz1), new Vector3(xm, h + rise * 0.92f, gz1));
        }
        st.SetMaterial(RoofMat(B.Style));
        var rm = new MeshInstance3D { Mesh = st.Commit(), Name = "RoofMesh" };
        roof.AddChild(rm); roofParts.Add(rm);
        var gm = (StandardMaterial3D)((StandardMaterial3D)WallMat(B.Style)).Duplicate();
        gm.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        gable.SetMaterial(gm);
        var gmi = new MeshInstance3D { Mesh = gable.Commit(), Name = "Gables" };
        roof.AddChild(gmi); roofParts.Add(gmi);
        // Chimney
        if (B.Style != BuildingStyle.Longhall)
        {
            var ch = new SurfaceTool(); ch.Begin(Mesh.PrimitiveType.Triangles);
            float cx = B.X + B.W - 1.3f, cz = B.Z + 1.3f;
            MeshKit.Box(ch, new Vector3(cx - 0.35f, h, cz - 0.35f), new Vector3(cx + 0.35f, h + rise + 0.8f, cz + 0.35f));
            ch.SetMaterial(Props.TexMat("stone_wall", new Color(0.5f, 0.48f, 0.45f), 0.5f));
            var cmi = new MeshInstance3D { Mesh = ch.Commit() };
            roof.AddChild(cmi); roofParts.Add(cmi);
        }
    }

    /// Cut away the roof and lower the walls (player inside, or building between camera and player).
    public void SetCutaway(bool v) => inside = v;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float wantWall = inside ? 0.38f : 1f, wantRoof = inside ? 0f : 1f;
        if (Mathf.IsEqualApprox(wallK, wantWall) && Mathf.IsEqualApprox(roofK, wantRoof)) return;
        wallK = Mathf.MoveToward(wallK, wantWall, dt * 3f);
        roofK = Mathf.MoveToward(roofK, wantRoof, dt * 4f);
        walls.Scale = new Vector3(1, wallK, 1);
        roof.Visible = roofK > 0.02f;
        foreach (var p in roofParts) p.Transparency = 1f - roofK;
        roof.Position = new Vector3(0, (1f - roofK) * 1.5f - (1f - wallK) * B.WallHeight, 0);
    }
}
