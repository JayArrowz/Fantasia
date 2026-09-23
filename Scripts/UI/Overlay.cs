using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

// =====================================================================================
/// Draws HP bars, hitsplats and overhead chat above entities.
public partial class Overlay : Control
{
    public Overlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var gw = GameWorld.I;
        var cam = gw?.Rig?.Cam;
        if (cam == null) return;
        var font = ThemeDB.FallbackFont;
        foreach (var v in gw.Views.Values)
        {
            var head = v.Head;
            if (cam.IsPositionBehind(head)) continue;
            var p = cam.UnprojectPosition(head);
            float y = p.Y;
            if (v.HpShow > 0 && v.MaxHp > 0 && !v.Dead)
            {
                float w = 40, ratio = Mathf.Clamp(v.Hp / (float)v.MaxHp, 0, 1);
                DrawRect(new Rect2(p.X - w / 2 - 1, y - 1, w + 2, 7), Colors.Black);
                DrawRect(new Rect2(p.X - w / 2, y, w, 5), new Color(0.8f, 0.05f, 0.05f));
                DrawRect(new Rect2(p.X - w / 2, y, w * ratio, 5), new Color(0.1f, 0.85f, 0.1f));
                y -= 8;
            }
            if (v.IsPlayer && !v.IsMe)
            {
                DrawString(font, new Vector2(p.X - 100, y - 2), v.DisplayName, HorizontalAlignment.Center, 200, 13, Colors.Black);
                DrawString(font, new Vector2(p.X - 101, y - 3), v.DisplayName, HorizontalAlignment.Center, 200, 13, Colors.White);
                y -= 14;
            }
            if (v.ChatT > 0 && v.Chat != null)
            {
                DrawString(font, new Vector2(p.X - 201, y - 3), v.Chat, HorizontalAlignment.Center, 400, 15, Colors.Black);
                DrawString(font, new Vector2(p.X - 200, y - 4), v.Chat, HorizontalAlignment.Center, 400, 15, new Color(1f, 1f, 0.1f));
            }
            var chest = cam.UnprojectPosition(v.Chest);
            foreach (var s in v.Splats)
            {
                float a = Mathf.Clamp(1.3f - s.T, 0, 1);
                var c = new Vector2(chest.X + s.Offset, chest.Y - s.T * 14);
                DrawCircle(c, 12, new Color(0, 0, 0, a * 0.8f));
                DrawCircle(c, 10.5f, new Color(s.Color, a));
                string txt = s.Damage.ToString();
                DrawString(font, c + new Vector2(-14, 5), txt, HorizontalAlignment.Center, 28, 14, new Color(1, 1, 1, a));
            }
        }
    }
}
