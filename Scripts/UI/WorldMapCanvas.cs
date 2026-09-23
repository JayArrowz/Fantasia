using System;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// The world map drawing: ground, labels, entrances and the player.
public partial class WorldMapCanvas : Control
{
    public MapData Map;
    public ImageTexture Tex;
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override void _Draw()
    {
        if (Map == null || Tex == null) return;
        float s = Mathf.Min(Size.X / Map.W, Size.Y / Map.H);
        var off = (Size - new Vector2(Map.W, Map.H) * s) / 2;
        var parch = Assets.Tex("parchment");
        if (parch != null) DrawTextureRect(parch, new Rect2(Vector2.Zero, Size), false, new Color(1, 1, 1, 0.9f));
        DrawTextureRect(Tex, new Rect2(off, new Vector2(Map.W, Map.H) * s), false, new Color(1, 1, 1, 0.92f));
        if (Map.PvpZone.Size != Vector2I.Zero)
        {
            var z = Map.PvpZone;
            DrawRect(new Rect2(off + new Vector2(z.Position.X, z.Position.Y) * s, new Vector2(z.Size.X, z.Size.Y) * s), new Color(0.8f, 0.05f, 0.05f, 0.18f));
        }
        var font = UiTheme.Heading;
        foreach (var l in Map.Labels)
        {
            var p = off + new Vector2(l.X, l.Z) * s;
            var w = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, 16).X;
            DrawString(font, p + new Vector2(-w / 2 + 1, 1), l.Text, HorizontalAlignment.Left, -1, 16, Colors.Black);
            DrawString(font, p + new Vector2(-w / 2, 0), l.Text, HorizontalAlignment.Left, -1, 16, l.Text.StartsWith("THE BLOOD") ? new Color(1f, 0.35f, 0.3f) : UiTheme.Gold);
        }
        foreach (var o in Map.Objects)
        {
            if (o.Action is "Enter" or "Climb-up" or "Bank")
            {
                var p = off + new Vector2(o.X + o.W / 2f, o.Z + o.D / 2f) * s;
                DrawCircle(p, 5, Colors.Black);
                DrawCircle(p, 4, o.Action == "Bank" ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.2f, 0.9f));
            }
        }
        var me = GameWorld.I?.Me;
        if (me != null)
        {
            var p = off + new Vector2(me.GlobalPosition.X, me.GlobalPosition.Z) * s;
            float pulse = 5 + Mathf.Sin(Time.GetTicksMsec() / 150f) * 1.5f;
            DrawCircle(p, pulse + 2, Colors.Black);
            DrawCircle(p, pulse, Colors.White);
        }
        DrawString(ThemeDB.FallbackFont, new Vector2(8, Size.Y - 8), "Purple: dungeon entrances    Gold: treasury    Red: the Bloodmarch (PvP)", HorizontalAlignment.Left, -1, 13, new Color(0.2f, 0.12f, 0.05f));
    }
}
