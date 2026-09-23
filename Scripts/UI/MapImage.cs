using System;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Ground-colour image of a map, shared by the minimap and the world map.
public static class MapImage
{
    public static Color GroundColor(Ground g) => g switch
    {
        Ground.Grass => new Color(0.28f, 0.48f, 0.2f), Ground.DarkGrass => new Color(0.22f, 0.25f, 0.15f),
        Ground.Moss => new Color(0.25f, 0.35f, 0.2f), Ground.Dirt => new Color(0.52f, 0.4f, 0.25f),
        Ground.Cobble => new Color(0.55f, 0.53f, 0.5f), Ground.Sand => new Color(0.78f, 0.7f, 0.48f),
        Ground.Water => new Color(0.18f, 0.35f, 0.6f), Ground.Stone => new Color(0.4f, 0.38f, 0.35f),
        Ground.Wood => new Color(0.45f, 0.3f, 0.18f), Ground.Ash => new Color(0.28f, 0.25f, 0.23f),
        _ => new Color(0.05f, 0.05f, 0.05f),
    };

    public static ImageTexture Build(MapData m)
    {
        var img = Image.CreateEmpty(m.W, m.H, false, Image.Format.Rgba8);
        for (int x = 0; x < m.W; x++)
            for (int z = 0; z < m.H; z++)
            {
                var c = GroundColor(m.Ground[x, z]);
                float shade = 0.9f + Hash.Unit(x, z, 5) * 0.1f;
                img.SetPixel(x, z, new Color(c.R * shade, c.G * shade, c.B * shade));
            }
        foreach (var o in m.Objects)
        {
            Color? c = o.Kind switch
            {
                ObjKind.TreeOak or ObjKind.TreePine or ObjKind.TreeWillow => new Color(0.1f, 0.3f, 0.1f),
                ObjKind.TreeDead => new Color(0.25f, 0.2f, 0.15f),
                ObjKind.CastleWall or ObjKind.Tower or ObjKind.Keep => new Color(0.75f, 0.75f, 0.75f),
                ObjKind.HouseTimber or ObjKind.HouseStone or ObjKind.Mausoleum => new Color(0.55f, 0.3f, 0.2f),
                ObjKind.Fence => new Color(0.4f, 0.28f, 0.15f),
                ObjKind.Bridge => new Color(0.5f, 0.35f, 0.2f),
                ObjKind.RockLarge => new Color(0.5f, 0.5f, 0.5f),
                ObjKind.Stall or ObjKind.BankBooth => new Color(0.9f, 0.75f, 0.3f),
                _ => null,
            };
            if (c == null) continue;
            for (int x = o.X; x < o.X + o.W; x++)
                for (int z = o.Z; z < o.Z + o.D; z++)
                    if (m.InBounds(x, z)) img.SetPixel(x, z, c.Value);
        }
        foreach (var b in m.Buildings)
            for (int x = b.X; x < b.X + b.W; x++)
                for (int z = b.Z; z < b.Z + b.D; z++)
                    if (b.IsWall(x, z)) img.SetPixel(x, z, new Color(0.85f, 0.85f, 0.82f));
                    else if (b.IsDoor(x, z)) img.SetPixel(x, z, new Color(0.35f, 0.2f, 0.1f));
        return ImageTexture.CreateFromImage(img);
    }
}
