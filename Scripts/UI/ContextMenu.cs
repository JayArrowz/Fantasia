using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// RuneScape-style right-click menu drawn by hand.
public partial class ContextMenu : Control
{
    List<MenuOption> options = new();
    int hover = -1;
    const int RowH = 20, Header = 22, Pad = 6;
    public bool IsOpen => Visible;

    public ContextMenu()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;
    }

    public void Open(Vector2 pos, List<MenuOption> opts)
    {
        options = new List<MenuOption>(opts) { new MenuOption { Verb = "Cancel", Target = "" } };
        float w = 120;
        var font = ThemeDB.FallbackFont;
        foreach (var o in options)
            w = Mathf.Max(w, font.GetStringSize(Label(o), HorizontalAlignment.Left, -1, 14).X + Pad * 2 + 8);
        Size = new Vector2(w, Header + options.Count * RowH + 4);
        var vs = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(pos.X - w / 2, 0, vs.X - w), Mathf.Clamp(pos.Y - 8, 0, vs.Y - Size.Y));
        hover = -1;
        Visible = true;
        QueueRedraw();
    }

    static string Label(MenuOption o) => $"{o.Verb} {o.Target}{o.Suffix}";

    public void Close() { Visible = false; }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = (int)((mm.Position.Y - Header) / RowH);
            hover = mm.Position.Y < Header || h < 0 || h >= options.Count ? -1 : h;
            QueueRedraw();
        }
        else if (e is InputEventMouseButton mb && mb.Pressed && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            if (hover >= 0 && hover < options.Count)
            {
                var o = options[hover];
                Close();
                if (o.Run != null) GameWorld.I?.Execute(o);
            }
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var m = GetGlobalMousePosition();
        var r = GetGlobalRect().Grow(12);
        if (!r.HasPoint(m)) Close();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.36f, 0.32f, 0.25f, 0.97f));
        DrawRect(new Rect2(1, 1, Size.X - 2, Header - 2), new Color(0, 0, 0, 0.95f));
        DrawString(font, new Vector2(Pad, 16), "Actions", HorizontalAlignment.Left, -1, 14, new Color(0.36f, 0.32f, 0.25f));
        DrawRect(new Rect2(1, Header, Size.X - 2, Size.Y - Header - 1), new Color(0, 0, 0, 0.92f));
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            float y = Header + i * RowH + 15;
            if (i == hover) DrawRect(new Rect2(2, Header + i * RowH + 1, Size.X - 4, RowH), new Color(1, 1, 1, 0.12f));
            var verbCol = i == hover ? new Color(1f, 1f, 0.3f) : Colors.White;
            DrawString(font, new Vector2(Pad, y), o.Verb, HorizontalAlignment.Left, -1, 14, verbCol);
            float x = Pad + font.GetStringSize(o.Verb + " ", HorizontalAlignment.Left, -1, 14).X;
            if (!string.IsNullOrEmpty(o.Target))
            {
                DrawString(font, new Vector2(x, y), o.Target, HorizontalAlignment.Left, -1, 14, o.TargetColor);
                x += font.GetStringSize(o.Target, HorizontalAlignment.Left, -1, 14).X;
            }
            if (!string.IsNullOrEmpty(o.Suffix)) DrawString(font, new Vector2(x, y), o.Suffix, HorizontalAlignment.Left, -1, 14, o.TargetColor);
        }
    }
}
