using System;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Rotating circular minimap with HP/run orbs and a compass.
public partial class Minimap : Control
{
    MapData map;
    ImageTexture tex;
    public PrivateState State;
    static readonly Vector2 Center = new(124, 104);
    const float Radius = 88f;
    const float Zoom = 3.2f;
    static readonly Vector2 HpOrb = new(24, 62), RunOrb = new(24, 118), MapOrb = new(24, 174), Compass = new(40, 20);

    public Minimap() => MouseFilter = MouseFilterEnum.Stop;

    public void SetMap(MapData m)
    {
        map = m;
        tex = MapImage.Build(m);
        QueueRedraw();
    }

    public override void _Process(double delta) => QueueRedraw();

    (Vector2 f, Vector2 r) Axes()
    {
        float yaw = GameWorld.I?.Rig?.Yaw ?? 0;
        var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        var r = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
        return (f, r);
    }

    Vector2 PlayerPos()
    {
        var me = GameWorld.I?.Me;
        return me != null ? new Vector2(me.GlobalPosition.X, me.GlobalPosition.Z) : new Vector2(map?.Spawn.X ?? 0, map?.Spawn.Z ?? 0);
    }

    Vector2 ScreenToWorld(Vector2 sp)
    {
        var (f, r) = Axes();
        var d = (sp - Center) / Zoom;
        return PlayerPos() + r * d.X - f * d.Y;
    }

    Vector2 WorldToScreen(Vector2 wp)
    {
        var (f, r) = Axes();
        var rel = wp - PlayerPos();
        return Center + new Vector2(rel.Dot(r), -rel.Dot(f)) * Zoom;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawCircle(Center, Radius + 6, new Color(0.3f, 0.24f, 0.16f));
        if (map != null && tex != null)
        {
            const int n = 48;
            var pts = new Vector2[n];
            var uvs = new Vector2[n];
            var cols = new Color[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.Tau;
                pts[i] = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
                var w = ScreenToWorld(pts[i]);
                uvs[i] = new Vector2(w.X / map.W, w.Y / map.H);
                cols[i] = Colors.White;
            }
            DrawPolygon(pts, cols, uvs, tex);

            var gw = GameWorld.I;
            if (gw != null)
            {
                foreach (var g in gw.Items.Values)
                {
                    var p = WorldToScreen(new Vector2(g.GlobalPosition.X, g.GlobalPosition.Z));
                    if (p.DistanceTo(Center) < Radius - 3) DrawCircle(p, 2f, new Color(1f, 0.1f, 0.1f));
                }
                foreach (var v in gw.Views.Values)
                {
                    if (v.IsMe || v.Dead) continue;
                    var p = WorldToScreen(new Vector2(v.GlobalPosition.X, v.GlobalPosition.Z));
                    if (p.DistanceTo(Center) > Radius - 3) continue;
                    DrawCircle(p, 2.6f, Colors.Black);
                    DrawCircle(p, 2f, v.IsPlayer ? Colors.White : new Color(1f, 1f, 0.2f));
                }
            }
            DrawRect(new Rect2(Center - new Vector2(2, 2), new Vector2(4, 4)), Colors.White);
            if (gw?.Destination is { } dest)
            {
                // A little red flag planted at the walk target.
                var fp = WorldToScreen(dest);
                if (fp.DistanceTo(Center) > Radius - 4) fp = Center + (fp - Center).Normalized() * (Radius - 4);
                DrawLine(fp, fp + new Vector2(0, -13), Colors.Black, 2.5f);
                DrawLine(fp, fp + new Vector2(0, -13), new Color(0.85f, 0.8f, 0.7f), 1.2f);
                var flag = new[] { fp + new Vector2(0.5f, -13), fp + new Vector2(9, -10), fp + new Vector2(0.5f, -7) };
                DrawColoredPolygon(flag, new Color(0.9f, 0.1f, 0.08f));
                DrawPolyline(new[] { flag[0], flag[1], flag[2], flag[0] }, Colors.Black, 1f);
                DrawCircle(fp, 1.8f, Colors.Black);
            }
        }
        DrawArc(Center, Radius + 3, 0, Mathf.Tau, 64, new Color(0.55f, 0.43f, 0.24f), 5, true);
        DrawArc(Center, Radius + 6, 0, Mathf.Tau, 64, new Color(0.15f, 0.1f, 0.05f), 2, true);

        // compass
        var (fw, _) = Axes();
        DrawCircle(Compass, 15, new Color(0.2f, 0.16f, 0.1f));
        DrawArc(Compass, 15, 0, Mathf.Tau, 24, UiTheme.PanelBorder, 2, true);
        // North points to world -Z: screen direction of (0,-1) world.
        var nd = (WorldToScreen(PlayerPos() + new Vector2(0, -1)) - Center).Normalized();
        DrawLine(Compass, Compass + nd * 11, new Color(0.9f, 0.1f, 0.1f), 3);
        DrawLine(Compass, Compass - nd * 11, Colors.White, 2);
        DrawString(font, Compass + nd * 13 - new Vector2(4, -4), "N", HorizontalAlignment.Left, -1, 10, Colors.White);

        // orbs
        int hp = State?.Hp ?? 10, maxHp = State != null ? Xp.LevelForXp(State.Xp[(int)Skill.Vitality]) : 10;
        Orb(HpOrb, hp / (float)Math.Max(1, maxHp), new Color(0.75f, 0.1f, 0.1f), hp.ToString(), "HP");
        bool run = State?.Run ?? true;
        Orb(RunOrb, run ? 1 : 0.25f, new Color(0.85f, 0.75f, 0.2f), run ? "ON" : "off", "Run");
        Orb(MapOrb, 1, new Color(0.3f, 0.55f, 0.3f), "M", "Map");
    }

    void Orb(Vector2 c, float fill, Color col, string text, string caption)
    {
        var font = ThemeDB.FallbackFont;
        DrawCircle(c, 19, new Color(0.12f, 0.1f, 0.07f));
        DrawCircle(c, 16, col.Darkened(0.65f));
        DrawCircle(c, 16 * Mathf.Clamp(fill, 0.05f, 1f), col);
        DrawArc(c, 19, 0, Mathf.Tau, 32, UiTheme.PanelBorder, 2, true);
        DrawString(font, c + new Vector2(-20, 5), text, HorizontalAlignment.Center, 40, 13, Colors.Black);
        DrawString(font, c + new Vector2(-21, 4), text, HorizontalAlignment.Center, 40, 13, Colors.White);
        DrawString(font, c + new Vector2(-20, 31), caption, HorizontalAlignment.Center, 40, 11, UiTheme.Gold);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;
        var p = mb.Position;
        AcceptEvent();
        if (p.DistanceTo(RunOrb) < 20) { Net.I.Send(new ClientMsg { T = C2S.Run, A = (State?.Run ?? true) ? 0 : 1 }); return; }
        if (p.DistanceTo(MapOrb) < 20) { GameWorld.I?.Hud.ToggleWorldMap(); return; }
        if (p.DistanceTo(Compass) < 16) { GameWorld.I?.Rig.ResetNorth(); return; }
        if (p.DistanceTo(Center) < Radius && map != null)
        {
            var w = ScreenToWorld(p);
            int x = (int)Mathf.Floor(w.X), z = (int)Mathf.Floor(w.Y);
            if (map.InBounds(x, z))
            {
                Net.I.Send(new ClientMsg { T = C2S.Walk, A = x, B = z });
                GameWorld.I?.SetDestination(new Vector2(x + 0.5f, z + 0.5f));
            }
        }
    }
}
